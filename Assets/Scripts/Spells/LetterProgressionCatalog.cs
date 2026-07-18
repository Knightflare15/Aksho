using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "LetterProgressionCatalog", menuName = "The Script/Content/Letter Progression Catalog")]
public class LetterProgressionCatalog : ScriptableObject
{
    [System.Serializable]
    public class LetterEntry
    {
        public string letter = "L";
        public List<string> speechAliases = new List<string>();
        public List<string> curatedWords = new List<string>();
        public AudioClip pronunciationClip;
        [Min(1)] public int formationsToUnlockNext = 3;
        [Min(1)] public int confidentFormationsToUnlockNext = 2;
        [Min(1)] public int wordMasteryPointsPerUnlock = 3;
    }

    public List<LetterEntry> letters = new List<LetterEntry>();

    public IReadOnlyList<LetterEntry> GetEntries()
    {
        if (letters == null || letters.Count == 0) letters = BuildDefaults();
        return letters;
    }

    public static List<LetterEntry> BuildDefaults()
    {
        string[] order = { "L", "F", "E", "H", "T", "I", "U", "C", "O", "Q", "G", "S", "J", "D", "P", "B", "R", "K", "A", "M", "N", "V", "W", "X", "Y", "Z" };
        var spoken = new Dictionary<string, string[]>
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
        var result = new List<LetterEntry>();
        foreach (string value in order)
            result.Add(new LetterEntry { letter = value, speechAliases = new List<string>(spoken[value]) });
        return result;
    }
}
