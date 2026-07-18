using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public partial class NpcDialogueUI
{
void RefreshResponseStatus()
    {
        if (responseStatusLabel == null)
            return;

        if (currentLine == null || string.IsNullOrWhiteSpace(currentLine.expectedEnglishResponse))
        {
        responseStatusLabel.text = "";
            SetGrimoireButtonVisible(false);
            if (speakResponseButton != null)
                speakResponseButton.interactable = false;
            return;
        }

        if (speakResponseButton != null)
            speakResponseButton.interactable = currentLine.inputMode != GrammarDialogueInputMode.WriteOnly &&
                                               (!UsesSpokenAnswerChoices() || !string.IsNullOrWhiteSpace(selectedSpokenChoice));
        if (speakResponseLabel != null)
            speakResponseLabel.text = UsesSpokenAnswerChoices() ? "Hold to Say" : "Answer";

        bool writeOnly = currentLine.inputMode == GrammarDialogueInputMode.WriteOnly;
        bool speakAndWrite = currentLine.inputMode == GrammarDialogueInputMode.SpeakAndWrite;
        bool canWrite = writeOnly || currentLine.inputMode == GrammarDialogueInputMode.SpeakOrWrite || speakAndWrite;
        bool scrambled = currentLine.malfunctionType == GrammarDialogueMalfunctionType.ScrambledSentence;
        if (scrambled)
        {
            responseStatusLabel.text = "Drag the words into the correct order.";
            SetGrimoireButtonVisible(false);
            return;
        }
        if (currentBuddy != null && currentBuddy.CurrentAssistMode == TranslatorAssistMode.Off)
            responseStatusLabel.text = speakAndWrite
                ? "Say and draw the answer. No Buddy help in this check."
                : canWrite
                ? "Answer in English. No Buddy help in this check."
                : "Listen, understand, then speak your answer in English.";
        else if (writeOnly)
            responseStatusLabel.text = "Type the clean English answer.";
        else if (speakAndWrite)
            responseStatusLabel.text = "Say the answer, then draw the same answer.";
        else
            responseStatusLabel.text = canWrite
                ? "Use the mic or draw the clean English answer."
                : "Use the mic button to answer the NPC in English.";
        SetGrimoireButtonVisible(false);
    }

    void RefreshAskBuddyButton()
    {
        if (askBuddyButton == null)
            return;

        bool allowed = currentLine != null && currentBuddy != null &&
                       CurrentDialogueZoneKind() != SemanticZoneKind.Gym &&
                       (currentLine.overrideAssistMode ? currentLine.assistMode : currentBuddy.CurrentAssistMode) != TranslatorAssistMode.Off &&
                       currentLine.allowAiHint;
        askBuddyButton.gameObject.SetActive(allowed);
        askBuddyButton.interactable = allowed && !buddyCallSession.IsActive;
        if (askBuddyLabel != null)
            askBuddyLabel.text = buddyCallSession.IsActive
                ? "On Call"
                : "Call Buddy";
    }

    void SetResponseStatus(string status)
    {
        if (responseStatusLabel != null)
        {
            responseStatusLabel.text = status ?? "";
            responseStatusLabel.gameObject.SetActive(!string.IsNullOrWhiteSpace(status));
        }
        if (handwritingActive && handwritingStatusLabel != null)
            handwritingStatusLabel.text = status ?? "";
    }

    void ShowGrimoireButtonFor(TutorFeedbackPlan feedback)
    {
        if (currentLine == null || !currentLine.openGrimoireOnWrongAnswer)
        {
            SetGrimoireButtonVisible(false);
            return;
        }

        GrammarConceptId conceptId = feedback != null && feedback.conceptId != GrammarConceptId.None
            ? feedback.conceptId
            : currentLine.conceptId;
        GrammarGrimoirePage page = null;
        bool available = conceptId != GrammarConceptId.None && GrammarGrimoireCatalog.TryGetPage(conceptId, out page);
        SetGrimoireButtonVisible(available);
        if (available && grimoireLabel != null)
            grimoireLabel.text = string.IsNullOrWhiteSpace(page.title) ? "Grimoire" : "Grimoire";
    }

    void SetGrimoireButtonVisible(bool visible)
    {
        if (grimoireButton != null)
            grimoireButton.gameObject.SetActive(visible);
    }

    void HandleGrimoirePressed()
    {
        if (currentLine == null || currentLine.conceptId == GrammarConceptId.None)
            return;

        OpenGrimoireConcept(currentLine.conceptId, "grimoire_open");
    }

    void OpenGrimoireConcept(GrammarConceptId conceptId, string activityType, string highlightKey = "")
    {
        if (currentLine == null || conceptId == GrammarConceptId.None || conceptId != currentLine.conceptId)
            return;

        CurriculumSessionManager.Instance?.RecordBuddySupportEvent(
            string.IsNullOrWhiteSpace(activityType) ? "grimoire_open" : activityType,
            conceptId,
            string.IsNullOrWhiteSpace(currentLine.dialogueTaskId) ? currentLine.lineId : currentLine.dialogueTaskId,
            dialogueTaskId: string.IsNullOrWhiteSpace(currentLine.dialogueTaskId) ? currentLine.lineId : currentLine.dialogueTaskId,
            hintCount: 0,
            grimoireReference: string.IsNullOrWhiteSpace(highlightKey)
                ? $"grimoire:{conceptId}"
                : $"grimoire:{conceptId}#{highlightKey}",
            questionPrompt: ResolveNpcLine(currentLine));

        GrimoireUI grimoire = FindAnyObjectByType<GrimoireUI>();
        if (grimoire == null)
        {
            PlayerController player = FindAnyObjectByType<PlayerController>();
            grimoire = player != null ? player.GetComponent<GrimoireUI>() : null;
        }
        grimoire?.OpenConceptFor(conceptId, highlightKey);
    }
}
