import { cert, initializeApp } from "firebase-admin/app";
import { FieldValue, getFirestore, type DocumentData } from "firebase-admin/firestore";
import { generatedDialogueTaskSeeds, generatedGrimoirePages } from "./generatedContent.js";

const seedTag = "deterministic-game-content-v1";
const maxWritesPerBatch = 50;
const args = parseArgs(process.argv.slice(2));
const config = readConfig(args);

initializeApp(config.serviceAccountPath
  ? { projectId: config.projectId, credential: cert(config.serviceAccountPath) }
  : { projectId: config.projectId });

const db = getFirestore();
const seededAt = new Date().toISOString();

interface SeedConfig {
  projectId: string;
  serviceAccountPath: string;
}

interface SeedRecord extends DocumentData {
  id: string;
}

async function writeCollection(collectionName: string, records: SeedRecord[]) {
  for (let start = 0; start < records.length; start += maxWritesPerBatch) {
    const batch = db.batch();
    const slice = records.slice(start, start + maxWritesPerBatch);
    for (const record of slice) {
      batch.set(db.doc(`${collectionName}/${record.id}`), withSeed(record), { merge: true });
    }
    await batch.commit();
  }
  console.log(`Seeded ${records.length} records into ${collectionName}.`);
}

function withSeed<T extends SeedRecord>(value: T): T {
  return {
    ...value,
    seedTag,
    contentVersion: seedTag,
    seededAt,
    updatedAt: FieldValue.serverTimestamp()
  };
}

function metaDocument(): SeedRecord {
  return {
    id: "current",
    description: "Canonical deterministic game/learning content for Unity, Teacher Portal, and AI Buddy grounding.",
    sourceOfTruth: [
      "NPC dialogue lines",
      "practice scaffolds",
      "answer validation rules",
      "grammar concepts",
      "grimoire pages",
      "lexicon",
      "area progression",
      "gym restrictions",
      "combat grammar patterns",
      "Buddy use-case contracts",
      "teacher report schemas"
    ],
    aiBoundary: "AI Buddy may explain, hint, coach, summarize, and personalize. It must not invent game state, answer keys, unlocks, combat rules, or gym bypasses.",
    collections: [
      "gameContentConcepts",
      "gameContentGrimoirePages",
      "gameContentVerbs",
      "gameContentNouns",
      "gameContentAdjectives",
      "gameContentFunctionWords",
      "gameContentAreas",
      "gameContentDialogueTasks",
      "gameContentPracticeScaffolds",
      "gameContentValidationRules",
      "gameContentGyms",
      "gameContentCombatPatterns",
      "gameContentBuddyContracts",
      "gameContentTeacherReportSchemas"
    ]
  };
}

const conceptProgression = [
  { conceptId: "Greetings", topicKey: "GREETINGSANDSURVIVALENGLISH", tier: 1, displayName: "Welcome Village" },
  { conceptId: "Alphabet", topicKey: "ALPHABET", tier: 2, displayName: "Alphabet Acres" },
  { conceptId: "VowelsConsonants", topicKey: "VOWELSANDCONSONANTS", tier: 3, displayName: "Vowel Valley" },
  { conceptId: "SentenceStartEnd", topicKey: "SENTENCESTARTANDFULLSTOP", tier: 4, displayName: "Sentence Square" },
  { conceptId: "BasicNouns", topicKey: "NOUNS", tier: 5, displayName: "Nounfield Town" },
  { conceptId: "BasicVerbs", topicKey: "VERBS", tier: 6, displayName: "Verb Village" },
  { conceptId: "Articles", topicKey: "ARTICLES", tier: 7, displayName: "Article Arcade" },
  { conceptId: "Pronouns", topicKey: "PRONOUNS", tier: 8, displayName: "Pronoun Port" },
  { conceptId: "Plurals", topicKey: "PLURALS", tier: 9, displayName: "Plural Plains" },
  { conceptId: "Adjectives", topicKey: "ADJECTIVES", tier: 10, displayName: "Adjective Grove" },
  { conceptId: "BasicPrepositions", topicKey: "BASICPREPOSITIONS", tier: 11, displayName: "Preposition Park" }
] as const;

const conceptMasteryTags: Record<string, string[]> = {
  Greetings: ["greeting", "polite phrase", "survival English"],
  Alphabet: ["capital letters", "small letters", "letter order", "first letter"],
  VowelsConsonants: ["vowel", "consonant", "short vowel"],
  SentenceStartEnd: ["capital start", "full stop", "complete thought"],
  BasicNouns: ["person", "animal", "place", "thing", "subject noun"],
  BasicVerbs: ["action word", "subject action", "third person s", "simple present"],
  Articles: ["a", "an", "the", "specific noun", "vowel sound"],
  Pronouns: ["I", "you", "he", "she", "it", "we", "they", "pronoun agreement"],
  Plurals: ["s", "es", "ies", "many", "plural ending"],
  Adjectives: ["describing word", "noun phrase", "adjective before noun"],
  BasicPrepositions: ["in", "on", "under", "behind", "near", "beside", "over", "preposition object"]
};

const concepts: SeedRecord[] = conceptProgression.map((progression) => {
  const page = generatedGrimoirePages.find((candidate) => candidate.conceptId === progression.conceptId);
  if (!page) throw new Error(`Missing generated Grimoire page for ${progression.conceptId}.`);
  return concept(
    page.conceptId,
    page.title,
    progression.tier,
    page.summary,
    conceptMasteryTags[page.conceptId] ?? [],
    [...page.examples]);
});

const grimoirePages: SeedRecord[] = generatedGrimoirePages.map((page) => ({ ...page }));

const verbs: SeedRecord[] = [
  verb("bite", "bites", "bit", "biting", ["rat", "cat", "dog", "pup"], "attack"),
  verb("run", "runs", "ran", "running", ["rat", "cat", "dog", "pup"], "movement"),
  verb("jump", "jumps", "jumped", "jumping", ["rat", "cat", "dog", "pup"], "movement"),
  verb("scratch", "scratches", "scratched", "scratching", ["rat", "cat", "dog"], "attack"),
  verb("fly", "flies", "flew", "flying", ["bird", "duck", "owl"], "movement"),
  verb("peck", "pecks", "pecked", "pecking", ["bird", "duck", "owl"], "attack"),
  verb("swim", "swims", "swam", "swimming", ["fish", "duck"], "movement"),
  verb("sit", "sits", "sat", "sitting", ["rat", "cat", "dog", "pup"], "state"),
  verb("go", "goes", "went", "going", ["I", "you", "he", "she", "it", "we", "they"], "movement"),
  verb("help", "helps", "helped", "helping", ["I", "you", "he", "she", "we", "they"], "social")
];

const nouns: SeedRecord[] = [
  noun("rat", "rats", "animal", "a", ["creature", "combat", "small"]),
  noun("cat", "cats", "animal", "a", ["creature", "combat", "small"]),
  noun("dog", "dogs", "animal", "a", ["creature", "combat"]),
  noun("pup", "pups", "animal", "a", ["creature", "combat", "young"]),
  noun("bird", "birds", "animal", "a", ["creature", "combat", "flying"]),
  noun("duck", "ducks", "animal", "a", ["creature", "combat", "water"]),
  noun("fish", "fish", "animal", "a", ["creature", "combat", "water"]),
  noun("owl", "owls", "animal", "an", ["creature", "combat", "flying", "vowel-sound"]),
  noun("box", "boxes", "thing", "a", ["object", "plural-es"]),
  noun("shop", "shops", "place", "a", ["place"]),
  noun("rock", "rocks", "thing", "a", ["object", "preposition-target"]),
  noun("wall", "walls", "thing", "a", ["object", "preposition-target"]),
  noun("roof", "roofs", "thing", "a", ["object", "preposition-target"])
];

const adjectives: SeedRecord[] = [
  adjective("big", ["large", "huge"], ["big rat", "a big dog"], { attackMultiplier: 1.35, speedMultiplier: 0.75 }),
  adjective("small", ["tiny", "little"], ["small cat", "the small bird"], { attackMultiplier: 0.75, speedMultiplier: 1.3 }),
  adjective("fast", ["quick"], ["fast dog", "fast bird"], { speedMultiplier: 1.4 }),
  adjective("slow", ["slowly"], ["slow rat"], { speedMultiplier: 0.65, accuracyMultiplier: 1.15 }),
  adjective("brave", ["bold"], ["brave dog"], { attackMultiplier: 1.2 }),
  adjective("quiet", ["silent"], ["quiet cat"], { defenseMultiplier: 1.12 }),
  adjective("bright", ["shiny", "glowing"], ["bright bird"], { speedMultiplier: 1.1 }),
  adjective("heavy", [], ["heavy dog"], { attackMultiplier: 1.18, defenseMultiplier: 1.12, speedMultiplier: 0.78 }),
  adjective("light", ["lightweight"], ["light bird"], { speedMultiplier: 1.25 }),
  adjective("old", [], ["old owl"], { defenseMultiplier: 1.14, speedMultiplier: 0.9 }),
  adjective("young", [], ["young pup"], { speedMultiplier: 1.16 })
];

const functionWords: SeedRecord[] = [
  functionWord("a", "article", "Use before consonant sounds.", ["a rat", "a dog"]),
  functionWord("an", "article", "Use before vowel sounds.", ["an owl"]),
  functionWord("the", "article", "Use for a specific noun.", ["the dog"]),
  ...["I", "you", "he", "she", "it", "we", "they"].map((word) => functionWord(word, "pronoun", "Use instead of a noun.", [`${word} bite`])),
  ...["in", "on", "under", "behind", "near", "beside", "over"].map((word) => functionWord(word, "preposition", "Shows location.", [`rat ${word} the box`]))
];

const areas: SeedRecord[] = conceptProgression.flatMap((progression, index) => {
  const prefix = `${progression.topicKey}:${progression.tier}`;
  const townId = `TOWN:${prefix}`;
  const routeId = `ROUTE:${prefix}`;
  const gymId = `GYM:${prefix}`;
  const next = conceptProgression[index + 1];
  const nextTownId = next ? `TOWN:${next.topicKey}:${next.tier}` : "";
  const labelBase = progression.displayName.replace(/\s+(Town|Village|Acres|Valley|Square|Arcade|Port|Plains|Grove|Park)$/i, "");
  return [
    area(townId, progression.displayName, "Town", progression.conceptId, `Learn ${progression.displayName}'s grammar rule with full support.`, [routeId]),
    area(routeId, `${labelBase} Route`, "Route", progression.conceptId, "Practise the same concept with partial hints and correction.", [townId, gymId]),
    area(gymId, `${labelBase} Gym`, "Gym", progression.conceptId, "Demonstrate the concept independently in dialogue and battle.", [routeId, ...(nextTownId ? [nextTownId] : [])])
  ];
});

const authoredDialogueTasks: SeedRecord[] = [
  task("welcome-greet", "Greetings", "Hello. Welcome to the village. You can answer: Hello.", "Hello", "FullSentence", "Full", "SpeakOnly", "None"),
  task("welcome-thank", "Greetings", "When someone helps you, say thank you. Try it with me.", "Thank you", "FullSentence", "Full", "SpeakOnly", "None"),
  task("noun-person-animal-place-thing", "BasicNouns", "A noun names a person, animal, place, or thing. Cat is a noun. Shop is a noun.", "Cat", "NounOnly", "Full", "SpeakOnly", "None"),
  task("noun-family-sort", "BasicNouns", "The sign says animal noun: ____.", "Rat", "NounOnly", "Partial", "WriteOnly", "MissingWord"),
  task("verb-action", "BasicVerbs", "A verb is an action. Bite, run, jump, and scratch are verbs.", "Bite", "VerbOnly", "Full", "SpeakOnly", "None"),
  task("verb-after-noun", "BasicVerbs", "A rat can bite. Say the whole action.", "Rat bites", "NounVerbPresent", "Full", "SpeakOnly", "None"),
  task("verb-road-missing-action", "BasicVerbs", "The route sign lost its action word: Dog ____.", "Dog runs", "NounVerbPresent", "Partial", "DrawAndSpeakBlank", "MissingWord", ["Dog run"]),
  task("verb-road-jumbled-command", "BasicVerbs", "The route tiles are mixed up: bites Rat. Put the command in order.", "Rat bites", "NounVerbPresent", "Partial", "DragDropWords", "ScrambledSentence", ["Rat bite"]),
  task("verb-road-correct-action", "BasicVerbs", "The route transcript said bird runs. Correct it with the action you heard.", "Bird flies", "NounVerbPresent", "Partial", "WriteOnly", "HeardWrong", ["Fish swims", "Dog runs"]),
  task("verb-gym-missing-action", "BasicVerbs", "Gym check: complete the action command: Fish ____.", "Fish swims", "NounVerbPresent", "Off", "DrawAndSpeakBlank", "MissingWord", ["Fish swim"]),
  task("verb-gym-jumbled-command", "BasicVerbs", "Gym check: put the mixed command in order: runs Dog.", "Dog runs", "NounVerbPresent", "Off", "DragDropWords", "ScrambledSentence", ["Dog run"]),
  task("verb-action-battle", "BasicVerbs", "Gym check: use a noun and a verb together without help.", "Rat bites", "NounVerbPresent", "Off", "SpeakOrWrite", "None", ["Dog runs", "Bird flies", "Fish swims"]),
  task("article-a", "Articles", "Use a before a consonant sound. Say: a rat.", "A rat", "DeterminerNoun", "Full", "SpeakOnly", "None"),
  task("article-an", "Articles", "Use an before a vowel sound. Say: an owl.", "An owl", "DeterminerNoun", "Full", "SpeakOnly", "None"),
  task("article-road-missing", "Articles", "The route transcript lost its article: ____ rat.", "A rat", "DeterminerNoun", "Partial", "WriteOnly", "MissingWord", ["The rat"]),
  task("article-gym-correct", "Articles", "Gym check: summon a noun with the correct article.", "An owl", "DeterminerNoun", "Off", "SpeakOrWrite", "None", ["A rat", "The cat"]),
  task("pronoun-curse-preview", "Pronouns", "He, she, and it change the verb: he bites.", "He bites", "PronounVerbPresent", "Full", "SpeakOnly", "None"),
  task("pronoun-ticket-replace", "Pronouns", "The ticket lost its subject: ____ bite.", "I bite", "PronounVerbPresent", "Partial", "WriteOnly", "MissingWord", ["You bite", "They bite"]),
  task("pronoun-road-correct", "Pronouns", "The route curse wanted they bite, not he bites. Correct it.", "They bite", "PronounVerbPresent", "Partial", "WriteOnly", "HeardWrong"),
  task("pronoun-boss-cycle", "Pronouns", "Gym check: answer the curse with he.", "He bites", "PronounVerbPresent", "Off", "SpeakOrWrite", "None"),
  task("plural-one-many", "Plurals", "One rat is singular. Many rats are plural.", "Rats", "FullSentence", "Full", "SpeakOnly", "None"),
  task("plural-road-missing", "Plurals", "The route board says one cat, many ____.", "Cats", "FullSentence", "Partial", "WriteOnly", "MissingWord"),
  task("plural-gym-boxes", "Plurals", "Gym check: tell me the plural of box.", "Boxes", "FullSentence", "Off", "SpeakOrWrite", "None"),
  task("adjective-describe-nouns", "Adjectives", "An adjective describes a noun. Big rat means a rat with more strength.", "Big rat", "AdjectiveNoun", "Full", "SpeakOnly", "None"),
  task("adjective-trainer-choice", "Adjectives", "Unscramble the summon you heard.", "Big dog", "AdjectiveNoun", "Partial", "WriteOnly", "ScrambledSentence"),
  task("adjective-boss-summon", "Adjectives", "Gym check: summon a noun with exactly one adjective.", "Big rat", "AdjectiveNoun", "Off", "SpeakOrWrite", "None", ["Small cat", "Big dog"]),
  task("preposition-in-on", "BasicPrepositions", "Prepositions choose real grid positions. Say: rat run beside the rock.", "Rat run beside the rock", "FullSentence", "Full", "SpeakOnly", "None"),
  task("preposition-road-missing", "BasicPrepositions", "The route transcript lost the place word: rat run ____ the rock.", "Rat run beside the rock", "FullSentence", "Partial", "WriteOnly", "MissingWord", ["Rat run near the rock", "Rat run behind the rock"]),
  task("preposition-road-correct", "BasicPrepositions", "The transcript said rat run beside the rock, but the target was behind it. Correct the command.", "Rat run behind the rock", "FullSentence", "Partial", "WriteOnly", "HeardWrong"),
  task("preposition-gym-behind", "BasicPrepositions", "Gym check: move behind the rock.", "Dog run behind the rock", "FullSentence", "Off", "SpeakOrWrite", "None")
];

const dialogueTasks: SeedRecord[] = [
  ...authoredDialogueTasks,
  ...generatedDialogueTaskSeeds.map((seed) => task(
    seed.id,
    seed.conceptId,
    seed.npcLine,
    seed.expectedResponse,
    seed.grammarPattern,
    seed.assistMode,
    seed.inputMode,
    seed.malfunctionType,
    seed.alternatives ?? []
  ))
];

const practiceScaffolds: SeedRecord[] = dialogueTasks.map((item) => {
  const scaffoldMode = resolveScaffoldMode(item.assistMode, item.malfunctionType);
  const prompt = buildPrompt(item.npcLine, item.expectedResponse, scaffoldMode);
  return {
    id: item.id,
    taskId: item.id,
    lineId: item.lineId,
    mode: scaffoldMode,
    prompt,
    heardTranscript: item.expectedResponse,
    challengeAudioText: item.expectedResponse,
    displayTranscript: prompt,
    answer: buildScaffoldAnswer(prompt, item.expectedResponse, scaffoldMode),
    fullAnswer: item.expectedResponse,
    tiles: scaffoldMode === "JumbledWords" ? buildJumbledTiles(item.expectedResponse) : [],
    responseModalities: responseModalitiesFor(item.inputMode, scaffoldMode),
    showSubtitle: item.assistMode !== "Off",
    allowBuddyHint: item.assistMode !== "Off",
    allowGrimoire: true
  };
});

const validationRules: SeedRecord[] = dialogueTasks.map((item) => ({
  id: item.id,
  taskId: item.id,
  expected: item.expectedResponse,
  accepted: item.acceptedResponses,
  requiredTokens: tokenize(item.expectedResponse),
  forbiddenTokens: item.expectedResponse.toLowerCase().includes("bites") ? ["bite"] : [],
  caseSensitive: false,
  punctuationSensitive: false,
  validationOwner: "game"
}));

const gyms: SeedRecord[] = concepts.map((item) => ({
  id: `GYM:${item.id}:${item.tier}`,
  conceptId: item.id,
  title: `${item.title} Gym`,
  assistMode: "Off",
  showSubtitle: false,
  allowBuddyHintDuringCheck: false,
  allowGrimoire: true,
  rules: [
    "No answer subtitles during active checks.",
    "No AI-generated answer leaks during active checks.",
    "Speech and writing validation remain deterministic.",
    "Grimoire can explain the concept, but the game owns correctness."
  ],
  checks: dialogueTasks.filter((taskRecord) => taskRecord.conceptId === item.id && taskRecord.assistMode === "Off").map((taskRecord) => taskRecord.id)
}));

const combatPatterns: SeedRecord[] = [
  combatPattern("NounOnly", ["noun"], ["Rat", "Dog", "Owl"], ["BasicNouns"]),
  combatPattern("VerbOnly", ["verb"], ["Bite", "Run", "Fly"], ["BasicVerbs"]),
  combatPattern("NounVerbPresent", ["noun", "verb"], ["Rat bites", "Dog runs", "Bird flies"], ["BasicNouns", "BasicVerbs"]),
  combatPattern("DeterminerNoun", ["article", "noun"], ["A rat", "An owl", "The dog"], ["Articles"]),
  combatPattern("PronounVerbPresent", ["pronoun", "verb"], ["I bite", "He bites", "They bite"], ["Pronouns"]),
  combatPattern("AdjectiveNoun", ["adjective", "noun"], ["Big rat", "Small cat"], ["Adjectives"]),
  combatPattern("DeterminerAdjectiveNoun", ["article", "adjective", "noun"], ["A big rat", "The small cat"], ["Articles", "Adjectives"]),
  combatPattern("FullSentence", ["subject", "verb", "objectOrPlace"], ["Rat run behind the rock", "Bird fly over the wall"], ["BasicPrepositions"])
];

const buddyContracts: SeedRecord[] = [
  buddyContract("npc_explanation", true, "Explain authored NPC English in local language + simple English. Do not invent subtitles."),
  buddyContract("adaptive_hint", true, "Use deterministic scaffold, expected answer, and concept data to give a hint without over-solving gym checks."),
  buddyContract("response_coach", true, "Guide the learner toward a correct English answer using friendly local-language support."),
  buddyContract("grimoire_coach", true, "Explain the opened grimoire page personally and briefly."),
  buddyContract("teacher_report_only", false, "Return structured teacher-facing notes without learner-facing help."),
  buddyContract("free_buddy_chat", true, "Answer normal learner questions about where to go, what to practice, and how to say things, grounded in provided game context.")
];

const teacherReportSchemas: SeedRecord[] = [
  {
    id: "buddy_conversation",
    eventType: "buddy_conversation",
    requiredFields: ["studentId", "learnerMessage", "buddyResponse", "sourceLanguage", "targetLanguage", "englishRatio"],
    optionalFields: ["dialogueTaskId", "contentId", "inputSource", "attemptGroupId", "attemptNumber", "conversationSkill", "wordChoiceIssue", "formationIssue", "errorCategory", "correctedResponse", "teacherNote", "safeMemoryTags", "safetyFlags", "buddyContractId", "promptTemplateId", "policyVersion", "reportable"],
    privacyRule: "Store only learning-useful safe memory tags. Do not store sensitive personal details."
  },
  {
    id: "phrase_evidence",
    eventType: "spoken_or_written_phrase",
    requiredFields: ["studentId", "phrase", "grammarPattern", "accepted", "zoneKind"],
    optionalFields: ["dialogueTaskId", "contentId", "inputSource", "attemptGroupId", "attemptNumber", "conceptId", "errorCategory", "hintLevelShown", "remediationStep", "correctedResponse", "pronunciationInsight", "rawAudioCaptured", "rawAudioUploaded", "rawAudioRetentionPolicy"]
  },
  {
    id: "grammar_battle",
    eventType: "grammar_battle",
    requiredFields: ["studentId", "playerPhrase", "grammarPattern", "accepted", "outcome", "zoneKind"],
    optionalFields: ["contentId", "inputSource", "attemptGroupId", "attemptNumber", "conceptId", "errorCategory", "hintLevelShown", "remediationStep", "correctedResponse", "pronunciationInsight", "commandPreposition", "commandConjunction", "rawAudioCaptured", "rawAudioUploaded", "rawAudioRetentionPolicy"]
  },
  {
    id: "spoken_mini_game",
    eventType: "counting_or_color_mini_game",
    requiredFields: ["studentId", "speechProofSucceeded", "outcomeStatus"],
    optionalFields: ["contentId", "inputSource", "attemptGroupId", "attemptNumber", "pronunciationInsight", "serverPronunciationInsight", "analysisMode", "onDeviceAnalysisProvider", "serverAnalysisStatus", "serverAnalysisJobId", "rawAudioCaptured", "rawAudioUploaded", "rawAudioRetentionPolicy"]
  }
];

function concept(id: string, title: string, tier: number, summary: string, masteryTags: string[], examples: string[]): SeedRecord {
  return { id, title, tier, summary, masteryTags, examples, unlocksAfter: [] };
}

function verb(id: string, thirdPerson: string, past: string, progressive: string, allowedSubjects: string[], role: string): SeedRecord {
  return {
    id,
    base: id,
    thirdPerson,
    past,
    progressive,
    future: `will ${id}`,
    allowedSubjects,
    semanticRole: role,
    pronunciationTargets: [id, thirdPerson, past, progressive],
    conjugations: ["I", "you", "he", "she", "it", "we", "they"].map((pronoun) => ({
      pronoun,
      present: ["he", "she", "it"].includes(pronoun) ? `${pronoun} ${thirdPerson}` : `${pronoun} ${id}`,
      past: `${pronoun} ${past}`,
      progressive: `${pronoun} ${pronoun === "I" ? "am" : ["he", "she", "it"].includes(pronoun) ? "is" : "are"} ${progressive}`,
      future: `${pronoun} will ${id}`
    }))
  };
}

function noun(id: string, plural: string, category: string, article: string, tags: string[]): SeedRecord {
  return { id, singular: id, plural, category, article, tags, pronunciationTargets: [id, plural] };
}

function adjective(id: string, aliases: string[], examples: string[], statEffects: Record<string, number>): SeedRecord {
  return { id, text: id, aliases, position: "before_noun", examples, statEffects };
}

function functionWord(id: string, kind: string, rule: string, examples: string[]): SeedRecord {
  return { id, text: id, kind, rule, examples };
}

function area(id: string, displayName: string, zoneKind: string, conceptId: string, objective: string, connectedAreaIds: string[]): SeedRecord {
  return { id, displayName, zoneKind, conceptId, currentObjective: objective, connectedAreaIds, availableDestinations: connectedAreaIds };
}

function task(
  id: string,
  conceptId: string,
  npcLine: string,
  expectedResponse: string,
  grammarPattern: string,
  assistMode: "Full" | "Partial" | "Off",
  inputMode: "SpeakOnly" | "WriteOnly" | "SpeakOrWrite" | "SpeakAndWrite" | "DrawAndSpeakBlank" | "DragDropWords",
  malfunctionType: "None" | "MissingWord" | "ScrambledSentence" | "PartialTranscript" | "HeardWrong",
  alternatives: string[] = []
): SeedRecord {
  const scaffoldMode = resolveScaffoldMode(assistMode, malfunctionType);
  const prompt = buildPrompt(npcLine, expectedResponse, scaffoldMode);
  const scaffoldAnswer = buildScaffoldAnswer(prompt, expectedResponse, scaffoldMode);
  return {
    id,
    lineId: id,
    taskId: id,
    conceptId,
    npcLine,
    englishText: npcLine,
    expectedResponse,
    acceptedResponses: unique([expectedResponse, ...alternatives]),
    grammarPattern,
    assistMode,
    inputMode,
    malfunctionType,
    heardTranscript: expectedResponse,
    challengeAudioText: expectedResponse,
    displayTranscript: prompt,
    expectedInputResponse: scaffoldAnswer,
    expectedFullResponse: expectedResponse,
    tiles: scaffoldMode === "JumbledWords" ? buildJumbledTiles(expectedResponse) : [],
    responseModalities: responseModalitiesFor(inputMode, scaffoldMode),
    highlightedConceptTokens: buildConceptTokenAnnotations(expectedResponse, conceptId, grammarPattern),
    pronunciationTargets: tokenize(expectedResponse),
    contentId: id,
    inputSource: assistMode === "Off" ? "gym_dialogue" : "npc_dialogue",
    buddyContractId: assistMode === "Off" ? "teacher_report_only" : assistMode === "Partial" ? "adaptive_hint" : "response_coach",
    promptTemplateId: "translator_buddy_hint_v1",
    policyVersion: "buddy_policy_v1",
    scaffoldMode,
    buddyUseCase: assistMode === "Off" ? "teacher_report_only" : assistMode === "Partial" ? "adaptive_hint" : "response_coach",
    allowAiHint: assistMode !== "Off",
    openGrimoireOnWrongAnswer: true
  };
}

function buildConceptTokenAnnotations(text: string, conceptId: string, grammarPattern: string) {
  const tokens = tokenize(text);
  return tokens.map((token, index) => ({
    token,
    index,
    role: inferTokenRole(token, index, grammarPattern),
    conceptId: conceptForToken(token, conceptId, grammarPattern)
  }));
}

function inferTokenRole(token: string, index: number, grammarPattern: string) {
  const lower = token.toLowerCase();
  if (["a", "an", "the"].includes(lower)) return "article";
  if (["i", "you", "he", "she", "it", "we", "they"].includes(lower)) return "pronoun";
  if (["in", "on", "under", "behind", "near", "beside", "over"].includes(lower)) return "preposition";
  if (["big", "small", "fast", "slow", "brave", "quiet", "bright", "heavy", "light", "old", "young"].includes(lower)) return "adjective";
  if (["bite", "bites", "run", "runs", "fly", "flies", "swim", "swims", "jump", "jumps", "scratch", "scratches"].includes(lower)) return "verb";
  if (grammarPattern === "VerbOnly") return "verb";
  if (grammarPattern === "NounOnly") return "noun";
  return index === 0 && grammarPattern.includes("Noun") ? "noun" : "word";
}

function conceptForToken(token: string, fallbackConceptId: string, grammarPattern: string) {
  const role = inferTokenRole(token, 0, grammarPattern);
  switch (role) {
    case "article": return "Articles";
    case "pronoun": return "Pronouns";
    case "preposition": return "BasicPrepositions";
    case "adjective": return "Adjectives";
    case "verb": return "BasicVerbs";
    case "noun": return "BasicNouns";
    default: return fallbackConceptId;
  }
}

function combatPattern(id: string, requiredSlots: string[], examples: string[], conceptIds: string[]): SeedRecord {
  return {
    id,
    requiredSlots,
    examples,
    conceptIds,
    pronunciationAssessment: true,
    allowedInCombat: id !== "FullSentence" || conceptIds.includes("BasicPrepositions"),
    validationOwner: "game"
  };
}

function buddyContract(id: string, learnerFacing: boolean, instruction: string): SeedRecord {
  return {
    id,
    learnerFacing,
    instruction,
    mustUseProvidedContext: true,
    mayStoreMemory: id !== "teacher_report_only",
    forbiddenActions: [
      "invent_quest_state",
      "unlock_content",
      "validate_answers_directly",
      "bypass_gym_restrictions",
      "invent_combat_rules",
      "store_sensitive_personal_data"
    ]
  };
}

function buildConjugations() {
  return [
    verb("bite", "bites", "bit", "biting", ["rat"], "attack"),
    verb("run", "runs", "ran", "running", ["dog"], "movement"),
    verb("jump", "jumps", "jumped", "jumping", ["cat"], "movement"),
    verb("scratch", "scratches", "scratched", "scratching", ["cat"], "attack"),
    verb("fly", "flies", "flew", "flying", ["bird"], "movement"),
    verb("peck", "pecks", "pecked", "pecking", ["bird"], "attack"),
    verb("swim", "swims", "swam", "swimming", ["fish"], "movement")
  ].flatMap((verbRecord) => verbRecord.conjugations);
}

function resolveScaffoldMode(assistMode: string, malfunctionType: string) {
  switch (malfunctionType) {
    case "MissingWord": return "FillInBlank";
    case "ScrambledSentence": return "JumbledWords";
    case "HeardWrong": return "CorrectTranscript";
    case "PartialTranscript": return "PartialTranscript";
    default: return assistMode === "Off" ? "NoSubtitleGym" : "AuthoredSubtitle";
  }
}

function buildPrompt(npcLine: string, expectedResponse: string, scaffoldMode: string) {
  if (scaffoldMode === "FillInBlank") {
    const authoredBlank = extractAuthoredBlankPrompt(npcLine);
    if (authoredBlank) return authoredBlank;
    const tokens = expectedResponse.split(/\s+/);
    if (tokens.length > 1) {
      tokens[Math.max(0, tokens.length - 1)] = "____";
      return tokens.join(" ");
    }
  }
  if (scaffoldMode === "JumbledWords") {
    return expectedResponse.split(/\s+/).reverse().join(" / ");
  }
  if (scaffoldMode === "NoSubtitleGym") {
    return "";
  }
  return npcLine;
}

function extractAuthoredBlankPrompt(npcLine: string) {
  if (!npcLine.includes("____")) return "";
  const afterColon = npcLine.includes(":") ? npcLine.slice(npcLine.lastIndexOf(":") + 1) : npcLine;
  return afterColon.trim().replace(/[.!?]+$/, "");
}

function buildScaffoldAnswer(prompt: string, expectedResponse: string, scaffoldMode: string) {
  if (scaffoldMode !== "FillInBlank") return expectedResponse;
  const promptTokens = prompt.split(/\s+/).map((token) => token.replace(/[^A-Za-z_]+/g, "")).filter(Boolean);
  const expectedTokens = expectedResponse.split(/\s+/).map((token) => token.replace(/[^A-Za-z]+/g, "")).filter(Boolean);
  const missing = promptTokens
    .map((token, index) => token === "____" ? expectedTokens[index] : "")
    .filter(Boolean);
  if (missing.length > 0) return missing.join(" ");
  return expectedTokens[expectedTokens.length - 1] ?? expectedResponse;
}

function buildJumbledTiles(expectedResponse: string) {
  return expectedResponse.split(/\s+/).reverse();
}

function responseModalitiesFor(inputMode: string, scaffoldMode: string) {
  if (inputMode === "DrawAndSpeakBlank") return ["draw_blank", "speak_blank"];
  if (inputMode === "DragDropWords" || scaffoldMode === "JumbledWords") return ["drag_drop_words"];
  if (inputMode === "SpeakOnly") return ["speak"];
  if (inputMode === "WriteOnly") return ["write"];
  if (inputMode === "SpeakAndWrite") return ["speak", "write"];
  return ["speak_or_write"];
}

function tokenize(value: string) {
  return value.toUpperCase().replace(/[^A-Z0-9 ]+/g, " ").split(/\s+/).filter(Boolean);
}

function unique(values: string[]) {
  return Array.from(new Set(values.filter((value) => value.trim()).map((value) => value.trim())));
}

function readConfig(values: Record<string, string>): SeedConfig {
  return {
    projectId: values.projectId || env("FIREBASE_PROJECT_ID") || env("GCLOUD_PROJECT") || env("GOOGLE_CLOUD_PROJECT") || "the-script-dea4f",
    // Explicit service accounts use cert(). Standard Application Default
    // Credentials are resolved by firebase-admin automatically, so do not
    // treat GOOGLE_APPLICATION_CREDENTIALS as a service-account JSON file.
    serviceAccountPath: values.serviceAccount || values.serviceAccountPath
  };
}

function parseArgs(argv: string[]): Record<string, string> {
  const result: Record<string, string> = {};
  for (const arg of argv) {
    if (!arg.startsWith("--")) continue;
    const separator = arg.indexOf("=");
    result[arg.slice(2, separator < 0 ? undefined : separator)] = separator < 0 ? "true" : arg.slice(separator + 1);
  }
  return result;
}

function env(name: string) {
  return process.env[name] ?? "";
}

async function main() {
  await writeCollection("gameContentMeta", [metaDocument()]);
  await writeCollection("gameContentConcepts", concepts);
  await writeCollection("gameContentGrimoirePages", grimoirePages);
  await writeCollection("gameContentVerbs", verbs);
  await writeCollection("gameContentNouns", nouns);
  await writeCollection("gameContentAdjectives", adjectives);
  await writeCollection("gameContentFunctionWords", functionWords);
  await writeCollection("gameContentAreas", areas);
  await writeCollection("gameContentDialogueTasks", dialogueTasks);
  await writeCollection("gameContentPracticeScaffolds", practiceScaffolds);
  await writeCollection("gameContentValidationRules", validationRules);
  await writeCollection("gameContentGyms", gyms);
  await writeCollection("gameContentCombatPatterns", combatPatterns);
  await writeCollection("gameContentBuddyContracts", buddyContracts);
  await writeCollection("gameContentTeacherReportSchemas", teacherReportSchemas);

  console.log("Deterministic game content seeded successfully.");
  console.log(`Project: ${config.projectId}`);
  console.log(`Seed tag: ${seedTag}`);
}

await main();
