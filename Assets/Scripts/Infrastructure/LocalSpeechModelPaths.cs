using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public enum LocalSpeechModelKind
{
    VoskRecognition,
    ZipaPronunciation,
    CharsiuPronunciation,
}

[Serializable]
public sealed class LocalSpeechModelConfiguration
{
    public int schemaVersion = 1;
    public string externalModelRoot = "ContentSource/SpeechModels";
    public string editorModelRoot = "Assets/MLModels";
    public string voskDirectoryName = "VoskModel";
    public string zipaDirectoryName = "Zipa";
    public string charsiuDirectoryName = "charsiu-en-w2v2-fc-10ms";
    public string charsiuFallbackDirectoryName = "charsiu-en-w2v2-tiny-fc-10ms";

    public static LocalSpeechModelConfiguration LoadFromResources()
    {
        TextAsset asset = Resources.Load<TextAsset>("LocalSpeechModelConfiguration");
        if (asset == null || string.IsNullOrWhiteSpace(asset.text))
            return new LocalSpeechModelConfiguration();

        try
        {
            LocalSpeechModelConfiguration configuration =
                JsonUtility.FromJson<LocalSpeechModelConfiguration>(asset.text);
            return configuration ?? new LocalSpeechModelConfiguration();
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[SpeechModels] Could not read LocalSpeechModelConfiguration.json; defaults will be used. {exception.Message}");
            return new LocalSpeechModelConfiguration();
        }
    }
}

public interface ILocalSpeechModelPathResolver
{
    IReadOnlyList<string> GetCandidatePaths(LocalSpeechModelKind modelKind);
    string ResolvePath(LocalSpeechModelKind modelKind, bool requireExistingDirectory = true);
}

/// <summary>
/// Resolves optional, heavyweight speech models without coupling recognition
/// providers to a particular folder layout. Deployments can set
/// THE_SCRIPT_MODEL_ROOT or edit the Resources JSON file without recompiling
/// gameplay code.
/// </summary>
public sealed class ConfigurableLocalSpeechModelPathResolver : ILocalSpeechModelPathResolver
{
    public const string ModelRootEnvironmentVariable = "THE_SCRIPT_MODEL_ROOT";

    readonly LocalSpeechModelConfiguration configuration;
    readonly string streamingAssetsRoot;
    readonly string projectRoot;
    readonly string environmentModelRoot;
    readonly Func<string, bool> directoryExists;

    public ConfigurableLocalSpeechModelPathResolver()
        : this(
            LocalSpeechModelConfiguration.LoadFromResources(),
            Application.streamingAssetsPath,
            Directory.GetParent(Application.dataPath)?.FullName ?? "",
            Environment.GetEnvironmentVariable(ModelRootEnvironmentVariable),
            Directory.Exists)
    {
    }

    public ConfigurableLocalSpeechModelPathResolver(
        LocalSpeechModelConfiguration configuration,
        string streamingAssetsRoot,
        string projectRoot,
        string environmentModelRoot = null,
        Func<string, bool> directoryExists = null)
    {
        this.configuration = configuration ?? new LocalSpeechModelConfiguration();
        this.streamingAssetsRoot = streamingAssetsRoot ?? "";
        this.projectRoot = projectRoot ?? "";
        this.environmentModelRoot = environmentModelRoot ?? "";
        this.directoryExists = directoryExists ?? Directory.Exists;
    }

    public IReadOnlyList<string> GetCandidatePaths(LocalSpeechModelKind modelKind)
    {
        var candidates = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string primaryDirectory = ResolveDirectoryName(modelKind, false);
        string fallbackDirectory = ResolveDirectoryName(modelKind, true);

        AddCandidate(candidates, seen, environmentModelRoot, primaryDirectory);
        AddCandidate(candidates, seen, streamingAssetsRoot, primaryDirectory);

        string editorRoot = CombineProjectRelative(configuration.editorModelRoot, "Assets/MLModels");
        string externalRoot = CombineProjectRelative(configuration.externalModelRoot, "ContentSource/SpeechModels");
        if (modelKind == LocalSpeechModelKind.CharsiuPronunciation)
        {
            AddCandidate(candidates, seen, editorRoot, primaryDirectory);
            AddCandidate(candidates, seen, editorRoot, fallbackDirectory);
        }

        AddCandidate(candidates, seen, externalRoot, primaryDirectory);
        AddCandidate(candidates, seen, externalRoot, fallbackDirectory);
        return candidates;
    }

    public string ResolvePath(LocalSpeechModelKind modelKind, bool requireExistingDirectory = true)
    {
        IReadOnlyList<string> candidates = GetCandidatePaths(modelKind);
        for (int i = 0; i < candidates.Count; i++)
        {
            if (directoryExists(candidates[i]))
                return candidates[i];
        }

        return !requireExistingDirectory && candidates.Count > 0 ? candidates[0] : "";
    }

    string ResolveDirectoryName(LocalSpeechModelKind modelKind, bool fallback)
    {
        switch (modelKind)
        {
            case LocalSpeechModelKind.VoskRecognition:
                return SanitizeDirectoryName(configuration.voskDirectoryName, "VoskModel");
            case LocalSpeechModelKind.ZipaPronunciation:
                return SanitizeDirectoryName(configuration.zipaDirectoryName, "Zipa");
            case LocalSpeechModelKind.CharsiuPronunciation:
                return fallback
                    ? SanitizeDirectoryName(configuration.charsiuFallbackDirectoryName, "charsiu-en-w2v2-tiny-fc-10ms")
                    : SanitizeDirectoryName(configuration.charsiuDirectoryName, "charsiu-en-w2v2-fc-10ms");
            default:
                return "";
        }
    }

    string CombineProjectRelative(string relativePath, string safeDefault)
    {
        string normalized = SanitizeRelativePath(relativePath, safeDefault);
        return string.IsNullOrWhiteSpace(projectRoot) ? normalized : Path.Combine(projectRoot, normalized);
    }

    static void AddCandidate(List<string> candidates, HashSet<string> seen, string root, string directoryName)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(directoryName))
            return;

        string candidate;
        try
        {
            candidate = root.Contains("://")
                ? root.TrimEnd('/', '\\') + "/" + directoryName
                : Path.GetFullPath(Path.Combine(root, directoryName));
        }
        catch
        {
            return;
        }

        if (seen.Add(candidate))
            candidates.Add(candidate);
    }

    public static string SanitizeDirectoryName(string value, string safeDefault)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value))
            return safeDefault;

        string trimmed = value.Trim().Trim('/', '\\');
        if (trimmed.Contains("/") || trimmed.Contains("\\") || trimmed == "." || trimmed == "..")
            return safeDefault;
        return trimmed;
    }

    public static string SanitizeRelativePath(string value, string safeDefault)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value))
            return safeDefault;

        string normalized = value.Trim().Replace('\\', '/').Trim('/');
        string[] segments = normalized.Split('/');
        for (int i = 0; i < segments.Length; i++)
        {
            if (segments[i] == "." || segments[i] == ".." || string.IsNullOrWhiteSpace(segments[i]))
                return safeDefault;
        }

        return normalized.Replace('/', Path.DirectorySeparatorChar);
    }
}
