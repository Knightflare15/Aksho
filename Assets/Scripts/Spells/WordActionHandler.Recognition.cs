using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Routes spoken/written battle words into grammar creature combat. Legacy spell pages remain for non-grammar modes.
/// </summary>
[RequireComponent(typeof(CreatureCombatController))]

public partial class WordActionHandler : MonoBehaviour
{
    void HandleVoiceRecognitionResolved(VoiceUnlockRecognizer.RecognitionEvent result)
    {
        if (PauseMenuController.IsPaused)
            return;

        if (result.Mode != VoiceUnlockRecognizer.VoiceInputMode.CombatAutoListen)
            return;

        Debug.Log($"[WordActionHandler] Combat voice result recognized={result.Recognized} text='{result.Text}' raw='{result.RawText}' keywords='{configuredCastKeywords}'");
        if (voiceCastRecognizer != null && voiceCastRecognizer.LastActivityAt <= lastConsumedVoiceActivityAt)
            return;

        if (result.Recognized)
        {
            if (voiceCastRecognizer != null)
                lastConsumedVoiceActivityAt = voiceCastRecognizer.LastActivityAt;
            SetStatusHint($"Heard {result.Text}.", 0.8f);
            HandleCastWordRecognized(result.Text);
        }
        else
        {
            HandleCastWordRejected(result.RawText);
        }
    }

    void HandleCastWordRecognized(string spellWord)
    {
        if (PauseMenuController.IsPaused)
            return;

        // Recognition can be delivered by editor tooling or another component
        // before this behaviour's Awake has populated its cached references.
        // CreatureCombatController is a required component, so resolve it at
        // the command boundary instead of silently dropping a valid phrase.
        if (useCreatureCombat && creatureCombat == null)
            creatureCombat = GetComponent<CreatureCombatController>();

        if (IsGrammarBattleFlowActive && SelectedSlot.IsEmpty && TryHandleCreaturePhrase(spellWord))
        {
            ScheduleNextVoiceListen(CombatVoiceRearmSeconds);
            return;
        }

        if (!legacyWordSpellCastingEnabled)
        {
            SetStatusHint("That is not an unlocked grammar battle phrase.");
            return;
        }

        if (SelectedSlot.IsEmpty && TryHandleCreaturePhrase(spellWord))
        {
            ScheduleNextVoiceListen(CombatVoiceRearmSeconds);
            return;
        }

        if (SelectedSlot.IsEmpty || spellRegistry == null)
            return;
        SpellbookSlot slot = SelectedSlot;
        string normalized = SpellRegistry.NormalizeWord(spellWord);
        bool valid = slot.pageType == SpellbookPageType.Letter
            ? !string.IsNullOrEmpty(slot.pageLetter) && normalized.StartsWith(slot.pageLetter)
            : normalized == slot.spellWord;
        CurriculumSessionManager curriculum = CurriculumSessionManager.Instance;
        if (curriculum != null && curriculum.IsSchoolModeActive && !curriculum.IsWordAllowed(normalized))
            valid = false;
        if (!valid || !spellRegistry.TryGetSpell(normalized, out SpellDefinition definition))
        {
            learningProfile?.RecordSpokenCast(
                normalized,
                false,
                false,
                Time.unscaledTime - castListeningStartedAt,
                null,
                Array.Empty<byte>(),
                false);
            SetStatusHint($"'{normalized}' does not fit this page.");
            ScheduleNextVoiceListen(voiceCastErrorRetryDelay);
            return;
        }

        AcquireSpellAimTarget(normalized, out SpellTarget target, out SpellPillarObjective pillarTarget);
        bool enemyTargeted = target != null;
        bool area = slot.pageType == SpellbookPageType.SpecialWord;
        if (target != null && !target.IsWeakTo(normalized))
            aimAssist?.ShowWrongSpellHint(target, normalized);

        if (!SpawnProjectile(definition, normalized, target, pillarTarget, area))
        {
            SetStatusHint($"Heard {normalized}, but could not fire.");
            ScheduleNextVoiceListen(CombatVoiceRearmSeconds);
            return;
        }

        playerController?.PlayAttackAnimation();
        byte[] pronunciationAudio = GetLastCastPronunciationAudio();
        bool requestAzurePronunciationInsight = pronunciationAudio != null && pronunciationAudio.Length > 44;
        if (enemyTargeted)
        {
            lastSuccessfulVoiceCastWord = normalized;
            lastSuccessfulVoiceCastWasArea = area;
            lastSuccessfulVoiceCastAt = Time.unscaledTime;
        }
        else
        {
            lastSuccessfulVoiceCastWord = "";
            lastSuccessfulVoiceCastWasArea = false;
            lastSuccessfulVoiceCastAt = -1f;
        }

        learningProfile?.RecordSpokenCast(
            normalized,
            true,
            area,
            Time.unscaledTime - castListeningStartedAt,
            null,
            requestAzurePronunciationInsight ? pronunciationAudio : Array.Empty<byte>(),
            requestAzurePronunciationInsight);
        Debug.Log(requestAzurePronunciationInsight
            ? $"[Pronunciation] Queued Azure pronunciation review for '{normalized}'."
            : $"[Pronunciation] Azure pronunciation review not queued for '{normalized}': no captured WAV audio.");

        slot.currentAmmo--;
        if (slot.currentAmmo <= 0)
        {
            slot.Clear();
            CancelVoiceCast();
        }
        else
        {
            ScheduleNextVoiceListen(CombatVoiceRearmSeconds);
        }
        SetStatusHint($"Cast {normalized}. Hold attack to cast again.");
    }

    void HandleCastPronunciationInsightReady(PronunciationInsightResult insight)
    {
        if (string.IsNullOrWhiteSpace(lastSuccessfulVoiceCastWord))
        {
            Debug.Log($"[Pronunciation] Delayed pronunciation result ignored: no remembered successful voice cast. target='{insight.TargetWord}' raw='{insight.RawRecognizedText}' message='{insight.Message}'");
            return;
        }

        float age = Time.unscaledTime - lastSuccessfulVoiceCastAt;
        if (age > DelayedPronunciationReviewWindowSeconds)
        {
            Debug.Log($"[Pronunciation] Delayed pronunciation result ignored: result arrived after window. age={age:0.00}s window={DelayedPronunciationReviewWindowSeconds:0.00}s lastCast='{lastSuccessfulVoiceCastWord}' target='{insight.TargetWord}' raw='{insight.RawRecognizedText}' message='{insight.Message}'");
            return;
        }

        string target = SpellRegistry.NormalizeWord(!string.IsNullOrWhiteSpace(insight.TargetWord)
            ? insight.TargetWord
            : insight.ConfirmedWord);
        if (!string.Equals(target, lastSuccessfulVoiceCastWord, System.StringComparison.OrdinalIgnoreCase))
        {
            Debug.Log($"[Pronunciation] Delayed pronunciation result ignored: target '{target}' does not match last cast '{lastSuccessfulVoiceCastWord}'. raw='{insight.RawRecognizedText}'");
            return;
        }
        if (!PhoneticDisplayState.HasActionablePronunciationInsight(insight))
        {
            Debug.Log($"[Pronunciation] Delayed pronunciation result not shown for '{lastSuccessfulVoiceCastWord}': not actionable. score={insight.Score:0.00} attempted={insight.AttemptedTarget} hint={insight.HintKey} message='{insight.Message}'");
            return;
        }

        Debug.Log($"[Pronunciation] Showing delayed pronunciation phonetics for '{lastSuccessfulVoiceCastWord}' score={insight.Score:0.00} hint={insight.HintKey} message='{insight.Message}'");
        phoneticDisplayState?.RecordSuccessfulCast(
            lastSuccessfulVoiceCastWord,
            lastSuccessfulVoiceCastWasArea,
            insight);
    }

    void HandleServerPronunciationInsightReady(WordCastRecord record)
    {
        if (record == null || record.serverPronunciationInsight == null)
            return;

        string target = SpellRegistry.NormalizeWord(record.word);
        if (string.IsNullOrWhiteSpace(target))
            return;

        PronunciationInsightResult insight = BuildPronunciationInsightResult(record.serverPronunciationInsight);
        if (!PhoneticDisplayState.HasActionablePronunciationInsight(insight))
            return;

        Debug.Log($"[Pronunciation] Showing Azure pronunciation phonetics for '{target}' score={insight.Score:0.00} message='{insight.Message}'");
        phoneticDisplayState?.RecordSuccessfulCast(target, record.specialMatch, insight);
    }

    static PronunciationInsightResult BuildPronunciationInsightResult(PronunciationInsightRecord record)
    {
        var segments = new List<PhoneticSoundSegment>();
        if (record.segments != null)
        {
            foreach (PhoneticSegmentRecord segment in record.segments)
            {
                System.Enum.TryParse(segment.status, out PhoneticSegmentStatus status);
                segments.Add(new PhoneticSoundSegment(
                    segment.spelling,
                    segment.friendlySound,
                    segment.beatIndex,
                    status,
                    segment.confidence,
                    segment.heardSound));
            }
        }

        PronunciationHintKey hintKey = PronunciationHintKey.TryAgain;
        System.Enum.TryParse(record.hintKey, out hintKey);
        return new PronunciationInsightResult(
            record.providerName,
            record.targetWord,
            record.confirmedWord,
            record.rawRecognizedText,
            record.voskConfirmedWord,
            record.attemptedTarget,
            record.score,
            hintKey,
            record.focusSegment != null
                ? new PhoneticSoundSegment(
                    record.focusSegment.spelling,
                    record.focusSegment.friendlySound,
                    record.focusSegment.beatIndex,
                    System.Enum.TryParse(record.focusSegment.status, out PhoneticSegmentStatus focusStatus) ? focusStatus : PhoneticSegmentStatus.Unknown,
                    record.focusSegment.confidence,
                    record.focusSegment.heardSound)
                : default,
            segments,
            record.syllableBeats ?? new List<string>(),
            record.message);
    }

    List<string> GetCastWordsForLetter(char letter)
    {
        return spellRegistry.GetWordsForLetter(letter, unlockedOnly: false);
    }

    void HandleCastWordRejected(string heardText)
    {
        if (IsGrammarBattleFlowActive)
        {
            SetStatusHint(string.IsNullOrEmpty(heardText) ? "I did not hear a grammar phrase." : $"Heard '{heardText}', but it is not a grammar battle phrase.");
            ScheduleNextVoiceListen(voiceCastErrorRetryDelay);
            return;
        }

        if (SelectedSlot.IsEmpty) return;
        string key = SpellRegistry.NormalizeWord(heardText);
        if (string.IsNullOrEmpty(key))
            key = SelectedSlot.pageType == SpellbookPageType.SpecialWord ? SelectedSlot.spellWord : SelectedSlot.pageLetter;
        learningProfile?.RecordSpokenCast(
            key,
            false,
            false,
            Time.unscaledTime - castListeningStartedAt,
            null,
            Array.Empty<byte>(),
            false);
        SetStatusHint(string.IsNullOrEmpty(heardText) ? "I did not hear a spell." : $"Heard '{heardText}', but it does not fit this page.");
        ScheduleNextVoiceListen(voiceCastErrorRetryDelay);
    }

    PronunciationInsightResult? GetLastCastPronunciationInsight(string expectedWord = "")
    {
        if (voiceCastRecognizer == null)
            return null;

        PronunciationInsightResult insight = voiceCastRecognizer.LastPronunciationInsight;
        bool hasInsight = !string.IsNullOrWhiteSpace(insight.TargetWord) ||
                          !string.IsNullOrWhiteSpace(insight.RawRecognizedText) ||
                          (insight.Segments != null && insight.Segments.Count > 0);
        if (!hasInsight)
            return null;

        string expected = SpellRegistry.NormalizeWord(expectedWord);
        if (!string.IsNullOrEmpty(expected))
        {
            string target = SpellRegistry.NormalizeWord(!string.IsNullOrWhiteSpace(insight.TargetWord)
                ? insight.TargetWord
                : insight.ConfirmedWord);
            if (!string.Equals(target, expected, System.StringComparison.OrdinalIgnoreCase))
                return null;
        }

        return insight;
    }

    byte[] GetLastCastPronunciationAudio()
    {
        return voiceCastRecognizer != null
            ? voiceCastRecognizer.GetLastCapturedPronunciationWav()
            : Array.Empty<byte>();
    }

    public void CancelVoiceCast()
    {
        attackButtonHeld = false;
        configuredCastPageSignature = "";
        StopCombatVoiceListening();
    }
}
