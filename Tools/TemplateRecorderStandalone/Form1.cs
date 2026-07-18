using System.Diagnostics;

namespace TemplateRecorderStandalone;

public partial class Form1 : Form
{
    enum RecorderMode
    {
        Save,
        Test
    }

    readonly TemplateStore _store = new();
    PortableRecognizer _recognizer = new();

    readonly Color _appBackground = Color.FromArgb(30, 26, 23);
    readonly Color _panelBackground = Color.FromArgb(49, 43, 38);
    readonly Color _cardBackground = Color.FromArgb(64, 56, 49);
    readonly Color _textColor = Color.FromArgb(242, 235, 224);
    readonly Color _mutedTextColor = Color.FromArgb(186, 173, 154);
    readonly Color _accent = Color.FromArgb(211, 159, 78);
    readonly Color _buttonFill = Color.FromArgb(79, 119, 181);
    readonly Color _dangerFill = Color.FromArgb(154, 71, 64);
    readonly Color _quietFill = Color.FromArgb(84, 73, 66);

    Panel _panelMainMenu = null!;
    Panel _panelLetterPicker = null!;
    Panel _panelRecorder = null!;
    Panel _panelGallery = null!;

    FlowLayoutPanel _letterGrid = null!;
    FlowLayoutPanel _galleryTabs = null!;
    FlowLayoutPanel _galleryList = null!;

    Label _labelCurrentLetter = null!;
    Label _labelSampleCount = null!;
    Label _labelInstruction = null!;
    Label _labelGalleryEmpty = null!;
    Label _labelPath = null!;

    Button _btnSaveSample = null!;
    Button _btnTestSample = null!;
    TextBox _writerIdTextBox = null!;
    TextBox _sessionIdTextBox = null!;
    ComboBox _ageBandComboBox = null!;
    ComboBox _handednessComboBox = null!;

    StrokeCanvasControl _canvas = null!;

    RecorderMode _mode = RecorderMode.Save;
    string _currentLetter = "A";
    string _galleryLetter = "";

    public Form1()
    {
        InitializeComponent();
        BuildUi();
        ReloadRecognizer();
        OpenMainMenu();
        UpdateWindowTitle();
    }

    void BuildUi()
    {
        Text = "Template Recorder Standalone";
        MinimumSize = new Size(960, 720);
        BackColor = _appBackground;
        ForeColor = _textColor;
        Font = new Font("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Point);

        _panelMainMenu = CreateScenePanel();
        _panelLetterPicker = CreateScenePanel();
        _panelRecorder = CreateScenePanel();
        _panelGallery = CreateScenePanel();

        Controls.Add(_panelMainMenu);
        Controls.Add(_panelLetterPicker);
        Controls.Add(_panelRecorder);
        Controls.Add(_panelGallery);

        BuildMainMenu();
        BuildLetterPicker();
        BuildRecorder();
        BuildGallery();
    }

    void BuildMainMenu()
    {
        var layout = CreateVerticalLayout();
        layout.Padding = new Padding(48, 56, 48, 40);
        layout.Controls.Add(CreateTitleLabel("Template Recorder Standalone", 24f));
        layout.Controls.Add(CreateBodyLabel("This version writes a local templates.json next to the EXE so someone else can record handwriting without Unity."));

        var buttonStack = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = true,
            Margin = new Padding(0, 18, 0, 18),
            BackColor = Color.Transparent
        };
        buttonStack.Controls.Add(CreateButton("Add Template", _buttonFill, (_, _) => OpenLetterPicker()));
        buttonStack.Controls.Add(CreateButton("Test Recognition", _quietFill, (_, _) => OpenRecognitionTest()));
        buttonStack.Controls.Add(CreateButton("View / Edit", _quietFill, (_, _) => OpenGallery()));
        buttonStack.Controls.Add(CreateButton("Open Data Folder", _quietFill, (_, _) => OpenDataFolder()));
        layout.Controls.Add(buttonStack);

        _labelPath = CreateBodyLabel("");
        _labelPath.Margin = new Padding(0, 10, 0, 0);
        layout.Controls.Add(_labelPath);
        _panelMainMenu.Controls.Add(layout);
    }

    void BuildLetterPicker()
    {
        var layout = CreateVerticalLayout();
        layout.Padding = new Padding(36, 32, 36, 24);
        layout.Controls.Add(CreateTitleLabel("Choose A Letter", 22f));
        layout.Controls.Add(CreateBodyLabel("The designer records templates here exactly like the Unity tool: left-click to draw, right-click to lift the pen."));

        var scrollPanel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 12, 0, 12)
        };
        _letterGrid = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(4),
            Margin = new Padding(0),
            BackColor = Color.Transparent
        };
        scrollPanel.Controls.Add(_letterGrid);
        layout.Controls.Add(scrollPanel);

        var footer = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent
        };
        footer.Controls.Add(CreateButton("Back", _quietFill, (_, _) => OpenMainMenu(), small: true));
        layout.Controls.Add(footer);
        _panelLetterPicker.Controls.Add(layout);
    }

    void BuildRecorder()
    {
        var layout = CreateVerticalLayout();
        layout.Padding = new Padding(36, 32, 36, 24);

        _labelCurrentLetter = CreateTitleLabel("", 22f);
        _labelSampleCount = CreateBodyLabel("");
        _labelInstruction = CreateBodyLabel("");
        _labelInstruction.MaximumSize = new Size(0, 0);

        layout.Controls.Add(_labelCurrentLetter);
        layout.Controls.Add(_labelSampleCount);
        layout.Controls.Add(_labelInstruction);

        var provenance = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 8, 0, 0)
        };
        provenance.Controls.Add(CreateBodyLabel("Pseudonymous writer ID:"));
        _writerIdTextBox = new TextBox { Width = 180, Text = "writer-" + Guid.NewGuid().ToString("N")[..8] };
        provenance.Controls.Add(_writerIdTextBox);
        provenance.Controls.Add(CreateBodyLabel("Session ID:"));
        _sessionIdTextBox = new TextBox { Width = 220, Text = Guid.NewGuid().ToString("N") };
        provenance.Controls.Add(_sessionIdTextBox);
        provenance.Controls.Add(CreateBodyLabel("Age band:"));
        _ageBandComboBox = new ComboBox { Width = 100, DropDownStyle = ComboBoxStyle.DropDownList };
        _ageBandComboBox.Items.AddRange(new object[] { "unknown", "4-5", "6-8", "9-11", "12-14", "15-17", "adult" });
        _ageBandComboBox.SelectedIndex = 0;
        provenance.Controls.Add(_ageBandComboBox);
        provenance.Controls.Add(CreateBodyLabel("Handedness:"));
        _handednessComboBox = new ComboBox { Width = 100, DropDownStyle = ComboBoxStyle.DropDownList };
        _handednessComboBox.Items.AddRange(new object[] { "unknown", "left", "right", "mixed" });
        _handednessComboBox.SelectedIndex = 0;
        provenance.Controls.Add(_handednessComboBox);
        layout.Controls.Add(provenance);

        _canvas = new StrokeCanvasControl
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 16, 0, 16),
            MinimumSize = new Size(400, 360)
        };
        layout.Controls.Add(_canvas);

        var footer = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent
        };
        _btnSaveSample = CreateButton("Save", _buttonFill, (_, _) => SaveCurrentSample(), small: true);
        _btnTestSample = CreateButton("Test", _buttonFill, (_, _) => TestCurrentSample(), small: true);
        footer.Controls.Add(_btnSaveSample);
        footer.Controls.Add(_btnTestSample);
        footer.Controls.Add(CreateButton("Clear", _dangerFill, (_, _) => ClearRecorder(), small: true));
        footer.Controls.Add(CreateButton("Back", _quietFill, (_, _) => HandleBackFromRecorder(), small: true));
        layout.Controls.Add(footer);

        _panelRecorder.Controls.Add(layout);
    }

    void BuildGallery()
    {
        var layout = CreateVerticalLayout();
        layout.Padding = new Padding(36, 32, 36, 24);
        layout.Controls.Add(CreateTitleLabel("Template Gallery", 22f));
        layout.Controls.Add(CreateBodyLabel("One column on purpose, so previews stay large and readable while your designer reviews samples."));

        Panel tabScroll = new Panel
        {
            Height = 56,
            Dock = DockStyle.Top,
            AutoScroll = true,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 12, 0, 8)
        };
        _galleryTabs = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 2, 0, 2),
            BackColor = Color.Transparent
        };
        tabScroll.Controls.Add(_galleryTabs);
        layout.Controls.Add(tabScroll);

        _labelGalleryEmpty = CreateBodyLabel("No templates saved yet.");
        _labelGalleryEmpty.Margin = new Padding(0, 4, 0, 10);
        layout.Controls.Add(_labelGalleryEmpty);

        _galleryList = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 12),
            Padding = new Padding(0, 2, 0, 2)
        };
        _galleryList.SizeChanged += (_, _) => UpdateGalleryCardWidths();
        layout.Controls.Add(_galleryList);

        var footer = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent
        };
        footer.Controls.Add(CreateButton("Back", _quietFill, (_, _) => OpenMainMenu(), small: true));
        layout.Controls.Add(footer);

        _panelGallery.Controls.Add(layout);
    }

    void OpenMainMenu()
    {
        _mode = RecorderMode.Save;
        UpdatePathLabel();
        ShowPanel(_panelMainMenu);
    }

    void OpenLetterPicker()
    {
        _mode = RecorderMode.Save;
        BuildLetterGrid();
        ShowPanel(_panelLetterPicker);
    }

    void OpenRecorder(string letter)
    {
        _mode = RecorderMode.Save;
        _currentLetter = letter;
        ClearRecorder();
        RefreshRecorderLabels();
        ShowPanel(_panelRecorder);
    }

    void OpenRecognitionTest()
    {
        _mode = RecorderMode.Test;
        _currentLetter = "?";
        ClearRecorder();
        RefreshRecorderLabels();
        ShowPanel(_panelRecorder);
    }

    void OpenGallery()
    {
        BuildGalleryTabs();
        ShowPanel(_panelGallery);
    }

    void ShowPanel(Control panelToShow)
    {
        _panelMainMenu.Visible = panelToShow == _panelMainMenu;
        _panelLetterPicker.Visible = panelToShow == _panelLetterPicker;
        _panelRecorder.Visible = panelToShow == _panelRecorder;
        _panelGallery.Visible = panelToShow == _panelGallery;
    }

    void BuildLetterGrid()
    {
        _letterGrid.SuspendLayout();
        _letterGrid.Controls.Clear();

        const string letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
        foreach (char letter in letters)
        {
            string letterKey = letter.ToString();
            int count = _store.GetEntries(letterKey).Count;
            string label = count > 0 ? $"{letterKey}{Environment.NewLine}({count})" : letterKey;
            Button button = CreateButton(label, count > 0 ? _buttonFill : _quietFill, (_, _) => OpenRecorder(letterKey), compact: true);
            button.Size = new Size(120, 82);
            button.TextAlign = ContentAlignment.MiddleCenter;
            _letterGrid.Controls.Add(button);
        }

        _letterGrid.ResumeLayout();
    }

    void BuildGalleryTabs()
    {
        _galleryTabs.SuspendLayout();
        _galleryTabs.Controls.Clear();

        IReadOnlyList<string> keys = _store.GetAllKeys();
        foreach (string key in keys)
        {
            int count = _store.GetEntries(key).Count;
            Button button = CreateButton($"{key} ({count})", _quietFill, (_, _) => ShowGalleryFor(key), small: true);
            _galleryTabs.Controls.Add(button);
        }

        _galleryTabs.ResumeLayout();

        if (keys.Count == 0)
        {
            _galleryLetter = "";
            _galleryList.Controls.Clear();
            _labelGalleryEmpty.Visible = true;
            _labelGalleryEmpty.Text = "No templates saved yet.";
            return;
        }

        if (string.IsNullOrEmpty(_galleryLetter) || !keys.Contains(_galleryLetter, StringComparer.Ordinal))
            _galleryLetter = keys[0];

        ShowGalleryFor(_galleryLetter);
    }

    void ShowGalleryFor(string letter)
    {
        _galleryLetter = letter;
        List<TemplateStore.TemplateEntry> entries = _store.GetEntries(letter);
        _galleryList.SuspendLayout();
        _galleryList.Controls.Clear();

        if (entries.Count == 0)
        {
            _labelGalleryEmpty.Visible = true;
            _labelGalleryEmpty.Text = $"No samples saved for '{letter}'.";
            _galleryList.ResumeLayout();
            return;
        }

        _labelGalleryEmpty.Visible = false;
        foreach (TemplateStore.TemplateEntry entry in entries)
            _galleryList.Controls.Add(BuildTemplateCard(entry));

        _galleryList.ResumeLayout();
        UpdateGalleryCardWidths();
    }

    Control BuildTemplateCard(TemplateStore.TemplateEntry entry)
    {
        var card = new Panel
        {
            BackColor = _cardBackground,
            Margin = new Padding(0, 0, 0, 12),
            Padding = new Padding(14),
            Height = 230
        };

        var preview = new TemplatePreviewControl
        {
            Dock = DockStyle.Top,
            Height = 122,
            Margin = new Padding(0)
        };
        preview.SetTemplate(entry);
        card.Controls.Add(preview);

        var meta = CreateBodyLabel(entry.isPrime ? "PRIME TEMPLATE" : entry.createdAt);
        meta.Dock = DockStyle.Top;
        meta.Margin = new Padding(0, 10, 0, 0);
        meta.ForeColor = entry.isPrime ? _accent : _mutedTextColor;
        card.Controls.Add(meta);

        string secondaryText = entry.isAdaptive
            ? $"Adaptive match {(int)Math.Round(entry.closenessScore * 100f)}%"
            : $"ID {entry.id[..Math.Min(8, entry.id.Length)]}";
        var secondary = CreateBodyLabel(secondaryText);
        secondary.Dock = DockStyle.Top;
        secondary.Margin = new Padding(0, 2, 0, 0);
        secondary.ForeColor = _mutedTextColor;
        card.Controls.Add(secondary);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 42,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0, 6, 0, 0)
        };
        Button primeButton = CreateButton("Prime", entry.isPrime ? _buttonFill : _quietFill, (_, _) => SetPrime(entry.id), small: true);
        primeButton.Enabled = !entry.isPrime;
        Button deleteButton = CreateButton("Delete", _dangerFill, (_, _) => DeleteEntry(entry), small: true);
        actions.Controls.Add(primeButton);
        actions.Controls.Add(deleteButton);
        card.Controls.Add(actions);

        return card;
    }

    void UpdateGalleryCardWidths()
    {
        int width = Math.Max(200, _galleryList.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 10);
        foreach (Control control in _galleryList.Controls)
            control.Width = width;
    }

    void SaveCurrentSample()
    {
        IReadOnlyList<IReadOnlyList<PointF>> strokes = CleanStrokes(_canvas.GetStrokeCopy());
        if (strokes.Count == 0)
        {
            _labelInstruction.Text = "Draw something first, then press Save.";
            return;
        }

        HandwritingSampleRecord sample = _canvas.BuildSample(
            _currentLetter,
            _writerIdTextBox.Text,
            _sessionIdTextBox.Text);
        sample.writerAgeBand = _ageBandComboBox.SelectedItem?.ToString() ?? "unknown";
        sample.handedness = _handednessComboBox.SelectedItem?.ToString() ?? "unknown";
        _store.Add(_currentLetter, strokes, sample);
        ReloadRecognizer();
        BuildLetterGrid();
        ClearRecorder();
        RefreshRecorderLabels();
        _labelInstruction.Text = $"Saved '{_currentLetter}'. The new templates.json is ready to send back later.";
    }

    void TestCurrentSample()
    {
        IReadOnlyList<IReadOnlyList<PointF>> strokes = CleanStrokes(_canvas.GetStrokeCopy());
        if (strokes.Count == 0)
        {
            _labelInstruction.Text = "Draw something first, then press Test.";
            return;
        }

        var points = new List<PortableRecognizer.Point>();
        for (int strokeIndex = 0; strokeIndex < strokes.Count; strokeIndex++)
        {
            foreach (PointF point in strokes[strokeIndex])
                points.Add(new PortableRecognizer.Point(point.X, point.Y, strokeIndex));
        }

        PortableRecognizer.RecognitionResult result = _recognizer.Recognize(points);
        string recognized = string.IsNullOrWhiteSpace(result.name) ? "Unknown" : result.name;
        string bestCandidate = string.IsNullOrWhiteSpace(result.bestCandidateName) ? recognized : result.bestCandidateName;
        string runnerUpText = result.runnerUpScore < float.MaxValue
            ? $" Runner-up: {result.runnerUpName} ({result.runnerUpScore:F1})"
            : string.Empty;

        _labelSampleCount.Text = $"Recognized: {recognized}";
        _labelInstruction.Text = result.isAmbiguous
            ? $"Recognizer result: ambiguous. Best match {bestCandidate} ({result.score:F1}).{runnerUpText}"
            : $"Recognizer result: {recognized} ({result.score:F1}).{runnerUpText}";
    }

    void SetPrime(string entryId)
    {
        _store.SetPrimeTemplate(entryId);
        ReloadRecognizer();
        BuildLetterGrid();
        BuildGalleryTabs();
    }

    void DeleteEntry(TemplateStore.TemplateEntry entry)
    {
        DialogResult result = MessageBox.Show(
            $"Delete this '{entry.letterKey}' template?",
            "Delete Template",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (result != DialogResult.Yes)
            return;

        _store.Delete(entry.id);
        ReloadRecognizer();
        BuildLetterGrid();
        BuildGalleryTabs();
    }

    void HandleBackFromRecorder()
    {
        if (_mode == RecorderMode.Test)
            OpenMainMenu();
        else
            OpenLetterPicker();
    }

    void ClearRecorder()
    {
        _canvas.ClearStrokes();
    }

    void RefreshRecorderLabels()
    {
        if (_mode == RecorderMode.Test)
        {
            _labelCurrentLetter.Text = "Recognition Test";
            _labelSampleCount.Text = $"Templates loaded: {_store.TotalCount} custom + built-ins";
            _labelInstruction.Text = "Draw any letter. Right-click lifts the pen for multi-stroke shapes. Press Test to see what the recognizer thinks.";
            _btnSaveSample.Visible = false;
            _btnTestSample.Visible = true;
            return;
        }

        int count = _store.GetEntries(_currentLetter).Count;
        _labelCurrentLetter.Text = $"Drawing: {_currentLetter}";
        _labelSampleCount.Text = $"{count} sample{(count == 1 ? string.Empty : "s")} saved";
        _labelInstruction.Text = "Left-click to draw. Right-click lifts the pen for multi-stroke letters. Press Save when the sample looks right.";
        _btnSaveSample.Visible = true;
        _btnTestSample.Visible = false;
    }

    void ReloadRecognizer()
    {
        _recognizer = new PortableRecognizer();
        _recognizer.LoadDefaultAlphabetTemplates();
        foreach ((string name, List<PortableRecognizer.Point> points) in _store.ToRecognizerTemplates())
            _recognizer.AddTemplate(name, points);
        UpdatePathLabel();
        UpdateWindowTitle();
    }

    void OpenDataFolder()
    {
        string target = File.Exists(_store.SavePath)
            ? _store.SavePath
            : (Path.GetDirectoryName(_store.SavePath) ?? AppContext.BaseDirectory);

        try
        {
            var info = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = File.Exists(target) ? $"/select,\"{target}\"" : $"\"{target}\"",
                UseShellExecute = true
            };
            Process.Start(info);
        }
        catch
        {
            MessageBox.Show(
                $"Could not open Explorer. The data file lives at:\n{_store.SavePath}",
                "Open Data Folder",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

    void UpdatePathLabel()
    {
        _labelPath.Text = $"JSON file: {_store.SavePath}";
        _labelPath.ForeColor = _mutedTextColor;
    }

    void UpdateWindowTitle()
    {
        Text = $"Template Recorder Standalone  |  {_store.TotalCount} custom templates";
    }

    static IReadOnlyList<IReadOnlyList<PointF>> CleanStrokes(IReadOnlyList<IReadOnlyList<PointF>> strokes)
    {
        return strokes.Where(stroke => stroke.Count >= 2).ToList();
    }

    Panel CreateScenePanel()
    {
        return new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = _panelBackground,
            Visible = false
        };
    }

    TableLayoutPanel CreateVerticalLayout()
    {
        return new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 0,
            AutoSize = false
        };
    }

    Label CreateTitleLabel(string text, float size)
    {
        return new Label
        {
            AutoSize = true,
            Text = text,
            ForeColor = _textColor,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI Semibold", size, FontStyle.Bold, GraphicsUnit.Point),
            Margin = new Padding(0, 0, 0, 8)
        };
    }

    Label CreateBodyLabel(string text)
    {
        return new Label
        {
            AutoSize = true,
            Text = text,
            ForeColor = _mutedTextColor,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Point),
            MaximumSize = new Size(860, 0),
            Margin = new Padding(0, 0, 0, 4)
        };
    }

    Button CreateButton(string text, Color fill, EventHandler onClick, bool small = false, bool compact = false)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = false,
            Size = compact ? new Size(116, 76) : small ? new Size(132, 38) : new Size(240, 52),
            BackColor = fill,
            ForeColor = _textColor,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0, 0, 10, 10),
            Font = new Font("Segoe UI Semibold", compact ? 10f : 11f, FontStyle.Bold, GraphicsUnit.Point),
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(110, 96, 86);
        button.FlatAppearance.BorderSize = 1;
        button.Click += onClick;
        return button;
    }
}
