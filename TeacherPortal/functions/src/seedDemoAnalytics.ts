import { cert, initializeApp } from "firebase-admin/app";
import { getAuth, type UserRecord } from "firebase-admin/auth";
import { FieldValue, getFirestore, type DocumentData } from "firebase-admin/firestore";

const seedTag = "demo-analytics-v1";
const args = parseArgs(process.argv.slice(2));
const config = readConfig(args);

initializeApp(config.serviceAccountPath
  ? { projectId: config.projectId, credential: cert(config.serviceAccountPath) }
  : { projectId: config.projectId });

const auth = getAuth();
const db = getFirestore();
const seededAt = new Date().toISOString();
const today = dateKey(new Date());
const classId = await resolveClass(config.schoolId, config.className);
const students = buildSeedStudents(config);
const studentIds = students.map((student) => student.studentId);

await db.doc(`schools/${config.schoolId}`).set({
  id: config.schoolId,
  name: config.schoolName,
  academicYear: config.academicYear,
  updatedAt: FieldValue.serverTimestamp(),
  createdAt: FieldValue.serverTimestamp()
}, { merge: true });

await db.doc(`schools/${config.schoolId}/classes/${classId}`).set({
  id: classId,
  schoolId: config.schoolId,
  name: config.className,
  studentIds,
  updatedAt: FieldValue.serverTimestamp(),
  createdAt: FieldValue.serverTimestamp()
}, { merge: true });

await clearSeededRecommendations(config.schoolId, studentIds);

for (const student of students) {
  const user = await upsertStudentAuthUser(student);
  await auth.setCustomUserClaims(user.uid, {
    role: "student",
    schoolId: config.schoolId,
    classIds: [classId],
    studentIds: [student.studentId],
    studentId: student.studentId
  });

  await db.doc(`users/${user.uid}`).set({
    uid: user.uid,
    email: student.email,
    displayName: student.name,
    role: "student",
    schoolId: config.schoolId,
    classIds: [classId],
    studentIds: [student.studentId],
    studentId: student.studentId,
    parentEmail: student.parentEmail,
    seedTag,
    seededAt,
    updatedAt: seededAt,
    createdAt: seededAt
  }, { merge: true });

  await db.doc(`schools/${config.schoolId}/students/${student.studentId}`).set({
    id: student.studentId,
    schoolId: config.schoolId,
    classId,
    name: student.name,
    authUid: user.uid,
    email: student.email,
    parentEmail: student.parentEmail,
    avatarColor: student.avatarColor,
    seedTag,
    seededAt,
    updatedAt: seededAt,
    createdAt: seededAt
  }, { merge: true });

  await resetStudentAnalytics(config.schoolId, student.studentId);
  await writeStudentAnalytics(config.schoolId, classId, student);
}

await writeClassMissions(config.schoolId, classId, config.teacherUid);
await db.collection("auditEvents").add({
  schoolId: config.schoolId,
  actorUid: config.teacherUid,
  action: "seed.demoAnalytics",
  targetPath: `schools/${config.schoolId}/classes/${classId}`,
  seedTag,
  seededAt,
  createdAt: FieldValue.serverTimestamp()
});

console.log("Demo analytics seeded successfully.");
console.log(`Project: ${config.projectId}`);
console.log(`School/class: ${config.schoolId}/${classId}`);
console.log(`Students: ${students.map((student) => `${student.name} <${student.email}>`).join(", ")}`);

interface SeedConfig {
  projectId: string;
  schoolId: string;
  schoolName: string;
  academicYear: string;
  className: string;
  teacherUid: string;
  mainStudentEmail: string;
  mainStudentPassword: string;
  defaultStudentPassword: string;
  extraEmailDomain: string;
  serviceAccountPath: string;
}

interface SeedStudent {
  studentId: string;
  uid: string;
  email: string;
  password: string;
  parentEmail: string;
  name: string;
  avatarColor: string;
  pattern: "mixed" | "literacy" | "speech" | "color" | "empathy" | "counting";
  weakLetters: string[];
  words: string[];
}

async function resolveClass(schoolId: string, className: string) {
  const classes = await db.collection(`schools/${schoolId}/classes`).where("name", "==", className).limit(1).get();
  if (!classes.empty) {
    return classes.docs[0].id;
  }
  return slugify(className);
}

function buildSeedStudents(seedConfig: SeedConfig): SeedStudent[] {
  const domain = seedConfig.extraEmailDomain;
  return [
    {
      studentId: "demo-aryan-raj",
      uid: "student_demo_aryan_raj",
      email: seedConfig.mainStudentEmail,
      password: seedConfig.mainStudentPassword,
      parentEmail: "parent.aryan@axxela.in",
      name: "Aryan Raj",
      avatarColor: "#7fc8ff",
      pattern: "mixed",
      weakLetters: ["B", "D", "C"],
      words: ["CAT", "BAT", "DOG", "RABBIT"]
    },
    {
      studentId: "demo-mira-shah",
      uid: "student_demo_mira_shah",
      email: `mira.shah.demo@${domain}`,
      password: seedConfig.defaultStudentPassword,
      parentEmail: `parent.mira.demo@${domain}`,
      name: "Mira Shah",
      avatarColor: "#ffc76b",
      pattern: "literacy",
      weakLetters: ["B", "D", "P"],
      words: ["BAT", "DOG", "PIG", "TOP"]
    },
    {
      studentId: "demo-rohan-mehta",
      uid: "student_demo_rohan_mehta",
      email: `rohan.mehta.demo@${domain}`,
      password: seedConfig.defaultStudentPassword,
      parentEmail: `parent.rohan.demo@${domain}`,
      name: "Rohan Mehta",
      avatarColor: "#9fdc8a",
      pattern: "speech",
      weakLetters: ["R", "S", "T"],
      words: ["RABBIT", "SUN", "STAR", "TREE"]
    },
    {
      studentId: "demo-tara-iyer",
      uid: "student_demo_tara_iyer",
      email: `tara.iyer.demo@${domain}`,
      password: seedConfig.defaultStudentPassword,
      parentEmail: `parent.tara.demo@${domain}`,
      name: "Tara Iyer",
      avatarColor: "#f6a3bf",
      pattern: "color",
      weakLetters: ["C", "G"],
      words: ["CAT", "GREEN", "BLUE", "RED"]
    },
    {
      studentId: "demo-kabir-rao",
      uid: "student_demo_kabir_rao",
      email: `kabir.rao.demo@${domain}`,
      password: seedConfig.defaultStudentPassword,
      parentEmail: `parent.kabir.demo@${domain}`,
      name: "Kabir Rao",
      avatarColor: "#b8a7ff",
      pattern: "empathy",
      weakLetters: ["K", "S"],
      words: ["KITE", "SUN", "HELP"]
    },
    {
      studentId: "demo-neha-kapoor",
      uid: "student_demo_neha_kapoor",
      email: `neha.kapoor.demo@${domain}`,
      password: seedConfig.defaultStudentPassword,
      parentEmail: `parent.neha.demo@${domain}`,
      name: "Neha Kapoor",
      avatarColor: "#80d8c8",
      pattern: "counting",
      weakLetters: ["N", "T"],
      words: ["NET", "TEN", "TOP"]
    }
  ];
}

async function upsertStudentAuthUser(student: SeedStudent): Promise<UserRecord> {
  try {
    const existing = await auth.getUserByEmail(student.email);
    await auth.updateUser(existing.uid, {
      password: student.password,
      displayName: student.name,
      disabled: false
    });
    return auth.getUser(existing.uid);
  } catch (error) {
    if (!isAuthUserNotFound(error)) {
      throw error;
    }
    return auth.createUser({
      uid: student.uid,
      email: student.email,
      password: student.password,
      displayName: student.name,
      emailVerified: false,
      disabled: false
    });
  }
}

async function resetStudentAnalytics(schoolId: string, studentId: string) {
  const basePath = `schools/${schoolId}/students/${studentId}`;
  for (const name of [
    "runSessions",
    "letterAttempts",
    "wordCastEvents",
    "acceptedHandwritingTemplates",
    "countingMiniGameAttempts",
    "colorMiniGameAttempts",
    "empathyEvents",
    "studentMissionOverrides",
    "parentSummaries"
  ]) {
    await deleteCollection(`${basePath}/${name}`);
  }
}

async function clearSeededRecommendations(schoolId: string, studentIds: string[]) {
  for (const studentId of studentIds) {
    await deleteCollection(`schools/${schoolId}/students/${studentId}/recommendations`);
  }
}

async function deleteCollection(path: string) {
  while (true) {
    const snap = await db.collection(path).limit(300).get();
    if (snap.empty) {
      return;
    }
    const batch = db.batch();
    snap.docs.forEach((doc) => batch.delete(doc.ref));
    await batch.commit();
  }
}

async function writeStudentAnalytics(schoolId: string, classId: string, student: SeedStudent) {
  const batch = db.batch();
  const basePath = `schools/${schoolId}/students/${student.studentId}`;
  const days = Array.from({ length: 7 }, (_, index) => daysAgo(6 - index));

  days.forEach((day, index) => {
    const missionId = `${classId}_${day}`;
    const intensity = patternIntensity(student.pattern, index);
    const words = rotate(student.words, index).slice(0, 3);
    const letters = rotate(student.weakLetters, index).slice(0, 3);
    const runId = `${seedTag}-run-${student.studentId}-${day}`;

    batch.set(db.doc(`${basePath}/runSessions/${runId}`), withSeed({
      id: runId,
      sessionId: runId,
      studentId: student.studentId,
      classId,
      schoolId,
      missionId,
      configuredDurationSeconds: 480,
      actualDurationSeconds: Math.round(330 + intensity.practice * 95 + index * 7),
      subarenasCleared: Math.max(1, Math.round(2 + intensity.confidence * 4)),
      fullLoopsCleared: intensity.confidence > 0.72 ? 1 : 0,
      lettersPracticed: letters,
      wordsPracticed: words,
      averageConfidence: round(intensity.confidence),
      averageAttemptsPerLetter: round(1.1 + intensity.attempts),
      creaturesCleared: Math.round(6 + intensity.confidence * 12),
      specialWordMatches: Math.round(intensity.confidence * 4),
      completed: intensity.confidence > 0.42,
      startedAt: `${day}T09:15:00.000Z`,
      endedAt: `${day}T09:${String(23 + index).padStart(2, "0")}:00.000Z`,
      startedAtUtc: `${day}T09:15:00.000Z`,
      endedAtUtc: `${day}T09:${String(23 + index).padStart(2, "0")}:00.000Z`
    }));

    letters.forEach((letter, letterIndex) => {
      const attemptId = `${seedTag}-letter-${student.studentId}-${day}-${letter}`;
      const diagnosis = diagnosticFor(student, letter, words[letterIndex % words.length], letterIndex, intensity);
      batch.set(db.doc(`${basePath}/letterAttempts/${attemptId}`), withSeed({
        id: attemptId,
        studentId: student.studentId,
        classId,
        schoolId,
        missionId,
        letter,
        confident: intensity.confidence > 0.64 && diagnosis.severity < 3,
        attempts: Math.max(1, Math.round(1 + intensity.attempts + letterIndex)),
        gifted: false,
        confidenceScore: round(Math.max(0.12, intensity.confidence - diagnosis.severity * 0.08)),
        handwritingDiagnostics: diagnosis,
        createdAt: `${day}T09:${String(25 + letterIndex).padStart(2, "0")}:00.000Z`,
        createdAtUtc: `${day}T09:${String(25 + letterIndex).padStart(2, "0")}:00.000Z`
      }));
    });

    words.slice(0, 2).forEach((word, wordIndex) => {
      const castId = `${seedTag}-cast-${student.studentId}-${day}-${word}`;
      const insight = pronunciationFor(student, word, intensity, wordIndex);
      batch.set(db.doc(`${basePath}/wordCastEvents/${castId}`), withSeed({
        id: castId,
        studentId: student.studentId,
        classId,
        schoolId,
        missionId,
        word,
        success: insight.voskConfirmedWord,
        specialMatch: wordIndex === 1 && intensity.confidence > 0.75,
        responseSeconds: round(1.2 + intensity.attempts * 0.7 + wordIndex * 0.4),
        pronunciationInsight: insight,
        createdAt: `${day}T09:${String(30 + wordIndex).padStart(2, "0")}:00.000Z`,
        createdAtUtc: `${day}T09:${String(30 + wordIndex).padStart(2, "0")}:00.000Z`
      }));
    });

    if (index % 2 === 0) {
      const templateId = `${seedTag}-template-${student.studentId}-${day}`;
      const letter = letters[0];
      batch.set(db.doc(`${basePath}/acceptedHandwritingTemplates/${templateId}`), withSeed({
        id: templateId,
        templateId,
        studentId: student.studentId,
        classId,
        schoolId,
        missionId,
        letter,
        targetWord: words[0],
        letterIndex: 0,
        attemptsForLetter: Math.max(1, Math.round(1 + intensity.attempts)),
        gifted: false,
        recognitionScore: round(24 + intensity.attempts * 9),
        recognizedName: letter,
        bestCandidateName: letter,
        runnerUpName: letter === "B" ? "D" : letter === "D" ? "B" : "O",
        runnerUpScore: round(45 + intensity.attempts * 11),
        scoreMargin: round(12 + intensity.confidence * 20),
        isAmbiguous: intensity.confidence < 0.55,
        handwritingDiagnostics: diagnosticFor(student, letter, words[0], 0, intensity),
        points: makeStrokePoints(letter),
        createdAt: `${day}T09:35:00.000Z`,
        createdAtUtc: `${day}T09:35:00.000Z`
      }));
    }

    batch.set(db.doc(`${basePath}/countingMiniGameAttempts/${seedTag}-count-${student.studentId}-${day}`), withSeed(countingAttempt(schoolId, classId, student, missionId, day, intensity)));
    batch.set(db.doc(`${basePath}/colorMiniGameAttempts/${seedTag}-color-${student.studentId}-${day}`), withSeed(colorAttempt(schoolId, classId, student, missionId, day, intensity)));
    batch.set(db.doc(`${basePath}/empathyEvents/${seedTag}-empathy-${student.studentId}-${day}`), withSeed(empathyAttempt(schoolId, classId, student, missionId, day, intensity)));
  });

  batch.set(db.doc(`${basePath}/recommendations/${seedTag}-rec-${student.studentId}`), withSeed(recommendationFor(schoolId, classId, student)));
  batch.set(db.doc(`${basePath}/parentSummaries/${seedTag}-summary-${student.studentId}`), withSeed(parentSummaryFor(schoolId, classId, student)));
  batch.set(db.doc(`${basePath}/studentMissionOverrides/${today}`), withSeed({
    id: today,
    schoolId,
    classId,
    studentId: student.studentId,
    baseMissionId: `${classId}_${today}`,
    date: today,
    missionType: "practice",
    missionDurationSeconds: 480,
    countingChestCount: student.pattern === "counting" ? 2 : 1,
    colorChestCount: student.pattern === "color" ? 2 : 1,
    lettersForToday: student.weakLetters.slice(0, 3),
    wordsForToday: student.words.slice(0, 3),
    revisionLetters: student.weakLetters.slice(0, 2),
    note: noteFor(student.pattern),
    createdByTeacherId: "seed-demo-teacher"
  }));

  await batch.commit();
}

async function writeClassMissions(schoolId: string, classId: string, teacherUid: string) {
  const batch = db.batch();
  for (let offset = 0; offset < 7; offset++) {
    const day = daysAgo(6 - offset);
    batch.set(db.doc(`schools/${schoolId}/classes/${classId}/dailyMissions/${day}`), withSeed({
      id: day,
      schoolId,
      classId,
      date: day,
      missionType: "practice",
      missionDurationSeconds: 480,
      countingChestCount: 2,
      colorChestCount: 2,
      lettersForToday: rotate(["A", "C", "T", "B", "D", "P", "S"], offset).slice(0, 4),
      wordsForToday: rotate(["ANT", "CAT", "BAT", "DOG", "RABBIT", "SUN", "TOP"], offset).slice(0, 4),
      revisionLetters: rotate(["B", "D", "C", "S"], offset).slice(0, 2),
      createdByTeacherId: teacherUid,
      createdAt: `${day}T08:00:00.000Z`,
      updatedAt: seededAt
    }), { merge: true });
  }
  await batch.commit();
}

function withSeed<T extends DocumentData>(value: T): T {
  return {
    ...value,
    seedTag,
    seededAt
  };
}

function patternIntensity(pattern: SeedStudent["pattern"], index: number) {
  const improving = index / 8;
  const values = (() => {
    switch (pattern) {
      case "literacy": return { confidence: 0.38 + improving, attempts: 3.3 - improving, practice: 0.55 };
      case "speech": return { confidence: 0.58 + improving, attempts: 2.4 - improving * 0.6, practice: 0.7 };
      case "color": return { confidence: 0.68 + improving * 0.4, attempts: 1.8, practice: 0.76 };
      case "empathy": return { confidence: 0.72, attempts: 1.7, practice: 0.65 };
      case "counting": return { confidence: 0.55 + improving * 0.5, attempts: 2.8, practice: 0.72 };
      default: return { confidence: 0.52 + improving * 0.8, attempts: 2.6 - improving, practice: 0.72 };
    }
  })();
  return {
    ...values,
    confidence: Math.max(0.05, Math.min(0.98, values.confidence))
  };
}

function diagnosticFor(student: SeedStudent, letter: string, targetWord: string, letterIndex: number, intensity: ReturnType<typeof patternIntensity>) {
  const tags = new Set<string>();
  if (student.pattern === "literacy" || student.pattern === "mixed") {
    if (["B", "D", "P"].includes(letter)) tags.add("mirror");
    if (["C", "S"].includes(letter)) tags.add("spacingDrift");
  }
  if (intensity.attempts > 2.2) tags.add("wrongStrokeOrder");
  if (intensity.confidence < 0.55) tags.add("wobbly");
  const severity = Math.min(3, Math.max(1, tags.size));
  return {
    letter,
    targetWord,
    letterIndex,
    severity,
    tags: Array.from(tags),
    primaryHint: primaryHint(Array.from(tags), letter),
    boundsX: 48,
    boundsY: 54,
    boundsWidth: tags.has("spacingDrift") ? 168 : 126,
    boundsHeight: tags.has("wobbly") ? 188 : 148,
    slotCenterOffsetX: tags.has("spacingDrift") ? 34 : 6,
    baselineOffset: tags.has("wobbly") ? -18 : 4,
    lineOverflowTop: tags.has("wobbly") ? 10 : 0,
    lineOverflowBottom: tags.has("wobbly") ? 16 : 0,
    mirrorScore: tags.has("mirror") ? 21 : 84,
    normalScore: tags.has("mirror") ? 76 : 28,
    wobbleScore: tags.has("wobbly") ? 0.34 : 0.08,
    localRoughness: tags.has("wobbly") ? 0.36 : 0.07,
    localKinkScore: tags.has("wobbly") ? 0.38 : 0.07,
    localKinkCount: tags.has("wobbly") ? 3 : 0,
    localKinkMax: tags.has("wobbly") ? 0.72 : 0.16,
    perpendicularJitterScore: tags.has("wobbly") ? 0.27 : 0.05,
    wobbleThresholdUsed: 0.3,
    accepted: !tags.has("mirror") || intensity.confidence > 0.48
  };
}

function pronunciationFor(student: SeedStudent, word: string, intensity: ReturnType<typeof patternIntensity>, index: number) {
  const expected = phoneticSegments(word);
  const speechNeed = student.pattern === "speech" || student.pattern === "mixed";
  const missFirst = speechNeed && index === 0;
  const segments = expected.map((segment, segmentIndex) => {
    if (missFirst && segmentIndex === 0) {
      return {
        ...segment,
        heardSound: word === "RABBIT" ? "w" : "",
        status: "Missing",
        confidence: 0.12
      };
    }
    if (speechNeed && segment.spelling === "S") {
      return { ...segment, heardSound: "th", status: "NeedsPractice", confidence: 0.36 };
    }
    return { ...segment, status: "Matched", confidence: 0.82 };
  });
  const focusSegment = segments.find((segment) => segment.status !== "Matched") ?? segments[0];
  const score = segments.reduce((sum, segment) => sum + (segment.status === "Matched" ? 1 : segment.confidence), 0) / Math.max(1, segments.length);
  return {
    providerName: "Azure Pronunciation Assessment",
    targetWord: word,
    confirmedWord: missFirst ? "" : word,
    rawRecognizedText: missFirst ? word.slice(1).toLowerCase() : word.toLowerCase(),
    voskConfirmedWord: !missFirst || intensity.confidence > 0.62,
    attemptedTarget: true,
    score: round(score),
    hintKey: focusSegment.status === "Matched" ? "GreatTry" : "TryFirstSound",
    message: focusSegment.status === "Matched"
      ? "Server pronunciation review matched the expected sound pattern."
      : "Server pronunciation review flagged a sound segment for gentle practice.",
    focusSegment,
    segments,
    syllableBeats: syllableBeats(word)
  };
}

function phoneticSegments(word: string) {
  return word.split("").map((letter, index) => ({
    spelling: letter,
    friendlySound: friendlySound(letter),
    heardSound: "",
    beatIndex: Math.floor(index / 2),
    status: "Unknown",
    confidence: 0
  }));
}

function countingAttempt(schoolId: string, classId: string, student: SeedStudent, missionId: string, day: string, intensity: ReturnType<typeof patternIntensity>) {
  const targetCount = 4 + (day.charCodeAt(day.length - 1) % 6);
  const difficult = student.pattern === "counting";
  const selectedCount = difficult && intensity.confidence < 0.78 ? Math.max(1, targetCount - 2) : targetCount;
  const id = `${seedTag}-count-${student.studentId}-${day}`;
  return {
    id,
    attemptId: id,
    studentId: student.studentId,
    classId,
    schoolId,
    missionId,
    chestCategory: "StageChest_1",
    targetCount,
    selectedCount,
    spokenNumber: numberWord(selectedCount),
    countCorrect: selectedCount === targetCount,
    speechProofSucceeded: !difficult || selectedCount === targetCount,
    hintUsed: difficult,
    outcomeStatus: selectedCount === targetCount ? "opened_correct" : "opened_wrong_answer",
    rewardAwarded: selectedCount === targetCount ? targetCount : Math.max(1, targetCount - 3),
    responseSeconds: round(difficult ? 5.8 : 3.2),
    createdAtUtc: `${day}T09:41:00.000Z`,
    createdAt: `${day}T09:41:00.000Z`
  };
}

function colorAttempt(schoolId: string, classId: string, student: SeedStudent, missionId: string, day: string, intensity: ReturnType<typeof patternIntensity>) {
  const targetColor = day.endsWith("1") || day.endsWith("3") ? "green" : "blue";
  const difficult = student.pattern === "color";
  const selectedColor = difficult && intensity.confidence < 0.82
    ? targetColor === "green" ? "red" : "purple"
    : targetColor;
  const id = `${seedTag}-color-${student.studentId}-${day}`;
  return {
    id,
    attemptId: id,
    studentId: student.studentId,
    classId,
    schoolId,
    missionId,
    chestCategory: "StageColorChest_1",
    targetColor,
    selectedColor,
    spokenColor: selectedColor,
    colorCorrect: selectedColor === targetColor,
    speechProofSucceeded: true,
    hintUsed: difficult,
    outcomeStatus: selectedColor === targetColor ? "opened_correct" : "opened_wrong_answer",
    rewardAwarded: selectedColor === targetColor ? 6 : 3,
    responseSeconds: round(difficult ? 5.1 : 3.6),
    createdAtUtc: `${day}T09:43:00.000Z`,
    createdAt: `${day}T09:43:00.000Z`
  };
}

function empathyAttempt(schoolId: string, classId: string, student: SeedStudent, missionId: string, day: string, intensity: ReturnType<typeof patternIntensity>) {
  const difficult = student.pattern === "empathy";
  const supportive = !difficult || intensity.confidence > 0.75;
  const id = `${seedTag}-empathy-${student.studentId}-${day}`;
  return {
    id,
    studentId: student.studentId,
    classId,
    schoolId,
    missionId,
    eventCategory: "comfort_peer",
    empathySkill: "comforting",
    prompt: "A friend feels sad after losing a game.",
    targetResponse: "Offer help and kind words",
    selectedResponse: supportive ? "Offer help and kind words" : "Walk away",
    reflectionText: supportive ? "I can help them try again." : "",
    outcomeStatus: supportive ? "supportive_choice" : "needs_support",
    responseSeconds: round(difficult ? 6.4 : 4.1),
    createdAtUtc: `${day}T09:45:00.000Z`,
    createdAt: `${day}T09:45:00.000Z`
  };
}

function recommendationFor(schoolId: string, classId: string, student: SeedStudent) {
  const map = {
    mixed: ["Review B, D, and first sounds", "Aryan shows mixed letter-formation and speech-sound practice needs. Keep short CVC prompts and review one weak letter at a time."],
    literacy: ["Letter reversal review", "Mira has repeated B/D/P formation signals. Use guided tracing and visual discrimination prompts before independent writing."],
    speech: ["Speech sound practice", "Rohan has repeated first-sound and substitution signals. Use short oral prompts and allow retry without blocking progress."],
    color: ["Color identification support", "Tara has repeated red/green and blue/purple color-choice signals. Pair color words with high-contrast examples."],
    empathy: ["Social response practice", "Kabir benefits from repeated role-play choices around helping, sharing, and comforting peers."],
    counting: ["Counting revision", "Neha has repeated wrong-count and hint-used signals. Use smaller sets before increasing chest counts."]
  } satisfies Record<SeedStudent["pattern"], [string, string]>;
  const [title, detail] = map[student.pattern];
  return {
    id: `${seedTag}-rec-${student.studentId}`,
    schoolId,
    classId,
    studentId: student.studentId,
    priority: student.pattern === "mixed" || student.pattern === "literacy" ? "high" : "medium",
    title,
    detail,
    createdAt: `${today}T10:00:00.000Z`
  };
}

function parentSummaryFor(schoolId: string, classId: string, student: SeedStudent) {
  return {
    id: `${seedTag}-summary-${student.studentId}`,
    schoolId,
    studentId: student.studentId,
    studentName: student.name,
    classId,
    weekStart: daysAgo(6),
    minutesPracticed: 42,
    lettersPracticed: student.weakLetters,
    wordsPracticed: student.words,
    bestLetter: student.weakLetters[student.weakLetters.length - 1],
    needsPracticeLetter: student.weakLetters[0],
    averageConfidence: student.pattern === "literacy" ? 0.48 : 0.68,
    averageAttemptsPerLetter: student.pattern === "literacy" ? 3.2 : 2.1,
    trendLabel: "Steady practice with targeted support",
    updatedAt: seededAt
  };
}

function makeStrokePoints(letter: string) {
  const base = letter === "T"
    ? [[[55, 70], [185, 70]], [[120, 70], [120, 220]]]
    : letter === "C"
      ? [[[178, 85], [140, 55], [85, 66], [55, 120], [67, 175], [118, 205], [174, 178]]]
      : [[[60, 220], [115, 60], [170, 220]], [[82, 155], [148, 155]]];
  let order = 0;
  return base.flatMap((stroke, strokeId) => stroke.map(([x, y]) => ({ x, y, strokeId, order: order++ })));
}

function readConfig(values: Record<string, string>): SeedConfig {
  const projectId = values.projectId || env("FIREBASE_PROJECT_ID") || env("GCLOUD_PROJECT") || env("GOOGLE_CLOUD_PROJECT") || "the-script-dea4f";
  return {
    projectId,
    schoolId: values.schoolId || env("SCHOOL_ID") || "pilot-school",
    schoolName: values.schoolName || env("SCHOOL_NAME") || "Pilot School",
    academicYear: values.academicYear || env("ACADEMIC_YEAR") || "2026-2027",
    className: values.className || env("CLASS_NAME") || "LKG A",
    teacherUid: values.teacherUid || env("TEACHER_UID") || "seed-demo-teacher",
    mainStudentEmail: normalizeEmail(values.mainStudentEmail || env("MAIN_STUDENT_EMAIL") || "aryan.raj@axxela.in"),
    mainStudentPassword: values.mainStudentPassword || env("MAIN_STUDENT_PASSWORD") || "12345678",
    defaultStudentPassword: values.defaultStudentPassword || env("DEFAULT_STUDENT_PASSWORD") || "12345678",
    extraEmailDomain: values.extraEmailDomain || env("EXTRA_EMAIL_DOMAIN") || "axxela.in",
    serviceAccountPath: values.serviceAccount || values.serviceAccountPath || env("GOOGLE_APPLICATION_CREDENTIALS")
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

function isAuthUserNotFound(error: unknown) {
  return typeof error === "object"
    && error !== null
    && "code" in error
    && (error as { code?: string }).code === "auth/user-not-found";
}

function slugify(value: string) {
  return value.toLowerCase().trim().replace(/[^a-z0-9]+/g, "-").replace(/^-+|-+$/g, "") || "lkg-a";
}

function normalizeEmail(email: string) {
  return email.trim().toLowerCase();
}

function daysAgo(offset: number) {
  const date = new Date();
  date.setUTCDate(date.getUTCDate() - offset);
  return dateKey(date);
}

function dateKey(date: Date) {
  return date.toISOString().slice(0, 10);
}

function rotate<T>(values: T[], amount: number) {
  return values.map((_, index) => values[(index + amount) % values.length]);
}

function round(value: number) {
  return Math.round(value * 100) / 100;
}

function primaryHint(tags: string[], letter: string) {
  if (tags.includes("mirror")) return `Check the side ${letter} opens toward before writing.`;
  if (tags.includes("wrongStrokeOrder")) return `Start ${letter} with the first guide stroke.`;
  if (tags.includes("wobbly")) return "Try a slower trace inside the guide.";
  if (tags.includes("spacingDrift")) return "Keep the letter centered between the guide lines.";
  return "Good attempt. Keep practising the same shape.";
}

function friendlySound(letter: string) {
  const sounds: Record<string, string> = { C: "k", R: "r", S: "s", T: "t", B: "b", D: "d", P: "p", A: "a", I: "i", O: "o", U: "u", E: "e" };
  return sounds[letter.toUpperCase()] ?? letter.toLowerCase();
}

function syllableBeats(word: string) {
  if (word.length <= 3) return [word];
  if (word === "RABBIT") return ["RA", "BBI", "T"];
  return [word.slice(0, 2), word.slice(2)];
}

function numberWord(value: number) {
  return ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten"][value] ?? String(value);
}

function noteFor(pattern: SeedStudent["pattern"]) {
  const notes = {
    mixed: "Balanced literacy practice with one speech-sound review.",
    literacy: "Prioritize visual letter discrimination and guided tracing.",
    speech: "Prioritize first-sound listening and gentle spoken retries.",
    color: "Add color identification tasks with high-contrast examples.",
    empathy: "Add short social-response choices after literacy practice.",
    counting: "Add two counting chests with smaller visual sets."
  };
  return notes[pattern];
}
