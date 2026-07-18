using System;
using System.Collections.Generic;
using UnityEngine;

public readonly struct ColorWordDefinition
{
    public readonly string Name;
    public readonly Color Color;

    public ColorWordDefinition(string name, Color color)
    {
        Name = name;
        Color = color;
    }
}

public static class ColorWordUtility
{
    static readonly ColorWordDefinition[] Colors =
    {
        new ColorWordDefinition("red", new Color(0.95f, 0.18f, 0.14f, 1f)),
        new ColorWordDefinition("blue", new Color(0.18f, 0.46f, 1f, 1f)),
        new ColorWordDefinition("yellow", new Color(1f, 0.86f, 0.18f, 1f)),
        new ColorWordDefinition("green", new Color(0.16f, 0.78f, 0.34f, 1f)),
        new ColorWordDefinition("orange", new Color(1f, 0.48f, 0.12f, 1f)),
        new ColorWordDefinition("purple", new Color(0.58f, 0.28f, 0.95f, 1f)),
    };

    static readonly Dictionary<string, string> CanonicalByWord = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["red"] = "red",
        ["read"] = "red",
        ["blue"] = "blue",
        ["blew"] = "blue",
        ["yellow"] = "yellow",
        ["green"] = "green",
        ["gren"] = "green",
        ["orange"] = "orange",
        ["purple"] = "purple",
    };

    public static IReadOnlyList<ColorWordDefinition> All => Colors;

    public static ColorWordDefinition RandomColor()
    {
        return Colors[UnityEngine.Random.Range(0, Colors.Length)];
    }

    public static bool TryGet(string value, out ColorWordDefinition definition)
    {
        string canonical = Normalize(value);
        foreach (ColorWordDefinition color in Colors)
        {
            if (string.Equals(color.Name, canonical, StringComparison.OrdinalIgnoreCase))
            {
                definition = color;
                return true;
            }
        }

        definition = default;
        return false;
    }

    public static string Normalize(string value)
    {
        string normalized = VoiceUnlockRecognizer.NormalizeKeyword(value);
        return CanonicalByWord.TryGetValue(normalized, out string canonical) ? canonical : normalized;
    }

    public static List<string> BuildKeywords()
    {
        var keywords = new List<string>();
        foreach (ColorWordDefinition color in Colors)
            keywords.Add(color.Name);
        return keywords;
    }

    public static Dictionary<string, string> BuildAliases()
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        aliases["read"] = "red";
        aliases["blew"] = "blue";
        aliases["gren"] = "green";
        return aliases;
    }
}

