using System.Collections.Generic;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Challenge draw mode with an optional first lesson slice:
/// say CAT, then write C-A-T with live ghost guidance.
/// </summary>
public partial class ChallengeMode : MonoBehaviour, IDrawMode, IRawLetterEvaluator, IExpectedLetterRecognitionContext, ILiveLetterFeedback, ILetterAcceptedObserver, IDrawInputAssist, IDrawWordActionPhraseProvider
{
    const float VoiceBadgeWidth = 280f;
    const float VoiceBadgeHeight = 104f;
    const float VoiceControlGap = 18f;
    const float VoiceFallbackButtonHeight = 54f;
    const float HintButtonWidth = 164f;
    const float HintButtonHeight = 76f;

    public enum ForgePageMode
    {
        LetterPage,
        SpecialWordPage,
    }
    [Header("References")]
    public WordListDatabase wordDatabase;
    public TextMeshProUGUI promptLabel;
    public TextMeshProUGUI tierLabel;
    public TextMeshProUGUI attemptsLabel;

    [Header("Difficulty")]
    [Range(0, 2)]
    public int currentTier = 0;

    [Header("Scoring thresholds")]
    public float thresholdCorrect = 30f;
    public float thresholdWarm = 45f;
    public float thresholdWrong = PDollarRecognizer.SCORE_THRESHOLD;
    [Tooltip("Expected-letter-only P$ acceptance score. P$ scores in this project commonly land in the hundreds for child handwriting.")]
    public float desiredLetterMatchThreshold = 500f;
    const float MinimumExpectedLetterMatchThreshold = 500f;
    float EffectiveDesiredLetterMatchThreshold => Mathf.Max(
        desiredLetterMatchThreshold,
        thresholdWrong,
        MinimumExpectedLetterMatchThreshold,
        PDollarRecognizer.SCORE_THRESHOLD);

    [Header("Attempts")]
    public int maxAttempts = 99;

    [Header("Lesson Slice")]
    [Tooltip("Legacy word-spell lesson behavior. Production grammar scenes leave this disabled.")]
    public bool legacySpellLessonsEnabled;
    public bool useSpellLessonSlice;
    public string spellLessonWord = "CAT";
    public bool requireSpeechUnlock = true;
    public bool allowEditorSpeechFallback = true;
    public KeyCode editorSpeechKey = KeyCode.V;

    [Header("Testing Feedback")]
    public bool showLargeFeedbackPopups = true;

    [Header("Stroke Visuals")]
    [Tooltip("Visual-only width for the animated guide/ghost stroke.")]
    public float guideStrokeThickness = 9f;

    private string targetWord = "";
    private int letterIndex;
    private int attemptsUsed;
    private string hint = "";
    private bool speechUnlocked;
    private float sessionStartedAt;
    private float speechUnlockedAt;
    private int wrongLetterAttempts;
    private int guideCorrections;
    private int giftedLetters;
    private LetterFormationCoach.FormationState lastFormationState = LetterFormationCoach.FormationState.Hidden;
    private int helpLevel;
    private readonly List<float> acceptedLetterScores = new List<float>();
    private readonly List<int> acceptedLetterTries = new List<int>();

    private FeedbackManager feedback;
    private DrawController drawController;
    private TemplateLibrary templateLibrary;
    private LetterFormationCoach formationCoach;
    private NotebookWritingGuide notebookGuide;
    private SpellLessonProgressStore progressStore;
    private VoiceUnlockRecognizer voiceUnlockRecognizer;
    private SpellRegistry spellRegistry;
    private EnemyWaveDirector waveDirector;
    private SpellPerformanceTracker spellPerformanceTracker;
    private SpellHintSpeaker spellHintSpeaker;
    private PhoneticDisplayState phoneticDisplayState;
    private CreatureCombatController creatureCombat;
    private CreatureCombatRegistry creatureCombatRegistry;
    private Button hintButton;
    private TextMeshProUGUI hintButtonLabel;
    private RectTransform voiceBadgeRoot;
    private Button voiceBadgeButton;
    private Image voiceBadgePanel;
    private Image voiceBadgeDot;
    private TextMeshProUGUI voiceBadgeStateLabel;
    private TextMeshProUGUI voiceBadgeDetailLabel;
    private RectTransform voiceFallbackRoot;
    private readonly List<Button> voiceFallbackButtons = new List<Button>();
    private string voiceFallbackSignature = "";
    private string voiceOneShotFeedback = "";
    private float lastVoiceBadgeListenStartedAt = -1f;
    private Coroutine guidePulseRoutine;
    private bool pendingForgeSelection;
    private bool choosingForgeSpell;
    private string hintedForgeWord = "";
    private string acceptedForgePhrase = "";
    private PronunciationInsightResult lastPronunciationInsight;
    private ForgePageMode requestedForgeMode = ForgePageMode.LetterPage;
    private PlayerLearningProfile learningProfile;
    private HandwritingDiagnosticSummary lastLetterDiagnostic;

    public string CurrentHint => hint;
    public bool ShouldSuppressPanelHint => drawController != null && drawController.canDraw && !speechUnlocked;
    public int LastAwardedShots { get; private set; } = 3;
    public float LastAverageLetterScore { get; private set; } = PDollarRecognizer.SCORE_THRESHOLD;
    public bool LastSubmissionWasCorrect { get; private set; }
    public ForgePageMode CurrentForgeMode => requestedForgeMode;

    public event System.Action<string> OnWordCompleted;

    void Awake()
    {
        maxAttempts = Mathf.Max(maxAttempts, 99);
        guideStrokeThickness = Mathf.Max(guideStrokeThickness, 1f);
        NormalizeRecognitionThresholds();
        feedback = GetComponent<FeedbackManager>();
        drawController = GetComponent<DrawController>();
        templateLibrary = GetComponent<TemplateLibrary>();
        progressStore = new SpellLessonProgressStore();
        spellRegistry = GetComponent<SpellRegistry>();
        if (legacySpellLessonsEnabled && spellRegistry == null)
            spellRegistry = gameObject.AddComponent<SpellRegistry>();
        waveDirector = GetComponent<EnemyWaveDirector>();
        if (legacySpellLessonsEnabled && waveDirector == null)
            waveDirector = gameObject.AddComponent<EnemyWaveDirector>();
        spellPerformanceTracker = GetComponent<SpellPerformanceTracker>();
        if (spellPerformanceTracker == null)
            spellPerformanceTracker = gameObject.AddComponent<SpellPerformanceTracker>();
        spellHintSpeaker = GetComponent<SpellHintSpeaker>();
        if (spellHintSpeaker == null)
            spellHintSpeaker = gameObject.AddComponent<SpellHintSpeaker>();
        phoneticDisplayState = PhoneticDisplayState.EnsureExists(gameObject);
        creatureCombat = GetComponent<CreatureCombatController>() ?? FindAnyObjectByType<CreatureCombatController>();
        creatureCombatRegistry = GetComponent<CreatureCombatRegistry>() ?? FindAnyObjectByType<CreatureCombatRegistry>();
        learningProfile = GetComponent<PlayerLearningProfile>() ?? FindAnyObjectByType<PlayerLearningProfile>();
        voiceUnlockRecognizer = GetComponent<VoiceUnlockRecognizer>();
        if (voiceUnlockRecognizer == null)
            voiceUnlockRecognizer = gameObject.AddComponent<VoiceUnlockRecognizer>();
        voiceUnlockRecognizer.OnRecognitionResolved += HandleVoiceRecognitionResolved;
        voiceUnlockRecognizer.OnPronunciationInsightReady += HandlePronunciationInsightReady;
        GameSettings.HandwritingDevDiagnosticsChanged += HandleHandwritingDevDiagnosticsChanged;
        ApplyUITheme();
    }

    void OnValidate()
    {
        guideStrokeThickness = Mathf.Max(guideStrokeThickness, 1f);
        NormalizeRecognitionThresholds();
    }

    void NormalizeRecognitionThresholds()
    {
        thresholdWrong = Mathf.Max(thresholdWrong, PDollarRecognizer.SCORE_THRESHOLD);
        desiredLetterMatchThreshold = Mathf.Max(desiredLetterMatchThreshold, MinimumExpectedLetterMatchThreshold);
    }

    void OnDestroy()
    {
        if (voiceUnlockRecognizer != null)
        {
            voiceUnlockRecognizer.OnRecognitionResolved -= HandleVoiceRecognitionResolved;
            voiceUnlockRecognizer.OnPronunciationInsightReady -= HandlePronunciationInsightReady;
        }

        GameSettings.HandwritingDevDiagnosticsChanged -= HandleHandwritingDevDiagnosticsChanged;
    }

    void Update()
    {
        if (PauseMenuController.IsPaused)
        {
            voiceUnlockRecognizer?.StopListening();
            return;
        }

        RefreshVoiceStatusBadge();

        if (!useSpellLessonSlice || !requireSpeechUnlock)
            return;

        if (speechUnlocked || drawController == null || !drawController.canDraw)
            return;

        if (attemptsLabel != null)
            attemptsLabel.gameObject.SetActive(false);

        hint = BuildSpeechHint();

        if (CanUseEditorSpeechFallback() && Input.GetKeyDown(editorSpeechKey))
        {
            if (choosingForgeSpell)
            {
                string fallbackChoice = ResolveForgeFallbackChoice();
                voiceUnlockRecognizer?.NotifyFallbackTriggered($"Fallback armed: {fallbackChoice}");
                SetForgeTarget(fallbackChoice);
            }
            else
            {
                voiceUnlockRecognizer?.NotifyFallbackTriggered($"Fallback accepted: {targetWord}");
                UnlockSpeechGate();
            }
        }
    }

    public void OnEnter()
    {
        if (pendingForgeSelection)
            BeginForgeSelection();
        else
            PickNewWord();

        RefreshUi();
        RefreshHintButtonVisibility();

        if (tierLabel != null)
        {
            tierLabel.gameObject.SetActive(true);
            tierLabel.text = useSpellLessonSlice
                ? BuildLessonStatusLabel()
                : wordDatabase != null
                    ? wordDatabase.TierName(currentTier)
                    : $"Tier {currentTier + 1}";
        }

        if (speechUnlocked)
            BeginGuideForCurrentLetter();
        else
        {
            ConfigureVoiceUnlock();
            HideGuide();
        }

        RefreshVoiceStatusBadge();
    }

    public void OnExit()
    {
        if (promptLabel) promptLabel.gameObject.SetActive(false);
        if (tierLabel) tierLabel.gameObject.SetActive(false);
        if (attemptsLabel) attemptsLabel.gameObject.SetActive(false);
        if (hintButton != null)
            hintButton.gameObject.SetActive(false);
        HideVoiceStatusBadge();
        HideVoiceFallback();
        StopGuidePulse();
        HideGuide();
        voiceUnlockRecognizer?.StopListening();
        choosingForgeSpell = false;
        pendingForgeSelection = false;
        hintedForgeWord = "";
        acceptedForgePhrase = "";
        voiceOneShotFeedback = "";
    }

    public void RefreshDrawingChromeVisibility()
    {
        RefreshHintButtonVisibility();
        RefreshVoiceStatusBadge();
        if (drawController == null || !drawController.canDraw)
            HideVoiceFallback();
    }
}
