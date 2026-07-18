import fs from "node:fs/promises";
import path from "node:path";
import process from "node:process";
import { fileURLToPath } from "node:url";

const functionsDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const portalDir = path.resolve(functionsDir, "..");
const args = parseArgs(process.argv.slice(2));
const env = await readDotEnv(path.join(portalDir, ".env.local"));

const apiKey = args.apiKey ?? env.VITE_FIREBASE_API_KEY;
const projectId = args.projectId ?? env.VITE_FIREBASE_PROJECT_ID ?? "the-script-dea4f";
const functionsBaseUrl = stripSlash(args.functionsBaseUrl ?? `https://us-central1-${projectId}.cloudfunctions.net`);
const email = args.email ?? "aryan.raj@axxela.in";
const password = args.password ?? "12345678";

if (!apiKey)
  throw new Error("Missing VITE_FIREBASE_API_KEY in TeacherPortal/.env.local.");

console.log("Buddy client smoke test");
console.log(`Project: ${projectId}`);
console.log(`Functions: ${functionsBaseUrl}`);

const signIn = await signInWithPassword();
const profile = await getUserProfile(signIn.localId, signIn.idToken);
const schoolId = stringField(profile, "schoolId");
const studentId = stringField(profile, "studentId") || arrayFirst(profile, "studentIds");

if (!schoolId || !studentId)
  throw new Error("Signed-in user is missing schoolId or studentId.");

if (args.call === "true") {
  await runCallContinuityTest();
  process.exit(0);
}
if (args.grimoire === "true") {
  await runGrimoireAssistTest();
  process.exit(0);
}

const testCases = [
  {
    name: "Town coaching",
    dialogueTaskId: "welcome-greet",
    zoneKind: "Town",
    areaId: "TOWN:GREETINGSANDSURVIVALENGLISH:1",
    learnerAttempt: "hello there"
  },
  {
    name: "Route clue",
    dialogueTaskId: "article-road-missing",
    zoneKind: "Route",
    areaId: "ROUTE:ARTICLES:7",
    learnerAttempt: "the rat",
    forbiddenAnswer: "A RAT"
  },
  {
    name: "Gym boundary",
    dialogueTaskId: "article-gym-correct",
    zoneKind: "Gym",
    areaId: "GYM:ARTICLES:7",
    learnerAttempt: "a owl",
    requiresBlockedResponse: true
  }
];

for (const testCase of testCases) {
  const response = await callBuddy({
    schoolId,
    studentId,
    sessionId: `buddy-smoke-${Date.now()}`,
    dialogueTaskId: testCase.dialogueTaskId,
    learnerAttempt: testCase.learnerAttempt,
    trigger: "ask",
    zoneKind: testCase.zoneKind,
    areaId: testCase.areaId
  }, signIn.idToken);

  console.log(`${testCase.name} response: ${JSON.stringify(response, null, 2)}`);
  verifyResponse(testCase, response);
  console.log(`${testCase.name}: ${response.status} via ${response.provider || "unknown"} model=${response.model || ""} fallback=${response.fallbackReason || ""}`);
  await delay(3200);
}

console.log("Buddy smoke test passed.");

async function runCallContinuityTest() {
  const sessionId = `buddy-call-session-${Date.now()}`;
  const callId = `buddy-call-${Date.now()}`;
  const base = {
    schoolId,
    studentId,
    sessionId,
    callId,
    isCallTurn: true,
    dialogueTaskId: "welcome-greet",
    zoneKind: "Town",
    areaId: "TOWN:GREETINGSANDSURVIVALENGLISH:1",
    conceptId: "GreetingsAndSurvivalEnglish",
    grimoireExcerpt: "Use a short greeting when you meet someone.",
    safeRelationshipMemory: ["interest:pirates", "style:playful"]
  };

  const opening = await callBuddy({
    ...base,
    callTurnIndex: 1,
    learnerAttempt: "",
    trigger: "ask"
  }, signIn.idToken);
  if (opening.status !== "ok" || opening.callId !== callId || !opening.speechText)
    throw new Error(`Opening call turn failed: ${JSON.stringify(opening)}`);
  console.log(`Call turn 1: ${opening.speechText}`);

  await delay(4000);
  const followUp = await callBuddy({
    ...base,
    callTurnIndex: 2,
    learnerAttempt: "Can you explain that like a pirate?",
    trigger: "follow_up"
  }, signIn.idToken);
  if (followUp.status !== "ok" || followUp.callId !== callId || followUp.callTurnIndex !== 2 || !followUp.speechText)
    throw new Error(`Follow-up call turn failed: ${JSON.stringify(followUp)}`);
  console.log(`Call turn 2: ${followUp.speechText}`);
  console.log(`Call action: openGrimoire=${Boolean(followUp.openGrimoire)} concept=${followUp.grimoireConceptId || ""} disposition=${followUp.callDisposition}`);
  console.log("Buddy call continuity smoke test passed.");
}

async function runGrimoireAssistTest() {
  const callId = `buddy-grimoire-${Date.now()}`;
  const response = await callBuddy({
    schoolId,
    studentId,
    sessionId: `buddy-grimoire-session-${Date.now()}`,
    callId,
    callTurnIndex: 1,
    isCallTurn: true,
    dialogueTaskId: "article-a",
    learnerAttempt: "Why is an rat wrong? Please show me the rule in the grammar book.",
    trigger: "follow_up",
    zoneKind: "Town",
    areaId: "TOWN:ARTICLES:7",
    conceptId: "Articles",
    grimoireExcerpt: "Articles: [rule] Use a before consonant sounds, an before vowel sounds, and the for a specific noun. [example:0] a rat [example:1] an owl [goof:0] a owl -> an owl [goof:1] an rat -> a rat",
    safeRelationshipMemory: ["style:examples"]
  }, signIn.idToken);
  if (response.status !== "ok" || response.callId !== callId)
    throw new Error(`Grimoire assist call failed: ${JSON.stringify(response)}`);
  if (!response.openGrimoire || response.grimoireConceptId !== "Articles" || !response.grimoireHighlightKey)
    throw new Error(`Buddy did not return a validated Grimoire highlight: ${JSON.stringify(response)}`);
  console.log(`Grimoire assist: concept=${response.grimoireConceptId} highlight=${response.grimoireHighlightKey}`);
  console.log(`Buddy speech: ${response.speechText}`);
  console.log("Buddy Grimoire assist smoke test passed.");
}

async function signInWithPassword() {
  const response = await postJson(
    `https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=${encodeURIComponent(apiKey)}`,
    { email, password, returnSecureToken: true }
  );
  if (!response.idToken || !response.localId)
    throw new Error("Firebase Auth did not return an idToken and localId.");
  return response;
}

async function getUserProfile(uid, idToken) {
  const response = await fetch(firestoreDocUrl(`users/${uid}`), {
    headers: { Authorization: `Bearer ${idToken}` }
  });
  if (!response.ok)
    throw new Error(`Could not read the signed-in user profile: ${response.status} ${await response.text()}`);
  return response.json();
}

async function callBuddy(payload, idToken) {
  const response = await postJson(`${functionsBaseUrl}/requestBuddyHelp`, { data: payload }, idToken);
  if (!response.result)
    throw new Error(`requestBuddyHelp returned no result: ${JSON.stringify(response)}`);
  return response.result;
}

function verifyResponse(testCase, response) {
  const learnerText = normalize(response.learnerText || response.buddyResponse || "");
  if (!response.status)
    throw new Error(`${testCase.name} returned no status.`);
  if (testCase.requiresBlockedResponse) {
    if (response.status !== "blocked")
      throw new Error(`${testCase.name} was not blocked. status=${response.status}`);
    return;
  }
  if (response.status === "blocked")
    throw new Error(`${testCase.name} was unexpectedly blocked.`);
  if (!learnerText)
    throw new Error(`${testCase.name} returned no learner-facing help.`);
  if (testCase.forbiddenAnswer && learnerText.includes(normalize(testCase.forbiddenAnswer)))
    throw new Error(`${testCase.name} leaked its exact answer.`);
}

async function postJson(url, body, idToken = "") {
  const headers = { "Content-Type": "application/json" };
  if (idToken)
    headers.Authorization = `Bearer ${idToken}`;
  const response = await fetch(url, { method: "POST", headers, body: JSON.stringify(body) });
  if (!response.ok)
    throw new Error(`POST ${url} failed: ${response.status} ${await response.text()}`);
  return response.json();
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

function normalize(value) {
  return String(value).trim().toUpperCase().replace(/\s+/g, " ");
}

function stripSlash(value) {
  return String(value ?? "").replace(/\/+$/, "");
}

function delay(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}

async function readDotEnv(filePath) {
  const result = {};
  try {
    const text = await fs.readFile(filePath, "utf8");
    for (const rawLine of text.split(/\r?\n/)) {
      const line = rawLine.trim();
      if (!line || line.startsWith("#")) continue;
      const equalsIndex = line.indexOf("=");
      if (equalsIndex > 0)
        result[line.slice(0, equalsIndex).trim()] = line.slice(equalsIndex + 1).trim();
    }
  } catch {
    // The API key can also be supplied as a command argument.
  }
  return result;
}

function parseArgs(argv) {
  const parsed = {};
  for (const arg of argv) {
    if (!arg.startsWith("--")) continue;
    const equalsIndex = arg.indexOf("=");
    parsed[equalsIndex < 0 ? arg.slice(2) : arg.slice(2, equalsIndex)] =
      equalsIndex < 0 ? "true" : arg.slice(equalsIndex + 1);
  }
  return parsed;
}
