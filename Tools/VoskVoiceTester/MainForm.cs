using System.Collections.Concurrent;
using System.Text.Json;
using Vosk;

namespace VoskVoiceTester;

internal sealed class MainForm : Form
{
    private readonly TextBox modelPathBox = new();
    private readonly ComboBox grammarBox = new();
    private readonly Button browseButton = new();
    private readonly Button startButton = new();
    private readonly Label statusLabel = new();
    private readonly Label partialLabel = new();
    private readonly ListBox resultList = new();
    private readonly ListBox vocabularyList = new();
    private readonly System.Windows.Forms.Timer uiTimer = new();
    private readonly ConcurrentQueue<byte[]> audioQueue = new();
    private readonly IReadOnlyList<VoiceEntry> allEntries = GameVoiceVocabulary.Build();
    private readonly Dictionary<string, VoiceEntry> spokenLookup;
    private readonly CancellationTokenSource lifetime = new();

    private WaveInRecorder? recorder;
    private Model? model;
    private VoskRecognizer? recognizer;
    private Task? recognitionTask;
    private bool listening;

    public MainForm()
    {
        Text = "The Script - Vosk Voice Tester";
        Width = 1040;
        Height = 720;
        StartPosition = FormStartPosition.CenterScreen;

        spokenLookup = allEntries
            .GroupBy(entry => GameVoiceVocabulary.NormalizeRecognized(entry.Spoken), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        BuildUi();
        RefreshVocabularyList();

        uiTimer.Interval = 100;
        uiTimer.Tick += (_, _) => UpdateStatus();
        uiTimer.Start();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        lifetime.Cancel();
        StopListening();
        recognizer?.Dispose();
        model?.Dispose();
        lifetime.Dispose();
        base.OnFormClosed(e);
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
            Padding = new Padding(12),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        modelPathBox.Dock = DockStyle.Fill;
        modelPathBox.Text = FindDefaultModelPath();
        browseButton.Text = "Browse...";
        browseButton.Dock = DockStyle.Fill;
        browseButton.Click += (_, _) => BrowseForModel();

        root.Controls.Add(new Label { Text = "Vosk model folder", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        root.Controls.Add(new Label { Text = "Vocabulary / results", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 1, 0);
        root.Controls.Add(modelPathBox, 0, 1);
        root.Controls.Add(browseButton, 1, 1);

        grammarBox.Dock = DockStyle.Left;
        grammarBox.DropDownStyle = ComboBoxStyle.DropDownList;
        grammarBox.Width = 220;
        grammarBox.Items.AddRange(new object[] { "All words + letters", "Spell words only", "Letters only" });
        grammarBox.SelectedIndex = 0;
        grammarBox.SelectedIndexChanged += (_, _) => RefreshVocabularyList();
        root.Controls.Add(grammarBox, 0, 2);

        startButton.Text = "Start Listening";
        startButton.Dock = DockStyle.Fill;
        startButton.Click += (_, _) => ToggleListening();
        root.Controls.Add(startButton, 1, 2);

        statusLabel.Dock = DockStyle.Fill;
        statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        partialLabel.Dock = DockStyle.Fill;
        partialLabel.TextAlign = ContentAlignment.MiddleLeft;
        partialLabel.Font = new Font(partialLabel.Font, FontStyle.Bold);
        root.Controls.Add(statusLabel, 0, 3);
        root.Controls.Add(partialLabel, 1, 3);

        resultList.Dock = DockStyle.Fill;
        vocabularyList.Dock = DockStyle.Fill;
        root.Controls.Add(resultList, 0, 4);
        root.Controls.Add(vocabularyList, 1, 4);
    }

    private void ToggleListening()
    {
        if (listening)
            StopListening();
        else
            StartListening();
    }

    private void StartListening()
    {
        try
        {
            StopListening();
            Vosk.Vosk.SetLogLevel(-1);
            model ??= new Model(modelPathBox.Text);
            recognizer = new VoskRecognizer(model, 16000.0f, GameVoiceVocabulary.BuildGrammarJson(SelectedEntries()));
            listening = true;
            startButton.Text = "Stop Listening";
            resultList.Items.Insert(0, $"Listening with {SelectedEntries().Count()} entries. Say any listed word/alias.");

            recorder = new WaveInRecorder();
            recorder.DataAvailable += bytes => audioQueue.Enqueue(bytes);
            recorder.Start();
            recognitionTask = Task.Run(RecognitionLoop, lifetime.Token);
        }
        catch (Exception ex)
        {
            StopListening();
            MessageBox.Show(this, ex.Message, "Could not start Vosk tester", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void StopListening()
    {
        listening = false;
        startButton.Text = "Start Listening";
        recorder?.Dispose();
        recorder = null;
        recognizer?.Dispose();
        recognizer = null;
        while (audioQueue.TryDequeue(out _))
        {
        }
    }

    private async Task RecognitionLoop()
    {
        while (!lifetime.IsCancellationRequested && listening)
        {
            if (!audioQueue.TryDequeue(out byte[]? bytes))
            {
                await Task.Delay(10, lifetime.Token).ConfigureAwait(false);
                continue;
            }

            VoskRecognizer? active = recognizer;
            if (active == null)
                continue;

            bool completed = active.AcceptWaveform(bytes, bytes.Length);
            string json = completed ? active.Result() : active.PartialResult();
            HandleRecognizerJson(json, completed);
        }
    }

    private void HandleRecognizerJson(string json, bool completed)
    {
        string key = completed ? "text" : "partial";
        string text = ExtractText(json, key);
        if (string.IsNullOrWhiteSpace(text) || string.Equals(text.Trim(), "[unk]", StringComparison.OrdinalIgnoreCase))
            return;

        BeginInvoke(() =>
        {
            if (!completed)
            {
                partialLabel.Text = $"Partial: {text}";
                return;
            }

            string normalized = GameVoiceVocabulary.NormalizeRecognized(text);
            string canonical = spokenLookup.TryGetValue(normalized, out VoiceEntry? entry)
                ? $"{entry.Canonical} ({entry.Kind})"
                : "no canonical match";
            resultList.Items.Insert(0, $"{DateTime.Now:T}  heard: {text.ToUpperInvariant()} -> {canonical}");
            partialLabel.Text = "";
        });
    }

    private IEnumerable<VoiceEntry> SelectedEntries()
    {
        return grammarBox.SelectedIndex switch
        {
            1 => allEntries.Where(entry => entry.Kind == "Spell word"),
            2 => allEntries.Where(entry => entry.Kind.StartsWith("Letter", StringComparison.OrdinalIgnoreCase)),
            _ => allEntries,
        };
    }

    private void RefreshVocabularyList()
    {
        vocabularyList.Items.Clear();
        foreach (VoiceEntry entry in SelectedEntries())
            vocabularyList.Items.Add($"{entry.Spoken} -> {entry.Canonical} [{entry.Kind}]");
    }

    private void UpdateStatus()
    {
        statusLabel.Text = listening
            ? $"Status: listening, queued chunks {audioQueue.Count}"
            : "Status: idle";
    }

    private void BrowseForModel()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the Vosk model folder containing am, conf, graph, and ivector.",
            SelectedPath = Directory.Exists(modelPathBox.Text) ? modelPathBox.Text : Environment.CurrentDirectory,
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            model?.Dispose();
            model = null;
            modelPathBox.Text = dialog.SelectedPath;
        }
    }

    private static string FindDefaultModelPath()
    {
        string current = AppContext.BaseDirectory;
        for (int i = 0; i < 8; i++)
        {
            string candidate = Path.GetFullPath(Path.Combine(current, "Assets", "StreamingAssets", "VoskModel"));
            if (Directory.Exists(candidate))
                return candidate;
            current = Path.GetFullPath(Path.Combine(current, ".."));
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Assets", "StreamingAssets", "VoskModel"));
    }

    private static string ExtractText(string json, string key)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(key, out JsonElement element)
                ? element.GetString() ?? ""
                : "";
        }
        catch
        {
            return "";
        }
    }
}
