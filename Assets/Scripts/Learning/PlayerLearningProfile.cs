using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class PlayerLearningProfile : MonoBehaviour
{
    [Serializable]
    public class LetterProgress
    {
        public string letter;
        public bool unlocked;
        public int successfulFormations;
        public int confidentFormations;
        public int totalAttempts;
        public int giftedCompletions;
    }

    [Serializable]
    public class WordProgress
    {
        public string word;
        public bool unlocked;
        public int masteryPoints;
        public int successfulCasts;
        public int specialCasts;
        public int castFailures;
        public float averageCastResponseSeconds;
        public int specialForges;
        public int exposureCount;
        public string lastExposedAtUtc;
    }

    [Serializable]
    public class CombatProgress
    {
        public int wavesCompleted;
        public int deaths;
        public int totalDamageTaken;
        public float averageWaveSeconds;
        public int recentReliefWaves;
    }

    [Serializable]
    public class BattleGrammarProgress
    {
        public string scope;
        public string key;
        public int attempts;
        public int successes;
        public int wrongFormCount;
        public int consecutiveFailures;
        public int masteryPoints;
        public string lastUsedAtUtc;
    }

    [Serializable]
    private class ProfileData
    {
        public int version = 3;
        public int highestUnlockedLevel = 1;
        public int lastPlayedLevel = 1;
        public List<SpellPerformanceTracker.SpellStats> spellStats = new List<SpellPerformanceTracker.SpellStats>();
        public List<LetterProgress> letters = new List<LetterProgress>();
        public List<WordProgress> words = new List<WordProgress>();
        public CombatProgress combat = new CombatProgress();
        public List<BattleGrammarProgress> battleGrammar = new List<BattleGrammarProgress>();
    }

    private static string SavePath =>
        PlayerSaveSlots.GetSaveFilePath("player_learning_profile.json");

    const float SaveDebounceSeconds = 1.25f;

    public static string SaveFilePath => SavePath;

    private SpellPerformanceTracker spellPerformanceTracker;
    private ProfileData data = new ProfileData();
    private bool saveDirty;
    private float nextSaveAt = -1f;

    public int HighestUnlockedLevel => Mathf.Max(1, data.highestUnlockedLevel);
    public int LastPlayedLevel => Mathf.Max(1, data.lastPlayedLevel);
    public CombatProgress Combat => data.combat;
    public event Action OnLearningProgressChanged;

    public static bool HasExistingSave()
    {
        return TryReadSummary(out _, out _);
    }

    public static bool TryReadSummary(out int highestUnlockedLevel, out int lastPlayedLevel)
    {
        return TryReadSummary(PlayerSaveSlots.ActiveSlot, out highestUnlockedLevel, out lastPlayedLevel);
    }

    public static bool TryReadSummary(int slot, out int highestUnlockedLevel, out int lastPlayedLevel)
    {
        highestUnlockedLevel = 1;
        lastPlayedLevel = 1;

        try
        {
            string path = PlayerSaveSlots.GetSaveFilePath("player_learning_profile.json", slot);
            if (!File.Exists(path))
                return false;

            string json = File.ReadAllText(path);
            var profile = JsonUtility.FromJson<ProfileData>(json);
            if (profile == null)
                return false;

            highestUnlockedLevel = Mathf.Max(1, profile.highestUnlockedLevel);
            lastPlayedLevel = Mathf.Max(1, profile.lastPlayedLevel);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PlayerLearningProfile] Summary read failed: {ex.Message}");
            return false;
        }
    }

    public static void DeleteProgressSave()
    {
        try
        {
            if (File.Exists(SavePath))
                File.Delete(SavePath);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PlayerLearningProfile] Delete failed: {ex.Message}");
        }
    }

    void Awake()
    {
        spellPerformanceTracker = GetComponent<SpellPerformanceTracker>();
        if (spellPerformanceTracker == null)
            spellPerformanceTracker = gameObject.AddComponent<SpellPerformanceTracker>();

        Load();
        spellPerformanceTracker.LoadSnapshot(data.spellStats);
        EnsureProgressDefaults();
        spellPerformanceTracker.OnStatsChanged += HandleStatsChanged;
    }

    void OnDestroy()
    {
        if (spellPerformanceTracker != null)
            spellPerformanceTracker.OnStatsChanged -= HandleStatsChanged;

        Save();
    }

    void Update()
    {
        if (saveDirty && nextSaveAt >= 0f && Time.unscaledTime >= nextSaveAt)
            Save();
    }

    public int ResolveStartingLevel(int fallbackLevel, int maxAvailableLevel)
    {
        int resolved = Mathf.Max(1, LastPlayedLevel);
        if (fallbackLevel > HighestUnlockedLevel)
            resolved = fallbackLevel;

        return Mathf.Clamp(resolved, 1, Mathf.Max(1, maxAvailableLevel));
    }

    public void SetCurrentLevel(int level, int maxAvailableLevel)
    {
        data.lastPlayedLevel = Mathf.Clamp(level, 1, Mathf.Max(1, maxAvailableLevel));
        data.highestUnlockedLevel = Mathf.Clamp(
            Mathf.Max(data.highestUnlockedLevel, data.lastPlayedLevel),
            1,
            Mathf.Max(1, maxAvailableLevel));
        Save();
    }

    public void MarkLevelCompleted(int completedLevel, int maxAvailableLevel)
    {
        int nextLevel = Mathf.Clamp(completedLevel + 1, 1, Mathf.Max(1, maxAvailableLevel));
        data.highestUnlockedLevel = Mathf.Clamp(
            Mathf.Max(data.highestUnlockedLevel, nextLevel),
            1,
            Mathf.Max(1, maxAvailableLevel));
        data.lastPlayedLevel = nextLevel;
        Save();
    }

    public bool IsLetterUnlocked(char letter)
    {
        CurriculumSessionManager curriculum = CurriculumSessionManager.EnsureExists();
        if (curriculum != null && curriculum.IsSchoolModeActive)
            return curriculum.IsLetterAllowed(letter);

        LetterProgress progress = GetLetter(letter);
        return progress != null && progress.unlocked;
    }

    public bool IsWordUnlocked(string word)
    {
        CurriculumSessionManager curriculum = CurriculumSessionManager.EnsureExists();
        if (curriculum != null && curriculum.IsSchoolModeActive)
            return curriculum.IsWordAllowed(word);

        WordProgress progress = GetWord(word);
        return progress != null && progress.unlocked;
    }

    public bool HasSuccessfullyUsedWord(string word)
    {
        WordProgress progress = GetWord(word);
        return progress != null && (progress.successfulCasts > 0 || progress.specialForges > 0);
    }

    public float GetLearningFocusWeight(string word)
    {
        string normalized = SpellRegistry.NormalizeWord(word);
        CurriculumSessionManager curriculum = CurriculumSessionManager.Instance;
        bool schoolAllowed = curriculum != null && curriculum.IsSchoolModeActive && curriculum.IsWordAllowed(normalized);
        WordProgress progress = GetWord(word);
        if (progress == null)
            return schoolAllowed ? 1.6f : 0f;
        if (!progress.unlocked && !schoolAllowed)
            return 0f;
        if (progress.castFailures > progress.successfulCasts && progress.exposureCount > 0)
            return 1.9f;
        if (progress.masteryPoints < 3)
            return 1.75f;
        if (progress.masteryPoints < 6)
            return 1.3f;
        return 0.85f;
    }

    public bool IsWordMastered(string word)
    {
        WordProgress progress = GetWord(word);
        return progress != null && progress.masteryPoints >= 6;
    }

    public float GetBattleMovePpMultiplier(string noun, string verb, float masteryBias = 0.2f, float mistakeBias = 0.35f)
    {
        BattleGrammarProgress verbProgress = GetBattleProgress("verb", verb);
        BattleGrammarProgress pairProgress = GetBattleProgress("noun-verb", $"{noun}:{verb}");

        float verbWeight = ResolveBattleSupportWeight(verbProgress, masteryBias, mistakeBias);
        float pairWeight = ResolveBattleSupportWeight(pairProgress, masteryBias * 0.85f, mistakeBias * 1.1f);
        float combined = Mathf.Lerp(verbWeight, pairWeight, 0.4f);
        return Mathf.Clamp(combined, 0.65f, 1.7f);
    }

    public void RecordBattleCommand(
        string noun,
        string verb,
        string adjective,
        string adverb,
        CreatureCommandTense tense,
        string pronoun,
        bool success,
        bool wrongForm = false)
    {
        RecordBattleProgress("noun", noun, success);
        RecordBattleProgress("verb", verb, success, wrongForm);
        RecordBattleProgress("noun-verb", $"{noun}:{verb}", success, wrongForm);

        if (!string.IsNullOrWhiteSpace(adjective))
            RecordBattleProgress("adjective", adjective, success);
        if (!string.IsNullOrWhiteSpace(adverb))
            RecordBattleProgress("adverb", adverb, success);

        string conjugationKey = BuildConjugationKey(tense, pronoun);
        if (!string.IsNullOrWhiteSpace(conjugationKey))
            RecordBattleProgress("conjugation", conjugationKey, success, wrongForm);

        Changed();
    }

    public void RecordLetterFormation(char letter, bool confident, int attempts, bool gifted)
    {
        LetterProgress progress = GetOrCreateLetter(letter);
        progress.unlocked = true;
        progress.successfulFormations++;
        progress.totalAttempts += Mathf.Max(1, attempts);
        if (confident && !gifted) progress.confidentFormations++;
        if (gifted) progress.giftedCompletions++;
        CurriculumSessionManager curriculum = CurriculumSessionManager.Instance;
        if (curriculum != null && curriculum.IsSchoolModeActive)
            curriculum.RecordLetterAttempt(letter, confident, attempts, gifted, confident ? 1f : 0f);
        else
            TryUnlockNextLetter(letter);
        Changed();
    }

    public void RecordSpokenCast(
        string word,
        bool success,
        bool special = false,
        float responseSeconds = 0f,
        PronunciationInsightResult? pronunciationInsight = null,
        byte[] pronunciationAudioWavBytes = null,
        bool requestPronunciationAnalysis = true,
        bool recordBuddyLearningAttempt = true)
    {
        WordProgress progress = GetOrCreateWord(word);
        if (success)
        {
            progress.successfulCasts++;
            progress.masteryPoints++;
            if (special) progress.specialCasts++;
        }
        else
        {
            progress.castFailures++;
        }
        progress.averageCastResponseSeconds = Blend(progress.averageCastResponseSeconds, responseSeconds, 0.25f);
        CurriculumSessionManager curriculum = CurriculumSessionManager.Instance;
        if (curriculum != null)
            curriculum.RecordWordCast(
                word,
                success,
                special,
                responseSeconds,
                pronunciationInsight,
                pronunciationAudioWavBytes,
                requestPronunciationAnalysis,
                recordBuddyLearningAttempt);
        if (curriculum == null || !curriculum.IsSchoolModeActive)
            TryUnlockNextWord(word);
        Changed();
    }

    public void RecordSpecialForge(string word)
    {
        WordProgress progress = GetOrCreateWord(word);
        progress.specialForges++;
        progress.masteryPoints += 2;
        CurriculumSessionManager curriculum = CurriculumSessionManager.Instance;
        if (curriculum == null || !curriculum.IsSchoolModeActive)
            TryUnlockNextWord(word);
        Changed();
    }

    public void RecordWaveOutcome(string primaryWord, int damageTaken, bool died, float durationSeconds)
    {
        data.combat ??= new CombatProgress();
        data.combat.wavesCompleted++;
        data.combat.totalDamageTaken += Mathf.Max(0, damageTaken);
        if (died) data.combat.deaths++;
        data.combat.averageWaveSeconds = Blend(data.combat.averageWaveSeconds, durationSeconds, 0.25f);
        data.combat.recentReliefWaves = died || damageTaken >= 3 ? 1 : Mathf.Max(0, data.combat.recentReliefWaves - 1);

        WordProgress progress = GetOrCreateWord(primaryWord);
        progress.exposureCount++;
        progress.lastExposedAtUtc = DateTime.UtcNow.ToString("o");
        Changed();
    }

    void HandleStatsChanged()
    {
        ScheduleSave();
    }

    void Save()
    {
        try
        {
            if (spellPerformanceTracker != null)
                data.spellStats = spellPerformanceTracker.CreateSnapshot();

            PlayerSaveSlots.EnsureActiveSlotDirectory();
            string json = JsonUtility.ToJson(data, true);
            File.WriteAllText(SavePath, json);
            saveDirty = false;
            nextSaveAt = -1f;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PlayerLearningProfile] Save failed: {ex.Message}");
        }
    }

    void Load()
    {
        try
        {
            if (!File.Exists(SavePath))
            {
                data = new ProfileData();
                return;
            }

            string json = File.ReadAllText(SavePath);
            data = JsonUtility.FromJson<ProfileData>(json) ?? new ProfileData();
            data.spellStats ??= new List<SpellPerformanceTracker.SpellStats>();
            data.letters ??= new List<LetterProgress>();
            data.words ??= new List<WordProgress>();
            data.combat ??= new CombatProgress();
            data.battleGrammar ??= new List<BattleGrammarProgress>();
            data.version = 3;
            data.highestUnlockedLevel = Mathf.Max(1, data.highestUnlockedLevel);
            data.lastPlayedLevel = Mathf.Max(1, data.lastPlayedLevel);
        }
        catch (Exception ex)
        {
            data = new ProfileData();
            Debug.LogWarning($"[PlayerLearningProfile] Load failed: {ex.Message}");
        }
    }

    void EnsureProgressDefaults()
    {
        LetterProgress first = GetOrCreateLetter('L');
        first.unlocked = true;
        WordProgress starter = GetOrCreateWord("LEG");
        starter.unlocked = true;
        // Keep the existing first arena playable while the new letter progression is authored.
        GetOrCreateLetter('C').unlocked = true;
        GetOrCreateWord("CAT").unlocked = true;

        foreach (SpellPerformanceTracker.SpellStats stats in data.spellStats)
        {
            if (stats == null || stats.successes <= 0 || string.IsNullOrWhiteSpace(stats.word))
                continue;
            string word = SpellRegistry.NormalizeWord(stats.word);
            GetOrCreateLetter(word[0]).unlocked = true;
            GetOrCreateWord(word).unlocked = true;
        }
    }

    LetterProgress GetLetter(char letter)
    {
        string key = char.ToUpperInvariant(letter).ToString();
        return data.letters.Find(entry => entry != null && entry.letter == key);
    }

    WordProgress GetWord(string word)
    {
        string key = SpellRegistry.NormalizeWord(word);
        return data.words.Find(entry => entry != null && entry.word == key);
    }

    LetterProgress GetOrCreateLetter(char letter)
    {
        string key = char.ToUpperInvariant(letter).ToString();
        LetterProgress progress = GetLetter(letter);
        if (progress != null) return progress;
        progress = new LetterProgress { letter = key };
        data.letters.Add(progress);
        return progress;
    }

    WordProgress GetOrCreateWord(string word)
    {
        string key = SpellRegistry.NormalizeWord(word);
        WordProgress progress = GetWord(key);
        if (progress != null) return progress;
        progress = new WordProgress { word = key };
        data.words.Add(progress);
        return progress;
    }

    BattleGrammarProgress GetBattleProgress(string scope, string key)
    {
        string normalizedScope = NormalizeBattleToken(scope);
        string normalizedKey = NormalizeBattleToken(key);
        if (string.IsNullOrEmpty(normalizedScope) || string.IsNullOrEmpty(normalizedKey))
            return null;

        return data.battleGrammar.Find(entry =>
            entry != null &&
            entry.scope == normalizedScope &&
            entry.key == normalizedKey);
    }

    BattleGrammarProgress GetOrCreateBattleProgress(string scope, string key)
    {
        string normalizedScope = NormalizeBattleToken(scope);
        string normalizedKey = NormalizeBattleToken(key);
        if (string.IsNullOrEmpty(normalizedScope) || string.IsNullOrEmpty(normalizedKey))
            return null;

        BattleGrammarProgress progress = GetBattleProgress(normalizedScope, normalizedKey);
        if (progress != null)
            return progress;

        progress = new BattleGrammarProgress
        {
            scope = normalizedScope,
            key = normalizedKey,
        };
        data.battleGrammar.Add(progress);
        return progress;
    }

    void RecordBattleProgress(string scope, string key, bool success, bool wrongForm = false)
    {
        BattleGrammarProgress progress = GetOrCreateBattleProgress(scope, key);
        if (progress == null)
            return;

        progress.attempts++;
        progress.lastUsedAtUtc = DateTime.UtcNow.ToString("o");
        if (success)
        {
            progress.successes++;
            progress.masteryPoints++;
            progress.consecutiveFailures = 0;
        }
        else
        {
            progress.consecutiveFailures++;
        }

        if (wrongForm)
            progress.wrongFormCount++;
    }

    void TryUnlockNextLetter(char letter)
    {
        var catalog = FindAnyObjectByType<SpellRegistry>()?.LetterCatalog;
        IReadOnlyList<LetterProgressionCatalog.LetterEntry> entries = catalog != null
            ? catalog.GetEntries()
            : LetterProgressionCatalog.BuildDefaults();
        for (int i = 0; i < entries.Count; i++)
        {
            LetterProgressionCatalog.LetterEntry entry = entries[i];
            if (entry == null || string.IsNullOrWhiteSpace(entry.letter) ||
                char.ToUpperInvariant(entry.letter[0]) != char.ToUpperInvariant(letter))
                continue;
            LetterProgress current = GetOrCreateLetter(letter);
            if (current.successfulFormations < entry.formationsToUnlockNext ||
                current.confidentFormations < entry.confidentFormationsToUnlockNext ||
                i + 1 >= entries.Count)
                return;
            LetterProgress next = GetOrCreateLetter(entries[i + 1].letter[0]);
            next.unlocked = true;
            UnlockFirstWordForLetter(entries[i + 1].letter[0]);
            return;
        }
    }

    void TryUnlockNextWord(string word)
    {
        string key = SpellRegistry.NormalizeWord(word);
        if (string.IsNullOrEmpty(key)) return;
        SpellRegistry registry = FindAnyObjectByType<SpellRegistry>();
        List<string> words = registry != null ? registry.GetWordsForLetter(key[0], unlockedOnly: false) : new List<string>();
        int index = words.IndexOf(key);
        if (index < 0 || index + 1 >= words.Count) return;
        WordProgress current = GetOrCreateWord(key);
        int threshold = registry != null ? registry.GetWordMasteryPointsPerUnlock(key[0]) : 3;
        if (current.masteryPoints < threshold) return;
        GetOrCreateWord(words[index + 1]).unlocked = true;
    }

    void UnlockFirstWordForLetter(char letter)
    {
        SpellRegistry registry = FindAnyObjectByType<SpellRegistry>();
        List<string> words = registry != null ? registry.GetWordsForLetter(letter, unlockedOnly: false) : new List<string>();
        if (words.Count > 0) GetOrCreateWord(words[0]).unlocked = true;
    }

    void Changed()
    {
        ScheduleSave();
        OnLearningProgressChanged?.Invoke();
    }

    void ScheduleSave()
    {
        saveDirty = true;
        nextSaveAt = Time.unscaledTime + SaveDebounceSeconds;
    }

    static float Blend(float current, float sample, float factor)
    {
        if (sample <= 0f) return current;
        return current <= 0f ? sample : Mathf.Lerp(current, sample, factor);
    }

    static float ResolveBattleSupportWeight(BattleGrammarProgress progress, float masteryBias, float mistakeBias)
    {
        if (progress == null || progress.attempts <= 0)
            return 1f + Mathf.Clamp(mistakeBias, 0f, 1.5f) * 0.65f;

        float successRate = progress.successes / (float)Mathf.Max(1, progress.attempts);
        float weaknessPressure = 1f - successRate;
        float mistakePressure = Mathf.Clamp01(progress.wrongFormCount / (float)Mathf.Max(1, progress.attempts));
        float masteryPressure = progress.masteryPoints >= 8
            ? -masteryBias
            : progress.masteryPoints >= 4
                ? -masteryBias * 0.45f
                : mistakeBias * 0.25f;
        float failPressure = Mathf.Clamp01(progress.consecutiveFailures * 0.2f) * mistakeBias;
        return 1f + (weaknessPressure * mistakeBias) + (mistakePressure * mistakeBias * 0.65f) + masteryPressure + failPressure;
    }

    static string NormalizeBattleToken(string value)
    {
        return CreaturePhraseUtility.NormalizeToken(value);
    }

    static string BuildConjugationKey(CreatureCommandTense tense, string pronoun)
    {
        string pronounToken = NormalizeBattleToken(pronoun);
        string tenseToken = tense.ToString().ToUpperInvariant();
        if (tense == CreatureCommandTense.None)
            return "";
        if (string.IsNullOrEmpty(pronounToken))
            return tenseToken;
        return $"{tenseToken}:{pronounToken}";
    }
}
