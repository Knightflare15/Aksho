using System.Collections.Generic;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public partial class ChallengeMode
{
    string BuildSpeechHint()
    {
        if (!string.IsNullOrEmpty(voiceOneShotFeedback) &&
            (voiceUnlockRecognizer == null || !voiceUnlockRecognizer.IsListening))
            return voiceOneShotFeedback;

        if (choosingForgeSpell)
        {
            if (voiceUnlockRecognizer != null && voiceUnlockRecognizer.IsAvailable)
            {
                string status = voiceUnlockRecognizer.IsListening
                    ? IsGrammarBattleForgeMode()
                        ? "Say a battle noun."
                        : $"Say any unlocked {VoiceSelectionNoun()}."
                    : voiceUnlockRecognizer.StatusMessage;
                if (!string.IsNullOrEmpty(hintedForgeWord))
                    status = $"Try: {hintedForgeWord}. {status}";
                if (CanUseEditorSpeechFallback())
                    return $"{status} Editor fallback: press {editorSpeechKey}.";

                return status;
            }

            return CanUseEditorSpeechFallback()
                ? !string.IsNullOrEmpty(hintedForgeWord)
                    ? $"Try: {hintedForgeWord}. Press {editorSpeechKey} to choose."
                    : IsGrammarBattleForgeMode()
                        ? $"Voice unlock is unavailable here. Press {editorSpeechKey} to choose a battle phrase."
                        : $"Voice unlock is unavailable here. Press {editorSpeechKey} to choose a {VoiceSelectionNoun()}."
                : "Voice unlock is unavailable on this platform.";
        }

        if (voiceUnlockRecognizer != null && voiceUnlockRecognizer.IsAvailable)
        {
            string status = voiceUnlockRecognizer.IsListening
                ? $"Say \"{targetWord}\"."
                : voiceUnlockRecognizer.StatusMessage;
            if (CanUseEditorSpeechFallback())
                return $"{status} Editor fallback: press {editorSpeechKey}.";

            return status;
        }

        return CanUseEditorSpeechFallback()
            ? $"Voice unlock is unavailable here. Press {editorSpeechKey} to continue."
            : "Voice unlock is unavailable on this platform.";
    }

    void ConfigureVoiceUnlock()
    {
        if (!useSpellLessonSlice || !requireSpeechUnlock || speechUnlocked || voiceUnlockRecognizer == null)
            return;

        if (choosingForgeSpell && IsGrammarBattleForgeMode())
        {
            CreatureCombatRegistry registry = ResolveCreatureCombatRegistry();
            if (registry != null)
                voiceUnlockRecognizer.ConfigureKeywords(
                    BuildGrammarForgeKeywords(registry),
                    null,
                    null,
                    autoStart: false);
            else
                voiceUnlockRecognizer.ConfigureKeyword(targetWord, autoStart: false);
        }
        else if (choosingForgeSpell && requestedForgeMode == ForgePageMode.LetterPage && spellRegistry != null)
        {
            List<string> letters = spellRegistry.GetUnlockedLetters();
            voiceUnlockRecognizer.ConfigureKeywords(
                letters,
                spellRegistry.GetLetterSpeechAliases(letters),
                null,
                autoStart: false);
        }
        else if (spellRegistry != null && spellRegistry.RegisteredWords.Count > 0)
        {
            int unlockedLevel = waveDirector != null
                ? waveDirector.CurrentLevel
                : Mathf.Max(1, currentTier + 1);
            List<string> unlockedWords = spellRegistry.GetUnlockedWords(unlockedLevel);
            voiceUnlockRecognizer.ConfigureKeywords(
                unlockedWords,
                spellRegistry.GetPronunciationAliases(unlockedWords),
                choosingForgeSpell ? null : targetWord,
                autoStart: false);
        }
        else
            voiceUnlockRecognizer.ConfigureKeyword(targetWord, autoStart: false);

        hint = BuildSpeechHint();

        if (attemptsLabel != null)
            attemptsLabel.gameObject.SetActive(false);
    }

    void HandleVoiceRecognitionResolved(VoiceUnlockRecognizer.RecognitionEvent result)
    {
        if (PauseMenuController.IsPaused)
            return;

        if (result.Mode != VoiceUnlockRecognizer.VoiceInputMode.WritingListenOnce)
            return;

        if (speechUnlocked)
            return;

        if (!result.Recognized)
        {
            voiceOneShotFeedback = BuildVoiceRetryFeedback(result.RawText);
            hint = voiceOneShotFeedback;
            ShowFeedbackPopup("Speech Check", hint, FeedbackPopupTone.Warning);
            if (attemptsLabel != null)
                attemptsLabel.text = hint;
            return;
        }

        string heardText = result.Text;
        if (choosingForgeSpell)
        {
            SetForgeTarget(heardText);
            return;
        }

        if (string.IsNullOrWhiteSpace(targetWord))
            return;

        if (!string.Equals(
                heardText.Trim(),
                targetWord,
                System.StringComparison.OrdinalIgnoreCase))
        {
            voiceOneShotFeedback = $"Heard \"{result.RawText}\". Press the mic and say \"{targetWord}\".";
            hint = voiceOneShotFeedback;
            ShowFeedbackPopup("Wrong Word Heard", hint, FeedbackPopupTone.Warning);
            if (attemptsLabel != null)
                attemptsLabel.text = hint;
            return;
        }

        voiceOneShotFeedback = "";
        UnlockSpeechGate();
    }

    void HandlePronunciationInsightReady(PronunciationInsightResult insight)
    {
        if (PauseMenuController.IsPaused)
        {
            Debug.Log($"[Pronunciation] Writing popup skipped: game paused. target='{insight.TargetWord}' raw='{insight.RawRecognizedText}'");
            return;
        }

        lastPronunciationInsight = insight;
        if (speechUnlocked || choosingForgeSpell || string.IsNullOrWhiteSpace(targetWord))
        {
            Debug.Log($"[Pronunciation] Writing popup skipped: gate inactive. speechUnlocked={speechUnlocked} choosingForgeSpell={choosingForgeSpell} targetWord='{targetWord}' insightTarget='{insight.TargetWord}' raw='{insight.RawRecognizedText}' score={insight.Score:0.00} hint={insight.HintKey}");
            return;
        }

        if (!string.Equals(insight.TargetWord, targetWord, System.StringComparison.OrdinalIgnoreCase))
        {
            Debug.Log($"[Pronunciation] Writing popup skipped: insight target mismatch. expected='{targetWord}' insightTarget='{insight.TargetWord}' confirmed='{insight.ConfirmedWord}' raw='{insight.RawRecognizedText}' score={insight.Score:0.00} hint={insight.HintKey}");
            return;
        }

        if (!HasActionablePronunciationInsight(insight))
        {
            Debug.Log($"[Pronunciation] Writing popup skipped: insight not actionable. target='{insight.TargetWord}' raw='{insight.RawRecognizedText}' score={insight.Score:0.00} attempted={insight.AttemptedTarget} voskConfirmed={insight.VoskConfirmedWord} hint={insight.HintKey} segments={insight.Segments?.Count ?? 0} message='{insight.Message}'");
            return;
        }

        voiceOneShotFeedback = BuildPronunciationRetryHint(insight);
        hint = voiceOneShotFeedback;
        Debug.Log($"[Pronunciation] Showing writing pronunciation popup target='{targetWord}' raw='{insight.RawRecognizedText}' score={insight.Score:0.00} hint={insight.HintKey} message='{hint}'");
        ShowFeedbackPopup("Pronunciation Hint", hint, FeedbackPopupTone.Guidance);
        if (attemptsLabel != null)
            attemptsLabel.text = hint;
    }

    string BuildVoiceRetryFeedback(string rawText)
    {
        if (!choosingForgeSpell &&
            !string.IsNullOrWhiteSpace(targetWord) &&
            string.Equals(lastPronunciationInsight.TargetWord, targetWord, System.StringComparison.OrdinalIgnoreCase) &&
            HasActionablePronunciationInsight(lastPronunciationInsight) &&
            lastPronunciationInsight.Segments != null &&
            lastPronunciationInsight.Segments.Count > 0)
            return BuildPronunciationRetryHint(lastPronunciationInsight);

        return string.IsNullOrWhiteSpace(rawText)
            ? "I did not hear it. Press the mic and try again."
            : $"Heard \"{rawText}\". Press the mic and try again.";
    }

    string BuildPronunciationRetryHint(PronunciationInsightResult insight)
    {
        string expectedSound = ExpectedPracticeSound(insight);
        string heardSound = insight.FocusSegment.HeardSound;
        string sound = string.IsNullOrWhiteSpace(expectedSound)
            ? insight.FocusSegment.Spelling
            : expectedSound;
        string scoreText = $"Closeness {Mathf.RoundToInt(insight.Score * 100f)}%.";

        switch (insight.HintKey)
        {
            case PronunciationHintKey.TryFirstSound:
                if (!string.IsNullOrWhiteSpace(heardSound))
                    return insight.VoskConfirmedWord
                        ? $"Nice, I heard the word. {scoreText} I heard \"{heardSound}\" at the start; practice \"{sound}\"."
                        : $"{scoreText} I heard \"{heardSound}\" at the start. Try \"{sound}\".";
                return string.IsNullOrWhiteSpace(sound)
                    ? $"Press the mic and start \"{targetWord}\" slowly."
                    : $"{scoreText} Press the mic and try the first sound: \"{sound}\".";
            case PronunciationHintKey.TryLastSound:
                if (!string.IsNullOrWhiteSpace(heardSound))
                    return insight.VoskConfirmedWord
                        ? $"Nice, I heard the word. {scoreText} I heard \"{heardSound}\" at the end; practice \"{sound}\"."
                        : $"{scoreText} I heard \"{heardSound}\" at the end. Try \"{sound}\".";
                return string.IsNullOrWhiteSpace(sound)
                    ? $"Press the mic and finish \"{targetWord}\" clearly."
                    : $"{scoreText} Press the mic and try the ending sound: \"{sound}\".";
            case PronunciationHintKey.TryAllBeats:
                string beats = insight.SyllableBeats != null && insight.SyllableBeats.Count > 0
                    ? string.Join("-", insight.SyllableBeats)
                    : targetWord;
                return $"{scoreText} Press the mic and clap the beats: {beats}.";
            case PronunciationHintKey.TrySlower:
                return $"{scoreText} Press the mic and say \"{targetWord}\" slower.";
            case PronunciationHintKey.GreatTry:
                return $"Great try. {scoreText} Press the mic and say \"{targetWord}\" once more.";
            default:
                return $"{scoreText} Press the mic and try \"{targetWord}\" again.";
        }
    }

    bool HasActionablePronunciationInsight(PronunciationInsightResult insight)
    {
        return insight.Segments != null &&
               insight.Segments.Count > 0 &&
               insight.HintKey != PronunciationHintKey.GreatTry &&
               !string.IsNullOrWhiteSpace(insight.TargetWord);
    }

    string ExpectedPracticeSound(PronunciationInsightResult insight)
    {
        string sound = string.IsNullOrWhiteSpace(insight.FocusSegment.FriendlySound)
            ? insight.FocusSegment.Spelling.ToLowerInvariant()
            : insight.FocusSegment.FriendlySound;

        if (insight.Segments == null || insight.Segments.Count == 0 || string.IsNullOrWhiteSpace(sound))
            return sound;

        for (int i = 0; i < insight.Segments.Count; i++)
        {
            PhoneticSoundSegment segment = insight.Segments[i];
            if (!string.Equals(segment.Spelling, insight.FocusSegment.Spelling, System.StringComparison.OrdinalIgnoreCase) ||
                segment.BeatIndex != insight.FocusSegment.BeatIndex)
                continue;

            if (i + 1 < insight.Segments.Count &&
                insight.Segments[i + 1].BeatIndex == segment.BeatIndex &&
                IsSingleVowelSound(insight.Segments[i + 1].FriendlySound) &&
                !IsSingleVowelSound(sound))
                return sound + insight.Segments[i + 1].FriendlySound;

            return sound;
        }

        return sound;
    }

    bool IsSingleVowelSound(string sound)
    {
        if (string.IsNullOrWhiteSpace(sound) || sound.Length != 1)
            return false;

        char c = char.ToLowerInvariant(sound[0]);
        return c == 'a' || c == 'e' || c == 'i' || c == 'o' || c == 'u';
    }
}
