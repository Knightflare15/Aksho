using System;
using System.Collections.Generic;
using UnityEngine;

public enum MissionType
{
    Practice,
    Revision,
    Test,
}

public enum CurriculumProviderMode
{
    LocalDemo,
    FirebaseRest,
}

public enum LearningAnalysisMode
{
    OnDeviceOnly,
    HybridServerAssist,
    ServerOnly,
}

[Serializable]
public sealed class SubArenaDefinition
{
    [Range(1, 3)] public int subArenaIndex = 1;
    public string displayName = "Meadow Gate";
    public string sceneName = "Level_1_Bat";
    public string focus = "practice";
}

[Serializable]
public sealed class MissionAssignment
{
    public string missionId = "demo-class_today";
    public string schoolId = "demo-school";
    public string classId = "demo-class";
    public string studentId = "demo-student-1";
    public string date = "";
    public MissionType missionType = MissionType.Practice;
    [Min(60)] public int missionDurationSeconds = 480;
    [Range(0, 2)] public int countingChestCount = 1;
    [Range(0, 2)] public int colorChestCount = 0;
    public List<string> lettersForToday = new List<string>();
    public List<string> wordsForToday = new List<string>();
    public List<string> revisionLetters = new List<string>();
    public List<SubArenaDefinition> subArenas = new List<SubArenaDefinition>();
}

[Serializable]
public sealed class WorldGoalAssignment
{
    public string goalId;
    public string schoolId;
    public string classId;
    public string studentId;
    public string weekStart;
    public string targetAreaId;
    public string targetGymId;
    public List<string> focusGrammarPatterns = new List<string>();
    public List<string> focusVocabulary = new List<string>();
    public string dueDate;
    public int rewardCoins = 25;
    public string schoolTimeZone = "Asia/Kolkata";
    public string assignedAtUtc;
    public string createdByTeacherId;
}

public enum WorldGoalStatus
{
    NotAssigned,
    InProgress,
    CompletedOnTime,
    CompletedLate,
    RewardClaimed,
    Expired,
}

[Serializable]
public sealed class WorldGoalClaimResult
{
    public bool ok;
    public string goalId;
    public string status;
    public string targetGymId;
    public int rewardCoins;
    public int walletBalance;
    public string completedAtUtc;
    public string claimedAtUtc;
    public bool alreadyClaimed;
}

[Serializable]
public sealed class LetterAttemptRecord
{
    public string sampleSchemaVersion;
    public string sampleId;
    public string writerId;
    public string writerAgeBand;
    public string handedness;
    public string collectionCohort;
    public string consentReference;
    public string captureSessionId;
    public string captureSource;
    public string rawCoordinateSystem;
    public string normalizedCoordinateSystem;
    public string captureStartedAtUtc;
    public long captureDurationMs;
    public HandwritingCaptureDevice captureDevice;
    public string attemptId;
    public string studentId;
    public string classId;
    public string schoolId;
    public string missionId;
    public string letter;
    public bool confident;
    public int attempts;
    public bool gifted;
    public float confidenceScore;
    public HandwritingDiagnosticSummary handwritingDiagnostics;
    public string neuralRecognizedName;
    public float neuralConfidence;
    public float combinedConfidence;
    public bool recognizerAgreement;
    public string recognitionDecision;
    public string assessmentOutcome;
    public string assessmentReason;
    public float pDollarScore;
    public string broadRecognizedName;
    public float broadRecognitionScore;
    public bool inputAssisted;
    public bool materiallyInputAssisted;
    public float inputAssistFraction;
    public float inputAssistMeanDistance;
    public float inputAssistMaxDistance;
    public float calibrationSeparation;
    public bool calibrationReliable;
    public bool rawStrokeCaptured;
    public int rawStrokePointCount;
    public string rawStrokeRetentionPolicy;
    public string normalizedCoordinateSpace;
    public List<HandwritingPointRecord> points = new List<HandwritingPointRecord>();
    public string createdAtUtc;
}

[Serializable]
public sealed class WordCastRecord
{
    public string eventId;
    public string studentId;
    public string classId;
    public string schoolId;
    public string missionId;
    public string contentId;
    public string inputSource;
    public string attemptGroupId;
    public int attemptNumber;
    public string word;
    public bool success;
    public bool specialMatch;
    public float responseSeconds;
    public PronunciationInsightRecord pronunciationInsight;
    public PronunciationInsightRecord serverPronunciationInsight;
    public string analysisMode;
    public string onDeviceAnalysisProvider;
    public string serverAnalysisStatus;
    public string serverAnalysisJobId;
    public string audioStoragePath;
    public string audioContentType;
    public float audioDurationSeconds;
    public bool rawAudioCaptured;
    public bool rawAudioUploaded;
    public string rawAudioRetentionPolicy;
    [NonSerialized] public byte[] pronunciationAudioWavBytes;
    public string createdAtUtc;
}

[Serializable]
public sealed class SpokenPhraseEventRecord
{
    public string eventId;
    public string studentId;
    public string classId;
    public string schoolId;
    public string missionId;
    public string goalId;
    public string dialogueTaskId;
    public string contentId;
    public string inputSource;
    public string attemptGroupId;
    public int attemptNumber;
    public string areaId;
    public string zoneKind;
    public string phrase;
    public string submittedPhrase;
    public string targetPhrase;
    // Server-compatible aliases used by the Firebase analysis job contract.
    public string submittedResponse;
    public string targetText;
    public string grammarPattern;
    public string conceptId;
    public string errorCategory;
    public string hintLevelShown;
    public string remediationStep;
    public string correctedResponse;
    public string buddyAssistMode;
    public List<string> vocabularyTokens = new List<string>();
    public List<string> masteryTags = new List<string>();
    public bool accepted;
    public string rejectionReason;
    public float responseSeconds;
    public PronunciationInsightRecord pronunciationInsight;
    public string analysisMode;
    public string onDeviceAnalysisProvider;
    public string serverAnalysisStatus;
    public string serverAnalysisJobId;
    public string audioStoragePath;
    public string audioContentType;
    public float audioDurationSeconds;
    public bool rawAudioCaptured;
    public bool rawAudioUploaded;
    public string rawAudioRetentionPolicy;
    [NonSerialized] public byte[] pronunciationAudioWavBytes;
    public string createdAtUtc;
}

[Serializable]
public sealed class WrittenPhraseEventRecord
{
    public string eventId;
    public string studentId;
    public string classId;
    public string schoolId;
    public string missionId;
    public string goalId;
    public string dialogueTaskId;
    public string contentId;
    public string inputSource;
    public string attemptGroupId;
    public int attemptNumber;
    public string areaId;
    public string zoneKind;
    public string phrase;
    public string submittedPhrase;
    public string targetPhrase;
    public string grammarPattern;
    public string conceptId;
    public string errorCategory;
    public string hintLevelShown;
    public string remediationStep;
    public string correctedResponse;
    public string buddyAssistMode;
    public List<string> vocabularyTokens = new List<string>();
    public List<string> masteryTags = new List<string>();
    public bool accepted;
    public string rejectionReason;
    public float responseSeconds;
    public string createdAtUtc;
}

[Serializable]
public sealed class GrammarBattleEventRecord
{
    public string eventId;
    public string studentId;
    public string classId;
    public string schoolId;
    public string missionId;
    public string goalId;
    public string contentId;
    public string inputSource;
    public string attemptGroupId;
    public int attemptNumber;
    public string areaId;
    public string zoneKind;
    public string encounterType;
    public string playerPhrase;
    public string grammarPattern;
    public string conceptId;
    public string errorCategory;
    public string hintLevelShown;
    public string remediationStep;
    public string correctedResponse;
    public string buddyAssistMode;
    public string commandPreposition;
    public string commandConjunction;
    public List<string> vocabularyTokens = new List<string>();
    public List<string> masteryTags = new List<string>();
    public string activeCurse;
    public string enemyNounFamily;
    public string enemyActionVerb;
    public string enemyGrammarCommand;
    public string enemyGrammarPattern;
    public string actionVerb;
    public string actionRole;
    public bool accepted;
    public string outcome;
    public int damageDealt;
    public int ppSpent;
    public PronunciationInsightRecord pronunciationInsight;
    public bool rawAudioCaptured;
    public bool rawAudioUploaded;
    public string rawAudioRetentionPolicy;
    public string createdAtUtc;
}

[Serializable]
public sealed class BuddyConversationTurnRecord
{
    public string eventId;
    public string studentId;
    public string classId;
    public string schoolId;
    public string missionId;
    public string goalId;
    public string dialogueTaskId;
    public string contentId;
    public string inputSource;
    public string attemptGroupId;
    public int attemptNumber;
    public string areaId;
    public string zoneKind;
    public string learnerMessage;
    public string buddyResponse;
    public string sourceLanguage;
    public string targetLanguage;
    public float englishRatio;
    public string conversationSkill;
    public string grammarPattern;
    public string conceptId;
    public string wordChoiceIssue;
    public string formationIssue;
    public string errorCategory;
    public string hintLevelShown;
    public string remediationStep;
    public string correctedResponse;
    public string buddyAssistMode;
    public List<string> vocabularyTokens = new List<string>();
    public List<string> masteryTags = new List<string>();
    public List<string> safeMemoryTags = new List<string>();
    public List<string> safetyFlags = new List<string>();
    public string teacherNote;
    public string buddyContractId;
    public string promptTemplateId;
    public string policyVersion;
    public bool reportable = true;
    public float responseSeconds;
    public string provider;
    public string model;
    public string trigger;
    public string buddyStatus;
    public string buddyFallbackReason;
    public string createdAtUtc;
}

[Serializable]
public sealed class GymAttemptRecord
{
    public string attemptId;
    public string studentId;
    public string classId;
    public string schoolId;
    public string missionId;
    public string goalId;
    public string areaId;
    public string gymId;
    public string zoneKind;
    public string buddyAssistMode;
    public List<string> masteryTags = new List<string>();
    public bool passed;
    public int spokenPhraseCount;
    public int writtenPhraseCount;
    public int grammarErrors;
    public int pronunciationRetries;
    public string startedAtUtc;
    public string endedAtUtc;
}

[Serializable]
public sealed class PronunciationInsightRecord
{
    public string providerName;
    public string targetWord;
    public string confirmedWord;
    public string rawRecognizedText;
    public bool voskConfirmedWord;
    public bool attemptedTarget;
    public float score;
    public float modelConfidence;
    public string hintKey;
    public string message;
    public PhoneticSegmentRecord focusSegment;
    public List<PhoneticSegmentRecord> segments = new List<PhoneticSegmentRecord>();
    public List<string> syllableBeats = new List<string>();
    public List<string> expectedPhonemes = new List<string>();
    public List<string> observedPhonemes = new List<string>();
    public List<PhonemeAlignmentRecord> phonemeIssues = new List<PhonemeAlignmentRecord>();
    public List<PhonemeAlignmentRecord> phonemeAlignment = new List<PhonemeAlignmentRecord>();
}

[Serializable]
public sealed class PhoneticSegmentRecord
{
    public string spelling;
    public string friendlySound;
    public string heardSound;
    public int beatIndex;
    public string status;
    public float confidence;
}

[Serializable]
public sealed class PhonemeAlignmentRecord
{
    public string expected;
    public string observed;
    public string status;
    public float confidence;
}

[Serializable]
public sealed class HandwritingPointRecord
{
    public float x;
    public float y;
    public float nx;
    public float ny;
    public float canvasX;
    public float canvasY;
    public long tMs;
    public long deltaMs;
    public float pressure = -1f;
    public float altitudeAngle = -1f;
    public float azimuthAngle = -1f;
    public int pointerId = -1;
    public string inputType;
    public int strokeId;
    public int order;
}

[Serializable]
public sealed class AcceptedHandwritingTemplateRecord
{
    public string sampleSchemaVersion;
    public string sampleId;
    public string writerId;
    public string writerAgeBand;
    public string handedness;
    public string collectionCohort;
    public string consentReference;
    public string captureSessionId;
    public string captureSource;
    public string rawCoordinateSystem;
    public string normalizedCoordinateSystem;
    public string captureStartedAtUtc;
    public long captureDurationMs;
    public HandwritingCaptureDevice captureDevice;
    public string templateId;
    public string studentId;
    public string classId;
    public string schoolId;
    public string missionId;
    public string letter;
    public string targetWord;
    public int letterIndex;
    public int attemptsForLetter;
    public bool gifted;
    public float recognitionScore;
    public string recognizedName;
    public string bestCandidateName;
    public string runnerUpName;
    public float runnerUpScore;
    public float scoreMargin;
    public bool isAmbiguous;
    public HandwritingDiagnosticSummary handwritingDiagnostics;
    public List<HandwritingPointRecord> points = new List<HandwritingPointRecord>();
    public bool rawStrokeCaptured;
    public int rawStrokePointCount;
    public string rawStrokeRetentionPolicy;
    public string normalizedCoordinateSpace;
    public string neuralRecognizedName;
    public float neuralConfidence;
    public float combinedConfidence;
    public bool recognizerAgreement;
    public string recognitionDecision;
    public string analysisMode;
    public string onDeviceAnalysisProvider;
    public string serverAnalysisStatus;
    public string serverAnalysisJobId;
    public string createdAtUtc;
}

[Serializable]
public sealed class ServerAnalysisJobRecord
{
    public string jobId;
    public string studentId;
    public string classId;
    public string schoolId;
    public string missionId;
    public string analysisKind;
    public string status;
    public string sourceCollection;
    public string sourceRecordId;
    public string targetText;
    public string audioStoragePath;
    public string onDeviceAnalysisProvider;
    public string analysisMode;
    public string createdAtUtc;
}

[Serializable]
public sealed class CurriculumRunSessionRecord
{
    public string sessionId;
    public string studentId;
    public string classId;
    public string schoolId;
    public string missionId;
    public int configuredDurationSeconds;
    public int actualDurationSeconds;
    public int subarenasCleared;
    public int fullLoopsCleared;
    public List<string> lettersPracticed = new List<string>();
    public List<string> wordsPracticed = new List<string>();
    public List<string> grammarPatternsPracticed = new List<string>();
    public List<string> masteryTagsPracticed = new List<string>();
    public List<string> vocabularyTokens = new List<string>();
    public List<string> acceptedSpokenVocabulary = new List<string>();
    public List<string> acceptedWrittenVocabulary = new List<string>();
    public List<string> acceptedBattleVocabulary = new List<string>();
    public int spokenPhraseCount;
    public int writtenPhraseCount;
    public int grammarBattleCount;
    public int grammarErrors;
    public int pronunciationRetries;
    public float averageConfidence;
    public float averageAttemptsPerLetter;
    public int creaturesCleared;
    public int specialWordMatches;
    public bool completed;
    public string startedAtUtc;
    public string endedAtUtc;
}

[Serializable]
public sealed class CountingMiniGameAttemptRecord
{
    public string attemptId;
    public string studentId;
    public string classId;
    public string schoolId;
    public string missionId;
    public string contentId;
    public string inputSource;
    public string attemptGroupId;
    public int attemptNumber;
    public string chestCategory;
    public int targetCount;
    public int selectedCount;
    public string spokenNumber;
    public bool countCorrect;
    public bool speechProofSucceeded;
    public bool hintUsed;
    public PronunciationInsightRecord pronunciationInsight;
    public PronunciationInsightRecord serverPronunciationInsight;
    public string analysisMode;
    public string onDeviceAnalysisProvider;
    public string serverAnalysisStatus;
    public string serverAnalysisJobId;
    public string audioStoragePath;
    public string audioContentType;
    public float audioDurationSeconds;
    public bool rawAudioCaptured;
    public bool rawAudioUploaded;
    public string rawAudioRetentionPolicy;
    [NonSerialized] public byte[] pronunciationAudioWavBytes;
    public string outcomeStatus;
    public int rewardAwarded;
    public float responseSeconds;
    public string createdAtUtc;
}

[Serializable]
public sealed class ColorMiniGameAttemptRecord
{
    public string attemptId;
    public string studentId;
    public string classId;
    public string schoolId;
    public string missionId;
    public string contentId;
    public string inputSource;
    public string attemptGroupId;
    public int attemptNumber;
    public string chestCategory;
    public string targetColor;
    public string selectedColor;
    public string spokenColor;
    public bool colorCorrect;
    public bool speechProofSucceeded;
    public bool hintUsed;
    public PronunciationInsightRecord pronunciationInsight;
    public PronunciationInsightRecord serverPronunciationInsight;
    public string analysisMode;
    public string onDeviceAnalysisProvider;
    public string serverAnalysisStatus;
    public string serverAnalysisJobId;
    public string audioStoragePath;
    public string audioContentType;
    public float audioDurationSeconds;
    public bool rawAudioCaptured;
    public bool rawAudioUploaded;
    public string rawAudioRetentionPolicy;
    [NonSerialized] public byte[] pronunciationAudioWavBytes;
    public string outcomeStatus;
    public int rewardAwarded;
    public float responseSeconds;
    public string createdAtUtc;
}

public enum BuddyLearningModality
{
    Unknown,
    Reading,
    Listening,
    Speaking,
    Writing,
    Arrangement,
    Combat,
    Assessment,
    Conversation,
    Reference,
}

public enum BuddyLearningSupportBand
{
    Foundation,
    Guided,
    Growing,
    Independent,
}

[Serializable]
public sealed class BuddyLearningAttemptRecord
{
    public int schemaVersion = 1;
    public string eventId;
    public string sourceRecordType;
    public string sourceRecordId;
    public string sessionId;
    public string studentId;
    public string classId;
    public string schoolId;
    public string missionId;
    public string goalId;
    public string areaId;
    public string zoneKind;
    public string activityType;
    public string modality;
    public string inputSource;
    public string contentId;
    public string dialogueTaskId;
    public string attemptGroupId;
    public int attemptNumber;
    public string questionPrompt;
    public string expectedResponse;
    public string submittedResponse;
    public string normalizedResponse;
    public string correctedResponse;
    public string grammarPattern;
    public string conceptId;
    public string grimoireReference;
    public List<string> vocabularyTokens = new List<string>();
    public List<string> masteryTags = new List<string>();
    public List<string> errorTags = new List<string>();
    public bool countsTowardMastery = true;
    public bool correct;
    public bool completedIndependently;
    public int hintCount;
    public string highestHintLevel;
    public string remediationStep;
    public string buddyAssistMode;
    public float responseSeconds;
    public float confidenceScore;
    public float recognitionConfidence;
    public float pronunciationScore;
    public PronunciationInsightRecord pronunciationInsight;
    public HandwritingDiagnosticSummary handwritingDiagnostics;
    public string activeCurse;
    public string actionVerb;
    public string actionRole;
    public string outcome;
    public string createdAtUtc;
}

[Serializable]
public sealed class BuddyLearningErrorCountRecord
{
    public string errorTag;
    public int count;
    public string lastSeenAtUtc;
}

[Serializable]
public sealed class BuddyModalityLearningStateRecord
{
    public string modality;
    public int attempts;
    public int correctAttempts;
    public int independentCorrectAttempts;
    public float totalResponseSeconds;
    public float averageResponseSeconds;
    public float successRate;
    public float independentSuccessRate;
    public string lastPracticedAtUtc;
}

[Serializable]
public sealed class BuddyConceptLearningStateRecord
{
    public string conceptId;
    public int attempts;
    public int correctAttempts;
    public int independentCorrectAttempts;
    public int assistedCorrectAttempts;
    public int firstAttemptCorrectAttempts;
    public int totalHints;
    public float totalResponseSeconds;
    public float averageResponseSeconds;
    public float successRate;
    public float independentSuccessRate;
    public float assistedSuccessRate;
    public float hintDependency;
    public float masteryEstimate;
    public string supportBand;
    public float recommendedEnglishRatio;
    // Lightweight spaced-repetition state. These values are updated locally
    // from authoritative learning attempts, so recommendations also work
    // offline and never require an LLM call.
    public int reviewStage;
    public int lapseCount;
    public int consecutiveIndependentCorrect;
    public string nextReviewAtUtc;
    public string lastIndependentCorrectAtUtc;
    public List<BuddyLearningErrorCountRecord> errorCounts = new List<BuddyLearningErrorCountRecord>();
    public List<BuddyModalityLearningStateRecord> modalityStates = new List<BuddyModalityLearningStateRecord>();
    public string lastPracticedAtUtc;
}

[Serializable]
public sealed class BuddyLearnerStateRecord
{
    public int schemaVersion = 1;
    public string studentId;
    public string homeLanguage = "hi";
    public string targetLanguage = "en";
    public int sourceEventCount;
    public int correctAttemptCount;
    public int independentCorrectAttemptCount;
    public int totalHints;
    public string supportBand = BuddyLearningSupportBand.Foundation.ToString();
    public float recommendedEnglishRatio = 0.3f;
    public List<BuddyConceptLearningStateRecord> concepts = new List<BuddyConceptLearningStateRecord>();
    public List<string> strengthConceptIds = new List<string>();
    public List<string> needConceptIds = new List<string>();
    public List<string> recurringErrorTags = new List<string>();
    public List<BuddyLearningAttemptRecord> recentAttempts = new List<BuddyLearningAttemptRecord>();
    public string lastEventAtUtc;
    public string updatedAtUtc;
}

public enum LearnerScheduleActivity
{
    Introduction,
    GuidedPractice,
    MicroLesson,
    RetrievalReview,
    Challenge,
}

[Serializable]
public sealed class LearnerScheduleRecommendation
{
    public int schemaVersion = 1;
    public string recommendationId;
    public string generatedAtUtc;
    public string conceptId;
    public string regionId;
    public string regionDisplayName;
    public string activityType;
    public string modality;
    [Range(1, 5)] public int difficulty = 1;
    public string buddySupport = "guided";
    [Range(0f, 1f)] public float recommendedEnglishRatio = 0.3f;
    public List<string> reviewConceptIds = new List<string>();
    public List<string> focusErrorTags = new List<string>();
    public string reasonCode;
    public string reason;
    public bool isReviewDue;
    public bool unlockReady;
    public string nextReviewAtUtc;
}

[Serializable]
public sealed class BuddyLearningSessionRecord
{
    public int schemaVersion = 1;
    public string sessionId;
    public string studentId;
    public string classId;
    public string schoolId;
    public string missionId;
    public string homeLanguage = "hi";
    public string targetLanguage = "en";
    public int attemptCount;
    public int correctAttemptCount;
    public int independentCorrectAttemptCount;
    public int totalHints;
    public List<string> conceptIds = new List<string>();
    public List<string> recurringErrorTags = new List<string>();
    public string supportBand = BuddyLearningSupportBand.Foundation.ToString();
    public float recommendedEnglishRatio = 0.3f;
    public string startedAtUtc;
    public string lastActivityAtUtc;
    public string endedAtUtc;
}

[Serializable]
public sealed class BuddyLearnerProfileRecord
{
    public int schemaVersion = 1;
    public string profileId = "current";
    public string studentId;
    public string classId;
    public string schoolId;
    public string homeLanguage = "hi";
    public string targetLanguage = "en";
    public bool allowTransliteration;
    public bool learningMemoryEnabled = true;
    public string explanationStyle = "short_then_expand";
    public string updatedAtUtc;
}

[Serializable]
public sealed class BuddyContextSnapshotRecord
{
    public int schemaVersion = 1;
    public string generatedAtUtc;
    public string studentId;
    public string sessionId;
    public string currentAreaId;
    public string currentZoneKind;
    public string currentConceptId;
    public BuddyLearnerProfileRecord profile;
    public BuddyLearnerStateRecord learnerState;
    public LearnerScheduleRecommendation schedulerRecommendation;
    public List<BuddyLearningAttemptRecord> relevantRecentAttempts = new List<BuddyLearningAttemptRecord>();
}

public interface ICurriculumAccessProvider
{
    MissionAssignment GetTodayMission(string studentId);
    WorldGoalAssignment GetCurrentWorldGoal(string studentId);
    void SubmitRunSession(CurriculumRunSessionRecord session);
    void SubmitLetterAttempt(LetterAttemptRecord attempt);
    void SubmitWordCast(WordCastRecord castEvent);
    void SubmitSpokenPhraseEvent(SpokenPhraseEventRecord phraseEvent);
    void SubmitWrittenPhraseEvent(WrittenPhraseEventRecord phraseEvent);
    void SubmitGrammarBattleEvent(GrammarBattleEventRecord battleEvent);
    void SubmitBuddyConversationTurn(BuddyConversationTurnRecord turn);
    void SubmitBuddyLearningAttempt(BuddyLearningAttemptRecord attempt);
    void SubmitBuddyLearningSession(BuddyLearningSessionRecord session);
    void SubmitBuddyLearnerProfile(BuddyLearnerProfileRecord profile);
    void SubmitGymAttempt(GymAttemptRecord gymAttempt);
    void SubmitAcceptedTemplate(AcceptedHandwritingTemplateRecord template);
    void SubmitCountingMiniGameAttempt(CountingMiniGameAttemptRecord attempt);
    void SubmitColorMiniGameAttempt(ColorMiniGameAttemptRecord attempt);
}

public interface IAsyncCurriculumAccessProvider
{
    void GetTodayMissionAsync(string studentId, Action<MissionAssignment> completed);
    void GetCurrentWorldGoalAsync(string studentId, Action<WorldGoalAssignment> completed);
}

/// <summary>
/// Optional capability for providers that can hydrate the authoritative learner
/// aggregate. Kept separate so local/demo providers remain simple and offline.
/// </summary>
public interface IRemoteLearnerStateProvider
{
    void GetBuddyLearnerStateAsync(string studentId, Action<BuddyLearnerStateRecord> completed);
}
