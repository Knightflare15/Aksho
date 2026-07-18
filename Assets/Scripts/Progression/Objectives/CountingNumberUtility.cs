using System;
using System.Collections.Generic;

public static class CountingNumberUtility
{
    static readonly string[] Words =
    {
        "",
        "one",
        "two",
        "three",
        "four",
        "five",
        "six",
        "seven",
        "eight",
        "nine",
        "ten",
    };

    static readonly Dictionary<string, int> NumberByWord = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["1"] = 1,
        ["one"] = 1,
        ["won"] = 1,
        ["2"] = 2,
        ["two"] = 2,
        ["too"] = 2,
        ["to"] = 2,
        ["3"] = 3,
        ["three"] = 3,
        ["tree"] = 3,
        ["4"] = 4,
        ["four"] = 4,
        ["for"] = 4,
        ["5"] = 5,
        ["five"] = 5,
        ["6"] = 6,
        ["six"] = 6,
        ["7"] = 7,
        ["seven"] = 7,
        ["8"] = 8,
        ["eight"] = 8,
        ["ate"] = 8,
        ["9"] = 9,
        ["nine"] = 9,
        ["10"] = 10,
        ["ten"] = 10,
    };

    public static string ToWord(int value)
    {
        value = UnityEngine.Mathf.Clamp(value, 1, 10);
        return Words[value];
    }

    public static bool TryParse(string value, out int number)
    {
        string normalized = VoiceUnlockRecognizer.NormalizeKeyword(value);
        if (NumberByWord.TryGetValue(normalized, out number))
            return true;

        number = 0;
        return false;
    }

    public static List<string> BuildKeywords()
    {
        var keywords = new List<string>();
        for (int i = 1; i <= 10; i++)
            keywords.Add(ToWord(i));
        return keywords;
    }

    public static Dictionary<string, string> BuildAliases()
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i <= 10; i++)
            aliases[i.ToString()] = ToWord(i);
        aliases["too"] = "two";
        aliases["to"] = "two";
        aliases["won"] = "one";
        aliases["tree"] = "three";
        aliases["for"] = "four";
        aliases["ate"] = "eight";
        return aliases;
    }
}
