using System;
using System.Collections.Generic;
using UnityEngine;

public class SpellPerformanceTracker : MonoBehaviour
{
    [Serializable]
    public class SpellStats
    {
        public string word;
        public int attempts;
        public int successes;
        public float averageLetterScore = PDollarRecognizer.SCORE_THRESHOLD * 0.5f;
        public float averageTriesPerLetter = 1.5f;
        public float averageDurationSeconds = 4f;
        public int wrongLetters;
        public int giftedLetters;
        public float adaptiveDifficulty = 1f;
    }

    [SerializeField]
    private List<SpellStats> stats = new List<SpellStats>();

    private readonly Dictionary<string, SpellStats> lookup = new Dictionary<string, SpellStats>();

    public IReadOnlyList<SpellStats> Stats => stats;
    public event Action OnStatsChanged;

    void Awake()
    {
        RebuildLookup();
    }

    void OnValidate()
    {
        RebuildLookup();
    }

    public void RecordAttempt(
        string spellWord,
        bool success,
        float averageLetterScore,
        float averageTriesPerLetter,
        int wrongLetterCount,
        int giftedLetterCount,
        float durationSeconds)
    {
        string key = SpellRegistry.NormalizeWord(spellWord);
        if (string.IsNullOrEmpty(key))
            return;

        SpellStats entry = GetOrCreate(key);
        entry.attempts++;
        if (success)
            entry.successes++;

        entry.averageLetterScore = Blend(entry.averageLetterScore, averageLetterScore, 0.35f);
        entry.averageTriesPerLetter = Blend(entry.averageTriesPerLetter, averageTriesPerLetter, 0.4f);
        entry.averageDurationSeconds = Blend(entry.averageDurationSeconds, durationSeconds, 0.25f);
        entry.wrongLetters += Mathf.Max(0, wrongLetterCount);
        entry.giftedLetters += Mathf.Max(0, giftedLetterCount);
        entry.adaptiveDifficulty = CalculateDifficulty(entry);
        OnStatsChanged?.Invoke();
    }

    public float GetDifficultyWeight(string spellWord)
    {
        string key = SpellRegistry.NormalizeWord(spellWord);
        if (!lookup.TryGetValue(key, out SpellStats entry))
            return 1f;

        return Mathf.Clamp(entry.adaptiveDifficulty, 0.75f, 2.75f);
    }

    public bool TryGetStats(string spellWord, out SpellStats statsEntry)
    {
        string key = SpellRegistry.NormalizeWord(spellWord);
        if (lookup.TryGetValue(key, out SpellStats entry))
        {
            statsEntry = entry;
            return true;
        }

        statsEntry = null;
        return false;
    }

    SpellStats GetOrCreate(string key)
    {
        if (lookup.TryGetValue(key, out SpellStats entry))
            return entry;

        entry = new SpellStats
        {
            word = key,
        };
        stats.Add(entry);
        lookup[key] = entry;
        return entry;
    }

    void RebuildLookup()
    {
        lookup.Clear();
        if (stats == null)
            stats = new List<SpellStats>();

        foreach (SpellStats entry in stats)
        {
            if (entry == null)
                continue;

            string key = SpellRegistry.NormalizeWord(entry.word);
            if (string.IsNullOrEmpty(key))
                continue;

            entry.word = key;
            entry.adaptiveDifficulty = CalculateDifficulty(entry);
            lookup[key] = entry;
        }
    }

    public List<SpellStats> CreateSnapshot()
    {
        var snapshot = new List<SpellStats>(stats.Count);
        foreach (SpellStats entry in stats)
        {
            if (entry == null)
                continue;

            snapshot.Add(new SpellStats
            {
                word = entry.word,
                attempts = entry.attempts,
                successes = entry.successes,
                averageLetterScore = entry.averageLetterScore,
                averageTriesPerLetter = entry.averageTriesPerLetter,
                averageDurationSeconds = entry.averageDurationSeconds,
                wrongLetters = entry.wrongLetters,
                giftedLetters = entry.giftedLetters,
                adaptiveDifficulty = entry.adaptiveDifficulty,
            });
        }

        return snapshot;
    }

    public void LoadSnapshot(List<SpellStats> snapshot)
    {
        stats = snapshot != null ? new List<SpellStats>(snapshot.Count) : new List<SpellStats>();
        if (snapshot != null)
        {
            foreach (SpellStats entry in snapshot)
            {
                if (entry == null)
                    continue;

                stats.Add(new SpellStats
                {
                    word = entry.word,
                    attempts = entry.attempts,
                    successes = entry.successes,
                    averageLetterScore = entry.averageLetterScore,
                    averageTriesPerLetter = entry.averageTriesPerLetter,
                    averageDurationSeconds = entry.averageDurationSeconds,
                    wrongLetters = entry.wrongLetters,
                    giftedLetters = entry.giftedLetters,
                    adaptiveDifficulty = entry.adaptiveDifficulty,
                });
            }
        }

        RebuildLookup();
    }

    static float Blend(float current, float sample, float factor)
    {
        if (float.IsNaN(sample) || float.IsInfinity(sample))
            return current;

        if (current <= 0f)
            return sample;

        return Mathf.Lerp(current, sample, Mathf.Clamp01(factor));
    }

    static float CalculateDifficulty(SpellStats entry)
    {
        float successRate = entry.attempts <= 0
            ? 0.75f
            : entry.successes / (float)entry.attempts;
        float accuracyPressure = 1f - successRate;
        float scorePressure = Mathf.InverseLerp(0f, PDollarRecognizer.SCORE_THRESHOLD, entry.averageLetterScore);
        float tryPressure = Mathf.InverseLerp(1f, 4f, entry.averageTriesPerLetter);
        float supportPressure = Mathf.Clamp01((entry.giftedLetters * 0.2f) + (entry.wrongLetters * 0.04f));

        return 0.85f +
               (accuracyPressure * 0.8f) +
               (scorePressure * 0.55f) +
               (tryPressure * 0.45f) +
               (supportPressure * 0.35f);
    }
}
