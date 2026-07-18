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
void BuildHandwritingOverlay()
    {
        GameObject overlay = new GameObject("HandwritingResponseOverlay", typeof(RectTransform), typeof(Image));
        overlay.transform.SetParent(canvas.transform, false);
        handwritingOverlay = overlay;
        RectTransform overlayRect = overlay.GetComponent<RectTransform>();
        overlayRect.anchorMin = new Vector2(0.5f, 0.5f);
        overlayRect.anchorMax = new Vector2(0.5f, 0.5f);
        overlayRect.pivot = new Vector2(0.5f, 0.5f);
        overlayRect.anchoredPosition = Vector2.zero;
        overlayRect.sizeDelta = new Vector2(1240f, 900f);
        overlay.GetComponent<Image>().color = new Color(0.02f, 0.06f, 0.09f, 0.98f);

        handwritingPromptLabel = MakeOverlayLabel(overlayRect, "Prompt", new Vector2(0f, 390f), new Vector2(1120f, 64f), 26f, true);
        handwritingWordLabel = MakeOverlayLabel(overlayRect, "Word", new Vector2(0f, 335f), new Vector2(820f, 44f), 22f, true);
        handwritingHintLabel = MakeOverlayLabel(overlayRect, "Hint", new Vector2(0f, -338f), new Vector2(900f, 46f), 20f, false);
        handwritingStatusLabel = MakeOverlayLabel(overlayRect, "Status", new Vector2(0f, -382f), new Vector2(900f, 42f), 18f, false);

        GameObject surface = new GameObject("HandwritingSurface", typeof(RectTransform), typeof(Image));
        surface.transform.SetParent(overlay.transform, false);
        handwritingSurface = surface.GetComponent<RectTransform>();
        handwritingSurface.anchorMin = new Vector2(0.5f, 0.5f);
        handwritingSurface.anchorMax = new Vector2(0.5f, 0.5f);
        handwritingSurface.pivot = new Vector2(0.5f, 0.5f);
        handwritingSurface.anchoredPosition = new Vector2(0f, -10f);
        handwritingSurface.sizeDelta = new Vector2(820f, 600f);
        surface.GetComponent<Image>().color = new Color(0.96f, 0.97f, 0.92f, 1f);

        GameObject point = new GameObject("HandwritingPointPrefab", typeof(RectTransform), typeof(Image));
        point.transform.SetParent(overlay.transform, false);
        pointPrefabForDraw = point;
        point.SetActive(false);
        point.GetComponent<Image>().color = Color.black;

        handwritingConfirmButton = MakeButton(overlayRect, "ConfirmLetter", "Confirm Letter", new Vector2(-492f, 18f), new Vector2(184f, 54f), out _);
        handwritingDeleteButton = MakeButton(overlayRect, "DeleteLetter", "Undo Letter", new Vector2(-292f, 18f), new Vector2(160f, 54f), out _);
        handwritingSubmitButton = MakeButton(overlayRect, "SubmitDrawing", "Submit Drawing", new Vector2(-116f, 18f), new Vector2(184f, 54f), out _);
        handwritingCancelButton = MakeButton(overlayRect, "CancelDrawing", "Cancel", new Vector2(-26f, 18f), new Vector2(150f, 54f), out _);

        handwritingConfirmButton.onClick.AddListener(() => handwritingDrawController?.ConfirmCurrentLetterFromButton());
        handwritingDeleteButton.onClick.AddListener(() => handwritingDrawController?.DeleteLastLetterFromButton());
        handwritingSubmitButton.onClick.AddListener(() => handwritingDrawController?.SubmitWordFromButton());
        handwritingCancelButton.onClick.AddListener(CancelHandwritingResponse);
        handwritingOverlay.SetActive(false);
    }

    void BuildScrambleOverlay()
    {
        GameObject overlay = new GameObject("SentenceScrambleOverlay", typeof(RectTransform), typeof(Image));
        overlay.transform.SetParent(canvas.transform, false);
        scrambleOverlay = overlay;
        RectTransform overlayRect = overlay.GetComponent<RectTransform>();
        overlayRect.anchorMin = new Vector2(0.5f, 0.5f);
        overlayRect.anchorMax = new Vector2(0.5f, 0.5f);
        overlayRect.pivot = new Vector2(0.5f, 0.5f);
        overlayRect.anchoredPosition = Vector2.zero;
        overlayRect.sizeDelta = new Vector2(1240f, 720f);
        overlay.GetComponent<Image>().color = new Color(0.02f, 0.06f, 0.09f, 0.98f);

        scramblePromptLabel = MakeOverlayLabel(overlayRect, "Prompt", new Vector2(0f, 300f), new Vector2(1120f, 90f), 25f, true);
        MakeOverlayLabel(overlayRect, "BankLabel", new Vector2(0f, 220f), new Vector2(1000f, 36f), 18f, true).text = "Word bank";
        MakeOverlayLabel(overlayRect, "DropLabel", new Vector2(0f, 62f), new Vector2(1000f, 36f), 18f, true).text = "Your sentence";
        scrambleStatusLabel = MakeOverlayLabel(overlayRect, "Status", new Vector2(0f, -282f), new Vector2(1080f, 48f), 19f, false);

        scrambleDragRoot = overlayRect;
        scrambleBank = CreateTileContainer(overlayRect, "WordBank", new Vector2(0f, 155f), new Vector2(1050f, 110f), new Color(0.08f, 0.14f, 0.18f, 1f));
        scrambleDropZone = CreateTileContainer(overlayRect, "DropZone", new Vector2(0f, -30f), new Vector2(1050f, 130f), new Color(0.12f, 0.20f, 0.16f, 1f));

        scrambleSubmitButton = MakeButton(overlayRect, "SubmitSentence", "Check Sentence", new Vector2(-216f, 18f), new Vector2(190f, 54f), out _);
        scrambleCancelButton = MakeButton(overlayRect, "CancelSentence", "Cancel", new Vector2(-26f, 18f), new Vector2(150f, 54f), out _);
        scrambleSubmitButton.onClick.AddListener(SubmitScrambleResponse);
        scrambleCancelButton.onClick.AddListener(CancelScrambleResponse);
        scrambleOverlay.SetActive(false);
    }

    RectTransform CreateTileContainer(RectTransform parent, string name, Vector2 position, Vector2 size, Color color)
    {
        GameObject container = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup));
        container.transform.SetParent(parent, false);
        RectTransform rect = container.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        container.GetComponent<Image>().color = color;
        HorizontalLayoutGroup layout = container.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 12f;
        layout.padding = new RectOffset(18, 18, 14, 14);
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        return rect;
    }

    void BeginScrambleResponse()
    {
        if (scrambleActive || currentLine == null)
            return;

        scrambleActive = true;
        panel.gameObject.SetActive(false);
        scrambleOverlay.SetActive(true);
        scramblePromptLabel.text = ResolveNpcLine(currentLine);
        scrambleStatusLabel.text = "Build the sentence. One signal word may be fake. Green means right word, right place.";
        ClearScrambleTiles();

        List<string> words = DialogueSentenceJumble.BuildWordBank(
            currentLine.expectedEnglishResponse,
            currentLine.conceptId,
            currentLine.grammarPattern,
            currentLine.lineId,
            currentLine.jumbleDistractorWords,
            maximumDistractors: 1);
        foreach (string word in words)
            CreateScrambleTile(word);
    }

    void CreateScrambleTile(string word)
    {
        GameObject tileObject = new GameObject($"WordTile_{word}", typeof(RectTransform), typeof(Image), typeof(CanvasGroup), typeof(NpcWordTile));
        tileObject.transform.SetParent(scrambleBank, false);
        RectTransform rect = tileObject.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(Mathf.Max(92f, 24f + word.Length * 18f), 64f);
        tileObject.GetComponent<Image>().color = new Color(0.22f, 0.40f, 0.58f, 1f);
        NpcWordTile tile = tileObject.GetComponent<NpcWordTile>();
        tile.Configure(this, word);
        scrambleTiles.Add(tile);
    }

    public void StartTileDrag(NpcWordTile tile)
    {
        if (tile == null)
            return;

        tile.transform.SetParent(scrambleDragRoot, true);
        tile.GetComponent<CanvasGroup>().blocksRaycasts = false;
    }

    public void DragTile(NpcWordTile tile, PointerEventData eventData)
    {
        if (tile == null || eventData == null)
            return;

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                scrambleDragRoot, eventData.position, eventData.pressEventCamera, out Vector2 local))
            tile.GetComponent<RectTransform>().anchoredPosition = local;
    }

    public void FinishTileDrag(NpcWordTile tile, PointerEventData eventData)
    {
        if (tile == null)
            return;

        bool overDropZone = eventData != null &&
            RectTransformUtility.RectangleContainsScreenPoint(scrambleDropZone, eventData.position, eventData.pressEventCamera);
        RectTransform destination = overDropZone ? scrambleDropZone : scrambleBank;
        tile.transform.SetParent(destination, false);

        if (overDropZone)
        {
            int siblingIndex = scrambleDropZone.childCount - 1;
            for (int i = 0; i < scrambleDropZone.childCount; i++)
            {
                Transform child = scrambleDropZone.GetChild(i);
                if (child == tile.transform)
                    continue;

                Vector3 childScreen = RectTransformUtility.WorldToScreenPoint(eventData.pressEventCamera, child.position);
                if (eventData.position.x < childScreen.x)
                {
                    siblingIndex = i;
                    break;
                }
            }
            tile.transform.SetSiblingIndex(Mathf.Clamp(siblingIndex, 0, scrambleDropZone.childCount - 1));
        }

        tile.GetComponent<CanvasGroup>().blocksRaycasts = true;
        RefreshScrambleFeedback(updateStatus: true);
    }

    void SubmitScrambleResponse()
    {
        if (!scrambleActive || currentLine == null)
            return;

        var submittedWords = new List<string>();
        for (int i = 0; i < scrambleDropZone.childCount; i++)
        {
            NpcWordTile tile = scrambleDropZone.GetChild(i).GetComponent<NpcWordTile>();
            if (tile != null)
                submittedWords.Add(tile.Word);
        }

        string submitted = string.Join(" ", submittedWords);
        DialogueJumbleEvaluation evaluation = DialogueSentenceJumble.Evaluate(
            submittedWords,
            currentLine.expectedEnglishResponse);
        ApplyScrambleFeedback(evaluation);
        if (evaluation.isCorrect)
        {
            RecordWrittenPhrase(submitted, true, "");
            currentBuddy?.RegisterSuccessfulResponse(currentLine);
            LocalizedDialogueLine acceptedLine = currentLine;
            scrambleActive = false;
            scrambleOverlay.SetActive(false);
            currentLine = null;
            currentNpc?.HandleDialogueResponseAccepted(acceptedLine);
            return;
        }

        string rejectionReason = BuildRejectionReason(currentLine, spoken: false);
        TutorFeedbackPlan feedback = currentBuddy != null
            ? currentBuddy.BuildTutorFeedback(currentLine, submitted, rejectionReason)
            : null;
        lastTutorFeedback = feedback;
        scrambleStatusLabel.text = DialogueSentenceJumble.BuildFeedback(evaluation);
        SetResponseStatus(scrambleStatusLabel.text);
        ShowGrimoireButtonFor(feedback);
        RecordWrittenPhrase(submitted, false, rejectionReason, feedback);
        RequestAutomaticBuddyCoach(submitted);
    }

    void RefreshScrambleFeedback(bool updateStatus)
    {
        if (!scrambleActive || currentLine == null || scrambleDropZone == null)
            return;

        var submitted = new List<string>();
        for (int i = 0; i < scrambleDropZone.childCount; i++)
        {
            NpcWordTile tile = scrambleDropZone.GetChild(i).GetComponent<NpcWordTile>();
            if (tile != null) submitted.Add(tile.Word);
        }

        DialogueJumbleEvaluation evaluation = DialogueSentenceJumble.Evaluate(
            submitted,
            currentLine.expectedEnglishResponse);
        ApplyScrambleFeedback(evaluation);
        if (updateStatus && scrambleStatusLabel != null && submitted.Count > 0)
            scrambleStatusLabel.text = DialogueSentenceJumble.BuildFeedback(evaluation);
    }

    void ApplyScrambleFeedback(DialogueJumbleEvaluation evaluation)
    {
        foreach (NpcWordTile tile in scrambleTiles)
            tile?.SetFeedback(DialogueJumbleWordState.Unused);
        if (evaluation?.words == null || scrambleDropZone == null)
            return;

        for (int i = 0; i < evaluation.words.Count && i < scrambleDropZone.childCount; i++)
        {
            NpcWordTile tile = scrambleDropZone.GetChild(i).GetComponent<NpcWordTile>();
            tile?.SetFeedback(evaluation.words[i].state);
        }
    }

    void CancelScrambleResponse()
    {
        scrambleActive = false;
        ClearScrambleTiles();
        if (scrambleOverlay != null)
            scrambleOverlay.SetActive(false);
        if (panel != null && currentLine != null)
            panel.gameObject.SetActive(true);
    }

    void ClearScrambleTiles()
    {
        foreach (NpcWordTile tile in scrambleTiles)
        {
            if (tile != null)
                Destroy(tile.gameObject);
        }
        scrambleTiles.Clear();
    }

    static List<string> TokenizeSentence(string sentence)
    {
        var words = new List<string>();
        if (string.IsNullOrWhiteSpace(sentence))
            return words;

        string[] tokens = sentence.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string token in tokens)
        {
            string cleaned = token.Trim();
            if (!string.IsNullOrWhiteSpace(cleaned))
                words.Add(cleaned);
        }
        return words;
    }

    GameObject pointPrefabForDraw;

    TextMeshProUGUI MakeOverlayLabel(RectTransform parent, string name, Vector2 position, Vector2 size, float fontSize, bool bold)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
        label.alignment = TextAlignmentOptions.Center;
        label.verticalAlignment = VerticalAlignmentOptions.Middle;
        label.textWrappingMode = TextWrappingModes.Normal;
        GameUiTheme.StyleText(label, fontSize, bold);
        return label;
    }

    void BeginHandwritingResponse()
    {
        if (handwritingActive || currentLine == null)
            return;

        EnsureBuilt();
        EnsureHandwritingRuntime();
        if (handwritingDrawController == null || handwritingPlayer == null)
        {
            SetResponseStatus("Handwriting input is not available in this scene yet.");
            return;
        }

        previousDrawMode = handwritingDrawController.ActiveMode;
        previousDrawingPanel = handwritingDrawController.drawingPanel;
        previousPointPrefab = handwritingDrawController.pointPrefab;
        previousWordDisplay = handwritingDrawController.wordDisplay;
        previousHintText = handwritingDrawController.hintText;
        previousPlayerDrawingPanel = handwritingPlayer.drawingPanel;
        previousPlayerDrawController = handwritingPlayer.drawController;

        handwritingMode ??= new NpcHandwritingMode();
        handwritingMode.Configure(ResolveHandwritingTarget(currentLine), HandleHandwritingSubmitted, RestoreAfterHandwritingExit);
        handwritingDrawController.drawingPanel = handwritingSurface;
        handwritingDrawController.pointPrefab = pointPrefabForDraw;
        handwritingDrawController.wordDisplay = handwritingWordLabel;
        handwritingDrawController.hintText = handwritingHintLabel;
        handwritingDrawController.SetMode(handwritingMode);
        handwritingPlayer.drawingPanel = handwritingOverlay;
        handwritingPlayer.drawController = handwritingDrawController;
        handwritingActive = true;
        panel.gameObject.SetActive(false);
        handwritingOverlay.SetActive(true);
        handwritingPromptLabel.text = ResolveNpcLine(currentLine);
        handwritingWordLabel.text = "";
        handwritingStatusLabel.text = "Draw one letter at a time. Confirm each letter, then submit.";
        handwritingPlayer.EnterDrawMode();
    }

    static string ResolveHandwritingTarget(LocalizedDialogueLine line)
    {
        if (line == null || string.IsNullOrWhiteSpace(line.expectedEnglishResponse))
            return "";

        if (line.malfunctionType != GrammarDialogueMalfunctionType.MissingWord)
            return line.expectedEnglishResponse;

        List<string> expectedWords = TokenizeSentence(line.expectedEnglishResponse);
        if (expectedWords.Count == 1)
            return expectedWords[0];

        string prompt = ResolveNpcLine(line)
            .Replace("____", "BLANK")
            .Replace("__", "BLANK");
        List<string> promptWords = TokenizeSentence(prompt);
        if (promptWords.Count == expectedWords.Count)
        {
            for (int i = 0; i < promptWords.Count; i++)
            {
                if (string.Equals(promptWords[i].Trim('.', ',', '?', '!'), "BLANK", StringComparison.OrdinalIgnoreCase))
                    return expectedWords[i];
            }
        }

        return line.expectedEnglishResponse;
    }

    void EnsureHandwritingRuntime()
    {
        handwritingPlayer = FindAnyObjectByType<PlayerController>();
        if (handwritingPlayer == null)
            return;

        handwritingDrawController = handwritingPlayer.drawController ?? handwritingPlayer.GetComponent<DrawController>();
        if (handwritingDrawController == null)
        {
            if (handwritingPlayer.GetComponent<RecognizerHost>() == null)
                handwritingPlayer.gameObject.AddComponent<RecognizerHost>();
            handwritingDrawController = handwritingPlayer.gameObject.AddComponent<DrawController>();
        }
    }

    void HandleHandwritingSubmitted(bool correct, string submitted)
    {
        if (currentLine == null)
            return;

        if (correct)
        {
            RecordWrittenPhrase(submitted, true, "");
            if (currentLine.inputMode == GrammarDialogueInputMode.SpeakAndWrite)
            {
                // DrawController exits the handwriting mode after this callback.
                // RestoreAfterHandwritingExit completes the dual-evidence task
                // once the drawing runtime has returned to its prior state.
                speakAndWriteTextAccepted = true;
                return;
            }

            currentBuddy?.RegisterSuccessfulResponse(currentLine);
            LocalizedDialogueLine acceptedLine = currentLine;
            handwritingActive = false;
            handwritingOverlay.SetActive(false);
            currentLine = null;
            currentNpc?.HandleDialogueResponseAccepted(acceptedLine);
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

    void CancelHandwritingResponse()
    {
        if (handwritingPlayer != null && handwritingPlayer.IsDrawingMode)
            handwritingPlayer.ExitDrawMode(false);
        else
            RestoreAfterHandwritingExit();
    }

    void RestoreAfterHandwritingExit()
    {
        if (handwritingDrawController != null && handwritingDrawController.ActiveMode == handwritingMode)
        {
            handwritingDrawController.SetMode(previousDrawMode);
            handwritingDrawController.drawingPanel = previousDrawingPanel;
            handwritingDrawController.pointPrefab = previousPointPrefab;
            handwritingDrawController.wordDisplay = previousWordDisplay;
            handwritingDrawController.hintText = previousHintText;
        }

        if (handwritingPlayer != null)
        {
            handwritingPlayer.drawingPanel = previousPlayerDrawingPanel;
            handwritingPlayer.drawController = previousPlayerDrawController ?? handwritingDrawController;
        }

        handwritingActive = false;
        if (handwritingOverlay != null)
            handwritingOverlay.SetActive(false);
        if (currentLine != null && currentLine.inputMode == GrammarDialogueInputMode.SpeakAndWrite && speakAndWriteTextAccepted)
        {
            TryCompleteSpeakAndWrite();
        }
        else if (panel != null && currentLine != null)
        {
            panel.gameObject.SetActive(true);
        }
    }
}
