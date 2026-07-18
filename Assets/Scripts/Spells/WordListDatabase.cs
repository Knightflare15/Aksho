using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ScriptableObject that holds tiered word lists.
/// Create via:  Assets → Create → SpellGame → Word List Database
///
/// Teachers / designers edit this in the Inspector — no code changes needed
/// to add or remove words from any difficulty tier.
/// </summary>
[CreateAssetMenu(fileName = "WordListDatabase",
                 menuName  = "SpellGame/Word List Database",
                 order     = 1)]
public class WordListDatabase : ScriptableObject
{
    [System.Serializable]
    public class WordTier
    {
        public string tierName = "Easy";

        [Tooltip("All words should be UPPERCASE. " +
                 "Shorter, phonetically simple words work best for early tiers.")]
        public List<string> words = new List<string>();
    }

    [SerializeField]
    public List<WordTier> tiers = new List<WordTier>();

    // ── Factory defaults ───────────────────────────────────────────────────
    // Called in OnEnable so the asset ships with sensible starter content.

    void OnEnable()
    {
        if (tiers != null && tiers.Count > 0) return; // already populated

        tiers = new List<WordTier>
        {
            new WordTier
            {
                tierName = "Easy",
                words    = new List<string>
                {
                    "CAT","DOG","SUN","BUS","CUP",
                    "HAT","MAP","PIG","JAM","FAN",
                    "BED","HEN","NET","LEG","WEB",
                    "DIG","HIP","BIT","FIG","LIP",
                    "COT","BOX","HOG","MOP","TON",
                    "BUG","FUN","GUM","NUT","RUG",
                    "BAD","CAN","DAD","DOT","FIN",
                    "GET","HIT","HOT","KID","MAD",
                    "MAN","MEN","PET","POP","PUP",
                    "RED","SIT","TAP","TEN","WET"
                }
            },
            new WordTier
            {
                tierName = "Medium",
                words    = new List<string>
                {
                    "FROG","CLAP","DRUM","FLAG","GRIP",
                    "LAMP","MELT","PLAN","SLIP","TRAP",
                    "BLOB","CROP","DROP","FLAT","GRIN",
                    "PLUG","SNIP","STEM","TRIP","WHIP",
                    "BELT","CAMP","DUSK","FILM","GUST",
                    "HUNT","JUMP","KELP","LIMP","MINT"
                }
            },
            new WordTier
            {
                tierName = "Hard",
                words    = new List<string>
                {
                    "BLANK","CLAMP","DRIFT","FLINT","GRASP",
                    "PLANK","PRIMP","SCANT","SKIMP","STOMP",
                    "BLEND","CLEFT","DWELT","GRUMP","PLUMP",
                    "SCALP","SHRUB","SLUMP","SMELT","TRAMP",
                    "CRISP","FLOSS","GLINT","KNELT","PRISM",
                    "SQUAB","STUMP","SWIFT","TWIRL","WHISK"
                }
            }
        };
    }

    // ── Runtime helpers ────────────────────────────────────────────────────

    /// <summary>Returns a random word from the given tier index (0=Easy, 1=Medium, 2=Hard).</summary>
    public string GetRandomWord(int tierIndex)
    {
        if (tiers == null || tiers.Count == 0) return "CAT";
        tierIndex = Mathf.Clamp(tierIndex, 0, tiers.Count - 1);
        var words = tiers[tierIndex].words;
        if (words == null || words.Count == 0) return "CAT";
        return words[Random.Range(0, words.Count)].ToUpper();
    }

    public int TierCount => tiers?.Count ?? 0;

    public string TierName(int index)
    {
        if (tiers == null || index < 0 || index >= tiers.Count) return "Unknown";
        return tiers[index].tierName;
    }
}
