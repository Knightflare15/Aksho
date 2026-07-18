using System;

public static class BuddyRealtimeFunctionUrl
{
    const string DefaultVoiceRegion = "asia-south1";

    public static string Resolve(CurriculumSessionManager curriculum)
    {
        if (curriculum == null)
            return "";

        string configured = TrimSlash(curriculum.firebaseBuddyVoiceFunctionsBaseUrl);
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        string projectId = (curriculum.firebaseProjectId ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(projectId))
            return $"https://{DefaultVoiceRegion}-{projectId}.cloudfunctions.net";

        return DeriveFromRegularFunctionsUrl(curriculum.firebaseFunctionsBaseUrl);
    }

    static string DeriveFromRegularFunctionsUrl(string regularUrl)
    {
        string trimmed = TrimSlash(regularUrl);
        if (string.IsNullOrWhiteSpace(trimmed))
            return "";

        try
        {
            var uri = new Uri(trimmed);
            const string suffix = ".cloudfunctions.net";
            if (!uri.Host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return trimmed;

            string prefix = uri.Host.Substring(0, uri.Host.Length - suffix.Length);
            int separator = prefix.IndexOf('-');
            if (separator < 0 || separator + 1 >= prefix.Length)
                return trimmed;

            string projectId = prefix.Substring(separator + 1);
            return $"https://{DefaultVoiceRegion}-{projectId}.cloudfunctions.net";
        }
        catch
        {
            return trimmed;
        }
    }

    static string TrimSlash(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "" : value.Trim().TrimEnd('/');
    }
}
