using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;


public sealed partial class CurriculumSessionManager
{
    public void BeginMissionTimer()
    {
        if (!HasWorldGoalPractice)
            LoadWorldGoalPractice();

        missionStartedAt = Time.unscaledTime;
        CurrentSubArenaIndex = 1;
        SubArenasCleared = 0;
        FullLoopsCleared = 0;
        confidenceSampleCount = 0;
        confidenceTotal = 0f;
        attemptsSampleCount = 0;
        attemptsTotal = 0;
        specialWordMatches = 0;
        eligibleServerPronunciationReviewsThisRun = 0;
        serverPronunciationReviewsThisRun = 0;
        lettersPracticed.Clear();
        wordsPracticed.Clear();
    }

    public void AdvanceSubArena()
    {
        if (!IsSchoolModeActive)
            return;

        SubArenasCleared++;
        CurrentSubArenaIndex++;
        if (CurrentSubArenaIndex > 3)
        {
            CurrentSubArenaIndex = 1;
            FullLoopsCleared++;
        }

        OnSubArenaAdvanced?.Invoke();
    }

    public string GetCurrentSubArenaSceneName(List<string> fallbackScenes)
    {
        if (CurrentMission?.subArenas != null)
        {
            foreach (SubArenaDefinition subArena in CurrentMission.subArenas)
            {
                if (subArena != null && subArena.subArenaIndex == CurrentSubArenaIndex &&
                    !string.IsNullOrWhiteSpace(subArena.sceneName))
                    return subArena.sceneName;
            }
        }

        if (fallbackScenes != null && fallbackScenes.Count > 0)
        {
            int index = Mathf.Clamp(CurrentSubArenaIndex - 1, 0, fallbackScenes.Count - 1);
            return fallbackScenes[index];
        }

        return "Level_1_Bat";
    }

    public bool IsLetterAllowed(char letter)
    {
        if (!IsSchoolModeActive)
            return true;

        return allowedLetters.Contains(char.ToUpperInvariant(letter).ToString());
    }

    public bool IsWordAllowed(string word)
    {
        if (!IsSchoolModeActive)
            return true;

        string normalized = SpellRegistry.NormalizeWord(word);
        return !string.IsNullOrEmpty(normalized) &&
            (allowedWords.Contains(normalized) || allowedLetters.Contains(normalized[0].ToString()));
    }

    public bool IsSandboxLetterUnlocked(char letter)
    {
        if (!IsSchoolModeActive)
            return true;

        string key = char.ToUpperInvariant(letter).ToString();
        return allowedLetters.Count == 0 || allowedLetters.Contains(key);
    }

    public bool IsSandboxWordUnlocked(string word)
    {
        if (!IsSchoolModeActive)
            return true;

        string normalizedWord = SpellRegistry.NormalizeWord(word);
        if (string.IsNullOrEmpty(normalizedWord))
            return false;

        return allowedWords.Count == 0 ||
            allowedWords.Contains(normalizedWord) ||
            allowedLetters.Contains(normalizedWord[0].ToString());
    }

    public List<string> FilterAllowedWords(IEnumerable<string> words)
    {
        var result = new List<string>();
        if (words == null)
            return result;

        foreach (string word in words)
        {
            string normalized = SpellRegistry.NormalizeWord(word);
            if (string.IsNullOrEmpty(normalized))
                continue;
            if (IsWordAllowed(normalized) && !result.Contains(normalized))
                result.Add(normalized);
        }

        return result;
    }
}
