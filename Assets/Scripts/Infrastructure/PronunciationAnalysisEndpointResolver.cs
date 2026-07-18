public readonly struct PronunciationAnalysisEndpointOptions
{
    public readonly WavLmEndpointMode Mode;
    public readonly string LocalBaseUrl;
    public readonly string CloudBaseUrl;
    public readonly string CustomBaseUrl;
    public readonly bool PreferLocalInAutoMode;

    public PronunciationAnalysisEndpointOptions(
        WavLmEndpointMode mode,
        string localBaseUrl,
        string cloudBaseUrl,
        string customBaseUrl,
        bool preferLocalInAutoMode)
    {
        Mode = mode;
        LocalBaseUrl = localBaseUrl ?? "";
        CloudBaseUrl = cloudBaseUrl ?? "";
        CustomBaseUrl = customBaseUrl ?? "";
        PreferLocalInAutoMode = preferLocalInAutoMode;
    }
}

public interface IPronunciationAnalysisEndpointResolver
{
    string Resolve(PronunciationAnalysisEndpointOptions options);
}

public sealed class PronunciationAnalysisEndpointResolver : IPronunciationAnalysisEndpointResolver
{
    public string Resolve(PronunciationAnalysisEndpointOptions options)
    {
        string localUrl = Normalize(options.LocalBaseUrl);
        string cloudUrl = Normalize(options.CloudBaseUrl);
        string customUrl = Normalize(options.CustomBaseUrl);

        switch (options.Mode)
        {
            case WavLmEndpointMode.LocalDemo:
                return FirstConfigured(localUrl, customUrl);
            case WavLmEndpointMode.CloudProduction:
                return FirstConfigured(cloudUrl, customUrl);
            case WavLmEndpointMode.Custom:
                return customUrl;
            case WavLmEndpointMode.Auto:
            default:
                return options.PreferLocalInAutoMode
                    ? FirstConfigured(localUrl, customUrl)
                    : FirstConfigured(cloudUrl, customUrl);
        }
    }

    public static string Normalize(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "" : value.Trim().TrimEnd('/');
    }

    static string FirstConfigured(string preferred, string fallback)
    {
        return string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;
    }
}
