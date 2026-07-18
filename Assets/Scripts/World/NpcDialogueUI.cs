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


public sealed partial class NpcDialogueUI : MonoBehaviour
{
    const int AutomaticBuddyCoachAttemptThreshold = 2;
    static NpcDialogueUI instance;

    public static bool IsOpen => instance != null && instance.panel != null && instance.panel.gameObject.activeSelf;

    Canvas canvas;
    RectTransform panel;
    TextMeshProUGUI titleLabel;
    TextMeshProUGUI bodyLabel;
    GameObject transcriptViewport;
    RectTransform transcriptWordRoot;
    readonly List<Button> transcriptWordButtons = new List<Button>();
    readonly List<string> transcriptWords = new List<string>();
    Button wordMeaningButton;
    TextMeshProUGUI wordMeaningLabel;
    string selectedLearningText = "";
    int selectedTranscriptWordIndex = -1;
    TextMeshProUGUI assistLabel;
    TextMeshProUGUI responseStatusLabel;
    Button speakButton;
    TextMeshProUGUI speakLabel;
    Button speakResponseButton;
    TextMeshProUGUI speakResponseLabel;
    RectTransform spokenChoiceRoot;
    readonly List<Button> spokenChoiceButtons = new List<Button>();
    readonly List<string> currentSpokenChoices = new List<string>();
    string selectedSpokenChoice = "";
    TMP_InputField textResponseInput;
    Button submitTextResponseButton;
    TextMeshProUGUI submitTextResponseLabel;
    GameObject handwritingOverlay;
    RectTransform handwritingSurface;
    TextMeshProUGUI handwritingPromptLabel;
    TextMeshProUGUI handwritingWordLabel;
    TextMeshProUGUI handwritingHintLabel;
    TextMeshProUGUI handwritingStatusLabel;
    Button handwritingConfirmButton;
    Button handwritingDeleteButton;
    Button handwritingSubmitButton;
    Button handwritingCancelButton;
    DrawController handwritingDrawController;
    NpcHandwritingMode handwritingMode;
    IDrawMode previousDrawMode;
    PlayerController handwritingPlayer;
    RectTransform previousDrawingPanel;
    GameObject previousPointPrefab;
    TextMeshProUGUI previousWordDisplay;
    TextMeshProUGUI previousHintText;
    GameObject previousPlayerDrawingPanel;
    DrawController previousPlayerDrawController;
    bool handwritingActive;
    GameObject scrambleOverlay;
    RectTransform scrambleBank;
    RectTransform scrambleDropZone;
    RectTransform scrambleDragRoot;
    TextMeshProUGUI scramblePromptLabel;
    TextMeshProUGUI scrambleStatusLabel;
    Button scrambleSubmitButton;
    Button scrambleCancelButton;
    readonly List<NpcWordTile> scrambleTiles = new List<NpcWordTile>();
    bool scrambleActive;
    Button grimoireButton;
    TextMeshProUGUI grimoireLabel;
    Button askBuddyButton;
    Button hintButton;
    TextMeshProUGUI askBuddyLabel;
    Button closeButton;
    bool hintVisible;
    GrammarNpc currentNpc;
    LocalizedDialogueLine currentLine;
    TranslatorBuddyService currentBuddy;
    VoiceUnlockRecognizer responseRecognizer;
    TutorFeedbackPlan lastTutorFeedback;
    PronunciationInsightResult? lastResponsePronunciationInsight;
    bool speakAndWriteSpeechAccepted;
    bool speakAndWriteTextAccepted;
    float responseAttemptStartedAt;
    string remoteBuddyText = "";
    readonly BuddyCallSession buddyCallSession = new BuddyCallSession();
    BuddyRelationshipMemoryStore buddyRelationshipMemory;
    IBuddySpeechOutput buddySpeechOutput;
    string buddyMemoryStudentId = "";
    int incorrectAttemptCount;
    bool automaticBuddyCoachRequested;
    FeedbackManager learningFeedback;
    IDialogueWordInteractionService wordInteractionService;

    public static NpcDialogueUI EnsureExists()
    {
        if (instance != null)
            return instance;

        NpcDialogueUI existing = FindAnyObjectByType<NpcDialogueUI>();
        if (existing != null)
        {
            instance = existing;
            return instance;
        }

        GameObject go = new GameObject("NpcDialogueUI");
        instance = go.AddComponent<NpcDialogueUI>();
        DontDestroyOnLoad(go);
        return instance;
    }

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        buddySpeechOutput = BuddySpeechOutputFactory.Create();
        wordInteractionService = DialogueWordInteractionServiceFactory.Create();
        Build();
        learningFeedback = GetComponent<FeedbackManager>() ?? gameObject.AddComponent<FeedbackManager>();
        learningFeedback.largeFeedbackPopupDuration = 1.8f;
        learningFeedback.largeFeedbackPopupSize = new Vector2(440f, 126f);
        Hide();
    }

    void OnEnable()
    {
        TranslatorBuddyService buddy = FindAnyObjectByType<TranslatorBuddyService>();
        if (buddy != null)
            buddy.OnLineResolved += HandleLineResolved;
    }

    void OnDisable()
    {
        TranslatorBuddyService buddy = FindAnyObjectByType<TranslatorBuddyService>();
        if (buddy != null)
            buddy.OnLineResolved -= HandleLineResolved;
    }

    void Update()
    {
        if (handwritingActive && handwritingPlayer != null && !handwritingPlayer.IsDrawingMode)
            RestoreAfterHandwritingExit();

        if (panel != null && panel.gameObject.activeSelf &&
            (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1)))
            Hide();
    }

    public void Show(GrammarNpc npc, LocalizedDialogueLine line, TranslatorBuddyService buddy)
    {
        EnsureBuilt();
        EndBuddyCall(stopSpeech: true);
        // The NPC panel is intentionally limited to the NPC line and controls.
        // Buddy help and response/status text live in their separate window.
        if (assistLabel != null)
            assistLabel.gameObject.SetActive(false);
        if (responseStatusLabel != null)
            responseStatusLabel.gameObject.SetActive(true);
        if (handwritingActive)
            CancelHandwritingResponse();
        if (scrambleActive)
            CancelScrambleResponse();
        currentNpc = npc;
        currentLine = line;
        // Heard-wrong corrections are spoken corrections by design. Enforce
        // this at the UI boundary as a guard for hand-authored lines that may
        // still carry an old WriteOnly/SpeakOrWrite setting.
        if (currentLine != null && currentLine.malfunctionType == GrammarDialogueMalfunctionType.HeardWrong)
            currentLine.inputMode = GrammarDialogueInputMode.SpeakOnly;
        currentBuddy = buddy;
        if (currentLine != null && currentLine.cachedSpeech == null)
            currentLine.cachedSpeech = NpcDialogueAudioCatalog.LoadFor(currentLine);
        lastTutorFeedback = null;
        lastResponsePronunciationInsight = null;
        speakAndWriteSpeechAccepted = false;
        speakAndWriteTextAccepted = false;
        remoteBuddyText = "";
        hintVisible = false;
        incorrectAttemptCount = 0;
        automaticBuddyCoachRequested = false;
        selectedSpokenChoice = "";
        selectedLearningText = "";
        selectedTranscriptWordIndex = -1;
        responseAttemptStartedAt = Time.unscaledTime;
        if (currentBuddy != null && currentLine != null && currentLine.overrideAssistMode)
            currentBuddy.SetAssistMode(currentLine.assistMode);
        SetGrimoireButtonVisible(false);
        RefreshAskBuddyButton();

        if (titleLabel != null)
            titleLabel.text = BuildDialogueTitle(npc, line);
        if (bodyLabel != null)
            bodyLabel.text = line != null ? ResolveNpcLine(line) : "";
        ConfigureInteractiveTranscript();
        RefreshAssistText();
        ConfigureSpokenAnswerChoices();
        ConfigureResponseRecognizer();
        ConfigureTextResponseInput();

        if (panel != null)
            panel.gameObject.SetActive(true);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Give the player an audible version of the NPC line as the dialogue opens.
        string npcText = ResolveNpcLine(currentLine);
        if (Application.isPlaying && !string.IsNullOrWhiteSpace(npcText))
            PronunciationSpeaker.EnsureExists().Speak(npcText, currentLine.cachedSpeech);
    }

    static string BuildDialogueTitle(GrammarNpc npc, LocalizedDialogueLine line)
    {
        if (IsRecognitionEncounterLine(npc, line))
            return line.grammarPattern == GrammarPhrasePattern.NounOnly ? "Wild Noun Encounter" : "Wild Letter Encounter";
        string speaker = npc != null ? npc.displayName : "NPC";
        if (line == null)
            return speaker;

        string topic = string.IsNullOrWhiteSpace(line.grammarTopic)
            ? line.conceptId == GrammarConceptId.None ? "" : line.conceptId.ToString()
            : line.grammarTopic.Trim();
        string place = line.zoneKind.ToString();
        return string.IsNullOrWhiteSpace(topic)
            ? $"{speaker} - {place}"
            : $"{speaker} - {place} - {topic}";
    }

    static bool IsRecognitionEncounterLine(GrammarNpc npc, LocalizedDialogueLine line)
    {
        if (npc == null || line == null || line.inputMode != GrammarDialogueInputMode.SpeakAndWrite)
            return false;
        if (npc.sceneKind == SemanticZoneKind.Town)
            return false;
        return line.grammarPattern == GrammarPhrasePattern.LetterOnly ||
               line.grammarPattern == GrammarPhrasePattern.NounOnly;
    }

    public void Hide()
    {
        if (handwritingActive)
            CancelHandwritingResponse();
        if (scrambleActive)
            CancelScrambleResponse();
        EndBuddyCall(stopSpeech: true);
        responseRecognizer?.StopListening();
        if (panel != null)
            panel.gameObject.SetActive(false);
        BuddyHelpWindow.HideIfExists();

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void RefreshAssistText()
    {
        if (assistLabel == null)
            return;

        if (currentBuddy == null || currentLine == null)
        {
            assistLabel.text = "";
            if (hintButton != null)
                hintButton.gameObject.SetActive(false);
            return;
        }

        string teachingHint = currentBuddy.BuildTeachingHint(currentLine);
        if (hintButton != null)
        {
            hintButton.gameObject.SetActive(!string.IsNullOrWhiteSpace(teachingHint));
            if (hintVisible && string.IsNullOrWhiteSpace(teachingHint))
                hintVisible = false;
        }

        string translation = currentBuddy.BuildAssistText(currentLine);
        string responsePrompt = currentBuddy.BuildResponsePrompt(currentLine);
        string buddyMessage;
        if (string.IsNullOrWhiteSpace(translation) && string.IsNullOrWhiteSpace(responsePrompt))
            buddyMessage = "Translator offline.";
        else if (string.IsNullOrWhiteSpace(responsePrompt))
            buddyMessage = translation;
        else if (string.IsNullOrWhiteSpace(translation))
            buddyMessage = responsePrompt;
        else
            buddyMessage = $"{translation}\n{responsePrompt}";

        if (hintVisible)
        {
            if (!string.IsNullOrWhiteSpace(teachingHint))
                buddyMessage = string.IsNullOrWhiteSpace(buddyMessage)
                    ? $"Hint: {teachingHint}"
                    : $"{buddyMessage}\n\nHint: {teachingHint}";
        }

        if (!string.IsNullOrWhiteSpace(remoteBuddyText))
            buddyMessage = string.IsNullOrWhiteSpace(buddyMessage)
                ? $"Buddy: {remoteBuddyText}"
                : $"{buddyMessage}\n\nBuddy: {remoteBuddyText}";

        BuddyHelpWindow.EnsureExists().Show(buddyMessage);
        assistLabel.text = "";

        RefreshResponseStatus();
        RefreshAskBuddyButton();
    }
}
