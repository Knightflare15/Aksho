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
void EnsureBuilt()
    {
        if (canvas == null || panel == null)
            Build();
    }

    void Build()
    {
        if (canvas != null)
            return;

        GameObject root = new GameObject("NpcDialogueCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);
        canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 430;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        GameObject panelGo = new GameObject("DialoguePanel", typeof(RectTransform), typeof(Image), typeof(Outline));
        panelGo.transform.SetParent(root.transform, false);
        panel = panelGo.GetComponent<RectTransform>();
        panel.anchorMin = new Vector2(0.5f, 0f);
        panel.anchorMax = new Vector2(0.5f, 0f);
        panel.pivot = new Vector2(0.5f, 0f);
        panel.anchoredPosition = new Vector2(0f, 64f);
        panel.sizeDelta = new Vector2(980f, 500f);
        GameUiTheme.StyleHudPanel(panel, 0.94f);

        titleLabel = MakeLabel(panel, "Title", new Vector2(26f, -20f), new Vector2(-26f, -68f), 28f, true, GameUiTheme.Gold);
        bodyLabel = MakeLabel(panel, "Body", new Vector2(26f, -72f), new Vector2(-26f, -148f), 23f, false, GameUiTheme.Text);
        BuildInteractiveTranscript(panel);
        assistLabel = MakeLabel(panel, "Assist", new Vector2(26f, -154f), new Vector2(-220f, -262f), 21f, false, GameUiTheme.Text);
        responseStatusLabel = MakeLabel(panel, "ResponseStatus", new Vector2(26f, -154f), new Vector2(-26f, -218f), 19f, false, GameUiTheme.Text);
        assistLabel.gameObject.SetActive(false);
        responseStatusLabel.gameObject.SetActive(true);

        GameObject choices = new GameObject("SpokenAnswerChoices", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        choices.transform.SetParent(panel, false);
        spokenChoiceRoot = choices.GetComponent<RectTransform>();
        spokenChoiceRoot.anchorMin = new Vector2(0f, 0f);
        spokenChoiceRoot.anchorMax = new Vector2(1f, 0f);
        spokenChoiceRoot.pivot = new Vector2(0.5f, 0f);
        spokenChoiceRoot.offsetMin = new Vector2(26f, 158f);
        spokenChoiceRoot.offsetMax = new Vector2(-26f, 224f);
        HorizontalLayoutGroup choiceLayout = choices.GetComponent<HorizontalLayoutGroup>();
        choiceLayout.spacing = 12f;
        choiceLayout.childControlWidth = true;
        choiceLayout.childControlHeight = true;
        choiceLayout.childForceExpandWidth = true;
        choiceLayout.childForceExpandHeight = true;
        choices.SetActive(false);

        textResponseInput = MakeInput(panel, "TextResponseInput", new Vector2(26f, 28f), new Vector2(520f, 54f));
        textResponseInput.gameObject.SetActive(false);

        speakButton = MakeButton(panel, "SpeakButton", "Speak", new Vector2(-362f, 28f), new Vector2(150f, 56f), out speakLabel);
        speakButton.onClick.AddListener(HandleSpeakPressed);
        speakResponseButton = MakeButton(panel, "SpeakResponseButton", "Answer", new Vector2(-194f, 28f), new Vector2(150f, 56f), out speakResponseLabel);
        AddHoldToTalkHandlers(speakResponseButton);
        submitTextResponseButton = MakeButton(panel, "SubmitTextResponseButton", "Submit", new Vector2(-194f, 92f), new Vector2(150f, 48f), out submitTextResponseLabel);
        submitTextResponseButton.onClick.AddListener(HandleTextResponsePressed);
        submitTextResponseButton.gameObject.SetActive(false);
        grimoireButton = MakeButton(panel, "GrimoireButton", "Grimoire", new Vector2(-362f, 92f), new Vector2(150f, 48f), out grimoireLabel);
        grimoireButton.onClick.AddListener(HandleGrimoirePressed);
        grimoireButton.gameObject.SetActive(false);
        askBuddyButton = MakeButton(panel, "AskBuddyButton", "Ask Buddy", new Vector2(-26f, 92f), new Vector2(150f, 48f), out askBuddyLabel);
        askBuddyButton.onClick.AddListener(HandleAskBuddyPressed);
        askBuddyButton.gameObject.SetActive(false);
        hintButton = MakeButton(panel, "HintButton", "Hint", new Vector2(-530f, 92f), new Vector2(150f, 48f), out _);
        hintButton.onClick.AddListener(HandleHintPressed);
        wordMeaningButton = MakeButton(panel, "WordMeaningButton", "Ask meaning", new Vector2(-26f, 236f), new Vector2(190f, 42f), out wordMeaningLabel);
        wordMeaningButton.onClick.AddListener(HandleWordMeaningPressed);
        wordMeaningButton.gameObject.SetActive(false);
        closeButton = MakeButton(panel, "CloseButton", "Close", new Vector2(-26f, 28f), new Vector2(150f, 56f), out _);
        closeButton.onClick.AddListener(Hide);

        BuildHandwritingOverlay();
        BuildScrambleOverlay();
    }

    void BuildInteractiveTranscript(RectTransform parent)
    {
        if (parent == null || transcriptViewport != null)
            return;

        transcriptViewport = new GameObject(
            "InteractiveTranscript",
            typeof(RectTransform),
            typeof(Image),
            typeof(RectMask2D),
            typeof(ScrollRect));
        transcriptViewport.transform.SetParent(parent, false);
        RectTransform viewportRect = transcriptViewport.GetComponent<RectTransform>();
        viewportRect.anchorMin = new Vector2(0f, 1f);
        viewportRect.anchorMax = new Vector2(1f, 1f);
        viewportRect.pivot = new Vector2(0.5f, 1f);
        viewportRect.offsetMin = new Vector2(26f, -148f);
        viewportRect.offsetMax = new Vector2(-26f, -72f);
        transcriptViewport.GetComponent<Image>().color = new Color(0.035f, 0.08f, 0.12f, 0.72f);

        GameObject content = new GameObject(
            "TranscriptWords",
            typeof(RectTransform),
            typeof(HorizontalLayoutGroup),
            typeof(ContentSizeFitter));
        content.transform.SetParent(transcriptViewport.transform, false);
        transcriptWordRoot = content.GetComponent<RectTransform>();
        transcriptWordRoot.anchorMin = new Vector2(0f, 0f);
        transcriptWordRoot.anchorMax = new Vector2(0f, 1f);
        transcriptWordRoot.pivot = new Vector2(0f, 0.5f);
        transcriptWordRoot.anchoredPosition = Vector2.zero;
        transcriptWordRoot.sizeDelta = Vector2.zero;

        HorizontalLayoutGroup layout = content.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.padding = new RectOffset(8, 8, 9, 9);
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

        ScrollRect scroll = transcriptViewport.GetComponent<ScrollRect>();
        scroll.viewport = viewportRect;
        scroll.content = transcriptWordRoot;
        scroll.horizontal = true;
        scroll.vertical = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 36f;
        transcriptViewport.SetActive(false);
    }

    void AddHoldToTalkHandlers(Button button)
    {
        if (button == null)
            return;

        EventTrigger trigger = button.gameObject.GetComponent<EventTrigger>() ?? button.gameObject.AddComponent<EventTrigger>();
        trigger.triggers ??= new List<EventTrigger.Entry>();

        EventTrigger.Entry down = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
        down.callback.AddListener(_ => HandleSpeakResponsePressed());
        trigger.triggers.Add(down);

        EventTrigger.Entry up = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
        up.callback.AddListener(_ => HandleSpeakResponseReleased());
        trigger.triggers.Add(up);

        EventTrigger.Entry exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        exit.callback.AddListener(_ => HandleSpeakResponseReleased());
        trigger.triggers.Add(exit);
    }

    void HandleSpeakResponseReleased()
    {
        if (responseRecognizer != null && responseRecognizer.IsListening)
        {
            Debug.Log("[NpcDialogueUI] Answer released. Finalizing speech capture.");
            responseRecognizer.FinishListeningAttempt();
            SetResponseStatus("Processing your response...");
        }
    }

    static TextMeshProUGUI MakeChoiceLabel(Transform parent, string value)
    {
        GameObject labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(parent, false);
        TextMeshProUGUI label = labelGo.GetComponent<TextMeshProUGUI>();
        label.text = value ?? "";
        label.alignment = TextAlignmentOptions.Center;
        label.verticalAlignment = VerticalAlignmentOptions.Middle;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = new Vector2(10f, 4f);
        label.rectTransform.offsetMax = new Vector2(-10f, -4f);
        GameUiTheme.StyleText(label, 18f, true);
        return label;
    }

    void ConfigureInteractiveTranscript()
    {
        ClearInteractiveTranscript();
        string transcript = ResolveNpcLine(currentLine);
        transcriptWords.AddRange(DialogueTranscriptTokenizer.Tokenize(transcript));
        bool hasWords = transcriptWordRoot != null && transcriptWords.Count > 0;
        if (bodyLabel != null)
            bodyLabel.gameObject.SetActive(!hasWords);
        if (transcriptViewport != null)
            transcriptViewport.SetActive(hasWords);
        if (!hasWords)
            return;

        for (int index = 0; index < transcriptWords.Count; index++)
        {
            string word = transcriptWords[index];
            int capturedIndex = index;
            GameObject wordObject = new GameObject(
                $"TranscriptWord_{index}",
                typeof(RectTransform),
                typeof(Image),
                typeof(Button),
                typeof(LayoutElement));
            wordObject.transform.SetParent(transcriptWordRoot, false);
            LayoutElement element = wordObject.GetComponent<LayoutElement>();
            element.preferredWidth = Mathf.Clamp(34f + word.Length * 14f, 70f, 190f);
            element.preferredHeight = 54f;
            Button button = wordObject.GetComponent<Button>();
            GameUiTheme.StyleButton(button, GameUiTheme.ButtonRole.Secondary);
            MakeChoiceLabel(wordObject.transform, word);
            button.onClick.AddListener(() => SelectTranscriptWord(capturedIndex));
            transcriptWordButtons.Add(button);
        }
    }

    void ClearInteractiveTranscript()
    {
        foreach (Button button in transcriptWordButtons)
            if (button != null) Destroy(button.gameObject);
        transcriptWordButtons.Clear();
        transcriptWords.Clear();
        selectedTranscriptWordIndex = -1;
        selectedLearningText = "";
        if (wordMeaningButton != null)
            wordMeaningButton.gameObject.SetActive(false);
    }

    void SelectTranscriptWord(int index)
    {
        if (index < 0 || index >= transcriptWords.Count)
            return;

        selectedTranscriptWordIndex = index;
        SelectLearningText(transcriptWords[index], speak: true);
        for (int i = 0; i < transcriptWordButtons.Count; i++)
        {
            Image image = transcriptWordButtons[i] != null ? transcriptWordButtons[i].GetComponent<Image>() : null;
            if (image != null)
                image.color = i == index
                    ? new Color(0.53f, 0.34f, 0.08f, 1f)
                    : new Color(0.06f, 0.10f, 0.18f, 0.98f);
        }
    }

    void SelectLearningText(string text, bool speak)
    {
        selectedLearningText = text?.Trim() ?? "";
        if (wordMeaningButton != null)
            wordMeaningButton.gameObject.SetActive(!string.IsNullOrWhiteSpace(selectedLearningText));
        if (wordMeaningLabel != null)
            wordMeaningLabel.text = "Ask meaning";
        if (speak && !string.IsNullOrWhiteSpace(selectedLearningText))
        {
            wordInteractionService ??= DialogueWordInteractionServiceFactory.Create();
            wordInteractionService.Speak(selectedLearningText);
            SetResponseStatus($"Heard: {selectedLearningText}. Tap Ask meaning for an explanation.");
        }
    }

    void HandleWordMeaningPressed()
    {
        if (currentLine == null || string.IsNullOrWhiteSpace(selectedLearningText))
            return;

        wordInteractionService ??= DialogueWordInteractionServiceFactory.Create();
        string meaning = wordInteractionService.GetMeaning(selectedLearningText, currentLine.conceptId);
        string localExplanation = string.IsNullOrWhiteSpace(meaning)
            ? $"Buddy can explain '{selectedLearningText}' in this sentence."
            : $"'{selectedLearningText}' means {meaning}.";
        BuddyHelpWindow.EnsureExists().Show(localExplanation);
        SetResponseStatus(localExplanation);

        bool canAskBuddy = currentBuddy != null &&
                           CurrentDialogueZoneKind() != SemanticZoneKind.Gym &&
                           currentLine.allowAiHint;
        if (!canAskBuddy)
            return;

        string question = $"What does '{selectedLearningText}' mean in this NPC sentence? Explain it simply.";
        RequestBuddyWordMeaning(question);
        SetResponseStatus($"{localExplanation} Buddy is adding a spoken example on the call...");
    }
}
