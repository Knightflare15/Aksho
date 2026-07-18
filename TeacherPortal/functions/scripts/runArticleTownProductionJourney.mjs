import { spawn } from "node:child_process";
import fs from "node:fs/promises";
import path from "node:path";
import process from "node:process";
import { fileURLToPath } from "node:url";

const functionsDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const portalDir = path.resolve(functionsDir, "..");
const args = parseArgs(process.argv.slice(2));
const env = await readDotEnv(path.join(portalDir, ".env.local"));
const journey = createJourney(args.journey ?? "article");

const apiKey = args.apiKey ?? env.VITE_FIREBASE_API_KEY;
const projectId = args.projectId ?? env.VITE_FIREBASE_PROJECT_ID ?? "the-script-dea4f";
const bucketName = args.bucket ?? env.VITE_FIREBASE_STORAGE_BUCKET ?? `${projectId}.firebasestorage.app`;
const functionsBaseUrl = stripSlash(args.functionsBaseUrl ?? `https://us-central1-${projectId}.cloudfunctions.net`);
const email = args.email ?? "aryan.raj@axxela.in";
const password = args.password ?? "12345678";
const timeoutMs = Number.parseInt(args.timeoutMs ?? "150000", 10);

if (!apiKey) {
  throw new Error("Missing Firebase API key. Set VITE_FIREBASE_API_KEY in TeacherPortal/.env.local or pass --apiKey=...");
}

const runId = args.runId ?? `${journey.slug}-journey-${new Date().toISOString().replace(/[^0-9]/g, "").slice(0, 14)}`;
const { townAreaId, routeAreaId, gymAreaId, grammarPattern, conceptId } = journey;

console.log(`${journey.title} production-style journey`);
console.log(`Project: ${projectId}`);
console.log(`Bucket: ${bucketName}`);
console.log(`Functions: ${functionsBaseUrl}`);
console.log(`Student email: ${email}`);
console.log(`Run: ${runId}`);
console.log("");

const signIn = await signInWithPassword();
const profile = await getUserProfile(signIn.localId, signIn.idToken);
const schoolId = stringField(profile, "schoolId");
const classId = arrayFirst(profile, "classIds") || stringField(profile, "classId");
const studentId = stringField(profile, "studentId") || arrayFirst(profile, "studentIds");
if (!schoolId || !classId || !studentId) {
  throw new Error(`Signed in, but users/${signIn.localId} is missing school/class/student fields.`);
}
console.log(`Student path: schools/${schoolId}/students/${studentId}`);

for (const taskId of journey.requiredTaskIds)
  await assertDialogueTask(taskId);

console.log(`\n1. Town NPC teaching: learner asks Buddy while practising '${journey.townPhrase}'.`);
const townBuddy = await callBuddy({
  schoolId,
  studentId,
  sessionId: `${runId}-town`,
  dialogueTaskId: journey.townTaskId,
  learnerAttempt: journey.townPhrase,
  trigger: "ask",
  zoneKind: "Town",
  areaId: townAreaId
}, signIn.idToken);
assertBuddyResponse(townBuddy, "Town Buddy");
console.log(`Town Buddy: ${townBuddy.status} via ${townBuddy.provider}; ${clip(townBuddy.learnerText)}`);

const townSpeech = await submitTtsPronunciation({
  spokenText: journey.townPhrase,
  targetText: journey.townPhrase,
  eventId: `${runId}-town-speech`,
  areaId: townAreaId,
  schoolId,
  classId,
  studentId,
  idToken: signIn.idToken
});
console.log(`Town speech: ${townSpeech.status} score=${formatScore(townSpeech.score)} heard="${townSpeech.recognizedText}"`);

await callFunction("submitSpokenPhraseEvent", {
  eventId: `${runId}-town-spoken`,
  schoolId,
  classId,
  studentId,
  areaId: townAreaId,
  zoneKind: "Town",
  dialogueTaskId: journey.townTaskId,
  conceptId,
  grammarPattern,
  transcript: journey.townPhrase,
  canonicalPhrase: journey.townPhrase.toUpperCase(),
  accepted: true,
  inputSource: "windows_tts_emulated_student",
  createdAtUtc: new Date().toISOString()
}, signIn.idToken);
console.log("Town evidence: spoken phrase event recorded.");

if (journey.extraTownTaskId) {
  console.log(`\n1b. Second Town NPC: learner practises '${journey.extraTownPhrase}'.`);
  const extraTownBuddy = await callBuddy({
    schoolId,
    studentId,
    sessionId: `${runId}-town-extra`,
    dialogueTaskId: journey.extraTownTaskId,
    learnerAttempt: journey.extraTownPhrase,
    trigger: "ask",
    zoneKind: "Town",
    areaId: townAreaId
  }, signIn.idToken);
  assertBuddyResponse(extraTownBuddy, "Second Town Buddy");
  console.log(`Second Town Buddy: ${extraTownBuddy.status} via ${extraTownBuddy.provider}; ${clip(extraTownBuddy.learnerText)}`);

  const extraTownSpeech = await submitTtsPronunciation({
    spokenText: journey.extraTownPhrase,
    targetText: journey.extraTownPhrase,
    eventId: `${runId}-town-extra-speech`,
    areaId: townAreaId,
    schoolId,
    classId,
    studentId,
    idToken: signIn.idToken
  });
  console.log(`Second Town speech: ${extraTownSpeech.status} score=${formatScore(extraTownSpeech.score)} heard="${extraTownSpeech.recognizedText}"`);

  await callFunction("submitSpokenPhraseEvent", {
    eventId: `${runId}-town-extra-spoken`,
    schoolId,
    classId,
    studentId,
    areaId: townAreaId,
    zoneKind: "Town",
    dialogueTaskId: journey.extraTownTaskId,
    conceptId,
    grammarPattern: journey.extraTownGrammarPattern,
    transcript: journey.extraTownPhrase,
    canonicalPhrase: journey.extraTownPhrase.toUpperCase(),
    accepted: true,
    inputSource: "windows_tts_emulated_student",
    createdAtUtc: new Date().toISOString()
  }, signIn.idToken);
  console.log("Second Town evidence: spoken phrase event recorded.");
}

console.log(`\n2. Route practice: learner first gives '${journey.routeWrongPhrase}'.`);
const routeBuddy = await callBuddy({
  schoolId,
  studentId,
  sessionId: `${runId}-route`,
  dialogueTaskId: journey.routeTaskId,
  learnerAttempt: journey.routeWrongPhrase,
  trigger: "wrong_answer",
  zoneKind: "Route",
  areaId: routeAreaId
}, signIn.idToken);
assertBuddyResponse(routeBuddy, "Route Buddy");
if (routeBuddy.status === "ok" && normalize(routeBuddy.learnerText).includes(normalize(journey.routeCorrectPhrase))) {
  throw new Error(`Route Buddy leaked the exact answer '${journey.routeCorrectPhrase}'.`);
}
console.log(`Route Buddy: ${routeBuddy.status} via ${routeBuddy.provider}; ${clip(routeBuddy.learnerText)}`);

await callFunction("submitWrittenPhraseEvent", {
  eventId: `${runId}-route-written-wrong`,
  schoolId,
  classId,
  studentId,
  areaId: routeAreaId,
  zoneKind: "Route",
  dialogueTaskId: journey.routeTaskId,
  conceptId,
  grammarPattern,
  submittedResponse: journey.routeWrongPhrase,
  correctedResponse: journey.routeCorrectPhrase,
  accepted: false,
  errorCategory: journey.routeErrorCategory,
  inputSource: "emulated_student_keyboard",
  createdAtUtc: new Date().toISOString()
}, signIn.idToken);
console.log("Route wrong attempt: written evidence recorded.");

const routeSpeech = await submitTtsPronunciation({
  spokenText: journey.routeCorrectPhrase,
  targetText: journey.routeCorrectPhrase,
  eventId: `${runId}-route-corrected`,
  areaId: routeAreaId,
  schoolId,
  classId,
  studentId,
  idToken: signIn.idToken
});
console.log(`Route corrected speech: ${routeSpeech.status} score=${formatScore(routeSpeech.score)} heard="${routeSpeech.recognizedText}"`);

await callFunction("submitWrittenPhraseEvent", {
  eventId: `${runId}-route-written-corrected`,
  schoolId,
  classId,
  studentId,
  areaId: routeAreaId,
  zoneKind: "Route",
  dialogueTaskId: journey.routeTaskId,
  conceptId,
  grammarPattern,
  submittedResponse: journey.routeCorrectPhrase,
  accepted: true,
  inputSource: "emulated_student_keyboard",
  createdAtUtc: new Date().toISOString()
}, signIn.idToken);
console.log("Route corrected attempt: written evidence recorded.");

for (const scaffold of journey.routeScaffoldChecks ?? []) {
  console.log(`\n2b. Route ${scaffold.label}: learner first gives '${scaffold.wrongPhrase}'.`);
  const scaffoldBuddy = await callBuddy({
    schoolId,
    studentId,
    sessionId: `${runId}-${scaffold.eventSlug}`,
    dialogueTaskId: scaffold.taskId,
    learnerAttempt: scaffold.wrongPhrase,
    trigger: "wrong_answer",
    zoneKind: "Route",
    areaId: routeAreaId
  }, signIn.idToken);
  assertBuddyResponse(scaffoldBuddy, `Route ${scaffold.label} Buddy`);
  if (scaffoldBuddy.status === "ok" && normalize(scaffoldBuddy.learnerText).includes(normalize(scaffold.correctPhrase))) {
    throw new Error(`Route ${scaffold.label} Buddy leaked the exact answer '${scaffold.correctPhrase}'.`);
  }
  console.log(`Route ${scaffold.label} Buddy: ${scaffoldBuddy.status} via ${scaffoldBuddy.provider}; ${clip(scaffoldBuddy.learnerText)}`);

  if (scaffold.interactionMode === "draw_and_speak_fill_blank") {
    await callFunction("submitWrittenPhraseEvent", {
      eventId: `${runId}-${scaffold.eventSlug}-draw-wrong`,
      schoolId,
      classId,
      studentId,
      areaId: routeAreaId,
      zoneKind: "Route",
      dialogueTaskId: scaffold.taskId,
      conceptId,
      grammarPattern: scaffold.grammarPattern ?? grammarPattern,
      submittedResponse: scaffold.wrongPhrase,
      correctedResponse: scaffold.correctPhrase,
      fullCorrectResponse: scaffold.fullCorrectPhrase,
      accepted: false,
      errorCategory: scaffold.errorCategory,
      malfunctionType: scaffold.malfunctionType,
      scaffoldMode: scaffold.scaffoldMode,
      heardTranscript: scaffold.heardTranscript,
      challengeAudioText: scaffold.heardTranscript,
      displayTranscript: scaffold.prompt,
      responseModalities: ["draw_blank", "speak_blank"],
      inputSource: "emulated_handwriting_canvas",
      handwritingTarget: scaffold.correctPhrase,
      handwritingRecognizedText: scaffold.wrongPhrase,
      createdAtUtc: new Date().toISOString()
    }, signIn.idToken);
    console.log(`Route ${scaffold.label} wrong drawn blank: handwriting evidence recorded.`);

    await callFunction("submitWrittenPhraseEvent", {
      eventId: `${runId}-${scaffold.eventSlug}-draw-correct`,
      schoolId,
      classId,
      studentId,
      areaId: routeAreaId,
      zoneKind: "Route",
      dialogueTaskId: scaffold.taskId,
      conceptId,
      grammarPattern: scaffold.grammarPattern ?? grammarPattern,
      submittedResponse: scaffold.correctPhrase,
      fullResponse: scaffold.fullCorrectPhrase,
      accepted: true,
      malfunctionType: scaffold.malfunctionType,
      scaffoldMode: scaffold.scaffoldMode,
      heardTranscript: scaffold.heardTranscript,
      challengeAudioText: scaffold.heardTranscript,
      displayTranscript: scaffold.prompt,
      responseModalities: ["draw_blank", "speak_blank"],
      inputSource: "emulated_handwriting_canvas",
      handwritingTarget: scaffold.correctPhrase,
      handwritingRecognizedText: scaffold.correctPhrase,
      createdAtUtc: new Date().toISOString()
    }, signIn.idToken);
    console.log(`Route ${scaffold.label} correct drawn blank: handwriting evidence recorded.`);

    await callFunction("submitSpokenPhraseEvent", {
      eventId: `${runId}-${scaffold.eventSlug}-wrong`,
      schoolId,
      classId,
      studentId,
      areaId: routeAreaId,
      zoneKind: "Route",
      dialogueTaskId: scaffold.taskId,
      conceptId,
      grammarPattern: scaffold.grammarPattern ?? grammarPattern,
      transcript: scaffold.wrongPhrase,
      canonicalPhrase: scaffold.wrongPhrase.toUpperCase(),
      submittedResponse: scaffold.wrongPhrase,
      correctedResponse: scaffold.correctPhrase,
      fullCorrectResponse: scaffold.fullCorrectPhrase,
      accepted: false,
      errorCategory: scaffold.errorCategory,
      malfunctionType: scaffold.malfunctionType,
      scaffoldMode: scaffold.scaffoldMode,
      heardTranscript: scaffold.heardTranscript,
      challengeAudioText: scaffold.heardTranscript,
      displayTranscript: scaffold.prompt,
      responseModalities: ["draw_blank", "speak_blank"],
      inputSource: "emulated_student_voice",
      createdAtUtc: new Date().toISOString()
    }, signIn.idToken);
    console.log(`Route ${scaffold.label} wrong spoken blank: evidence recorded.`);

    const spokenBlank = await submitTtsPronunciation({
      spokenText: scaffold.correctPhrase,
      targetText: scaffold.correctPhrase,
      eventId: `${runId}-${scaffold.eventSlug}-correct`,
      areaId: routeAreaId,
      schoolId,
      classId,
      studentId,
      idToken: signIn.idToken,
      recordFunctionName: "submitSpokenPhraseEvent",
      recordPayload: {
        eventId: `${runId}-${scaffold.eventSlug}-correct`,
        zoneKind: "Route",
        dialogueTaskId: scaffold.taskId,
        conceptId,
        grammarPattern: scaffold.grammarPattern ?? grammarPattern,
        transcript: scaffold.correctPhrase,
        canonicalPhrase: scaffold.correctPhrase.toUpperCase(),
        submittedResponse: scaffold.correctPhrase,
        fullResponse: scaffold.fullCorrectPhrase,
        accepted: true,
        malfunctionType: scaffold.malfunctionType,
        scaffoldMode: scaffold.scaffoldMode,
        heardTranscript: scaffold.heardTranscript,
        challengeAudioText: scaffold.heardTranscript,
        displayTranscript: scaffold.prompt,
        responseModalities: ["draw_blank", "speak_blank"],
        inputSource: "speech_command_with_server_pronunciation"
      }
    });
    console.log(`Route ${scaffold.label} spoken blank: ${spokenBlank.status} score=${formatScore(spokenBlank.score)} heard="${spokenBlank.recognizedText}"`);
    console.log(`Route ${scaffold.label} correct spoken blank: evidence recorded with Azure pronunciation job.`);
    continue;
  }

  await callFunction("submitWrittenPhraseEvent", {
    eventId: `${runId}-${scaffold.eventSlug}-wrong`,
    schoolId,
    classId,
    studentId,
    areaId: routeAreaId,
    zoneKind: "Route",
    dialogueTaskId: scaffold.taskId,
    conceptId,
    grammarPattern: scaffold.grammarPattern ?? grammarPattern,
    submittedResponse: scaffold.wrongPhrase,
    correctedResponse: scaffold.correctPhrase,
    orderedTokens: scaffold.wrongTokens,
    correctTokens: scaffold.correctTokens,
    accepted: false,
    errorCategory: scaffold.errorCategory,
    malfunctionType: scaffold.malfunctionType,
    scaffoldMode: scaffold.scaffoldMode,
    heardTranscript: scaffold.heardTranscript,
    challengeAudioText: scaffold.heardTranscript,
    displayTranscript: scaffold.prompt,
    responseModalities: ["drag_drop_words"],
    inputSource: "emulated_drag_drop",
    createdAtUtc: new Date().toISOString()
  }, signIn.idToken);
  console.log(`Route ${scaffold.label} wrong tile order: drag/drop evidence recorded.`);

  await callFunction("submitWrittenPhraseEvent", {
    eventId: `${runId}-${scaffold.eventSlug}-correct`,
    schoolId,
    classId,
    studentId,
    areaId: routeAreaId,
    zoneKind: "Route",
    dialogueTaskId: scaffold.taskId,
    conceptId,
    grammarPattern: scaffold.grammarPattern ?? grammarPattern,
    submittedResponse: scaffold.correctPhrase,
    orderedTokens: scaffold.correctTokens,
    accepted: true,
    malfunctionType: scaffold.malfunctionType,
    scaffoldMode: scaffold.scaffoldMode,
    heardTranscript: scaffold.heardTranscript,
    challengeAudioText: scaffold.heardTranscript,
    displayTranscript: scaffold.prompt,
    responseModalities: ["drag_drop_words"],
    inputSource: "emulated_drag_drop",
    createdAtUtc: new Date().toISOString()
  }, signIn.idToken);
  console.log(`Route ${scaffold.label} correct tile order: drag/drop evidence recorded.`);
}

console.log(`\n3. Route combat: learner speaks '${journey.combatPhrase}'.`);
const combatSpeech = await submitTtsPronunciation({
  spokenText: journey.combatPhrase,
  targetText: journey.combatPhrase,
  eventId: `${runId}-battle-command`,
  areaId: routeAreaId,
  schoolId,
  classId,
  studentId,
  idToken: signIn.idToken,
  recordFunctionName: "submitGrammarBattleEvent",
  recordPayload: {
    eventId: `${runId}-battle-command`,
    zoneKind: "Route",
    conceptId,
    grammarPattern,
    playerPhrase: journey.combatPhrase,
    recognizedPhrase: journey.combatPhrase,
    canonicalPhrase: journey.combatPhrase.toUpperCase(),
    action: journey.combatAction,
    targetCreature: "RAT",
    accepted: true,
    outcome: journey.combatOutcome,
    inputSource: "speech_command_with_server_pronunciation"
  }
});
console.log(`Combat speech: ${combatSpeech.status} score=${formatScore(combatSpeech.score)} heard="${combatSpeech.recognizedText}"`);
console.log("Combat accepted action: grammar battle event recorded with Azure pronunciation job.");

if (journey.secondaryCombatPhrase) {
  const secondaryCombatSpeech = await submitTtsPronunciation({
    spokenText: journey.secondaryCombatPhrase,
    targetText: journey.secondaryCombatPhrase,
    eventId: `${runId}-battle-secondary-command`,
    areaId: routeAreaId,
    schoolId,
    classId,
    studentId,
    idToken: signIn.idToken,
    recordFunctionName: "submitGrammarBattleEvent",
    recordPayload: {
      eventId: `${runId}-battle-secondary-command`,
      zoneKind: "Route",
      conceptId,
      grammarPattern,
      playerPhrase: journey.secondaryCombatPhrase,
      recognizedPhrase: journey.secondaryCombatPhrase,
      canonicalPhrase: journey.secondaryCombatPhrase.toUpperCase(),
      action: journey.secondaryCombatAction,
      targetCreature: "RAT",
      accepted: true,
      outcome: journey.secondaryCombatOutcome,
      inputSource: "speech_command_with_server_pronunciation"
    }
  });
  console.log(`Combat second accepted action: '${journey.secondaryCombatPhrase}' recorded with score=${formatScore(secondaryCombatSpeech.score)}.`);
}

const invalidCombatSpeech = await submitTtsPronunciation({
  spokenText: journey.invalidCombatPhrase,
  targetText: journey.invalidCombatPhrase,
  eventId: `${runId}-battle-invalid-command`,
  areaId: routeAreaId,
  schoolId,
  classId,
  studentId,
  idToken: signIn.idToken,
  recordFunctionName: "submitGrammarBattleEvent",
  recordPayload: {
    eventId: `${runId}-battle-invalid-command`,
    zoneKind: "Route",
    conceptId,
    grammarPattern,
    playerPhrase: journey.invalidCombatPhrase,
    recognizedPhrase: journey.invalidCombatPhrase,
    canonicalPhrase: "",
    action: "none",
    accepted: false,
    outcome: "short_correction_retry",
    errorCategory: journey.invalidCombatErrorCategory,
    inputSource: "speech_command_with_server_pronunciation"
  }
});
console.log(`Combat invalid action: rejected battle event recorded with score=${formatScore(invalidCombatSpeech.score)}.`);

console.log("\n4. Gym assessment: Buddy must be unavailable, then learner answers without help.");
for (const scaffold of journey.gymScaffoldChecks ?? []) {
  const gymScaffoldBuddy = await callBuddy({
    schoolId,
    studentId,
    sessionId: `${runId}-${scaffold.eventSlug}`,
    dialogueTaskId: scaffold.taskId,
    learnerAttempt: scaffold.correctPhrase,
    trigger: "ask",
    zoneKind: "Gym",
    areaId: gymAreaId
  }, signIn.idToken);
  assertStatus(gymScaffoldBuddy, "blocked", `Gym ${scaffold.label} Buddy`);
  console.log(`Gym ${scaffold.label} Buddy: ${gymScaffoldBuddy.status}; help correctly unavailable.`);

  if (scaffold.interactionMode === "draw_and_speak_fill_blank") {
    await callFunction("submitGymAttempt", {
      attemptId: `${runId}-${scaffold.eventSlug}-draw-wrong`,
      schoolId,
      classId,
      studentId,
      areaId: gymAreaId,
      gymId: gymAreaId,
      zoneKind: "Gym",
      conceptId,
      grammarPattern: scaffold.grammarPattern ?? grammarPattern,
      dialogueTaskId: scaffold.taskId,
      submittedResponse: scaffold.wrongPhrase,
      correctedResponse: scaffold.correctPhrase,
      fullCorrectResponse: scaffold.fullCorrectPhrase,
      passed: false,
      score: 0,
      errorCategory: scaffold.errorCategory,
      malfunctionType: scaffold.malfunctionType,
      scaffoldMode: scaffold.scaffoldMode,
      heardTranscript: scaffold.heardTranscript,
      challengeAudioText: scaffold.heardTranscript,
      displayTranscript: scaffold.prompt,
      responseModalities: ["draw_blank", "speak_blank"],
      inputSource: "emulated_handwriting_canvas",
      handwritingTarget: scaffold.correctPhrase,
      handwritingRecognizedText: scaffold.wrongPhrase,
      createdAtUtc: new Date().toISOString()
    }, signIn.idToken);
    console.log(`Gym ${scaffold.label} wrong drawn blank: no-help gym attempt recorded.`);

    await callFunction("submitGymAttempt", {
      attemptId: `${runId}-${scaffold.eventSlug}-draw-pass`,
      schoolId,
      classId,
      studentId,
      areaId: gymAreaId,
      gymId: gymAreaId,
      zoneKind: "Gym",
      conceptId,
      grammarPattern: scaffold.grammarPattern ?? grammarPattern,
      dialogueTaskId: scaffold.taskId,
      submittedResponse: scaffold.correctPhrase,
      fullResponse: scaffold.fullCorrectPhrase,
      passed: true,
      score: 1,
      malfunctionType: scaffold.malfunctionType,
      scaffoldMode: scaffold.scaffoldMode,
      heardTranscript: scaffold.heardTranscript,
      challengeAudioText: scaffold.heardTranscript,
      displayTranscript: scaffold.prompt,
      responseModalities: ["draw_blank", "speak_blank"],
      inputSource: "emulated_handwriting_canvas",
      handwritingTarget: scaffold.correctPhrase,
      handwritingRecognizedText: scaffold.correctPhrase,
      createdAtUtc: new Date().toISOString()
    }, signIn.idToken);
    console.log(`Gym ${scaffold.label} correct drawn blank: no-help gym attempt recorded.`);

    const spokenBlank = await submitTtsPronunciation({
      spokenText: scaffold.correctPhrase,
      targetText: scaffold.correctPhrase,
      eventId: `${runId}-${scaffold.eventSlug}-pass`,
      areaId: gymAreaId,
      schoolId,
      classId,
      studentId,
      idToken: signIn.idToken,
      recordFunctionName: "submitGymAttempt",
      recordPayload: {
        attemptId: `${runId}-${scaffold.eventSlug}-pass`,
        gymId: gymAreaId,
        zoneKind: "Gym",
        conceptId,
        grammarPattern: scaffold.grammarPattern ?? grammarPattern,
        dialogueTaskId: scaffold.taskId,
        submittedResponse: scaffold.correctPhrase,
        fullResponse: scaffold.fullCorrectPhrase,
        passed: true,
        malfunctionType: scaffold.malfunctionType,
        scaffoldMode: scaffold.scaffoldMode,
        heardTranscript: scaffold.heardTranscript,
        challengeAudioText: scaffold.heardTranscript,
        displayTranscript: scaffold.prompt,
        responseModalities: ["draw_blank", "speak_blank"],
        inputSource: "speech_command_with_server_pronunciation"
      }
    });
    console.log(`Gym ${scaffold.label} spoken blank: ${spokenBlank.status} score=${formatScore(spokenBlank.score)} heard="${spokenBlank.recognizedText}"`);
    console.log(`Gym ${scaffold.label} correct spoken blank: no-help gym attempt recorded with Azure pronunciation job.`);
    continue;
  }

  await callFunction("submitGymAttempt", {
    attemptId: `${runId}-${scaffold.eventSlug}-wrong`,
    schoolId,
    classId,
    studentId,
    areaId: gymAreaId,
    gymId: gymAreaId,
    zoneKind: "Gym",
    conceptId,
    grammarPattern: scaffold.grammarPattern ?? grammarPattern,
    dialogueTaskId: scaffold.taskId,
    submittedResponse: scaffold.wrongPhrase,
    correctedResponse: scaffold.correctPhrase,
    orderedTokens: scaffold.wrongTokens,
    correctTokens: scaffold.correctTokens,
    passed: false,
    score: 0,
    errorCategory: scaffold.errorCategory,
    malfunctionType: scaffold.malfunctionType,
    scaffoldMode: scaffold.scaffoldMode,
    heardTranscript: scaffold.heardTranscript,
    challengeAudioText: scaffold.heardTranscript,
    displayTranscript: scaffold.prompt,
    responseModalities: ["drag_drop_words"],
    inputSource: "emulated_drag_drop",
    createdAtUtc: new Date().toISOString()
  }, signIn.idToken);
  console.log(`Gym ${scaffold.label} wrong tile order: no-help gym attempt recorded.`);

  await callFunction("submitGymAttempt", {
    attemptId: `${runId}-${scaffold.eventSlug}-pass`,
    schoolId,
    classId,
    studentId,
    areaId: gymAreaId,
    gymId: gymAreaId,
    zoneKind: "Gym",
    conceptId,
    grammarPattern: scaffold.grammarPattern ?? grammarPattern,
    dialogueTaskId: scaffold.taskId,
    submittedResponse: scaffold.correctPhrase,
    orderedTokens: scaffold.correctTokens,
    passed: true,
    score: 1,
    malfunctionType: scaffold.malfunctionType,
    scaffoldMode: scaffold.scaffoldMode,
    heardTranscript: scaffold.heardTranscript,
    challengeAudioText: scaffold.heardTranscript,
    displayTranscript: scaffold.prompt,
    responseModalities: ["drag_drop_words"],
    inputSource: "emulated_drag_drop",
    createdAtUtc: new Date().toISOString()
  }, signIn.idToken);
  console.log(`Gym ${scaffold.label} correct tile order: no-help gym attempt recorded.`);
}

const gymBuddy = await callBuddy({
  schoolId,
  studentId,
  sessionId: `${runId}-gym`,
  dialogueTaskId: journey.gymTaskId,
  learnerAttempt: journey.gymPhrase,
  trigger: "ask",
  zoneKind: "Gym",
  areaId: gymAreaId
}, signIn.idToken);
assertStatus(gymBuddy, "blocked", "Gym Buddy");
console.log(`Gym Buddy: ${gymBuddy.status}; help correctly unavailable.`);

const gymSpeech = await submitTtsPronunciation({
  spokenText: journey.gymPhrase,
  targetText: journey.gymPhrase,
  eventId: `${runId}-gym-pass`,
  areaId: gymAreaId,
  schoolId,
  classId,
  studentId,
  idToken: signIn.idToken,
  recordFunctionName: "submitGymAttempt",
  recordPayload: {
    attemptId: `${runId}-gym-pass`,
    gymId: gymAreaId,
    zoneKind: "Gym",
    conceptId,
    grammarPattern,
    dialogueTaskId: journey.gymTaskId,
    submittedResponse: journey.gymPhrase,
    recognizedPhrase: journey.gymPhrase,
    passed: true,
    inputSource: "speech_command_with_server_pronunciation"
  }
});
console.log(`Gym speech: ${gymSpeech.status} score=${formatScore(gymSpeech.score)} heard="${gymSpeech.recognizedText}"`);
console.log("Gym result: passed attempt recorded with Azure pronunciation job.");

console.log(`\n${journey.title} journey passed.`);

async function submitTtsPronunciation({
  spokenText,
  targetText,
  eventId,
  areaId,
  schoolId,
  classId,
  studentId,
  idToken,
  recordFunctionName = "submitWordCast",
  recordPayload = null
}) {
  const tempRoot = process.env.TMP ?? process.env.TEMP ?? functionsDir;
  const rawWav = path.join(tempRoot, `${eventId}-tts.wav`);
  const finalWav = path.join(tempRoot, `${eventId}-16k.wav`);
  await synthesizeWindowsTts(spokenText, rawWav);
  await convertToAzureWav(rawWav, finalWav);
  const bytes = await fs.readFile(finalWav);
  const objectName = `schools/${schoolId}/students/${studentId}/pronunciationAudio/${eventId}.wav`;
  await uploadWav(objectName, bytes, idToken);
  const analysisFields = {
    audioStoragePath: `gs://${bucketName}/${objectName}`,
    audioContentType: "audio/wav",
    rawAudioUploaded: true,
    serverAnalysisJobId: eventId,
    serverAnalysisStatus: "pending",
    serverAnalysisRequestedAtUtc: new Date().toISOString(),
    onDeviceAnalysisProvider: "windows-tts-emulated-student",
    analysisMode: "HybridServerAssist",
    targetText,
    createdAtUtc: new Date().toISOString(),
    smokeTest: true
  };
  if (recordPayload) {
    await callFunction(recordFunctionName, {
      ...recordPayload,
      schoolId,
      classId,
      studentId,
      areaId,
      ...analysisFields
    }, idToken);
  } else {
    await callFunction("submitWordCast", {
      eventId,
      schoolId,
      classId,
      studentId,
      missionId: `${journey.slug}-production-journey`,
      areaId,
      word: targetText,
      success: true,
      responseSeconds: 1,
      ...analysisFields
    }, idToken);
  }
  const result = await waitForJob(schoolId, studentId, eventId, idToken);
  await fs.unlink(rawWav).catch(() => undefined);
  await fs.unlink(finalWav).catch(() => undefined);
  if (result.status !== "complete") {
    throw new Error(`${targetText} pronunciation analysis did not complete: ${result.error}`);
  }
  return result;
}

function synthesizeWindowsTts(text, outputPath) {
  const script = [
    "Add-Type -AssemblyName System.Speech",
    "$s = New-Object System.Speech.Synthesis.SpeechSynthesizer",
    "$s.Rate = -1",
    "$s.Volume = 100",
    `$s.SetOutputToWaveFile(${psQuote(outputPath)})`,
    `$s.Speak(${psQuote(text)})`,
    "$s.Dispose()"
  ].join("; ");
  return runProcess("powershell.exe", ["-NoProfile", "-Command", script]);
}

function convertToAzureWav(inputPath, outputPath) {
  return runProcess("ffmpeg", [
    "-y",
    "-hide_banner",
    "-loglevel",
    "error",
    "-i",
    inputPath,
    "-ac",
    "1",
    "-ar",
    "16000",
    "-c:a",
    "pcm_s16le",
    outputPath
  ]);
}

function runProcess(command, argv) {
  return new Promise((resolve, reject) => {
    const child = spawn(command, argv);
    let stderr = "";
    child.stderr.on("data", chunk => {
      stderr += chunk.toString();
    });
    child.on("error", reject);
    child.on("close", code => {
      if (code === 0) resolve();
      else reject(new Error(`${command} failed with exit code ${code}: ${stderr.trim()}`));
    });
  });
}

async function signInWithPassword() {
  const response = await postJson(
    `https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=${encodeURIComponent(apiKey)}`,
    { email, password, returnSecureToken: true }
  );
  if (!response.idToken || !response.localId) {
    throw new Error("Firebase Auth sign-in succeeded without idToken/localId.");
  }
  return response;
}

async function getUserProfile(uid, idToken) {
  const response = await fetchWithRetry(firestoreDocUrl(`users/${uid}`), {
    headers: { Authorization: `Bearer ${idToken}` }
  });
  if (!response.ok) {
    throw new Error(`Could not read users/${uid}: ${response.status} ${await response.text()}`);
  }
  return response.json();
}

async function assertDialogueTask(taskId) {
  const response = await fetchWithRetry(firestoreDocUrl(`gameContentDialogueTasks/${taskId}`), {
    headers: { Authorization: `Bearer ${signIn.idToken}` }
  });
  if (!response.ok) {
    throw new Error(`Missing dialogue task ${taskId}: ${response.status} ${await response.text()}`);
  }
}

async function callBuddy(payload, idToken) {
  const response = await callFunction("requestBuddyHelp", payload, idToken);
  if (!response.result) {
    throw new Error(`requestBuddyHelp returned no result: ${JSON.stringify(response)}`);
  }
  return response.result;
}

async function callFunction(name, payload, idToken) {
  return postJson(`${functionsBaseUrl}/${name}`, { data: payload }, idToken);
}

async function uploadWav(objectName, bytes, idToken) {
  const url = `https://firebasestorage.googleapis.com/v0/b/${encodeURIComponent(bucketName)}/o?uploadType=media&name=${encodeURIComponent(objectName)}`;
  const response = await fetchWithRetry(url, {
    method: "POST",
    headers: {
      Authorization: `Bearer ${idToken}`,
      "Content-Type": "audio/wav"
    },
    body: bytes
  });
  if (!response.ok) {
    throw new Error(`Storage upload failed: ${response.status} ${await response.text()}`);
  }
}

async function waitForJob(schoolId, studentId, jobId, idToken) {
  const started = Date.now();
  const url = firestoreDocUrl(`schools/${schoolId}/students/${studentId}/analysisJobs/${jobId}`);
  while (Date.now() - started < timeoutMs) {
    await delay(2500);
    const response = await fetchWithRetry(url, {
      headers: { Authorization: `Bearer ${idToken}` }
    });
    if (response.status === 404) {
      process.stdout.write(".");
      continue;
    }
    if (!response.ok) {
      throw new Error(`Job read failed: ${response.status} ${await response.text()}`);
    }
    const doc = await response.json();
    const fields = doc.fields ?? {};
    const status = fields.status?.stringValue ?? "";
    if (status === "complete" || status === "failed") {
      return {
        status,
        score: numberOrNull(nestedField(fields, ["result", "serverPronunciationInsight", "score"])),
        recognizedText: String(nestedField(fields, ["result", "serverPronunciationInsight", "rawRecognizedText"]) ?? ""),
        error: fields.error?.stringValue ?? ""
      };
    }
    process.stdout.write(".");
  }
  throw new Error(`Timed out after ${timeoutMs}ms waiting for analysisJobs/${jobId}`);
}

async function postJson(url, body, idToken = "") {
  const headers = { "Content-Type": "application/json" };
  if (idToken) headers.Authorization = `Bearer ${idToken}`;
  const response = await fetchWithRetry(url, {
    method: "POST",
    headers,
    body: JSON.stringify(body)
  });
  if (!response.ok) {
    throw new Error(`POST ${url} failed: ${response.status} ${await response.text()}`);
  }
  return response.json();
}

async function fetchWithRetry(url, options, attempts = 3) {
  let lastError = null;
  for (let attempt = 1; attempt <= attempts; attempt++) {
    try {
      return await fetch(url, options);
    } catch (error) {
      lastError = error;
      if (attempt === attempts) break;
      await delay(750 * attempt);
    }
  }
  throw lastError;
}

function assertStatus(response, expected, label) {
  if (response.status !== expected) {
    throw new Error(`${label} expected status ${expected}, got ${response.status}: ${JSON.stringify(response)}`);
  }
}

function assertProvider(response, expected, label) {
  if (response.provider !== expected) {
    throw new Error(`${label} expected provider ${expected}, got ${response.provider}: ${JSON.stringify(response)}`);
  }
}

function assertBuddyResponse(response, label) {
  if (response.status === "ok") {
    assertProvider(response, "gemini", label);
    return;
  }
  if (response.status === "fallback" && response.provider === "deterministic_fallback") {
    console.warn(`${label}: deterministic fallback (${response.fallbackReason || "unspecified"}).`);
    return;
  }
  throw new Error(`${label} returned an unexpected response: ${JSON.stringify(response)}`);
}

function firestoreDocUrl(documentPath) {
  return `https://firestore.googleapis.com/v1/projects/${encodeURIComponent(projectId)}/databases/(default)/documents/${documentPath.split("/").map(encodeURIComponent).join("/")}`;
}

function stringField(doc, field) {
  return doc.fields?.[field]?.stringValue ?? "";
}

function arrayFirst(doc, field) {
  return doc.fields?.[field]?.arrayValue?.values?.[0]?.stringValue ?? "";
}

function nestedField(fields, pathParts) {
  let current = { mapValue: { fields } };
  for (const part of pathParts) {
    current = current?.mapValue?.fields?.[part];
    if (!current) return null;
  }
  if ("doubleValue" in current) return Number(current.doubleValue);
  if ("integerValue" in current) return Number(current.integerValue);
  if ("stringValue" in current) return current.stringValue;
  return null;
}

function numberOrNull(value) {
  const number = Number(value);
  return Number.isFinite(number) ? number : null;
}

function normalize(value) {
  return String(value ?? "").trim().toUpperCase().replace(/\s+/g, " ");
}

function clip(value) {
  const text = String(value ?? "").replace(/\s+/g, " ").trim();
  return text.length > 140 ? `${text.slice(0, 137)}...` : text;
}

function formatScore(value) {
  return value == null ? "n/a" : Number(value).toFixed(3);
}

function stripSlash(value) {
  return String(value ?? "").replace(/\/+$/, "");
}

function psQuote(value) {
  return `'${String(value).replace(/'/g, "''")}'`;
}

function delay(ms) {
  return new Promise(resolve => setTimeout(resolve, ms));
}

function createJourney(name) {
  const normalized = String(name).trim().toLowerCase();
  if (normalized === "verb" || normalized === "verbs") {
    return {
      slug: "verb-town",
      title: "Verb Town",
      townAreaId: "TOWN:BASICVERBS:6",
      routeAreaId: "ROUTE:BASICVERBS:6",
      gymAreaId: "GYM:BASICVERBS:6",
      grammarPattern: "NounVerbPresent",
      conceptId: "BasicVerbs",
      requiredTaskIds: [
        "verb-action",
        "verb-after-noun",
        "verb-road-missing-action",
        "verb-road-jumbled-command",
        "verb-road-correct-action",
        "verb-gym-missing-action",
        "verb-gym-jumbled-command",
        "verb-action-battle"
      ],
      townTaskId: "verb-after-noun",
      townPhrase: "Rat bites",
      extraTownTaskId: "verb-action",
      extraTownPhrase: "Bite",
      extraTownGrammarPattern: "VerbOnly",
      routeTaskId: "verb-road-correct-action",
      routeWrongPhrase: "Bird runs",
      routeCorrectPhrase: "Bird flies",
      routeErrorCategory: "wrong_verb_context",
      routeScaffoldChecks: [
        {
          label: "FITB",
          eventSlug: "route-fitb",
          taskId: "verb-road-missing-action",
          prompt: "Dog ____",
          heardTranscript: "Dog runs",
          interactionMode: "draw_and_speak_fill_blank",
          wrongPhrase: "run",
          correctPhrase: "runs",
          fullCorrectPhrase: "Dog runs",
          errorCategory: "missing_verb",
          malfunctionType: "MissingWord",
          scaffoldMode: "FillInBlank"
        },
        {
          label: "jumbled words",
          eventSlug: "route-jumbled",
          taskId: "verb-road-jumbled-command",
          wrongPhrase: "Bites Rat",
          correctPhrase: "Rat bites",
          prompt: "bites / Rat",
          heardTranscript: "Rat bites",
          interactionMode: "drag_drop_words",
          wrongTokens: ["bites", "Rat"],
          correctTokens: ["Rat", "bites"],
          errorCategory: "jumbled_word_order",
          malfunctionType: "ScrambledSentence",
          scaffoldMode: "JumbledWords"
        }
      ],
      gymScaffoldChecks: [
        {
          label: "FITB",
          eventSlug: "gym-fitb",
          taskId: "verb-gym-missing-action",
          prompt: "Fish ____",
          heardTranscript: "Fish swims",
          interactionMode: "draw_and_speak_fill_blank",
          wrongPhrase: "swim",
          correctPhrase: "swims",
          fullCorrectPhrase: "Fish swims",
          errorCategory: "missing_verb",
          malfunctionType: "MissingWord",
          scaffoldMode: "FillInBlank"
        },
        {
          label: "jumbled words",
          eventSlug: "gym-jumbled",
          taskId: "verb-gym-jumbled-command",
          wrongPhrase: "Runs Dog",
          correctPhrase: "Dog runs",
          prompt: "runs / Dog",
          heardTranscript: "Dog runs",
          interactionMode: "drag_drop_words",
          wrongTokens: ["runs", "Dog"],
          correctTokens: ["Dog", "runs"],
          errorCategory: "jumbled_word_order",
          malfunctionType: "ScrambledSentence",
          scaffoldMode: "JumbledWords"
        }
      ],
      combatPhrase: "Dog runs",
      combatAction: "attack",
      combatOutcome: "dog_ran_attack",
      secondaryCombatPhrase: "Rat bites",
      secondaryCombatAction: "attack",
      secondaryCombatOutcome: "rat_bite_attack",
      invalidCombatPhrase: "Bird swims",
      invalidCombatErrorCategory: "invalid_verb_context",
      gymTaskId: "verb-action-battle",
      gymPhrase: "Fish swims"
    };
  }
  if (normalized !== "article" && normalized !== "articles")
    throw new Error(`Unknown journey '${name}'. Use article or verb.`);
  return {
    slug: "article-town",
    title: "Article Town",
    townAreaId: "TOWN:ARTICLES:7",
    routeAreaId: "ROUTE:ARTICLES:7",
    gymAreaId: "GYM:ARTICLES:7",
    grammarPattern: "DeterminerNoun",
    conceptId: "Articles",
    requiredTaskIds: ["gen-article-a-dog-1", "gen-article-a-cat-1", "gen-article-the-cat-1"],
    townTaskId: "gen-article-a-dog-1",
    townPhrase: "A dog",
    routeTaskId: "gen-article-a-cat-1",
    routeWrongPhrase: "cat",
    routeCorrectPhrase: "A cat",
    routeErrorCategory: "missing_article",
    combatPhrase: "A rat",
    combatAction: "summon",
    combatOutcome: "summoned_rat",
    invalidCombatPhrase: "rat the",
    invalidCombatErrorCategory: "invalid_word_order",
    gymTaskId: "gen-article-the-cat-1",
    gymPhrase: "The cat"
  };
}

async function readDotEnv(filePath) {
  const result = {};
  try {
    const text = await fs.readFile(filePath, "utf8");
    for (const rawLine of text.split(/\r?\n/)) {
      const line = rawLine.trim();
      if (!line || line.startsWith("#")) continue;
      const equalsIndex = line.indexOf("=");
      if (equalsIndex <= 0) continue;
      result[line.slice(0, equalsIndex).trim()] = line.slice(equalsIndex + 1).trim();
    }
  } catch {
    // Optional file.
  }
  return result;
}

function parseArgs(argv) {
  const parsed = {};
  for (const arg of argv) {
    if (!arg.startsWith("--")) continue;
    const equalsIndex = arg.indexOf("=");
    if (equalsIndex < 0) parsed[arg.slice(2)] = "true";
    else parsed[arg.slice(2, equalsIndex)] = arg.slice(equalsIndex + 1);
  }
  return parsed;
}
