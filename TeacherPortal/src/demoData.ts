import type {
  AcceptedHandwritingTemplate,
  BuddyConversationTurn,
  Classroom,
  ColorMiniGameAttempt,
  CountingMiniGameAttempt,
  DailyMissionAssignment,
  EmpathyEventAttempt,
  GrammarBattleEvent,
  HandwritingDiagnosticSummary,
  GymAttempt,
  LetterAttempt,
  Recommendation,
  RunSession,
  School,
  SpokenPhraseEvent,
  Student,
  WorldGoalAssignment,
  WrittenPhraseEvent,
  WordCast
} from "./types";

export const school: School = {
  id: "demo-school",
  name: "Little Lantern Preschool"
};

export const classroom: Classroom = {
  id: "demo-class-lkg-a",
  schoolId: school.id,
  name: "LKG A",
  studentIds: [
    "demo-student-1",
    "demo-student-2",
    "demo-student-3",
    "demo-student-4",
    "demo-student-5"
  ]
};

export const students: Student[] = [
  { id: "demo-student-1", schoolId: school.id, classId: classroom.id, name: "Aarav", avatarColor: "#7fc8ff" },
  { id: "demo-student-2", schoolId: school.id, classId: classroom.id, name: "Mira", avatarColor: "#ffc76b" },
  { id: "demo-student-3", schoolId: school.id, classId: classroom.id, name: "Vihaan", avatarColor: "#9fdc8a" },
  { id: "demo-student-4", schoolId: school.id, classId: classroom.id, name: "Ira", avatarColor: "#f6a3bf" },
  { id: "demo-student-5", schoolId: school.id, classId: classroom.id, name: "Kabir", avatarColor: "#b8a7ff" }
];

export const initialMission: DailyMissionAssignment = {
  id: `${classroom.id}_${new Date().toISOString().slice(0, 10)}`,
  schoolId: school.id,
  classId: classroom.id,
  date: new Date().toISOString().slice(0, 10),
  missionType: "practice",
  missionDurationSeconds: 8 * 60,
  countingChestCount: 1,
  colorChestCount: 0,
  lettersForToday: ["A", "C", "T"],
  wordsForToday: ["ANT", "CAT", "TOP"],
  revisionLetters: ["A"],
  createdByTeacherId: "demo-teacher"
};

export const worldGoals: WorldGoalAssignment[] = [
  {
    goalId: `${classroom.id}_${new Date().toISOString().slice(0, 10)}_world`,
    schoolId: school.id,
    classId: classroom.id,
    weekStart: new Date().toISOString().slice(0, 10),
    targetAreaId: "TOWN:BASICPREPOSITIONS:11",
    targetGymId: "GYM:BASICPREPOSITIONS:11",
    focusGrammarPatterns: ["FullSentence"],
    focusVocabulary: ["IN", "ON", "UNDER", "BEHIND", "RAT", "BOX", "ROOF"],
    dueDate: new Date(Date.now() + 6 * 86400000).toISOString().slice(0, 10),
    rewardCoins: 25,
    schoolTimeZone: "Asia/Kolkata",
    assignedAtUtc: new Date().toISOString(),
    createdByTeacherId: "demo-teacher"
  }
];

export const spokenPhraseEvents: SpokenPhraseEvent[] = [
  {
    id: "spoken-phrase-1",
    studentId: "demo-student-1",
    classId: classroom.id,
    schoolId: school.id,
    missionId: initialMission.id,
    goalId: worldGoals[0].goalId,
    areaId: "ROUTE:PRONOUNS:8",
    zoneKind: "Route",
    phrase: "he bites",
    grammarPattern: "PronounVerbPresent",
    conceptId: "Pronouns",
    vocabularyTokens: ["HE", "BITES"],
    masteryTags: ["pronoun", "pronoun-verb", "curse-subject"],
    accepted: true,
    responseSeconds: 2.1,
    createdAtUtc: new Date(Date.now() - 3600000).toISOString()
  },
  {
    id: "spoken-phrase-2",
    studentId: "demo-student-2",
    classId: classroom.id,
    schoolId: school.id,
    missionId: initialMission.id,
    goalId: worldGoals[0].goalId,
    areaId: "ROUTE:PRONOUNS:8",
    zoneKind: "Route",
    phrase: "he bite",
    grammarPattern: "PronounVerbPresent",
    conceptId: "Pronouns",
    errorCategory: "response_mismatch",
    hintLevelShown: "RuleHint",
    remediationStep: "GuidedRetry",
    correctedResponse: "He bites",
    vocabularyTokens: ["HE", "BITE"],
    masteryTags: ["pronoun", "pronoun-verb", "curse-subject"],
    accepted: false,
    rejectionReason: "response_mismatch",
    responseSeconds: 4.8,
    createdAtUtc: new Date(Date.now() - 5400000).toISOString()
  },
  {
    id: "spoken-phrase-3",
    studentId: "demo-student-1",
    classId: classroom.id,
    schoolId: school.id,
    missionId: initialMission.id,
    goalId: worldGoals[0].goalId,
    areaId: "ROUTE:ARTICLES:7",
    zoneKind: "Route",
    phrase: "an owl",
    submittedPhrase: "an",
    targetPhrase: "an owl",
    grammarPattern: "DeterminerNoun",
    conceptId: "Articles",
    vocabularyTokens: ["AN", "OWL"],
    masteryTags: ["articles", "a-an-the", "noun-phrases"],
    accepted: true,
    responseSeconds: 2.7,
    createdAtUtc: new Date(Date.now() - 6200000).toISOString()
  },
  {
    id: "spoken-phrase-4",
    studentId: "demo-student-1",
    classId: classroom.id,
    schoolId: school.id,
    missionId: initialMission.id,
    goalId: worldGoals[0].goalId,
    areaId: "ROUTE:BASICPREPOSITIONS:11",
    zoneKind: "Route",
    phrase: "rat is on the box",
    submittedPhrase: "rat is on the box",
    targetPhrase: "rat is behind the box",
    grammarPattern: "FullSentence",
    conceptId: "BasicPrepositions",
    errorCategory: "heard_wrong",
    hintLevelShown: "RuleHint",
    remediationStep: "GuidedRetry",
    correctedResponse: "Rat is behind the box.",
    vocabularyTokens: ["RAT", "ON", "BOX", "BEHIND"],
    masteryTags: ["prepositions", "location-words", "sentence-meaning"],
    accepted: false,
    rejectionReason: "heard_wrong",
    responseSeconds: 5.4,
    createdAtUtc: new Date(Date.now() - 1800000).toISOString()
  },
  {
    id: "spoken-phrase-5",
    studentId: "demo-student-3",
    classId: classroom.id,
    schoolId: school.id,
    missionId: initialMission.id,
    goalId: worldGoals[0].goalId,
    areaId: "TOWN:ADJECTIVES:10",
    zoneKind: "Town",
    phrase: "a big rat",
    grammarPattern: "DeterminerAdjectiveNoun",
    conceptId: "Adjectives",
    vocabularyTokens: ["A", "BIG", "RAT"],
    masteryTags: ["adjective", "summon-tradeoff", "anti-spam"],
    accepted: true,
    responseSeconds: 2.9,
    createdAtUtc: new Date(Date.now() - 2600000).toISOString()
  }
];

export const writtenPhraseEvents: WrittenPhraseEvent[] = [
  {
    id: "written-phrase-1",
    studentId: "demo-student-1",
    classId: classroom.id,
    schoolId: school.id,
    missionId: initialMission.id,
    goalId: worldGoals[0].goalId,
    areaId: "ROUTE:PRONOUNS:8",
    zoneKind: "Route",
    phrase: "I bite",
    submittedPhrase: "I bite",
    targetPhrase: "I bite",
    grammarPattern: "PronounVerbPresent",
    conceptId: "Pronouns",
    vocabularyTokens: ["I", "BITE"],
    masteryTags: ["pronoun", "pronoun-verb", "curse-subject"],
    accepted: true,
    responseSeconds: 3.2,
    createdAtUtc: new Date(Date.now() - 5000000).toISOString()
  },
  {
    id: "written-phrase-2",
    studentId: "demo-student-1",
    classId: classroom.id,
    schoolId: school.id,
    missionId: initialMission.id,
    goalId: worldGoals[0].goalId,
    areaId: "ROUTE:BASICPREPOSITIONS:11",
    zoneKind: "Route",
    phrase: "Rat is under the box.",
    submittedPhrase: "Rat is under the box.",
    targetPhrase: "Rat is under the box.",
    grammarPattern: "FullSentence",
    conceptId: "BasicPrepositions",
    vocabularyTokens: ["RAT", "UNDER", "BOX"],
    masteryTags: ["prepositions", "location-words", "sentence-meaning"],
    accepted: true,
    responseSeconds: 4.1,
    createdAtUtc: new Date(Date.now() - 1500000).toISOString()
  },
  {
    id: "written-phrase-3",
    studentId: "demo-student-2",
    classId: classroom.id,
    schoolId: school.id,
    missionId: initialMission.id,
    goalId: worldGoals[0].goalId,
    areaId: "ROUTE:ADJECTIVES:10",
    zoneKind: "Route",
    phrase: "big rat",
    submittedPhrase: "rat big",
    targetPhrase: "Big rat",
    grammarPattern: "AdjectiveNoun",
    conceptId: "Adjectives",
    errorCategory: "scrambled_sentence",
    hintLevelShown: "DirectCorrection",
    remediationStep: "Retry",
    correctedResponse: "Big rat",
    vocabularyTokens: ["BIG", "RAT"],
    masteryTags: ["adjective", "summon-tradeoff", "anti-spam"],
    accepted: false,
    rejectionReason: "scrambled_sentence",
    responseSeconds: 4.6,
    createdAtUtc: new Date(Date.now() - 2100000).toISOString()
  }
];

export const grammarBattleEvents: GrammarBattleEvent[] = [
  {
    id: "battle-event-1",
    studentId: "demo-student-1",
    classId: classroom.id,
    schoolId: school.id,
    missionId: initialMission.id,
    goalId: worldGoals[0].goalId,
    areaId: "ROUTE:PRONOUNS:8",
    encounterType: "trainer",
    playerPhrase: "he bites",
    grammarPattern: "PronounVerbPresent",
    conceptId: "Pronouns",
    vocabularyTokens: ["HE", "BITES"],
    masteryTags: ["pronoun", "pronoun-verb", "curse-subject"],
    activeCurse: "HeSheIt",
    enemyNounFamily: "BIRD",
    enemyActionVerb: "PECK",
    enemyGrammarCommand: "Bird pecks",
    enemyGrammarPattern: "PronounVerbPresent",
    actionVerb: "BITE",
    actionRole: "Command",
    accepted: true,
    outcome: "command_success",
    damageDealt: 4,
    ppSpent: 3,
    createdAtUtc: new Date(Date.now() - 3000000).toISOString()
  },
  {
    id: "battle-event-2",
    studentId: "demo-student-1",
    classId: classroom.id,
    schoolId: school.id,
    missionId: initialMission.id,
    goalId: worldGoals[0].goalId,
    areaId: "ROUTE:ADJECTIVES:10",
    encounterType: "trainer",
    playerPhrase: "a big rat",
    grammarPattern: "DeterminerAdjectiveNoun",
    conceptId: "Adjectives",
    vocabularyTokens: ["A", "BIG", "RAT"],
    masteryTags: ["adjective", "summon-tradeoff", "anti-spam"],
    activeCurse: "None",
    enemyNounFamily: "DOG",
    enemyActionVerb: "BITE",
    enemyGrammarCommand: "Dog bites",
    enemyGrammarPattern: "NounVerbPresent",
    actionVerb: "",
    actionRole: "Summon",
    accepted: true,
    outcome: "summoned",
    ppSpent: 2,
    createdAtUtc: new Date(Date.now() - 2300000).toISOString()
  },
  {
    id: "battle-event-3",
    studentId: "demo-student-2",
    classId: classroom.id,
    schoolId: school.id,
    missionId: initialMission.id,
    goalId: worldGoals[0].goalId,
    areaId: "ROUTE:ARTICLES:7",
    encounterType: "trainer",
    playerPhrase: "a owl",
    grammarPattern: "DeterminerNoun",
    conceptId: "Articles",
    errorCategory: "response_mismatch",
    hintLevelShown: "RuleHint",
    remediationStep: "GuidedRetry",
    correctedResponse: "An owl",
    vocabularyTokens: ["A", "OWL", "AN"],
    masteryTags: ["articles", "a-an-the", "noun-phrases"],
    activeCurse: "None",
    enemyNounFamily: "OWL",
    enemyActionVerb: "PECK",
    enemyGrammarCommand: "Owl pecks",
    enemyGrammarPattern: "NounVerbPresent",
    actionVerb: "",
    actionRole: "Summon",
    accepted: false,
    outcome: "response_mismatch",
    ppSpent: 0,
    createdAtUtc: new Date(Date.now() - 3200000).toISOString()
  }
];

export const buddyConversationTurns: BuddyConversationTurn[] = [
  {
    id: "buddy-turn-1",
    eventId: "buddy-turn-1",
    studentId: "demo-student-1",
    classId: classroom.id,
    schoolId: school.id,
    missionId: initialMission.id,
    goalId: worldGoals[0].goalId,
    areaId: "ROUTE:PRONOUNS:8",
    zoneKind: "Route",
    learnerMessage: "He bite?",
    buddyResponse: "Almost. One person needs a special verb ending. Say the sentence again.",
    sourceLanguage: "hi",
    targetLanguage: "en",
    englishRatio: 0.55,
    conversationSkill: "friendly_correction",
    grammarPattern: "PronounVerbPresent",
    conceptId: "Pronouns",
    formationIssue: "third_person_s_missing",
    errorCategory: "subject_verb_agreement",
    hintLevelShown: "RuleHint",
    remediationStep: "GuidedRetry",
    buddyAssistMode: "Partial",
    vocabularyTokens: ["HE", "BITE", "BITES"],
    masteryTags: ["pronoun", "pronoun-verb", "curse-subject"],
    safeMemoryTags: ["likes-friendly-examples", "needs-third-person-s-practice"],
    teacherNote: "Buddy corrected third-person present during route conversation.",
    reportable: true,
    provider: "deterministic_fallback",
    trigger: "wrong_answer",
    buddyStatus: "fallback",
    buddyFallbackReason: "demo_mode",
    responseSeconds: 2.7,
    createdAtUtc: new Date(Date.now() - 1200000).toISOString()
  }
];

export const gymAttempts: GymAttempt[] = [
  {
    id: "gym-attempt-1",
    studentId: "demo-student-1",
    classId: classroom.id,
    schoolId: school.id,
    missionId: initialMission.id,
    goalId: worldGoals[0].goalId,
    areaId: "GYM:BASICPREPOSITIONS:11",
    gymId: "GYM:BASICPREPOSITIONS:11",
    masteryTags: ["prepositions", "location-words", "sentence-meaning"],
    passed: true,
    spokenPhraseCount: 10,
    writtenPhraseCount: 4,
    grammarErrors: 1,
    pronunciationRetries: 2,
    startedAtUtc: new Date(Date.now() - 2500000).toISOString(),
    endedAtUtc: new Date(Date.now() - 2200000).toISOString()
  },
  {
    id: "gym-attempt-2",
    studentId: "demo-student-2",
    classId: classroom.id,
    schoolId: school.id,
    missionId: initialMission.id,
    goalId: worldGoals[0].goalId,
    areaId: "GYM:ARTICLES:7",
    gymId: "GYM:ARTICLES:7",
    masteryTags: ["articles", "a-an-the", "noun-phrases"],
    passed: false,
    spokenPhraseCount: 5,
    writtenPhraseCount: 2,
    grammarErrors: 3,
    pronunciationRetries: 1,
    startedAtUtc: new Date(Date.now() - 4100000).toISOString(),
    endedAtUtc: new Date(Date.now() - 3900000).toISOString()
  }
];

export const runSessions: RunSession[] = [
  {
    id: "run-1",
    studentId: "demo-student-1",
    classId: classroom.id,
    schoolId: school.id,
    missionId: initialMission.id,
    configuredDurationSeconds: 480,
    actualDurationSeconds: 470,
    subarenasCleared: 7,
    fullLoopsCleared: 1,
    lettersPracticed: ["A", "C", "T"],
    wordsPracticed: ["ANT", "CAT", "RAT", "HE", "AN", "UNDER", "BEHIND", "BIG"],
    grammarPatternsPracticed: ["PronounVerbPresent", "DeterminerNoun", "DeterminerAdjectiveNoun", "FullSentence"],
    masteryTagsPracticed: ["pronoun", "articles", "adjective", "prepositions"],
    vocabularyTokens: ["RAT", "HE", "BITES", "AN", "OWL", "UNDER", "BEHIND", "BIG", "BOX"],
    acceptedSpokenVocabulary: ["RAT", "BITES", "AN", "OWL", "BIG"],
    acceptedWrittenVocabulary: ["I", "BITE", "UNDER", "BOX"],
    acceptedBattleVocabulary: ["HE", "BITES", "A", "BIG", "RAT"],
    spokenPhraseCount: 11,
    writtenPhraseCount: 5,
    grammarBattleCount: 6,
    grammarErrors: 1,
    pronunciationRetries: 2,
    averageConfidence: 0.82,
    averageAttemptsPerLetter: 2.1,
    creaturesCleared: 16,
    specialWordMatches: 4,
    completed: true,
    startedAt: new Date(Date.now() - 86400000).toISOString(),
    endedAt: new Date(Date.now() - 86400000 + 470000).toISOString()
  },
  {
    id: "run-2",
    studentId: "demo-student-2",
    classId: classroom.id,
    schoolId: school.id,
    missionId: initialMission.id,
    configuredDurationSeconds: 480,
    actualDurationSeconds: 480,
    subarenasCleared: 4,
    fullLoopsCleared: 1,
    lettersPracticed: ["A", "C"],
    wordsPracticed: ["CAT", "HE", "BITE", "AN", "OWL", "BIG"],
    grammarPatternsPracticed: ["PronounVerbPresent", "DeterminerNoun", "AdjectiveNoun"],
    masteryTagsPracticed: ["pronoun", "articles", "adjective"],
    vocabularyTokens: ["HE", "BITE", "AN", "OWL", "BIG", "RAT"],
    acceptedSpokenVocabulary: [],
    acceptedWrittenVocabulary: [],
    acceptedBattleVocabulary: [],
    spokenPhraseCount: 4,
    writtenPhraseCount: 2,
    grammarBattleCount: 3,
    grammarErrors: 4,
    pronunciationRetries: 1,
    averageConfidence: 0.68,
    averageAttemptsPerLetter: 3.4,
    creaturesCleared: 9,
    specialWordMatches: 1,
    completed: true,
    startedAt: new Date(Date.now() - 7200000).toISOString(),
    endedAt: new Date(Date.now() - 7200000 + 480000).toISOString()
  }
];

export const recommendations: Recommendation[] = [
  {
    id: "rec-1",
    classId: classroom.id,
    studentId: "demo-student-2",
    priority: "high",
    title: "Article and adjective reteach",
    detail: "Mira is still flipping article choice and adjective order. Keep Article Arcade and Adjective Grove in the retry set before pushing to the next gym.",
    createdAt: new Date().toISOString()
  },
  {
    id: "rec-2",
    classId: classroom.id,
    priority: "medium",
    title: "Preposition target is on track",
    detail: "Aarav cleared the Preposition Park gym. Keep route practice on in, under, and behind so the rest of the class can close the current class focus.",
    createdAt: new Date().toISOString()
  }
];

export const acceptedHandwritingTemplates: AcceptedHandwritingTemplate[] = [
  makeTemplate("tpl-1", "demo-student-1", "A", "ANT", 0, [
    [[60, 220], [115, 60], [170, 220]],
    [[82, 155], [148, 155]]
  ], 24.5, 1, makeDiagnostic("A", "ANT", 0, ["wrongStrokeOrder"], "Start A with the first diagonal stroke.", 2)),
  makeTemplate("tpl-2", "demo-student-1", "C", "CAT", 0, [
    [[178, 85], [140, 55], [85, 66], [55, 120], [67, 175], [118, 205], [174, 178]]
  ], 31.2, 2, makeDiagnostic("C", "CAT", 0, ["aboveBaseline"], "Bring the letter down to sit on the baseline.", 1)),
  makeTemplate("tpl-3", "demo-student-2", "C", "CAT", 0, [
    [[184, 78], [132, 42], [72, 64], [48, 126], [72, 190], [138, 210]]
  ], 43.8, 3, makeDiagnostic("C", "CAT", 0, ["oversized", "spacingDrift"], "Keep it between the notebook lines.", 2)),
  makeTemplate("tpl-4", "demo-student-2", "T", "TOP", 0, [
    [[55, 70], [185, 70]],
    [[120, 70], [120, 220]]
  ], 27.1, 1, makeDiagnostic("T", "TOP", 0, ["wrongStrokeOrder", "reversedStroke"], "Start with the top line of T.", 3))
];

export const letterAttempts: LetterAttempt[] = [
  makeAttempt("att-1", "demo-student-1", "A", true, 1, makeDiagnostic("A", "ANT", 0, ["wrongStrokeOrder"], "Start A with the first diagonal stroke.", 2)),
  makeAttempt("att-2", "demo-student-1", "C", true, 2, makeDiagnostic("C", "CAT", 0, ["aboveBaseline"], "Bring the letter down to sit on the baseline.", 1)),
  makeAttempt("att-3", "demo-student-2", "B", false, 2, makeDiagnostic("B", "BUG", 0, ["mirror"], "That looks flipped. Start B from the normal side.", 3)),
  makeAttempt("att-4", "demo-student-2", "C", true, 3, makeDiagnostic("C", "CAT", 0, ["oversized", "spacingDrift"], "Keep it between the notebook lines.", 2)),
  makeAttempt("att-5", "demo-student-2", "T", true, 1, makeDiagnostic("T", "TOP", 0, ["wrongStrokeOrder", "reversedStroke"], "Start with the top line of T.", 3)),
  makeAttempt("att-6", "demo-student-4", "D", false, 2, makeDiagnostic("D", "DOG", 0, ["mirror", "belowBaseline"], "That looks flipped. Start D from the normal side.", 3))
];

export const wordCasts: WordCast[] = [
  {
    id: "cast-1",
    studentId: "demo-student-1",
    classId: classroom.id,
    schoolId: school.id,
    missionId: initialMission.id,
    word: "RABBIT",
    success: true,
    specialMatch: false,
    responseSeconds: 1.4,
    pronunciationInsight: {
      providerName: "Azure Pronunciation Assessment",
      targetWord: "RABBIT",
      confirmedWord: "RABBIT",
      rawRecognizedText: "wabbit",
      voskConfirmedWord: true,
      attemptedTarget: true,
      score: 0.62,
      hintKey: "TryFirstSound",
      message: "Accepted by Vosk, with an initial sound to practice.",
      focusSegment: { spelling: "R", friendlySound: "r", heardSound: "wa", beatIndex: 0, status: "Missing", confidence: 0.12 },
      segments: [
        { spelling: "R", friendlySound: "r", heardSound: "wa", beatIndex: 0, status: "Missing", confidence: 0.12 },
        { spelling: "A", friendlySound: "a", beatIndex: 0, status: "Matched", confidence: 0.82 },
        { spelling: "B", friendlySound: "b", beatIndex: 1, status: "Matched", confidence: 0.82 },
        { spelling: "B", friendlySound: "b", beatIndex: 1, status: "Matched", confidence: 0.82 },
        { spelling: "I", friendlySound: "i", beatIndex: 1, status: "Matched", confidence: 0.82 },
        { spelling: "T", friendlySound: "t", beatIndex: 2, status: "Matched", confidence: 0.82 }
      ],
      syllableBeats: ["RA", "BBI", "T"]
    },
    createdAtUtc: new Date().toISOString()
  },
  {
    id: "cast-2",
    studentId: "demo-student-2",
    classId: classroom.id,
    schoolId: school.id,
    missionId: initialMission.id,
    word: "CAT",
    success: false,
    specialMatch: false,
    responseSeconds: 2.1,
    pronunciationInsight: {
      providerName: "Azure Pronunciation Assessment",
      targetWord: "CAT",
      confirmedWord: "",
      rawRecognizedText: "at",
      voskConfirmedWord: false,
      attemptedTarget: true,
      score: 0.48,
      hintKey: "TryFirstSound",
      message: "Missing the first sound.",
      focusSegment: { spelling: "C", friendlySound: "k", heardSound: "", beatIndex: 0, status: "Missing", confidence: 0.12 },
      segments: [
        { spelling: "C", friendlySound: "k", heardSound: "", beatIndex: 0, status: "Missing", confidence: 0.12 },
        { spelling: "A", friendlySound: "a", beatIndex: 0, status: "Matched", confidence: 0.82 },
        { spelling: "T", friendlySound: "t", beatIndex: 1, status: "Matched", confidence: 0.82 }
      ],
      syllableBeats: ["CA", "T"]
    },
    createdAtUtc: new Date().toISOString()
  }
];

export const countingMiniGameAttempts: CountingMiniGameAttempt[] = [
  {
    id: "count-1",
    studentId: "demo-student-1",
    classId: classroom.id,
    schoolId: school.id,
    missionId: initialMission.id,
    chestCategory: "StageChest_1",
    targetCount: 7,
    selectedCount: 7,
    spokenNumber: "seven",
    countCorrect: true,
    speechProofSucceeded: true,
    hintUsed: false,
    outcomeStatus: "opened_correct",
    rewardAwarded: 7,
    responseSeconds: 3.2,
    createdAtUtc: new Date().toISOString()
  },
  {
    id: "count-2",
    studentId: "demo-student-1",
    classId: classroom.id,
    schoolId: school.id,
    missionId: initialMission.id,
    chestCategory: "StageChest_2",
    targetCount: 5,
    selectedCount: 5,
    spokenNumber: "five",
    countCorrect: true,
    speechProofSucceeded: false,
    hintUsed: true,
    outcomeStatus: "opened_correct_pronunciation_failed",
    rewardAwarded: 5,
    responseSeconds: 4.9,
    createdAtUtc: new Date(Date.now() - 3600000).toISOString()
  },
  {
    id: "count-3",
    studentId: "demo-student-2",
    classId: classroom.id,
    schoolId: school.id,
    missionId: initialMission.id,
    chestCategory: "StageChest_1",
    targetCount: 8,
    selectedCount: 6,
    spokenNumber: "six",
    countCorrect: false,
    speechProofSucceeded: true,
    hintUsed: false,
    outcomeStatus: "opened_wrong_answer",
    rewardAwarded: 4,
    responseSeconds: 5.5,
    createdAtUtc: new Date(Date.now() - 7200000).toISOString()
  },
  {
    id: "count-4",
    studentId: "demo-student-1",
    classId: classroom.id,
    schoolId: school.id,
    missionId: initialMission.id,
    chestCategory: "StageChest_3",
    targetCount: 4,
    selectedCount: 0,
    spokenNumber: "",
    countCorrect: false,
    speechProofSucceeded: false,
    hintUsed: false,
    outcomeStatus: "seen_ignored",
    rewardAwarded: 0,
    responseSeconds: 0,
    createdAtUtc: new Date(Date.now() - 10800000).toISOString()
  }
];

export const colorMiniGameAttempts: ColorMiniGameAttempt[] = [
  {
    id: "color-1",
    studentId: "demo-student-1",
    classId: classroom.id,
    schoolId: school.id,
    missionId: initialMission.id,
    chestCategory: "StageColorChest_1",
    targetColor: "blue",
    selectedColor: "blue",
    spokenColor: "blue",
    colorCorrect: true,
    speechProofSucceeded: true,
    hintUsed: false,
    outcomeStatus: "opened_correct",
    rewardAwarded: 6,
    responseSeconds: 3.8,
    createdAtUtc: new Date(Date.now() - 1800000).toISOString()
  },
  {
    id: "color-2",
    studentId: "demo-student-2",
    classId: classroom.id,
    schoolId: school.id,
    missionId: initialMission.id,
    chestCategory: "StageColorChest_1",
    targetColor: "green",
    selectedColor: "yellow",
    spokenColor: "yellow",
    colorCorrect: false,
    speechProofSucceeded: true,
    hintUsed: false,
    outcomeStatus: "opened_wrong_answer",
    rewardAwarded: 3,
    responseSeconds: 5.1,
    createdAtUtc: new Date(Date.now() - 5400000).toISOString()
  },
  {
    id: "color-3",
    studentId: "demo-student-1",
    classId: classroom.id,
    schoolId: school.id,
    missionId: initialMission.id,
    chestCategory: "StageColorChest_2",
    targetColor: "purple",
    selectedColor: "purple",
    spokenColor: "purple",
    colorCorrect: true,
    speechProofSucceeded: false,
    hintUsed: true,
    outcomeStatus: "opened_correct_pronunciation_failed",
    rewardAwarded: 6,
    responseSeconds: 4.4,
    createdAtUtc: new Date(Date.now() - 9000000).toISOString()
  }
];

export const empathyEventAttempts: EmpathyEventAttempt[] = [
  {
    id: "empathy-1",
    studentId: "demo-student-1",
    classId: classroom.id,
    schoolId: school.id,
    missionId: initialMission.id,
    eventCategory: "comfort_peer",
    empathySkill: "comforting",
    prompt: "A friend feels sad after losing a game.",
    targetResponse: "Offer help and kind words",
    selectedResponse: "Offer help and kind words",
    reflectionText: "I can help them try again.",
    outcomeStatus: "supportive_choice",
    responseSeconds: 6.2,
    createdAtUtc: new Date(Date.now() - 2400000).toISOString()
  },
  {
    id: "empathy-2",
    studentId: "demo-student-1",
    classId: classroom.id,
    schoolId: school.id,
    missionId: initialMission.id,
    eventCategory: "sharing",
    empathySkill: "sharing",
    prompt: "Two students want the same crayon.",
    targetResponse: "Take turns",
    selectedResponse: "Keep it",
    reflectionText: "",
    outcomeStatus: "needs_support",
    responseSeconds: 5.4,
    createdAtUtc: new Date(Date.now() - 8400000).toISOString()
  },
  {
    id: "empathy-3",
    studentId: "demo-student-2",
    classId: classroom.id,
    schoolId: school.id,
    missionId: initialMission.id,
    eventCategory: "feeling_identification",
    empathySkill: "feeling identification",
    prompt: "A friend is quiet and looking down.",
    targetResponse: "Ask if they are sad",
    selectedResponse: "",
    reflectionText: "",
    outcomeStatus: "seen_ignored",
    responseSeconds: 0,
    createdAtUtc: new Date(Date.now() - 11200000).toISOString()
  }
];

function makeTemplate(
  id: string,
  studentId: string,
  letter: string,
  targetWord: string,
  letterIndex: number,
  strokes: number[][][],
  recognitionScore: number,
  attemptsForLetter: number,
  handwritingDiagnostics?: HandwritingDiagnosticSummary
): AcceptedHandwritingTemplate {
  let order = 0;
  return {
    id,
    studentId,
    classId: classroom.id,
    schoolId: school.id,
    missionId: initialMission.id,
    letter,
    targetWord,
    letterIndex,
    attemptsForLetter,
    gifted: false,
    recognitionScore,
    recognizedName: letter,
    bestCandidateName: letter,
    runnerUpName: letter === "C" ? "O" : "L",
    runnerUpScore: recognitionScore + 18,
    scoreMargin: 18,
    isAmbiguous: recognitionScore > 40,
    handwritingDiagnostics,
    points: strokes.flatMap((stroke, strokeId) => stroke.map(([x, y]) => ({
      x,
      y,
      strokeId,
      order: order++
    }))),
    createdAt: new Date().toISOString()
  };
}

function makeAttempt(
  id: string,
  studentId: string,
  letter: string,
  confident: boolean,
  attempts: number,
  handwritingDiagnostics: HandwritingDiagnosticSummary
): LetterAttempt {
  return {
    id,
    studentId,
    classId: classroom.id,
    schoolId: school.id,
    missionId: initialMission.id,
    letter,
    confident,
    attempts,
    gifted: false,
    confidenceScore: confident ? 0.8 : 0.15,
    handwritingDiagnostics,
    createdAt: new Date().toISOString()
  };
}

function makeDiagnostic(
  letter: string,
  targetWord: string,
  letterIndex: number,
  tags: string[],
  primaryHint: string,
  severity: number
): HandwritingDiagnosticSummary {
  return {
    letter,
    targetWord,
    letterIndex,
    severity,
    tags,
    primaryHint,
    boundsX: 48,
    boundsY: 54,
    boundsWidth: tags.includes("oversized") ? 168 : 118,
    boundsHeight: tags.includes("oversized") ? 210 : 148,
    slotCenterOffsetX: tags.includes("spacingDrift") ? 34 : 6,
    baselineOffset: tags.includes("aboveBaseline") ? 28 : tags.includes("belowBaseline") ? -24 : 4,
    lineOverflowTop: tags.includes("oversized") ? 18 : 0,
    lineOverflowBottom: tags.includes("belowBaseline") ? 16 : 0,
    mirrorScore: tags.includes("mirror") ? 22 : 84,
    normalScore: tags.includes("mirror") ? 77 : 26,
    wobbleScore: tags.includes("wobbly") ? 0.34 : 0.08,
    localRoughness: tags.includes("wobbly") ? 0.38 : 0.07,
    localKinkScore: tags.includes("wobbly") ? 0.38 : 0.07,
    localKinkCount: tags.includes("wobbly") ? 3 : 0,
    localKinkMax: tags.includes("wobbly") ? 0.72 : 0.16,
    perpendicularJitterScore: tags.includes("wobbly") ? 0.27 : 0.05,
    wobbleThresholdUsed: 0.3,
    accepted: !tags.includes("mirror") || severity < 3
  };
}
