using System.Collections.Generic;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public partial class ChallengeMode
{
    float SanitizeRecognitionScore(float score)
    {
        if (float.IsNaN(score) || float.IsInfinity(score))
            score = thresholdWrong;

        return Mathf.Clamp(score, 0f, thresholdWrong);
    }

    int CalculateShotAward()
    {
        if (acceptedLetterTries.Count == 0)
            return 3;

        int totalTries = 0;
        int maxTriesForSingleLetter = 0;
        foreach (int tries in acceptedLetterTries)
        {
            totalTries += tries;
            if (tries > maxTriesForSingleLetter)
                maxTriesForSingleLetter = tries;
        }

        LastAverageLetterScore = AverageAcceptedScore();
        if (maxTriesForSingleLetter > 3)
            return 3;

        float averageTries = totalTries / (float)acceptedLetterTries.Count;
        int attemptBase = averageTries <= 1.15f
            ? 5
            : averageTries <= 2.15f
                ? 4
                : 3;

        float closeness01 = 1f - Mathf.InverseLerp(0f, thresholdWrong, LastAverageLetterScore);
        int closenessBonus = closeness01 >= 0.82f
            ? 3
            : closeness01 >= 0.58f
                ? 2
                : closeness01 >= 0.32f
                    ? 1
                    : 0;

        return Mathf.Clamp(attemptBase + closenessBonus, 3, 8);
    }

    float AverageAcceptedScore()
    {
        if (acceptedLetterScores.Count == 0)
            return thresholdWrong;

        float total = 0f;
        foreach (float score in acceptedLetterScores)
            total += score;

        return total / acceptedLetterScores.Count;
    }

    float AverageAcceptedTriesPerLetter()
    {
        if (acceptedLetterTries.Count == 0)
            return Mathf.Max(1f, maxAttempts * 0.25f);

        float total = 0f;
        foreach (int tries in acceptedLetterTries)
            total += tries;

        return total / acceptedLetterTries.Count;
    }
}
