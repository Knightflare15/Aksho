using System.Text;

namespace VoskVoiceTester;

internal sealed record VoiceEntry(string Canonical, string Spoken, string Kind);

internal static class GameVoiceVocabulary
{
    public static IReadOnlyList<VoiceEntry> Build()
    {
        var entries = new List<VoiceEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string word in SpellWords())
            Add(entries, seen, word, word, "Spell word");

        foreach (var pair in LetterAliases())
        {
            Add(entries, seen, pair.Key, pair.Key, "Letter");
            foreach (string alias in pair.Value)
                Add(entries, seen, pair.Key, alias, "Letter alias");
        }

        return entries
            .OrderBy(entry => entry.Kind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Canonical, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Spoken, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string BuildGrammarJson(IEnumerable<VoiceEntry> entries)
    {
        var spokenValues = entries
            .Select(entry => NormalizeForGrammar(entry.Spoken))
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var builder = new StringBuilder("[");
        for (int i = 0; i < spokenValues.Count; i++)
        {
            if (i > 0)
                builder.Append(',');
            AppendJsonString(builder, spokenValues[i].ToLowerInvariant());
        }

        if (spokenValues.Count > 0)
            builder.Append(',');
        AppendJsonString(builder, "[unk]");
        builder.Append(']');
        return builder.ToString();
    }

    public static string NormalizeRecognized(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var builder = new StringBuilder(value.Length);
        bool pendingSpace = false;
        foreach (char c in value.Trim())
        {
            if (char.IsLetterOrDigit(c))
            {
                if (pendingSpace && builder.Length > 0)
                    builder.Append(' ');
                builder.Append(char.ToUpperInvariant(c));
                pendingSpace = false;
            }
            else if (char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSymbol(c))
            {
                pendingSpace = builder.Length > 0;
            }
        }

        return builder.ToString();
    }

    private static void Add(List<VoiceEntry> entries, HashSet<string> seen, string canonical, string spoken, string kind)
    {
        canonical = NormalizeRecognized(canonical);
        spoken = NormalizeRecognized(spoken);
        if (canonical.Length == 0 || spoken.Length == 0)
            return;

        string key = $"{canonical}\n{spoken}\n{kind}";
        if (seen.Add(key))
            entries.Add(new VoiceEntry(canonical, spoken, kind));
    }

    private static string NormalizeForGrammar(string value)
    {
        return NormalizeRecognized(value).ToLowerInvariant();
    }

    private static void AppendJsonString(StringBuilder builder, string value)
    {
        builder.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }
        builder.Append('"');
    }

    private static IReadOnlyList<string> SpellWords()
    {
        return new[]
        {
            "CAT", "HEN", "PIG", "HOP", "BUG", "SHIP", "THIN", "CRAB", "BARK", "STONE",
            "LEG", "FAN", "EGG", "TOP", "INK", "UP", "OWL", "QUEEN", "GUM", "SUN",
            "JAM", "DOG", "RUG", "KITE", "ANT", "MAP", "NET", "VAN", "WEB", "BOX",
            "YAK", "ZIP"
        };
    }

    private static IReadOnlyDictionary<string, string[]> LetterAliases()
    {
        return new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = new[] { "AY", "ALPHA" },
            ["B"] = new[] { "BEE", "BRAVO" },
            ["C"] = new[] { "SEE", "SEA", "CHARLIE" },
            ["D"] = new[] { "DEE", "DELTA" },
            ["E"] = new[] { "EE", "ECHO" },
            ["F"] = new[] { "EFF", "FOXTROT" },
            ["G"] = new[] { "GEE", "GOLF" },
            ["H"] = new[] { "AITCH", "HOTEL" },
            ["I"] = new[] { "EYE", "INDIA" },
            ["J"] = new[] { "JAY", "JULIET" },
            ["K"] = new[] { "KAY", "KILO" },
            ["L"] = new[] { "EL", "LIMA" },
            ["M"] = new[] { "EM", "MIKE" },
            ["N"] = new[] { "EN", "NOVEMBER" },
            ["O"] = new[] { "OH", "OSCAR" },
            ["P"] = new[] { "PEE", "PAPA" },
            ["Q"] = new[] { "CUE", "QUEUE", "QUEBEC" },
            ["R"] = new[] { "ARE", "ROMEO" },
            ["S"] = new[] { "ESS", "SIERRA" },
            ["T"] = new[] { "TEE", "TEA", "TANGO" },
            ["U"] = new[] { "YOU", "UNIFORM" },
            ["V"] = new[] { "VEE", "VICTOR" },
            ["W"] = new[] { "DOUBLE U", "WHISKEY" },
            ["X"] = new[] { "EX", "XRAY", "X RAY" },
            ["Y"] = new[] { "WHY", "YANKEE" },
            ["Z"] = new[] { "ZEE", "ZED", "ZULU" },
        };
    }
}
