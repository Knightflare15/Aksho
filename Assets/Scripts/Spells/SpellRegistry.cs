using System.Collections.Generic;
using UnityEngine;

public class SpellRegistry : MonoBehaviour
{
    const string DefaultSpellCatalogResourcePath = "SpellCatalog_Main";
    const string DefaultLetterProgressionCatalogResourcePath = "LetterProgressionCatalog_Main";

    [Header("Spells")]
    public SpellCatalog spellCatalog;
    public LetterProgressionCatalog letterProgressionCatalog;
    public List<SpellDefinition> spellDefinitions = new List<SpellDefinition>();

    private readonly Dictionary<string, SpellDefinition> spellLookup = new Dictionary<string, SpellDefinition>();
    private readonly List<string> registeredWords = new List<string>();

    public IReadOnlyList<SpellDefinition> SpellDefinitions => GetConfiguredSpellDefinitions();
    public IReadOnlyList<string> RegisteredWords => registeredWords;
    public LetterProgressionCatalog LetterCatalog => letterProgressionCatalog;
    public PlayerLearningProfile LearningProfile { get; private set; }

    void Awake()
    {
        EnsureDefaultSpells();
        LearningProfile = GetComponent<PlayerLearningProfile>() ?? FindAnyObjectByType<PlayerLearningProfile>();
        RebuildLookup();
    }

    void OnValidate()
    {
        EnsureCatalogReferences();
        RebuildLookup();
    }

    public bool TryGetSpell(string word, out SpellDefinition definition)
    {
        return spellLookup.TryGetValue(NormalizeWord(word), out definition);
    }

    public bool HasSpell(string word)
    {
        return spellLookup.ContainsKey(NormalizeWord(word));
    }

    public string ResolveLessonWord(string preferredWord, int maxUnlockLevel = int.MaxValue)
    {
        CurriculumSessionManager curriculum = CurriculumSessionManager.Instance;
        string preferred = NormalizeWord(preferredWord);
        if (!string.IsNullOrEmpty(preferred) &&
            spellLookup.TryGetValue(preferred, out SpellDefinition preferredDefinition) &&
            preferredDefinition.unlockLevel <= maxUnlockLevel &&
            (curriculum == null || !curriculum.IsSchoolModeActive || curriculum.IsWordAllowed(preferred)))
            return preferred;

        foreach (SpellDefinition definition in GetConfiguredSpellDefinitions())
        {
            if (definition == null || definition.unlockLevel > maxUnlockLevel)
                continue;

            string candidate = NormalizeWord(definition.word);
            if (!string.IsNullOrEmpty(candidate) &&
                (curriculum == null || !curriculum.IsSchoolModeActive || curriculum.IsWordAllowed(candidate)))
                return candidate;
        }

        return preferred;
    }

    public List<string> GetUnlockedWords(int maxUnlockLevel)
    {
        ResolveLearningProfile();
        CurriculumSessionManager curriculum = CurriculumSessionManager.Instance;
        var result = new List<string>();
        foreach (SpellDefinition definition in GetConfiguredSpellDefinitions())
        {
            if (definition == null || definition.unlockLevel > maxUnlockLevel)
                continue;

            string word = NormalizeWord(definition.word);
            if (curriculum != null && curriculum.IsSchoolModeActive && !curriculum.IsWordAllowed(word))
                continue;
            if (!string.IsNullOrEmpty(word) &&
                (LearningProfile == null || LearningProfile.IsWordUnlocked(word)))
                result.Add(word);
        }

        return result;
    }

    public List<string> GetWordsForLetter(char letter, bool unlockedOnly = true)
    {
        ResolveLearningProfile();
        CurriculumSessionManager curriculum = CurriculumSessionManager.Instance;
        char normalized = char.ToUpperInvariant(letter);
        var result = new List<string>();
        if (curriculum != null && curriculum.IsSchoolModeActive && !curriculum.IsLetterAllowed(normalized))
            return result;

        LetterProgressionCatalog.LetterEntry progressionEntry = GetLetterEntry(normalized);
        if (progressionEntry != null && progressionEntry.curatedWords != null && progressionEntry.curatedWords.Count > 0)
        {
            foreach (string value in progressionEntry.curatedWords)
            {
                string word = NormalizeWord(value);
                if (string.IsNullOrEmpty(word) || word[0] != normalized || !HasSpell(word))
                    continue;
                if (curriculum != null && curriculum.IsSchoolModeActive && !curriculum.IsWordAllowed(word))
                    continue;
                if (!unlockedOnly || LearningProfile == null || LearningProfile.IsWordUnlocked(word))
                    result.Add(word);
            }
            return result;
        }

        foreach (SpellDefinition definition in GetConfiguredSpellDefinitions())
        {
            string word = definition != null ? NormalizeWord(definition.word) : "";
            if (string.IsNullOrEmpty(word) || word[0] != normalized)
                continue;
            if (curriculum != null && curriculum.IsSchoolModeActive && !curriculum.IsWordAllowed(word))
                continue;
            if (unlockedOnly && LearningProfile != null && !LearningProfile.IsWordUnlocked(word))
                continue;
            result.Add(word);
        }
        return result;
    }

    public int GetWordMasteryPointsPerUnlock(char letter)
    {
        LetterProgressionCatalog.LetterEntry entry = GetLetterEntry(letter);
        return entry != null ? Mathf.Max(1, entry.wordMasteryPointsPerUnlock) : 3;
    }

    public List<string> GetUnlockedLetters()
    {
        ResolveLearningProfile();
        CurriculumSessionManager curriculum = CurriculumSessionManager.Instance;
        var result = new List<string>();
        IReadOnlyList<LetterProgressionCatalog.LetterEntry> entries = GetLetterEntries();
        foreach (LetterProgressionCatalog.LetterEntry entry in entries)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.letter))
                continue;
            char letter = char.ToUpperInvariant(entry.letter.Trim()[0]);
            if (curriculum != null && curriculum.IsSchoolModeActive && !curriculum.IsLetterAllowed(letter))
                continue;
            if (LearningProfile == null || LearningProfile.IsLetterUnlocked(letter))
                result.Add(letter.ToString());
        }
        return result;
    }

    public Dictionary<string, string> GetLetterSpeechAliases(IEnumerable<string> letters)
    {
        var allowed = new HashSet<string>();
        if (letters != null)
            foreach (string letter in letters)
                allowed.Add(NormalizeWord(letter));

        var aliases = new Dictionary<string, string>();
        foreach (LetterProgressionCatalog.LetterEntry entry in GetLetterEntries())
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.letter))
                continue;
            string canonical = NormalizeWord(entry.letter);
            if (!allowed.Contains(canonical)) continue;
            if (entry.speechAliases == null) continue;
            foreach (string value in entry.speechAliases)
            {
                string alias = VoiceUnlockRecognizer.NormalizeKeyword(value);
                if (!string.IsNullOrEmpty(alias)) aliases[alias] = canonical;
            }
        }
        return aliases;
    }

    public AudioClip GetLetterPronunciationClip(char letter)
    {
        char normalized = char.ToUpperInvariant(letter);
        LetterProgressionCatalog.LetterEntry entry = GetLetterEntry(normalized);
        if (entry != null && entry.pronunciationClip != null)
        {
            Debug.Log($"[Pronunciation] Letter '{normalized}' clip resolved from LetterProgressionCatalog: {DescribePronunciationClip(entry.pronunciationClip)}");
            return entry.pronunciationClip;
        }

        AudioClip clip = LoadLetterPronunciationClip(normalized);
        if (clip == null)
            Debug.LogWarning($"[Pronunciation] Letter '{normalized}' pronunciation clip not found. In builds, put clips under Resources/Audio/Pronunciations/Letters or assign them in the catalog.");
        else
            Debug.Log($"[Pronunciation] Letter '{normalized}' clip resolved from asset lookup: {DescribePronunciationClip(clip)}");
        return clip;
    }

    static AudioClip LoadLetterPronunciationClip(char letter)
    {
#if UNITY_EDITOR
        return UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(
            $"Assets/Audio/Pronunciations/Letters/{letter}.wav");
#else
        return Resources.Load<AudioClip>($"Audio/Pronunciations/Letters/{letter}") ??
               Resources.Load<AudioClip>($"Pronunciations/Letters/{letter}");
#endif
    }

    static string DescribePronunciationClip(AudioClip clip)
    {
        if (clip == null)
            return "null";

        return $"'{clip.name}' length={clip.length:0.000}s samples={clip.samples} frequency={clip.frequency} channels={clip.channels} loadState={clip.loadState}";
    }

    IReadOnlyList<LetterProgressionCatalog.LetterEntry> GetLetterEntries()
    {
        return letterProgressionCatalog != null
            ? letterProgressionCatalog.GetEntries()
            : LetterProgressionCatalog.BuildDefaults();
    }

    LetterProgressionCatalog.LetterEntry GetLetterEntry(char letter)
    {
        char normalized = char.ToUpperInvariant(letter);
        foreach (LetterProgressionCatalog.LetterEntry entry in GetLetterEntries())
        {
            if (entry != null && !string.IsNullOrWhiteSpace(entry.letter) &&
                char.ToUpperInvariant(entry.letter.Trim()[0]) == normalized)
                return entry;
        }
        return null;
    }

    void ResolveLearningProfile()
    {
        if (LearningProfile == null)
            LearningProfile = GetComponent<PlayerLearningProfile>() ?? FindAnyObjectByType<PlayerLearningProfile>();
    }

    public Dictionary<string, string> GetPronunciationAliases(IEnumerable<string> allowedWords)
    {
        var allowed = new HashSet<string>();
        if (allowedWords != null)
            foreach (string word in allowedWords)
                allowed.Add(NormalizeWord(word));

        var result = new Dictionary<string, string>();
        foreach (SpellDefinition definition in GetConfiguredSpellDefinitions())
        {
            if (definition == null) continue;
            string canonical = NormalizeWord(definition.word);
            if (!allowed.Contains(canonical) || definition.pronunciationAliases == null) continue;
            foreach (string aliasValue in definition.pronunciationAliases)
            {
                string alias = VoiceUnlockRecognizer.NormalizeKeyword(aliasValue);
                if (!string.IsNullOrEmpty(alias)) result[alias] = canonical;
            }
        }
        return result;
    }

    public void EnsureDefaultSpells()
    {
        EnsureCatalogReferences();
        GetConfiguredSpellDefinitions();
    }

    void RebuildLookup()
    {
        spellLookup.Clear();
        registeredWords.Clear();

        foreach (SpellDefinition definition in GetConfiguredSpellDefinitions())
        {
            if (definition == null)
                continue;

            string key = NormalizeWord(definition.word);
            if (string.IsNullOrEmpty(key))
                continue;

            spellLookup[key] = definition;
        }

        registeredWords.AddRange(spellLookup.Keys);
    }

    List<SpellDefinition> GetConfiguredSpellDefinitions()
    {
        EnsureCatalogReferences();

        if (spellCatalog != null &&
            spellCatalog.spellDefinitions != null &&
            spellCatalog.spellDefinitions.Count > 0)
        {
            return spellCatalog.spellDefinitions;
        }

        if (spellDefinitions == null || spellDefinitions.Count == 0)
            spellDefinitions = BuildDefaultSpellDefinitions();

        return spellDefinitions;
    }

    void EnsureCatalogReferences()
    {
        if (spellCatalog == null)
            spellCatalog = Resources.Load<SpellCatalog>(DefaultSpellCatalogResourcePath);

        if (letterProgressionCatalog == null)
            letterProgressionCatalog = Resources.Load<LetterProgressionCatalog>(DefaultLetterProgressionCatalogResourcePath);
    }

    static List<SpellDefinition> BuildDefaultSpellDefinitions()
    {
        var result = new List<SpellDefinition>
        {
            new SpellDefinition
            {
                word = "CAT",
                unlockLevel = 1,
                instructionalFocus = "First CVC: introduces curved C, diagonal A, and simple T with one sound per letter.",
                projectileColour = new Color(1f, 0.92f, 0.35f, 1f),
                projectileSpeed = 16f,
                fallbackShots = 3,
            },
            new SpellDefinition
            {
                word = "HEN",
                unlockLevel = 2,
                instructionalFocus = "Short e contrast; reuses a simple CVC rhythm while introducing H, E, and N.",
                projectileColour = new Color(1f, 0.79f, 0.46f, 1f),
                projectileSpeed = 16f,
                fallbackShots = 3,
            },
            new SpellDefinition
            {
                word = "PIG",
                unlockLevel = 3,
                instructionalFocus = "Short i CVC; introduces P, I, and G with clear, distinct forms.",
                projectileColour = new Color(0.84f, 0.63f, 1f, 1f),
                projectileSpeed = 16f,
                fallbackShots = 3,
            },
            new SpellDefinition
            {
                word = "HOP",
                unlockLevel = 4,
                instructionalFocus = "Short o CVC; reuses H and P while introducing rounded O.",
                projectileColour = new Color(0.56f, 0.93f, 0.55f, 1f),
                projectileSpeed = 16.5f,
                fallbackShots = 3,
            },
            new SpellDefinition
            {
                word = "BUG",
                unlockLevel = 5,
                instructionalFocus = "Short u CVC; reuses G and introduces B only after other rounded forms.",
                projectileColour = new Color(0.51f, 0.87f, 1f, 1f),
                projectileSpeed = 16.5f,
                fallbackShots = 3,
            },
            new SpellDefinition
            {
                word = "SHIP",
                unlockLevel = 6,
                instructionalFocus = "Adds the sh digraph and extends writing from three to four letters.",
                projectileColour = new Color(0.55f, 0.84f, 1f, 1f),
                projectileSpeed = 15f,
                fallbackShots = 3,
            },
            new SpellDefinition
            {
                word = "THIN",
                unlockLevel = 7,
                instructionalFocus = "Adds the th digraph while reusing T, H, I, and N.",
                projectileColour = new Color(0.67f, 0.97f, 0.72f, 1f),
                projectileSpeed = 17f,
                fallbackShots = 3,
            },
            new SpellDefinition
            {
                word = "CRAB",
                unlockLevel = 8,
                instructionalFocus = "Introduces R later, inside an initial blend, after simpler forms are established.",
                projectileColour = new Color(1f, 0.65f, 0.65f, 1f),
                projectileSpeed = 14f,
                fallbackShots = 3,
            },
            new SpellDefinition
            {
                word = "BARK",
                unlockLevel = 9,
                instructionalFocus = "Introduces r-controlled ar while staying concrete and decodable.",
                projectileColour = new Color(0.98f, 0.76f, 0.42f, 1f),
                projectileSpeed = 15f,
                fallbackShots = 3,
            },
            new SpellDefinition
            {
                word = "STONE",
                unlockLevel = 10,
                instructionalFocus = "Adds silent-e vowel pattern and a consonant blend.",
                projectileColour = new Color(0.85f, 0.9f, 1f, 1f),
                projectileSpeed = 18f,
                fallbackShots = 3,
            },
        };

        string[] starterWords =
        {
            "LEG", "FAN", "EGG", "HEN", "TOP", "INK", "UP", "CAT", "OWL", "QUEEN",
            "GUM", "SUN", "JAM", "DOG", "PIG", "BUG", "RUG", "KITE", "ANT", "MAP",
            "NET", "VAN", "WEB", "BOX", "YAK", "ZIP", "BAD", "CAN", "DAD", "DOT",
            "FIN", "GET", "HIT", "HOT", "KID", "MAD", "MAN", "MEN", "PET", "POP",
            "PUP", "RED", "SIT", "TAP", "TEN", "WET"
        };
        foreach (string word in starterWords)
        {
            if (result.Exists(entry => NormalizeWord(entry.word) == word))
                continue;
            result.Add(new SpellDefinition
            {
                word = word,
                unlockLevel = 1,
                instructionalFocus = "Starter word for letter-page progression.",
                projectileColour = Color.Lerp(new Color(0.45f, 0.78f, 1f), new Color(1f, 0.72f, 0.35f), (word[0] - 'A') / 25f),
                projectileSpeed = 16f,
                fallbackShots = 3,
            });
        }
        return result;
    }

    public static string NormalizeWord(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "" : value.ToUpperInvariant().Trim();
    }
}
