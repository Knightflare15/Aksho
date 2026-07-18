using System;
using System.Collections.Generic;
using UnityEngine;

public enum CombatWordRole
{
    Adjective,
    Verb,
    Adverb,
}

[Serializable]
public sealed class CombatWordVarietyEvaluation
{
    public float effectiveness = 1f;
    public float ppCostMultiplier = 1f;
    public float cooldownMultiplier = 1f;
    public int repeatLevel;
    public string repeatedWord = "";
    public CombatWordRole repeatedRole = CombatWordRole.Verb;
    public int verbRepeatLevel;
    public int adverbRepeatLevel;

    public bool IsDiminished => effectiveness < 0.995f;

    public string BuildFeedback()
    {
        if (!IsDiminished || string.IsNullOrWhiteSpace(repeatedWord))
            return "";

        string role = repeatedRole.ToString().ToLowerInvariant();
        string alternative = repeatedRole == CombatWordRole.Adjective
            ? "Try a different adjective for full strength."
            : "Try a different verb or adverb for full strength.";
        return $"Repeated {role} {CreaturePhraseUtility.NormalizeToken(repeatedWord)} is at {Mathf.RoundToInt(effectiveness * 100f)}% strength. {alternative}";
    }
}

/// <summary>
/// Tracks a short history of successfully used combat words. Reusing a word
/// that is still in the recent window gradually reduces its effect and raises
/// its PP/cooldown cost. Commands rejected before they spend resources are not
/// recorded; valid attempts still count even when an attack misses.
/// </summary>
public sealed class CombatWordVarietyTracker
{
    public const int DefaultHistorySize = 4;

    readonly int historySize;
    readonly List<string> adjectiveHistory = new List<string>();
    readonly List<string> verbHistory = new List<string>();
    readonly List<string> adverbHistory = new List<string>();

    public CombatWordVarietyTracker(int historySize = DefaultHistorySize)
    {
        this.historySize = Mathf.Max(1, historySize);
    }

    public CombatWordVarietyEvaluation Evaluate(CombatWordRole role, string word)
    {
        string normalized = CreaturePhraseUtility.NormalizeToken(word);
        int repeatLevel = CountRecent(GetHistory(role), normalized);
        float effectiveness = ResolveEffectiveness(repeatLevel);
        return new CombatWordVarietyEvaluation
        {
            effectiveness = effectiveness,
            ppCostMultiplier = ResolvePpCostMultiplier(effectiveness),
            cooldownMultiplier = ResolveCooldownMultiplier(effectiveness),
            repeatLevel = repeatLevel,
            repeatedWord = repeatLevel > 0 ? normalized : "",
            repeatedRole = role,
            verbRepeatLevel = role == CombatWordRole.Verb ? repeatLevel : 0,
            adverbRepeatLevel = role == CombatWordRole.Adverb ? repeatLevel : 0,
        };
    }

    public CombatWordVarietyEvaluation EvaluateAction(string verb, string adverb)
    {
        CombatWordVarietyEvaluation verbEvaluation = Evaluate(CombatWordRole.Verb, verb);
        CombatWordVarietyEvaluation adverbEvaluation = string.IsNullOrWhiteSpace(adverb)
            ? new CombatWordVarietyEvaluation()
            : Evaluate(CombatWordRole.Adverb, adverb);

        CombatWordVarietyEvaluation limiting = verbEvaluation.effectiveness <= adverbEvaluation.effectiveness
            ? verbEvaluation
            : adverbEvaluation;
        float effectiveness = Mathf.Min(verbEvaluation.effectiveness, adverbEvaluation.effectiveness);
        return new CombatWordVarietyEvaluation
        {
            effectiveness = effectiveness,
            ppCostMultiplier = ResolvePpCostMultiplier(effectiveness),
            cooldownMultiplier = ResolveCooldownMultiplier(effectiveness),
            repeatLevel = Mathf.Max(verbEvaluation.repeatLevel, adverbEvaluation.repeatLevel),
            repeatedWord = limiting.repeatedWord,
            repeatedRole = limiting.repeatedRole,
            verbRepeatLevel = verbEvaluation.repeatLevel,
            adverbRepeatLevel = adverbEvaluation.repeatLevel,
        };
    }

    public void Record(CombatWordRole role, string word)
    {
        string normalized = CreaturePhraseUtility.NormalizeToken(word);
        if (string.IsNullOrEmpty(normalized))
            return;

        List<string> history = GetHistory(role);
        history.Add(normalized);
        while (history.Count > historySize)
            history.RemoveAt(0);
    }

    public void RecordAction(string verb, string adverb)
    {
        Record(CombatWordRole.Verb, verb);
        if (!string.IsNullOrWhiteSpace(adverb))
            Record(CombatWordRole.Adverb, adverb);
    }

    public void Clear()
    {
        adjectiveHistory.Clear();
        verbHistory.Clear();
        adverbHistory.Clear();
    }

    static float ResolveEffectiveness(int repeatLevel)
    {
        switch (Mathf.Max(0, repeatLevel))
        {
            case 0:
                return 1f;
            case 1:
                return 0.82f;
            case 2:
                return 0.66f;
            case 3:
                return 0.54f;
            default:
                return 0.45f;
        }
    }

    static float ResolvePpCostMultiplier(float effectiveness)
    {
        return 1f + (1f - Mathf.Clamp01(effectiveness)) * 0.8f;
    }

    static float ResolveCooldownMultiplier(float effectiveness)
    {
        return 1f + (1f - Mathf.Clamp01(effectiveness)) * 0.6f;
    }

    static int CountRecent(List<string> history, string word)
    {
        if (history == null || string.IsNullOrEmpty(word))
            return 0;

        int count = 0;
        foreach (string recent in history)
            if (recent == word)
                count++;
        return count;
    }

    List<string> GetHistory(CombatWordRole role)
    {
        switch (role)
        {
            case CombatWordRole.Adjective:
                return adjectiveHistory;
            case CombatWordRole.Adverb:
                return adverbHistory;
            default:
                return verbHistory;
        }
    }
}
