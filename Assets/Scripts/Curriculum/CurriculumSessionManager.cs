using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public enum WavLmEndpointMode
{
    Auto,
    LocalDemo,
    CloudProduction,
    Custom,
}

public sealed partial class CurriculumSessionManager : MonoBehaviour
{
    public const string DefaultLocalWavLmApiBaseUrl = "http://127.0.0.1:8080";

    public static CurriculumSessionManager Instance { get; private set; }

    [Header("Mode")]
    public bool schoolModeEnabled = true;
    public CurriculumProviderMode providerMode = CurriculumProviderMode.LocalDemo;
    public LearningAnalysisMode analysisMode = LearningAnalysisMode.HybridServerAssist;
    public string firebaseProjectId = "";
    public string firebaseStorageBucket = "";
    public string firebaseFunctionsBaseUrl = "";
    public string firebaseBuddyVoiceFunctionsBaseUrl = "";
    public WavLmEndpointMode wavLmEndpointMode = WavLmEndpointMode.Auto;
    public string localWavLmApiBaseUrl = DefaultLocalWavLmApiBaseUrl;
    public string cloudWavLmApiBaseUrl = "";
    public string wavLmApiBaseUrl = "";

    [Header("Demo Identity")]
    public string activeStudentId = "demo-student-1";
    public string activeSchoolId = "demo-school";
    public string activeClassId = "demo-class";

    [Header("Student Session")]
    [TextArea] public string studentIdToken = "";
    [NonSerialized] public string firebaseAppCheckToken = "";
    string studentRefreshToken = "";
    bool refreshingStudentToken;
    float nextStudentTokenRefreshAt;
    const float StudentTokenRefreshIntervalSeconds = 50f * 60f;
    int studentSessionGeneration;
    bool missionRequestPending;
    bool worldGoalRequestPending;
    bool learnerStateHydrationPending;

    [Header("Buddy Learning Data")]
    public string buddyHomeLanguage = "hi";
    public string buddyTargetLanguage = "en";
    public bool buddyAllowTransliteration;
    public bool buddyLearningMemoryEnabled = true;
    public string buddyExplanationStyle = "short_then_expand";
    [Tooltip("Upload a bounded raw stroke sequence for rejected/uncertain handwriting attempts only when school consent and retention policy are configured.")]
    public bool collectRejectedHandwritingEvidence;
    [Tooltip("Upload bounded accepted handwriting strokes only when school consent and retention policy are configured.")]
    public bool collectAcceptedHandwritingEvidence;
    [Tooltip("Optional coarse age band stored with consented handwriting evidence, for example 6-8. Do not enter a birth date.")]
    public string handwritingWriterAgeBand = "";
    public string handwritingHandedness = "unknown";
    public string handwritingCollectionCohort = "";
    public string handwritingConsentReference = "";

    private readonly HashSet<string> allowedLetters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> allowedWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> lettersPracticed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> wordsPracticed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> grammarPatternsPracticed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> masteryTagsPracticed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> vocabularyTokensPracticed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> acceptedSpokenVocabulary = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> acceptedWrittenVocabulary = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> acceptedBattleVocabulary = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> attemptCountsByGroup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    private ICurriculumAccessProvider provider;
    private ICurriculumProviderFactory providerFactory;
    private IPronunciationAnalysisEndpointResolver pronunciationEndpointResolver;
    private float missionStartedAt;
    private int confidenceSampleCount;
    private float confidenceTotal;
    private int attemptsSampleCount;
    private int attemptsTotal;
    private int specialWordMatches;
    private int spokenPhraseEventsThisRun;
    private int writtenPhraseEventsThisRun;
    private int grammarBattleEventsThisRun;
    private int grammarErrorsThisRun;
    private int pronunciationRetriesThisRun;
    private int eligibleServerPronunciationReviewsThisRun;
    private int serverPronunciationReviewsThisRun;
    const int ServerPronunciationReviewStride = 3;
    const int MaxServerPronunciationReviewsPerRun = 12;
    private bool devSandboxMissionConfigured;
    private BuddyLearningDataRecorder buddyLearningData;

    public MissionAssignment CurrentMission { get; private set; }
    public WorldGoalAssignment CurrentWorldGoal { get; private set; }
    public bool HasMission => CurrentMission != null;
    public bool HasWorldGoalPractice => CurrentWorldGoal != null;
    public bool IsDevSandboxMissionConfigured => devSandboxMissionConfigured && HasWorldGoalPractice;
    public bool IsSchoolModeActive => schoolModeEnabled && HasStudentSession;
    // LocalDemo is the explicit offline/demo provider. It retains the complete
    // analytics contract without requiring a production authentication token,
    // matching the local Buddy-learning recorder.
    public bool ShouldSubmitTeacherAnalytics =>
        CurrentMission != null &&
        (providerMode == CurriculumProviderMode.LocalDemo || IsSchoolModeActive);
    public bool ShouldRequestServerAnalysis => ShouldSubmitTeacherAnalytics && analysisMode != LearningAnalysisMode.OnDeviceOnly;
    public string EffectiveWavLmApiBaseUrl => ResolveWavLmApiBaseUrl();
    public int CurrentSubArenaIndex { get; private set; } = 1;
    public int SubArenasCleared { get; private set; }
    public int FullLoopsCleared { get; private set; }
    public float ElapsedSeconds => missionStartedAt > 0f ? Mathf.Max(0f, Time.unscaledTime - missionStartedAt) : 0f;
    public float RemainingSeconds => Mathf.Max(0f, MissionDurationSeconds - ElapsedSeconds);
    public int MissionDurationSeconds => CurrentMission != null ? Mathf.Max(60, CurrentMission.missionDurationSeconds) : 0;
    public IReadOnlyCollection<string> AllowedLetters => allowedLetters;
    public IReadOnlyCollection<string> AllowedWords => allowedWords;
    public IReadOnlyCollection<string> LettersPracticed => lettersPracticed;
    public IReadOnlyCollection<string> WordsPracticed => wordsPracticed;
    public float AverageConfidence => confidenceSampleCount <= 0 ? 0f : confidenceTotal / confidenceSampleCount;
    public float AverageAttemptsPerLetter => attemptsSampleCount <= 0 ? 0f : attemptsTotal / (float)attemptsSampleCount;
    public int SpecialWordMatches => specialWordMatches;
    public bool HasStudentSession =>
        !string.IsNullOrWhiteSpace(activeSchoolId) &&
        !string.IsNullOrWhiteSpace(activeClassId) &&
        !string.IsNullOrWhiteSpace(activeStudentId) &&
        !string.IsNullOrWhiteSpace(studentIdToken) &&
        !string.Equals(activeStudentId, "demo-student-1", StringComparison.OrdinalIgnoreCase);
    public BuddyLearnerStateRecord BuddyLearnerState
    {
        get
        {
            EnsureBuddyLearningData();
            return buddyLearningData.State;
        }
    }
    public LearnerScheduleRecommendation CurrentLearnerRecommendation { get; private set; }

    public bool HasDemonstratedConceptMastery(
        string conceptId,
        int minimumAttempts = 8,
        int minimumIndependentCorrect = 4,
        float minimumMasteryEstimate = 0.75f)
    {
        if (string.IsNullOrWhiteSpace(conceptId))
            return false;

        EnsureBuddyLearningData();
        BuddyConceptLearningStateRecord concept = buddyLearningData?.State?.concepts?.Find(
            candidate => candidate != null &&
                string.Equals(candidate.conceptId, conceptId.Trim(), StringComparison.OrdinalIgnoreCase));
        return concept != null &&
            concept.attempts >= Mathf.Max(1, minimumAttempts) &&
            concept.independentCorrectAttempts >= Mathf.Max(1, minimumIndependentCorrect) &&
            concept.masteryEstimate >= Mathf.Clamp01(minimumMasteryEstimate);
    }
    public string BuddyLearningSessionId
    {
        get
        {
            EnsureBuddyLearningData();
            return buddyLearningData.SessionId;
        }
    }

    public event Action<MissionAssignment> OnMissionLoaded;
    public event Action OnStudentSessionChanged;
    public event Action<PronunciationInsightRecord> OnAzurePronunciationInsightReady;
    public event Action<LearnerScheduleRecommendation> OnLearnerRecommendationChanged;
    public event Action<BuddyLearnerStateRecord> OnLearnerStateHydrated;
    public event Action OnSubArenaAdvanced;
    public event Action<WordCastRecord> OnServerPronunciationInsightReady;

    public static CurriculumSessionManager EnsureExists()
    {
        if (Instance != null)
            return Instance;

        Instance = FindAnyObjectByType<CurriculumSessionManager>();
        if (Instance != null)
        {
            PreserveAcrossScenes(Instance.gameObject);
            return Instance;
        }

        var go = new GameObject("CurriculumSessionManager");
        Instance = go.AddComponent<CurriculumSessionManager>();
        PreserveAcrossScenes(go);
        return Instance;
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        PreserveAcrossScenes(gameObject);
        RestoreStudentSessionFromPrefs();
        EnsureProvider();
        EnsureBuddyLearningData();
        BeginLearnerStateHydration();
        AzurePronunciationInsightWindow.EnsureExists();
        SubmitBuddyLearnerProfile();
        if (!string.IsNullOrWhiteSpace(studentRefreshToken) && !string.IsNullOrWhiteSpace(studentIdToken))
            StartCoroutine(RefreshStudentTokenIfPossible());
    }

    void Update()
    {
        if (!HasStudentSession || refreshingStudentToken || string.IsNullOrWhiteSpace(studentRefreshToken) ||
            Time.unscaledTime < nextStudentTokenRefreshAt)
            return;

        nextStudentTokenRefreshAt = Time.unscaledTime + StudentTokenRefreshIntervalSeconds;
        StartCoroutine(RefreshStudentTokenIfPossible());
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    static void PreserveAcrossScenes(GameObject target)
    {
        if (Application.isPlaying && target != null)
            DontDestroyOnLoad(target);
    }

    void OnApplicationPause(bool paused)
    {
        if (paused)
            CheckpointBuddyLearningSession(false);
    }

    void OnApplicationQuit()
    {
        CheckpointBuddyLearningSession(true);
    }

}
