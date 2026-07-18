using System.Diagnostics;

namespace Wav2Vec2PhoneticTester;

internal static class Program
{
    private static int Main(string[] args)
    {
        string baseDirectory = AppContext.BaseDirectory;
        string backend = ResolveBackend(args);
        string scriptPath = ResolveScriptPath(baseDirectory, backend);
        if (!File.Exists(scriptPath))
        {
            Console.Error.WriteLine($"Could not find {Path.GetFileName(scriptPath)} near {baseDirectory}");
            return 1;
        }

        string repoRoot = ResolveRepoRoot(scriptPath);
        string python = ResolvePythonExecutable(baseDirectory);
        if (args.Length == 0)
        {
            Console.WriteLine("Allosaurus Phonetic Tester");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  Wav2Vec2PhoneticTester.exe --word CAT --live");
            Console.WriteLine("  Wav2Vec2PhoneticTester.exe --word CAT --seconds 3");
            Console.WriteLine("  Wav2Vec2PhoneticTester.exe --word SHIP --wav C:\\path\\to\\ship.wav");
            Console.WriteLine("  Wav2Vec2PhoneticTester.exe --backend wavlm --word CAT --wav C:\\path\\to\\cat.wav");
            Console.WriteLine("  Wav2Vec2PhoneticTester.exe --backend zipa --word CAT --wav C:\\path\\to\\cat.wav");
            Console.WriteLine();
            Console.WriteLine("This executable launches Allosaurus eng2102 with lang=eng and emit=0.75.");
            Console.WriteLine("Use --backend wavlm, --backend wavlm-base-plus-fr-it, --backend wav2vec2, --backend hubert, or --backend zipa with --wav to try phone models.");
            Console.WriteLine("Set PYTHON to a Python executable with allosaurus and sounddevice installed.");

            if (Console.IsInputRedirected)
                return 0;

            Console.WriteLine();
            Console.WriteLine("Live mode listens for one utterance, analyzes it, then returns here.");
            Console.WriteLine("Audio is analyzed through a temporary WAV and is not saved by default.");

            while (true)
            {
                Console.WriteLine();
                Console.Write("Next word to test, e.g. CAT (blank to quit): ");
                string? word = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(word))
                    return 0;

                Console.WriteLine();
                int exitCode = RunPhonemeTester(
                    python,
                    scriptPath,
                    repoRoot,
                    new[]
                    {
                        "--word", word.Trim(),
                        "--live"
                    });
                if (exitCode != 0)
                    Console.WriteLine($"Run ended with exit code {exitCode}. You can try another word.");
            }
        }

        return RunPhonemeTester(python, scriptPath, repoRoot, NormalizeBackendArgs(args, backend));
    }

    private static int RunPhonemeTester(string python, string scriptPath, string repoRoot, string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = python,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = false,
            WorkingDirectory = repoRoot,
        };
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";

        startInfo.ArgumentList.Add(scriptPath);
        foreach (string arg in args)
            startInfo.ArgumentList.Add(arg);

        try
        {
            using Process? process = Process.Start(startInfo);
            if (process == null)
            {
                Console.Error.WriteLine("Could not start the Python phoneme process.");
                return 1;
            }

            process.OutputDataReceived += (_, eventArgs) =>
            {
                if (eventArgs.Data != null)
                    Console.Out.WriteLine(eventArgs.Data);
            };
            process.ErrorDataReceived += (_, eventArgs) =>
            {
                if (eventArgs.Data != null)
                    Console.Error.WriteLine(eventArgs.Data);
            };

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to launch phoneme tester: {ex.Message}");
            Console.Error.WriteLine("Set PYTHON to a Python executable with allosaurus installed, then retry.");
            return 1;
        }
    }

    private static string ResolveBackend(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--backend", StringComparison.OrdinalIgnoreCase))
                return args[i + 1].Trim().ToLowerInvariant();
        }

        return "allosaurus";
    }

    private static string[] NormalizeBackendArgs(string[] args, string backend)
    {
        if (!string.Equals(backend, "allosaurus", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(backend, "zipa", StringComparison.OrdinalIgnoreCase))
            return args;

        var normalized = new List<string>(args.Length);
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--backend", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                i++;
                continue;
            }

            normalized.Add(args[i]);
        }

        return normalized.ToArray();
    }

    private static string ResolveScriptPath(string baseDirectory, string backend)
    {
        string scriptName = backend switch
        {
            "wavlm" or "wavlm-base-plus-fr-it" or "wav2vec2" or "hubert" => "hf_ctc_tester.py",
            "zipa" => "zipa_onnx_tester.py",
            "allosaurus" => "allosaurus_tester.py",
            _ => "allosaurus_tester.py",
        };

        string bundled = Path.Combine(baseDirectory, scriptName);
        if (File.Exists(bundled))
            return bundled;

        string source = Path.Combine(baseDirectory, "..", "..", "..", scriptName);
        return Path.GetFullPath(source);
    }

    private static string ResolveRepoRoot(string scriptPath)
    {
        DirectoryInfo? directory = new FileInfo(scriptPath).Directory;
        for (int i = 0; i < 6 && directory != null; i++)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "Assets")) &&
                Directory.Exists(Path.Combine(directory.FullName, "Tools")))
                return directory.FullName;

            directory = directory.Parent;
        }

        return AppContext.BaseDirectory;
    }

    private static string ResolvePythonExecutable(string baseDirectory)
    {
        string? configured = Environment.GetEnvironmentVariable("PYTHON");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        foreach (string candidate in ResolveLocalPythonCandidates(baseDirectory))
            if (File.Exists(candidate))
                return candidate;

        string userProfile = Environment.GetEnvironmentVariable("USERPROFILE") ??
                             Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string bundled = Path.Combine(
            userProfile,
            ".cache",
            "codex-runtimes",
            "codex-primary-runtime",
            "dependencies",
            "python",
            "python.exe");
        if (File.Exists(bundled))
            return bundled;

        return "python";
    }

    private static IEnumerable<string> ResolveLocalPythonCandidates(string baseDirectory)
    {
        DirectoryInfo? directory = new DirectoryInfo(baseDirectory);
        for (int i = 0; i < 4 && directory != null; i++)
        {
            yield return Path.Combine(directory.FullName, ".venv", "Scripts", "python.exe");
            directory = directory.Parent;
        }
    }
}
