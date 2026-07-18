export type MissionType = "practice" | "revision" | "test";
export type UserRole = "admin" | "teacher" | "parent" | "student";
export type AccessCodeType = "parent" | "student" | "teacherInvite";
export type AccessCodeStatus = "active" | "redeemed" | "revoked" | "expired";

export interface UserProfile {
  uid: string;
  email?: string;
  displayName: string;
  role: UserRole;
  schoolId: string;
  classIds: string[];
  studentIds: string[];
  studentId?: string;
  parentEmail?: string;
  createdAt: string;
  updatedAt: string;
}

export interface School {
  id: string;
  name: string;
  academicYear?: string;
}

export interface Classroom {
  id: string;
  schoolId: string;
  name: string;
  studentIds: string[];
  customToken?: string;
}

export interface TeacherProfile {
  uid: string;
  schoolId: string;
  email: string;
  displayName: string;
  classIds: string[];
  createdAt?: string;
  updatedAt?: string;
}

export interface Student {
  id: string;
  schoolId: string;
  classId: string;
  name: string;
  avatarColor: string;
  authUid?: string;
  email?: string;
  parentEmail?: string;
  parentUid?: string;
  studentCodeIssuedAt?: string;
  subscriptionTier?: "free" | "standard" | "premium";
  privacy?: StudentPrivacySettings;
}

export interface StudentPrivacySettings {
  consentStatus: "pending" | "granted" | "withdrawn";
  policyVersion: string;
  gameplayAnalyticsAllowed: boolean;
  buddyAllowed: boolean;
  audioProcessingAllowed: boolean;
  handwritingEvidenceAllowed: boolean;
  diagnosticsAllowed: boolean;
  consentedAtUtc: string;
  consentSource: string;
}

export type BuddyLearningModality =
  | "Unknown"
  | "Reading"
  | "Listening"
  | "Speaking"
  | "Writing"
  | "Arrangement"
  | "Combat"
  | "Assessment"
  | "Conversation"
  | "Reference";

export type BuddyLearningSupportBand = "Foundation" | "Guided" | "Growing" | "Independent";

export interface BuddyLearningAttempt {
  schemaVersion: number;
  eventId: string;
  sourceRecordType?: string;
  sourceRecordId?: string;
  sessionId: string;
  studentId: string;
  classId: string;
  schoolId: string;
  missionId?: string;
  goalId?: string;
  areaId?: string;
  zoneKind?: string;
  activityType: string;
  modality: BuddyLearningModality | string;
  inputSource: string;
  contentId: string;
  dialogueTaskId?: string;
  attemptGroupId: string;
  attemptNumber: number;
  questionPrompt?: string;
  expectedResponse?: string;
  submittedResponse?: string;
  normalizedResponse?: string;
  correctedResponse?: string;
  grammarPattern?: string;
  conceptId: string;
  grimoireReference?: string;
  vocabularyTokens: string[];
  masteryTags: string[];
  errorTags: string[];
  countsTowardMastery: boolean;
  correct: boolean;
  completedIndependently: boolean;
  hintCount: number;
  highestHintLevel?: string;
  remediationStep?: string;
  buddyAssistMode?: string;
  responseSeconds: number;
  confidenceScore: number;
  recognitionConfidence: number;
  pronunciationScore: number;
  pronunciationInsight?: PronunciationInsight;
  handwritingDiagnostics?: HandwritingDiagnosticSummary;
  activeCurse?: string;
  actionVerb?: string;
  actionRole?: string;
  outcome?: string;
  createdAtUtc: string;
  createdAt?: string;
}

export interface BuddyLearningErrorCount {
  errorTag: string;
  count: number;
  lastSeenAtUtc?: string;
}

export interface BuddyModalityLearningState {
  modality: string;
  attempts: number;
  correctAttempts: number;
  independentCorrectAttempts: number;
  totalResponseSeconds: number;
  averageResponseSeconds: number;
  successRate: number;
  independentSuccessRate: number;
  lastPracticedAtUtc?: string;
}

export interface BuddyConceptLearningState {
  conceptId: string;
  attempts: number;
  correctAttempts: number;
  independentCorrectAttempts: number;
  assistedCorrectAttempts: number;
  firstAttemptCorrectAttempts: number;
  totalHints: number;
  totalResponseSeconds: number;
  averageResponseSeconds: number;
  successRate: number;
  independentSuccessRate: number;
  assistedSuccessRate: number;
  hintDependency: number;
  masteryEstimate: number;
  supportBand: BuddyLearningSupportBand;
  recommendedEnglishRatio: number;
  errors?: Record<string, number>;
  modalities?: Record<string, BuddyModalityLearningState>;
  lastPracticedAtUtc?: string;
}

export interface BuddyLearnerState {
  schemaVersion: number;
  studentId: string;
  schoolId?: string;
  homeLanguage?: string;
  targetLanguage?: string;
  sourceEventCount: number;
  correctAttemptCount: number;
  independentCorrectAttemptCount: number;
  totalHints: number;
  supportBand: BuddyLearningSupportBand;
  recommendedEnglishRatio: number;
  concepts: Record<string, BuddyConceptLearningState>;
  strengthConceptIds: string[];
  needConceptIds: string[];
  recurringErrorTags: string[];
  lastEventId?: string;
  lastEventAtUtc?: string;
  updatedAt?: string;
}

export interface BuddyLearningSession {
  schemaVersion: number;
  sessionId: string;
  studentId: string;
  classId: string;
  schoolId: string;
  missionId?: string;
  homeLanguage: string;
  targetLanguage: string;
  attemptCount: number;
  correctAttemptCount: number;
  independentCorrectAttemptCount: number;
  totalHints: number;
  conceptIds: string[];
  recurringErrorTags: string[];
  supportBand: BuddyLearningSupportBand;
  recommendedEnglishRatio: number;
  startedAtUtc: string;
  lastActivityAtUtc: string;
  endedAtUtc?: string;
}

export interface BuddyLearnerProfile {
  schemaVersion: number;
  profileId: "current" | string;
  studentId: string;
  classId: string;
  schoolId: string;
  homeLanguage: "hi" | string;
  targetLanguage: "en" | string;
  allowTransliteration: boolean;
  learningMemoryEnabled: boolean;
  explanationStyle: string;
  updatedAtUtc: string;
}

export interface DailyMissionAssignment {
  id: string;
  schoolId: string;
  classId: string;
  date: string;
  missionType: MissionType;
  missionDurationSeconds: number;
  countingChestCount: number;
  colorChestCount: number;
  lettersForToday: string[];
  wordsForToday: string[];
  revisionLetters: string[];
  createdByTeacherId: string;
}

export interface WorldGoalAssignment {
  goalId: string;
  schoolId: string;
  classId: string;
  studentId?: string;
  weekStart: string;
  targetAreaId: string;
  targetGymId: string;
  focusGrammarPatterns: string[];
  focusVocabulary: string[];
  dueDate: string;
  rewardCoins: number;
  schoolTimeZone: string;
  assignedAtUtc: string;
  createdByTeacherId: string;
}

export interface WorldGoalClaim {
  id: string;
  goalId: string;
  schoolId: string;
  classId: string;
  studentId: string;
  targetGymId: string;
  status: "reward_claimed" | "completed_late";
  onTime: boolean;
  rewardCoins: number;
  configuredRewardCoins: number;
  walletBalance: number;
  completedAtUtc: string;
  claimedAtUtc: string;
}

export interface StudentMissionOverride extends DailyMissionAssignment {
  studentId: string;
  baseMissionId: string;
  note?: string;
}

export interface RunSession {
  id: string;
  studentId: string;
  classId: string;
  schoolId: string;
  missionId: string;
  configuredDurationSeconds: number;
  actualDurationSeconds: number;
  subarenasCleared: number;
  fullLoopsCleared: number;
  lettersPracticed: string[];
  wordsPracticed: string[];
  grammarPatternsPracticed?: string[];
  masteryTagsPracticed?: string[];
  vocabularyTokens?: string[];
  acceptedSpokenVocabulary?: string[];
  acceptedWrittenVocabulary?: string[];
  acceptedBattleVocabulary?: string[];
  spokenPhraseCount?: number;
  writtenPhraseCount?: number;
  grammarBattleCount?: number;
  grammarErrors?: number;
  pronunciationRetries?: number;
  averageConfidence: number;
  averageAttemptsPerLetter: number;
  creaturesCleared: number;
  specialWordMatches: number;
  completed: boolean;
  startedAt: string;
  endedAt: string;
}

export interface Recommendation {
  id: string;
  schoolId?: string;
  classId: string;
  studentId?: string;
  priority: "low" | "medium" | "high";
  title: string;
  detail: string;
  createdAt: string;
}

export interface HandwritingPoint {
  x: number;
  y: number;
  strokeId: number;
  order: number;
}

export interface HandwritingDiagnosticSummary {
  letter: string;
  targetWord: string;
  letterIndex: number;
  severity: number;
  tags: string[];
  primaryHint: string;
  boundsX: number;
  boundsY: number;
  boundsWidth: number;
  boundsHeight: number;
  slotCenterOffsetX: number;
  baselineOffset: number;
  lineOverflowTop: number;
  lineOverflowBottom: number;
  mirrorScore: number;
  normalScore: number;
  wobbleScore?: number;
  localRoughness?: number;
  localKinkScore?: number;
  localKinkCount?: number;
  localKinkMax?: number;
  perpendicularJitterScore?: number;
  wobbleThresholdUsed?: number;
  pointDensity?: number;
  templateDeviationAverage?: number;
  templateDeviationMax?: number;
  templateNoiseScore?: number;
  pathLength?: number;
  directness?: number;
  pointCount?: number;
  strokeCount?: number;
  expectedStrokeCount?: number;
  formationState?: string;
  formationDiagnostic?: string;
  formationConfidence?: number;
  mirrorRecognized?: boolean;
  mirroredRecognitionName?: string;
  mirroredRecognitionScore?: number;
  neuralRecognizedName?: string;
  neuralConfidence?: number;
  combinedConfidence?: number;
  recognizerAgreement?: boolean;
  recognitionDecision?: string;
  accepted: boolean;
}

export interface LetterAttempt {
  id: string;
  studentId: string;
  classId: string;
  schoolId: string;
  missionId: string;
  letter: string;
  confident: boolean;
  attempts: number;
  gifted: boolean;
  confidenceScore: number;
  handwritingDiagnostics?: HandwritingDiagnosticSummary;
  neuralRecognizedName?: string;
  neuralConfidence?: number;
  combinedConfidence?: number;
  recognizerAgreement?: boolean;
  recognitionDecision?: string;
  createdAt: string;
}

export interface PhoneticSegment {
  spelling: string;
  friendlySound: string;
  heardSound?: string;
  beatIndex: number;
  status: "Unknown" | "Matched" | "NeedsPractice" | "Missing" | string;
  confidence: number;
}

export interface PhonemeAlignment {
  expected: string;
  observed: string;
  status: "matched" | "close" | "substituted" | "missing" | "extra" | string;
  confidence: number;
}

export interface PronunciationInsight {
  providerName: string;
  targetWord: string;
  confirmedWord: string;
  rawRecognizedText: string;
  voskConfirmedWord: boolean;
  attemptedTarget: boolean;
  score: number;
  modelConfidence?: number;
  hintKey: string;
  message: string;
  focusSegment?: PhoneticSegment;
  segments: PhoneticSegment[];
  syllableBeats: string[];
  expectedPhonemes?: string[];
  observedPhonemes?: string[];
  phonemeIssues?: PhonemeAlignment[];
  phonemeAlignment?: PhonemeAlignment[];
}

export interface WordCast {
  id: string;
  eventId?: string;
  studentId: string;
  classId: string;
  schoolId: string;
  missionId: string;
  contentId?: string;
  inputSource?: string;
  attemptGroupId?: string;
  attemptNumber?: number;
  word: string;
  success: boolean;
  specialMatch: boolean;
  responseSeconds: number;
  pronunciationInsight?: PronunciationInsight;
  serverPronunciationInsight?: PronunciationInsight;
  analysisMode?: "OnDeviceOnly" | "HybridServerAssist" | "ServerOnly" | string;
  onDeviceAnalysisProvider?: string;
  serverAnalysisStatus?: "not_requested" | "pending" | "processing" | "complete" | "failed" | string;
  serverAnalysisError?: string;
  serverAnalysisJobId?: string;
  rawAudioCaptured?: boolean;
  rawAudioUploaded?: boolean;
  rawAudioRetentionPolicy?: string;
  createdAtUtc?: string;
  createdAt?: string;
}

export interface SpokenPhraseEvent {
  id: string;
  eventId?: string;
  studentId: string;
  classId: string;
  schoolId: string;
  missionId?: string;
  goalId?: string;
  dialogueTaskId?: string;
  contentId?: string;
  inputSource?: string;
  attemptGroupId?: string;
  attemptNumber?: number;
  areaId: string;
  zoneKind: string;
  phrase: string;
  submittedPhrase?: string;
  targetPhrase?: string;
  grammarPattern: string;
  conceptId?: string;
  errorCategory?: string;
  hintLevelShown?: string;
  remediationStep?: string;
  correctedResponse?: string;
  buddyAssistMode?: string;
  vocabularyTokens?: string[];
  masteryTags?: string[];
  accepted: boolean;
  rejectionReason?: string;
  responseSeconds?: number;
  pronunciationInsight?: PronunciationInsight;
  rawAudioCaptured?: boolean;
  rawAudioUploaded?: boolean;
  rawAudioRetentionPolicy?: string;
  createdAtUtc?: string;
  createdAt?: string;
}

export interface WrittenPhraseEvent {
  id: string;
  eventId?: string;
  studentId: string;
  classId: string;
  schoolId: string;
  missionId?: string;
  goalId?: string;
  dialogueTaskId?: string;
  contentId?: string;
  inputSource?: string;
  attemptGroupId?: string;
  attemptNumber?: number;
  areaId: string;
  zoneKind: string;
  phrase: string;
  submittedPhrase?: string;
  targetPhrase?: string;
  grammarPattern: string;
  conceptId?: string;
  errorCategory?: string;
  hintLevelShown?: string;
  remediationStep?: string;
  correctedResponse?: string;
  buddyAssistMode?: string;
  vocabularyTokens?: string[];
  masteryTags?: string[];
  accepted: boolean;
  rejectionReason?: string;
  responseSeconds?: number;
  createdAtUtc?: string;
  createdAt?: string;
}

export interface GrammarBattleEvent {
  id: string;
  eventId?: string;
  studentId: string;
  classId: string;
  schoolId: string;
  missionId?: string;
  goalId?: string;
  contentId?: string;
  inputSource?: string;
  attemptGroupId?: string;
  attemptNumber?: number;
  areaId: string;
  zoneKind?: string;
  encounterType: string;
  playerPhrase: string;
  grammarPattern: string;
  conceptId?: string;
  errorCategory?: string;
  hintLevelShown?: string;
  remediationStep?: string;
  correctedResponse?: string;
  buddyAssistMode?: string;
  commandPreposition?: string;
  commandConjunction?: string;
  vocabularyTokens?: string[];
  masteryTags?: string[];
  activeCurse: string;
  enemyNounFamily?: string;
  enemyActionVerb?: string;
  enemyGrammarCommand?: string;
  enemyGrammarPattern?: string;
  actionVerb: string;
  actionRole: string;
  accepted: boolean;
  outcome: string;
  damageDealt?: number;
  ppSpent?: number;
  pronunciationInsight?: PronunciationInsight;
  rawAudioCaptured?: boolean;
  rawAudioUploaded?: boolean;
  rawAudioRetentionPolicy?: string;
  createdAtUtc?: string;
  createdAt?: string;
}

export interface BuddyConversationTurn {
  id: string;
  eventId?: string;
  studentId: string;
  classId: string;
  schoolId: string;
  missionId?: string;
  goalId?: string;
  dialogueTaskId?: string;
  contentId?: string;
  inputSource?: string;
  attemptGroupId?: string;
  attemptNumber?: number;
  areaId: string;
  zoneKind?: string;
  learnerMessage: string;
  buddyResponse: string;
  sourceLanguage?: string;
  targetLanguage?: string;
  englishRatio?: number;
  conversationSkill?: string;
  grammarPattern?: string;
  conceptId?: string;
  wordChoiceIssue?: string;
  formationIssue?: string;
  errorCategory?: string;
  hintLevelShown?: string;
  remediationStep?: string;
  correctedResponse?: string;
  buddyAssistMode?: string;
  vocabularyTokens?: string[];
  masteryTags?: string[];
  safeMemoryTags?: string[];
  safetyFlags?: string[];
  teacherNote?: string;
  buddyContractId?: string;
  promptTemplateId?: string;
  policyVersion?: string;
  reportable?: boolean;
  responseSeconds?: number;
  provider?: string;
  model?: string;
  trigger?: "ask" | "wrong_answer" | string;
  buddyStatus?: "ok" | "fallback" | "blocked" | string;
  buddyFallbackReason?: string;
  modelUsage?: Record<string, unknown>;
  createdAtUtc?: string;
  createdAt?: string;
}

export interface GymAttempt {
  id: string;
  attemptId?: string;
  studentId: string;
  classId: string;
  schoolId: string;
  missionId?: string;
  goalId?: string;
  areaId: string;
  gymId: string;
  zoneKind?: string;
  buddyAssistMode?: string;
  masteryTags?: string[];
  passed: boolean;
  spokenPhraseCount: number;
  writtenPhraseCount: number;
  grammarErrors: number;
  pronunciationRetries: number;
  startedAtUtc?: string;
  endedAtUtc?: string;
  startedAt?: string;
  endedAt?: string;
}

export interface CountingMiniGameAttempt {
  id: string;
  studentId: string;
  classId: string;
  schoolId: string;
  missionId: string;
  contentId?: string;
  inputSource?: string;
  attemptGroupId?: string;
  attemptNumber?: number;
  chestCategory: string;
  targetCount: number;
  selectedCount: number;
  spokenNumber?: string;
  countCorrect: boolean;
  speechProofSucceeded: boolean;
  hintUsed: boolean;
  pronunciationInsight?: PronunciationInsight;
  serverPronunciationInsight?: PronunciationInsight;
  analysisMode?: "OnDeviceOnly" | "HybridServerAssist" | "ServerOnly" | string;
  onDeviceAnalysisProvider?: string;
  serverAnalysisStatus?: "not_requested" | "pending" | "processing" | "complete" | "failed" | string;
  serverAnalysisError?: string;
  serverAnalysisJobId?: string;
  rawAudioCaptured?: boolean;
  rawAudioUploaded?: boolean;
  rawAudioRetentionPolicy?: string;
  outcomeStatus?: string;
  rewardAwarded: number;
  responseSeconds: number;
  createdAtUtc?: string;
  createdAt?: string;
}

export interface ColorMiniGameAttempt {
  id: string;
  studentId: string;
  classId: string;
  schoolId: string;
  missionId: string;
  contentId?: string;
  inputSource?: string;
  attemptGroupId?: string;
  attemptNumber?: number;
  chestCategory: string;
  targetColor: string;
  selectedColor: string;
  spokenColor?: string;
  colorCorrect: boolean;
  speechProofSucceeded: boolean;
  hintUsed: boolean;
  pronunciationInsight?: PronunciationInsight;
  serverPronunciationInsight?: PronunciationInsight;
  analysisMode?: "OnDeviceOnly" | "HybridServerAssist" | "ServerOnly" | string;
  onDeviceAnalysisProvider?: string;
  serverAnalysisStatus?: "not_requested" | "pending" | "processing" | "complete" | "failed" | string;
  serverAnalysisError?: string;
  serverAnalysisJobId?: string;
  rawAudioCaptured?: boolean;
  rawAudioUploaded?: boolean;
  rawAudioRetentionPolicy?: string;
  outcomeStatus?: string;
  rewardAwarded: number;
  responseSeconds: number;
  createdAtUtc?: string;
  createdAt?: string;
}

export interface EmpathyEventAttempt {
  id: string;
  studentId: string;
  classId: string;
  schoolId: string;
  missionId: string;
  eventCategory: string;
  empathySkill?: string;
  prompt?: string;
  targetResponse?: string;
  selectedResponse?: string;
  reflectionText?: string;
  outcomeStatus?: string;
  responseSeconds?: number;
  createdAtUtc?: string;
  createdAt?: string;
}

export interface AcceptedHandwritingTemplate {
  id: string;
  studentId: string;
  classId: string;
  schoolId: string;
  missionId: string;
  letter: string;
  targetWord: string;
  letterIndex: number;
  attemptsForLetter: number;
  gifted: boolean;
  recognitionScore: number;
  recognizedName: string;
  bestCandidateName: string;
  runnerUpName: string;
  runnerUpScore: number;
  scoreMargin: number;
  isAmbiguous: boolean;
  handwritingDiagnostics?: HandwritingDiagnosticSummary;
  points: HandwritingPoint[];
  neuralRecognizedName?: string;
  neuralConfidence?: number;
  combinedConfidence?: number;
  recognizerAgreement?: boolean;
  recognitionDecision?: string;
  analysisMode?: "OnDeviceOnly" | "HybridServerAssist" | "ServerOnly" | string;
  onDeviceAnalysisProvider?: string;
  serverAnalysisStatus?: "not_requested" | "pending" | "processing" | "complete" | "failed" | string;
  serverAnalysisJobId?: string;
  createdAt: string;
}

export interface ParentStudentSummary {
  id: string;
  schoolId: string;
  studentId: string;
  studentName: string;
  classId: string;
  weekStart: string;
  minutesPracticed: number;
  lettersPracticed: string[];
  wordsPracticed: string[];
  bestLetter: string;
  needsPracticeLetter: string;
  averageConfidence: number;
  averageAttemptsPerLetter: number;
  trendLabel: string;
  updatedAt: string;
}

export interface AccessCodeRecord {
  id: string;
  type: AccessCodeType;
  status: AccessCodeStatus;
  schoolId: string;
  classId?: string;
  studentId?: string;
  teacherEmail?: string;
  codeHash: string;
  expiresAt: string;
  createdByUid: string;
  createdAt: string;
  redeemedByUid?: string;
  redeemedAt?: string;
}

export interface AuditEvent {
  id: string;
  schoolId: string;
  actorUid: string;
  action: string;
  targetPath: string;
  createdAt: string;
}
