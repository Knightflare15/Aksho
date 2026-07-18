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
void HandleSpeakResponsePressed()
    {
        if (currentLine == null || string.IsNullOrWhiteSpace(currentLine.expectedEnglishResponse))
        {
            SetResponseStatus("No spoken response needed.");
            return;
        }

        if (currentLine.inputMode == GrammarDialogueInputMode.WriteOnly)
        {
            SetResponseStatus("This task needs a drawn answer.");
            return;
        }

        if (UsesSpokenAnswerChoices() && string.IsNullOrWhiteSpace(selectedSpokenChoice))
        {
            SetResponseStatus("Choose an answer first. Tap an option to hear it.");
            return;
        }

        if (buddyCallSession.IsActive)
        {
            if (buddyCallSession.State != BuddyCallState.ReadyToTalk)
            {
                SetResponseStatus("Wait for Buddy to finish, then hold Answer.");
                return;
            }
            buddySpeechOutput?.Stop();
            SetBuddyCallState(BuddyCallState.TaskAnswering);
        }

        EnsureResponseRecognizer();
        // Never let an option preview or Buddy TTS bleed into the learner's
        // microphone capture.
        PronunciationSpeaker.EnsureExists().StopSpeaking();
        lastTutorFeedback = null;
        lastResponsePronunciationInsight = null;
        Debug.Log("[NpcDialogueUI] Answer pressed. Starting speech capture.");
        responseRecognizer.StartListening(VoiceUnlockRecognizer.VoiceInputMode.Manual);
        SetResponseStatus("Listening for your English response...");
    }

    void HandleTextResponsePressed()
    {
        if (UsesTypedSentenceInput(currentLine))
        {
            HandleTypedSentenceResponse();
            return;
        }
        HandleDrawResponsePressed();
    }

    void HandleTypedSentenceResponse()
    {
        if (currentLine == null || textResponseInput == null)
            return;

        string submitted = textResponseInput.text ?? "";
        bool correct = IsAcceptedTypedResponse(currentLine, submitted);
        if (correct)
        {
            RecordWrittenPhrase(submitted, true, "");
            textResponseInput.text = "";
            if (currentLine.inputMode == GrammarDialogueInputMode.SpeakAndWrite)
            {
                speakAndWriteTextAccepted = true;
                TryCompleteSpeakAndWrite();
            }
            else
            {
                CompleteAcceptedResponse();
            }
            return;
        }

        string rejectionReason = BuildRejectionReason(currentLine, spoken: false);
        TutorFeedbackPlan feedback = currentBuddy != null
            ? currentBuddy.BuildTutorFeedback(currentLine, submitted, rejectionReason)
            : null;
        lastTutorFeedback = feedback;
        SetResponseStatus(BuildCorrectionStatus(currentLine, submitted, rejectionReason, feedback));
        ShowGrimoireButtonFor(feedback);
        RecordWrittenPhrase(submitted, false, rejectionReason, feedback);
        RequestAutomaticBuddyCoach(submitted);
    }

    void HandleDrawResponsePressed()
    {
        if (currentLine == null || string.IsNullOrWhiteSpace(currentLine.expectedEnglishResponse))
        {
            SetResponseStatus("No drawing response needed.");
            return;
        }

        if (currentLine.malfunctionType == GrammarDialogueMalfunctionType.ScrambledSentence)
        {
            BeginScrambleResponse();
            return;
        }

        if (currentLine.inputMode == GrammarDialogueInputMode.SpeakOnly)
        {
            SetResponseStatus("This task needs a spoken answer.");
            return;
        }

        if (currentLine.inputMode == GrammarDialogueInputMode.SpeakAndWrite && !speakAndWriteSpeechAccepted)
        {
            SetResponseStatus("Say the answer first, then draw the same answer.");
            return;
        }

        BeginHandwritingResponse();
    }

    void HandleResponseResolved(VoiceUnlockRecognizer.RecognitionEvent evt)
    {
        if (evt.Mode == VoiceUnlockRecognizer.VoiceInputMode.BuddyConversation)
            return;

        if (currentLine == null || string.IsNullOrWhiteSpace(currentLine.expectedEnglishResponse))
            return;

        if (currentLine.inputMode == GrammarDialogueInputMode.WriteOnly)
        {
            string spokenAttempt = string.IsNullOrWhiteSpace(evt.RawText) ? evt.Text : evt.RawText;
            TutorFeedbackPlan inputModeFeedback = currentBuddy != null
                ? currentBuddy.BuildTutorFeedback(currentLine, spokenAttempt, "input_mode_write_required", ResolveLatestPronunciationInsight())
                : null;
            lastTutorFeedback = inputModeFeedback;
            SetResponseStatus(BuildCorrectionStatus(currentLine, spokenAttempt, "input_mode_write_required", inputModeFeedback));
            ShowGrimoireButtonFor(inputModeFeedback);
            RecordSpokenPhrase(spokenAttempt, false, "input_mode_write_required", ResolveLatestPronunciationInsight(), inputModeFeedback);
            RequestAutomaticBuddyCoach(spokenAttempt);
            return;
        }

        if (evt.Recognized)
        {
            if (UsesSpokenAnswerChoices() && !IsAcceptedResponse(currentLine, selectedSpokenChoice))
            {
                string choiceRejectionReason = BuildRejectionReason(currentLine, spoken: true);
                TutorFeedbackPlan choiceFeedback = currentBuddy != null
                    ? currentBuddy.BuildTutorFeedback(currentLine, selectedSpokenChoice, choiceRejectionReason, null)
                    : null;
                lastTutorFeedback = choiceFeedback;
                SetResponseStatus("That option was spoken clearly, but it does not answer this question. Pick another option.");
                ShowGrimoireButtonFor(choiceFeedback);
                RecordSpokenPhrase(selectedSpokenChoice, false, choiceRejectionReason, null, choiceFeedback);
                RequestAutomaticBuddyCoach(selectedSpokenChoice);
                if (buddyCallSession.IsActive && buddyCallSession.State == BuddyCallState.TaskAnswering)
                    SetBuddyCallState(BuddyCallState.ReadyToTalk);
                return;
            }
            RecordSpokenPhrase(evt.Text, true, "", ResolveLatestPronunciationInsight());
            if (currentLine.inputMode == GrammarDialogueInputMode.SpeakAndWrite)
            {
                speakAndWriteSpeechAccepted = true;
                if (speakAndWriteTextAccepted)
                {
                    TryCompleteSpeakAndWrite();
                }
                else if (UsesTypedSentenceInput(currentLine))
                {
                    SetResponseStatus("Speech accepted. Now type it with the capital and full stop.");
                    textResponseInput?.ActivateInputField();
                }
                else
                {
                    SetResponseStatus("Speech accepted. Now draw the same answer.");
                    BeginHandwritingResponse();
                }
            }
            else
            {
                CompleteAcceptedResponse();
            }
            return;
        }

        string heard = string.IsNullOrWhiteSpace(evt.RawText) ? "nothing clear" : evt.RawText;
        string rejectionReason = BuildRejectionReason(currentLine, spoken: true);
        PronunciationInsightResult? pronunciationInsight = ResolveLatestPronunciationInsight();
        TutorFeedbackPlan feedback = currentBuddy != null
            ? currentBuddy.BuildTutorFeedback(currentLine, heard, rejectionReason, pronunciationInsight)
            : null;
        lastTutorFeedback = feedback;
        SetResponseStatus(BuildCorrectionStatus(currentLine, heard, rejectionReason, feedback));
        ShowGrimoireButtonFor(feedback);
        RecordSpokenPhrase(evt.RawText, false, rejectionReason, pronunciationInsight, feedback);
        RequestAutomaticBuddyCoach(heard);
        if (buddyCallSession.IsActive && buddyCallSession.State == BuddyCallState.TaskAnswering)
            SetBuddyCallState(BuddyCallState.ReadyToTalk);
    }

    void HandleLineResolved(LocalizedDialogueLine line)
    {
        if (currentLine == null || line == null || line.lineId != currentLine.lineId)
            return;

        currentLine = line;
        ConfigureInteractiveTranscript();
        RefreshAssistText();
    }

    void ConfigureResponseRecognizer()
    {
        EnsureResponseRecognizer();
        List<string> accepted = UsesSpokenAnswerChoices() && !string.IsNullOrWhiteSpace(selectedSpokenChoice)
            ? new List<string> { selectedSpokenChoice }
            : UsesSpokenAnswerChoices() ? new List<string>() : BuildAcceptedResponses(currentLine);
        responseRecognizer.ConfigureKeywords(
            accepted,
            UsesSpokenAnswerChoices() ? selectedSpokenChoice : currentLine != null ? currentLine.expectedEnglishResponse : "",
            false);
        RefreshResponseStatus();
    }

    void ConfigureSpokenAnswerChoices()
    {
        ClearSpokenAnswerChoiceButtons();
        currentSpokenChoices.Clear();
        selectedSpokenChoice = "";
        if (spokenChoiceRoot == null || !ShouldOfferSpokenAnswerChoices())
        {
            if (spokenChoiceRoot != null) spokenChoiceRoot.gameObject.SetActive(false);
            return;
        }

        currentSpokenChoices.AddRange(NpcSpokenAnswerChoiceBuilder.Build(currentLine));
        bool visible = currentSpokenChoices.Count >= 2;
        spokenChoiceRoot.gameObject.SetActive(visible);
        if (!visible) return;
        foreach (string choice in currentSpokenChoices)
            MakeSpokenAnswerChoiceButton(choice);
    }

    bool ShouldOfferSpokenAnswerChoices()
    {
        if (currentLine == null || !currentLine.useSpokenAnswerChoices ||
            string.IsNullOrWhiteSpace(currentLine.expectedEnglishResponse) ||
            currentLine.inputMode == GrammarDialogueInputMode.WriteOnly ||
            currentLine.inputMode == GrammarDialogueInputMode.None ||
            currentLine.malfunctionType == GrammarDialogueMalfunctionType.ScrambledSentence)
            return false;
        // Choice selection plus audio preview turns the Gym into recognition
        // practice and reveals the response set. Assessment requires free
        // production from the learner.
        if (CurrentDialogueZoneKind() == SemanticZoneKind.Gym)
            return false;
        return !IsRecognitionEncounterLine(currentNpc, currentLine);
    }

    bool UsesSpokenAnswerChoices()
    {
        return spokenChoiceRoot != null && spokenChoiceRoot.gameObject.activeSelf && currentSpokenChoices.Count >= 2;
    }

    void MakeSpokenAnswerChoiceButton(string choice)
    {
        GameObject go = new GameObject("SpokenChoice", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(spokenChoiceRoot, false);
        LayoutElement layout = go.GetComponent<LayoutElement>();
        layout.flexibleWidth = 1f;
        layout.preferredHeight = 62f;
        Button button = go.GetComponent<Button>();
        GameUiTheme.StyleButton(button, GameUiTheme.ButtonRole.Secondary);
        TextMeshProUGUI label = MakeChoiceLabel(go.transform, choice);
        button.onClick.AddListener(() => SelectSpokenAnswerChoice(choice));
        spokenChoiceButtons.Add(button);
    }

    void SelectSpokenAnswerChoice(string choice)
    {
        selectedSpokenChoice = choice?.Trim() ?? "";
        selectedTranscriptWordIndex = -1;
        for (int i = 0; i < spokenChoiceButtons.Count; i++)
        {
            Image image = spokenChoiceButtons[i] != null ? spokenChoiceButtons[i].GetComponent<Image>() : null;
            if (image != null)
                image.color = i < currentSpokenChoices.Count && string.Equals(currentSpokenChoices[i], selectedSpokenChoice, StringComparison.Ordinal)
                    ? new Color(0.53f, 0.34f, 0.08f, 1f)
                    : new Color(0.06f, 0.10f, 0.18f, 0.98f);
        }
        if (responseRecognizer == null || responseRecognizer.ActiveMode != VoiceUnlockRecognizer.VoiceInputMode.BuddyConversation)
            ConfigureResponseRecognizer();
        SelectLearningText(selectedSpokenChoice, speak: true);
        SetResponseStatus($"Selected: {selectedSpokenChoice}. Hold Answer when ready.");
    }

    void ClearSpokenAnswerChoiceButtons()
    {
        foreach (Button button in spokenChoiceButtons)
            if (button != null) Destroy(button.gameObject);
        spokenChoiceButtons.Clear();
    }

    void ConfigureTextResponseInput()
    {
        bool expectsText = currentLine != null &&
            !string.IsNullOrWhiteSpace(currentLine.expectedEnglishResponse) &&
            (currentLine.inputMode == GrammarDialogueInputMode.WriteOnly ||
             currentLine.inputMode == GrammarDialogueInputMode.SpeakOrWrite ||
             currentLine.inputMode == GrammarDialogueInputMode.SpeakAndWrite);

        bool typedSentence = expectsText && UsesTypedSentenceInput(currentLine);
        if (textResponseInput != null)
        {
            // Sentence-shape tasks need case and punctuation evidence, which a
            // letter-only handwriting recognizer cannot represent reliably.
            textResponseInput.gameObject.SetActive(typedSentence);
            textResponseInput.text = "";
            textResponseInput.placeholder.GetComponent<TextMeshProUGUI>().text =
                typedSentence
                    ? "Type the full sentence with a capital and full stop"
                    : currentLine != null && currentLine.malfunctionType == GrammarDialogueMalfunctionType.MissingWord
                    ? "Draw the missing word or full answer"
                    : currentLine != null && currentLine.inputMode == GrammarDialogueInputMode.SpeakAndWrite
                        ? "Draw the same answer after speaking it"
                        : "Draw your English answer";
        }

        if (submitTextResponseButton != null)
            submitTextResponseButton.gameObject.SetActive(expectsText);
        if (submitTextResponseLabel != null)
            submitTextResponseLabel.text = typedSentence
                ? "Check Sentence"
                : currentLine?.malfunctionType == GrammarDialogueMalfunctionType.ScrambledSentence
                ? "Unscramble"
                : "Draw Answer";
    }

    void TryCompleteSpeakAndWrite()
    {
        if (currentLine == null || currentLine.inputMode != GrammarDialogueInputMode.SpeakAndWrite)
            return;

        if (speakAndWriteSpeechAccepted && speakAndWriteTextAccepted)
        {
            CompleteAcceptedResponse();
            return;
        }

        if (speakAndWriteSpeechAccepted)
            SetResponseStatus(UsesTypedSentenceInput(currentLine)
                ? "Speech accepted. Now type the same answer."
                : "Speech accepted. Now draw the same answer.");
        else if (speakAndWriteTextAccepted)
            SetResponseStatus("Writing accepted. Now say the same answer.");
    }

    void CompleteAcceptedResponse()
    {
        if (currentLine == null)
            return;

        bool closeBuddyCall = buddyCallSession.IsActive;
        SetResponseStatus($"Accepted: {currentLine.expectedEnglishResponse}");
        SetGrimoireButtonVisible(false);
        currentBuddy?.RegisterSuccessfulResponse(currentLine);
        lastTutorFeedback = null;
        GrammarNpc acceptedNpc = currentNpc;
        LocalizedDialogueLine acceptedLine = currentLine;
        Hide();
        learningFeedback?.PlaySuccessFeedback();
        learningFeedback?.ShowLargeFeedbackPopup(
            "Nice work!",
            string.IsNullOrWhiteSpace(acceptedLine.expectedEnglishResponse)
                ? "Practice step complete."
                : $"{acceptedLine.expectedEnglishResponse} — practice step complete.",
            new Color(0.28f, 0.92f, 0.48f, 1f),
            1.8f);
        if (closeBuddyCall)
        {
            buddySpeechOutput ??= BuddySpeechOutputFactory.Create();
            buddySpeechOutput.Speak("Great work! You found the right answer. Happy to help!", "en");
        }
        acceptedNpc?.HandleDialogueResponseAccepted(acceptedLine);
    }

    void EnsureResponseRecognizer()
    {
        if (responseRecognizer != null)
            return;

        responseRecognizer = gameObject.AddComponent<VoiceUnlockRecognizer>();
        responseRecognizer.CombatPronunciationInsightEnabled = true;
        responseRecognizer.OnRecognitionResolved += HandleResponseResolved;
        responseRecognizer.OnPronunciationInsightReady += HandlePronunciationInsightReady;
    }

    void HandlePronunciationInsightReady(PronunciationInsightResult insight)
    {
        lastResponsePronunciationInsight = insight;
    }

    static List<string> BuildAcceptedResponses(LocalizedDialogueLine line)
    {
        var accepted = new List<string>();
        if (line == null)
            return accepted;

        AddResponse(accepted, line.expectedEnglishResponse);
        AddMissingWordResponses(accepted, line, line.expectedEnglishResponse);
        if (line.acceptedEnglishResponses != null)
        {
            foreach (string response in line.acceptedEnglishResponses)
            {
                AddResponse(accepted, response);
                AddMissingWordResponses(accepted, line, response);
            }
        }
        return accepted;
    }

    static void AddResponse(List<string> responses, string response)
    {
        string normalized = VoiceUnlockRecognizer.NormalizeKeyword(response);
        if (!string.IsNullOrWhiteSpace(normalized) && !responses.Contains(normalized))
            responses.Add(normalized);
    }

    static void AddMissingWordResponses(List<string> responses, LocalizedDialogueLine line, string fullResponse)
    {
        if (line == null ||
            line.malfunctionType != GrammarDialogueMalfunctionType.MissingWord ||
            string.IsNullOrWhiteSpace(fullResponse))
            return;

        List<string> templateTokens = TokenizeMissingWordTemplate(ResolveNpcLine(line));
        if (templateTokens.Count == 0 || !templateTokens.Contains("____"))
            return;

        List<string> responseTokens = TokenizeResponse(fullResponse);
        if (responseTokens.Count != templateTokens.Count)
            return;

        var missing = new List<string>();
        for (int i = 0; i < templateTokens.Count; i++)
        {
            if (templateTokens[i] == "____")
            {
                missing.Add(responseTokens[i]);
                continue;
            }

            if (templateTokens[i] != responseTokens[i])
                return;
        }

        if (missing.Count > 0)
            AddResponse(responses, string.Join(" ", missing));
    }

    static List<string> TokenizeMissingWordTemplate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new List<string>();

        int colon = value.LastIndexOf(':');
        if (colon >= 0 && value.IndexOf("____", colon, StringComparison.Ordinal) >= 0)
            value = value.Substring(colon + 1);

        var tokens = new List<string>();
        var builder = new System.Text.StringBuilder();
        for (int i = 0; i < value.Length; i++)
        {
            char character = value[i];
            if (character == '_')
            {
                FlushToken(tokens, builder);
                while (i + 1 < value.Length && value[i + 1] == '_')
                    i++;
                tokens.Add("____");
                continue;
            }

            if (char.IsLetterOrDigit(character))
                builder.Append(char.ToUpperInvariant(character));
            else
                FlushToken(tokens, builder);
        }

        FlushToken(tokens, builder);
        return tokens;
    }

    static List<string> TokenizeResponse(string response)
    {
        string normalized = NormalizeResponse(response);
        var tokens = new List<string>();
        if (string.IsNullOrWhiteSpace(normalized))
            return tokens;

        string[] parts = normalized.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string part in parts)
            tokens.Add(part);
        return tokens;
    }

    static void FlushToken(List<string> tokens, System.Text.StringBuilder builder)
    {
        if (builder.Length == 0)
            return;

        tokens.Add(builder.ToString());
        builder.Length = 0;
    }
}
