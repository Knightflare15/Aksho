using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public partial class NpcDialogueUI
{
    void HandleSpeakPressed()
    {
        if (currentLine == null)
            return;

        string npcText = ResolveNpcLine(currentLine);
        if (currentLine.cachedSpeech == null)
            currentLine.cachedSpeech = NpcDialogueAudioCatalog.LoadFor(currentLine);

        if (currentLine.cachedSpeech != null)
        {
            PronunciationSpeaker.EnsureExists().Speak(npcText, currentLine.cachedSpeech);
            return;
        }

        if (currentBuddy != null)
            currentBuddy.SpeakAssistLine(currentLine.lineId, npcText, currentLine.sourceLanguage);
        else
            PronunciationSpeaker.EnsureExists().Speak(npcText);
    }

    void HandleAskBuddyPressed()
    {
        StartBuddyRealtimeCall("ask", "");
    }

    void StartBuddyRealtimeCall(string trigger, string learnerAttempt)
    {
        if (currentLine == null || currentBuddy == null || buddyCallSession.IsActive)
            return;

        SemanticZoneKind zoneKind = CurrentDialogueZoneKind();
        if (!currentBuddy.CanStartBuddyRealtimeCall(currentLine, zoneKind))
        {
            if (zoneKind != SemanticZoneKind.Gym)
                SetResponseStatus("Buddy needs a signed-in connection before the call can start.");
            return;
        }

        CurriculumSessionManager curriculum = CurriculumSessionManager.EnsureExists();
        string dialogueTaskId = string.IsNullOrWhiteSpace(currentLine.dialogueTaskId)
            ? currentLine.lineId
            : currentLine.dialogueTaskId;
        EnsureBuddyRelationshipMemory(curriculum);
        string buddyVoiceFunctionsBaseUrl = BuddyRealtimeFunctionUrl.Resolve(curriculum);
        buddyCallSession.Begin(curriculum.BuddyLearningSessionId, dialogueTaskId, currentLine.conceptId);
        BuddyCallOverlayRuntime.EnsureExists().Show(
            ToggleBuddyCallMicrophone,
            () => EndBuddyCall(stopSpeech: true));
        SetBuddyCallState(BuddyCallState.Connecting);
        SetResponseStatus("Calling Buddy...");

        BuddyRealtimeCallClient realtime = BuddyRealtimeCallClient.EnsureExists();
        bool started = realtime.BeginCall(
            new BuddyRealtimeCallRequest
            {
                functionsBaseUrl = buddyVoiceFunctionsBaseUrl,
                firebaseIdToken = curriculum.studentIdToken ?? "",
                firebaseAppCheckToken = curriculum.firebaseAppCheckToken ?? "",
                schoolId = curriculum.activeSchoolId ?? "",
                studentId = curriculum.activeStudentId ?? "",
                dialogueTaskId = dialogueTaskId,
                clientRequestId = buddyCallSession.CallId,
                trigger = string.Equals(trigger, "wrong_answer", StringComparison.OrdinalIgnoreCase)
                    ? "wrong_answer"
                    : string.Equals(trigger, "word_meaning", StringComparison.OrdinalIgnoreCase) ? "word_meaning" : "ask",
                learnerAttempt = learnerAttempt ?? "",
                safeRelationshipMemory = buddyRelationshipMemory?.SafeTags != null
                    ? new List<string>(buddyRelationshipMemory.SafeTags)
                    : new List<string>(),
            },
            HandleBuddyRealtimeStateChanged);
        if (!started)
        {
            SetResponseStatus("Buddy is already on another call or the connection is incomplete.");
            EndBuddyCall(stopSpeech: true);
        }
        RefreshAskBuddyButton();
    }

    void EnsureBuddyRelationshipMemory(CurriculumSessionManager curriculum)
    {
        string studentId = curriculum != null ? curriculum.activeStudentId ?? "" : "";
        if (buddyRelationshipMemory != null && string.Equals(studentId, buddyMemoryStudentId, StringComparison.Ordinal))
            return;

        buddyMemoryStudentId = studentId;
        buddyRelationshipMemory = new BuddyRelationshipMemoryStore(studentId);
    }

    void HandleBuddyRealtimeStateChanged(BuddyRealtimeCallStatus status, string message)
    {
        if (!buddyCallSession.IsActive)
            return;
        switch (status)
        {
            case BuddyRealtimeCallStatus.Ringing:
            case BuddyRealtimeCallStatus.Reconnecting:
                SetBuddyCallState(BuddyCallState.Connecting);
                break;
            case BuddyRealtimeCallStatus.BuddySpeaking:
                SetBuddyCallState(BuddyCallState.BuddySpeaking);
                break;
            case BuddyRealtimeCallStatus.Connected:
                if (buddyCallSession.State != BuddyCallState.TaskAnswering)
                    SetBuddyCallState(BuddyCallState.ReadyToTalk);
                break;
            case BuddyRealtimeCallStatus.Error:
                SetResponseStatus(string.IsNullOrWhiteSpace(message) ? "The Buddy call ended unexpectedly." : message);
                FinalizeBuddyCallUi();
                return;
            case BuddyRealtimeCallStatus.Ended:
                FinalizeBuddyCallUi();
                return;
        }
        if (!string.IsNullOrWhiteSpace(message))
            SetResponseStatus(message);
    }

    void ToggleBuddyCallMicrophone()
    {
        BuddyRealtimeCallClient realtime = FindAnyObjectByType<BuddyRealtimeCallClient>();
        if (realtime == null || !realtime.IsActive)
            return;
        bool muted = realtime.ToggleMicrophone();
        FindAnyObjectByType<BuddyCallOverlayRuntime>()?.SetMicrophoneMuted(muted);
    }

    void SetBuddyCallState(BuddyCallState state)
    {
        BuddyCallState previous = buddyCallSession.State;
        buddyCallSession.SetState(state);
        BuddyRealtimeCallClient realtime = FindAnyObjectByType<BuddyRealtimeCallClient>();
        if (realtime != null && realtime.IsActive)
        {
            if (state == BuddyCallState.TaskAnswering)
                realtime.SetTaskCaptureActive(true);
            else if (previous == BuddyCallState.TaskAnswering)
                realtime.SetTaskCaptureActive(false);
        }
        FindAnyObjectByType<BuddyCallOverlayRuntime>()?.SetState(state);
        RefreshAskBuddyButton();
    }

    void EndBuddyCall(bool stopSpeech)
    {
        if (stopSpeech)
            buddySpeechOutput?.Stop();
        BuddyRealtimeCallClient realtime = FindAnyObjectByType<BuddyRealtimeCallClient>();
        if (realtime != null && realtime.IsActive)
            realtime.EndCall();
        FinalizeBuddyCallUi();
    }

    void FinalizeBuddyCallUi()
    {
        bool wasActive = buddyCallSession.IsActive;
        FindAnyObjectByType<BuddyCallOverlayRuntime>()?.Hide();
        if (!wasActive)
            return;
        buddyCallSession.End();
        buddyCallSession.Reset();
        if (currentLine != null && panel != null && panel.gameObject.activeInHierarchy)
            ConfigureResponseRecognizer();
        RefreshAskBuddyButton();
    }

    void HandleHintPressed()
    {
        hintVisible = !hintVisible;
        if (hintButton != null)
        {
            TextMeshProUGUI label = hintButton.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
                label.text = hintVisible ? "Hide Hint" : "Hint";
        }
        RefreshAssistText();

        if (hintVisible && currentBuddy != null && currentLine != null)
        {
            string teachingHint = currentBuddy.BuildTeachingHint(currentLine);
            if (!string.IsNullOrWhiteSpace(teachingHint))
                PronunciationSpeaker.EnsureExists().Speak(teachingHint);
        }
    }

    void RequestAutomaticBuddyCoach(string learnerAttempt)
    {
        if (currentLine == null || currentBuddy == null || CurrentDialogueZoneKind() == SemanticZoneKind.Gym)
            return;

        incorrectAttemptCount++;
        if (!buddyCallSession.IsActive)
        {
            if (incorrectAttemptCount < AutomaticBuddyCoachAttemptThreshold || automaticBuddyCoachRequested)
                return;
            automaticBuddyCoachRequested = true;
            StartBuddyRealtimeCall("wrong_answer", learnerAttempt ?? "");
            return;
        }

        BuddyRealtimeCallClient realtime = FindAnyObjectByType<BuddyRealtimeCallClient>();
        if (realtime == null || !realtime.IsConnected)
            return;
        buddyCallSession.NextTurn();
        realtime.SendGameEvent(
            "wrong_answer",
            learnerAttempt ?? "",
            currentLine.conceptId.ToString(),
            buddyCallSession.TurnIndex);
        SetResponseStatus("Buddy heard that attempt and is preparing a clue...");
    }

    void RequestBuddyWordMeaning(string question)
    {
        if (!buddyCallSession.IsActive)
        {
            StartBuddyRealtimeCall("word_meaning", question ?? "");
            return;
        }
        BuddyRealtimeCallClient realtime = FindAnyObjectByType<BuddyRealtimeCallClient>();
        if (realtime == null || !realtime.IsConnected || currentLine == null)
            return;
        buddyCallSession.NextTurn();
        realtime.SendGameEvent(
            "word_meaning",
            question ?? "",
            currentLine.conceptId.ToString(),
            buddyCallSession.TurnIndex);
    }

    SemanticZoneKind CurrentDialogueZoneKind()
    {
        return currentNpc != null ? currentNpc.sceneKind : SemanticZoneKind.Town;
    }
}
