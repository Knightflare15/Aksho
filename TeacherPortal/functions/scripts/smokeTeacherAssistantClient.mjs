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
const question = args.question ?? "Which learners need the most support this week, and what should I do next?";
const dryRun = args.dryRun !== "false";
const quiet = args.quiet === "true";
const realClassroomApproval = args.approvePrivateGeminiEvidence === "I_UNDERSTAND_PRIVATE_EDUCATIONAL_EVIDENCE_GOES_TO_GEMINI";

if (!apiKey)
  throw new Error("Missing VITE_FIREBASE_API_KEY in TeacherPortal/.env.local.");
if (!dryRun && !realClassroomApproval) {
  console.error([
    "Refusing to run the live Gemini efficacy path against real classroom evidence.",
    "This sends bounded private educational evidence for the selected class/student to Gemini.",
    "Use --dryRun=true for the safe deterministic smoke test.",
    "To run the live path after explicit approval, pass:",
    "--dryRun=false --approvePrivateGeminiEvidence=I_UNDERSTAND_PRIVATE_EDUCATIONAL_EVIDENCE_GOES_TO_GEMINI"
  ].join("\n"));
  process.exit(2);
}

console.log("Teacher Assistant smoke test");
console.log(`Project: ${projectId}`);
console.log(`Functions: ${functionsBaseUrl}`);
console.log(`Mode: ${dryRun ? "dry-run no-model" : "LIVE Gemini with explicit private-evidence approval"}`);

let signIn = await signInWithPassword();
const profile = await getUserProfile(signIn.localId, signIn.idToken);
const role = stringField(profile, "role");
const schoolId = stringField(profile, "schoolId");
const classId = arrayFirst(profile, "classIds");

if (role !== "teacher" && role !== "admin")
  throw new Error(`Signed-in user must be a teacher/admin. role=${role || "missing"}`);
if (!schoolId || !classId)
  throw new Error("Signed-in teacher profile is missing schoolId or classIds[0].");

console.log(`Signed in role=${role} school=${schoolId} class=${classId}`);
const refreshed = await refreshTeacherClaims(signIn.idToken);
if (refreshed.ok) {
  signIn = await refreshIdToken(signIn.refreshToken);
  console.log("Teacher claims refreshed.");
}
const classResponse = await callTeacherAssistant({ schoolId, classId, question, dryRun }, signIn.idToken);
verifyAssistantResponse("class", classResponse);
printAssistantResponse("Class", classResponse);

const firstStudentId = await findFirstStudentId(schoolId, classId, signIn.idToken);
if (firstStudentId) {
  const studentResponse = await callTeacherAssistant({
    schoolId,
    classId,
    studentId: firstStudentId,
    question: "Summarize this learner's strongest evidence and one next teaching action.",
    dryRun
  }, signIn.idToken);
  verifyAssistantResponse("student", studentResponse);
  printAssistantResponse("Student", studentResponse);
} else {
  console.log("No student found for student-scoped smoke; class-scoped smoke passed.");
}

console.log("Teacher Assistant smoke test passed.");

async function signInWithPassword() {
  const response = await postJson(
    `https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=${encodeURIComponent(apiKey)}`,
    { email, password, returnSecureToken: true }
  );
  if (!response.idToken || !response.localId)
    throw new Error("Firebase Auth did not return an idToken and localId.");
  return response;
}

async function refreshIdToken(refreshToken) {
  if (!refreshToken)
    throw new Error("Firebase Auth did not return a refresh token.");
  const response = await postForm(
    `https://securetoken.googleapis.com/v1/token?key=${encodeURIComponent(apiKey)}`,
    { grant_type: "refresh_token", refresh_token: refreshToken }
  );
  const idToken = response.id_token ?? response.idToken;
  const localId = response.user_id ?? response.localId;
  if (!idToken || !localId)
    throw new Error("Firebase Auth did not return an idToken for the refreshed teacher session.");
  return { ...response, idToken, localId, refreshToken: response.refresh_token ?? refreshToken };
}

async function refreshTeacherClaims(idToken) {
  const response = await postJson(`${functionsBaseUrl}/refreshTeacherClaims`, { data: {} }, idToken);
  if (!response.result?.ok)
    throw new Error(`refreshTeacherClaims failed: ${JSON.stringify(response)}`);
  return response.result;
}

async function getUserProfile(uid, idToken) {
  const response = await fetch(firestoreDocUrl(`users/${uid}`), {
    headers: { Authorization: `Bearer ${idToken}` }
  });
  if (!response.ok)
    throw new Error(`Could not read signed-in user profile: ${response.status} ${await response.text()}`);
  return response.json();
}

async function findFirstStudentId(schoolId, classId, idToken) {
  const body = {
    structuredQuery: {
      from: [{ collectionId: "students" }],
      where: {
        fieldFilter: {
          field: { fieldPath: "classId" },
          op: "EQUAL",
          value: { stringValue: classId }
        }
      },
      limit: 1
    }
  };
  const response = await postJson(
    `https://firestore.googleapis.com/v1/projects/${encodeURIComponent(projectId)}/databases/(default)/documents/schools/${encodeURIComponent(schoolId)}:runQuery`,
    body,
    idToken
  );
  const row = Array.isArray(response) ? response.find(item => item.document?.name) : undefined;
  const name = row?.document?.name ?? "";
  return name ? name.split("/").pop() : "";
}

async function callTeacherAssistant(payload, idToken) {
  const response = await postJson(`${functionsBaseUrl}/askTeacherAssistant`, { data: payload }, idToken);
  if (!response.result)
    throw new Error(`askTeacherAssistant returned no result: ${JSON.stringify(response)}`);
  return response.result;
}

function verifyAssistantResponse(scope, response) {
  if (!response.answer || response.answer.length < 40)
    throw new Error(`${scope} assistant response has no substantive answer: ${JSON.stringify(response)}`);
  if (!Array.isArray(response.suggestedActions) || response.suggestedActions.length === 0)
    throw new Error(`${scope} assistant response has no suggested actions.`);
  if (!Array.isArray(response.citations) || response.citations.length === 0)
    throw new Error(`${scope} assistant response has no citations.`);
  if (!Array.isArray(response.agentTrace) || response.agentTrace.length < 3)
    throw new Error(`${scope} assistant response has no usable agent trace.`);
}

function printAssistantResponse(label, response) {
  console.log(`${label} model=${response.model || "unknown"} fallback=${response.fallbackReason || ""}`);
  if (dryRun || quiet) {
    console.log(`${label} answerChars=${String(response.answer).length}`);
    console.log(`${label} actionCount=${(response.suggestedActions ?? []).length}`);
    console.log(`${label} citationCount=${(response.citations ?? []).length}`);
    console.log(`${label} traceCount=${(response.agentTrace ?? []).length}`);
    if (response.usage)
      console.log(`${label} usage=${JSON.stringify(response.usage)}`);
    return;
  }
  console.log(`${label} answer=${String(response.answer).slice(0, 700)}`);
  console.log(`${label} actions=${JSON.stringify((response.suggestedActions ?? []).slice(0, 4))}`);
  console.log(`${label} citations=${JSON.stringify((response.citations ?? []).slice(0, 6))}`);
  console.log(`${label} trace=${JSON.stringify((response.agentTrace ?? []).slice(0, 5))}`);
}

async function postJson(url, body, idToken = "") {
  const headers = { "Content-Type": "application/json" };
  if (idToken)
    headers.Authorization = `Bearer ${idToken}`;
  const response = await fetch(url, { method: "POST", headers, body: JSON.stringify(body) });
  const raw = await response.text();
  if (!response.ok)
    throw new Error(`POST ${url} failed: ${response.status} ${raw.slice(0, 1200)}`);
  return JSON.parse(raw);
}

async function postForm(url, body) {
  const form = new URLSearchParams(body);
  const response = await fetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: form.toString()
  });
  const raw = await response.text();
  if (!response.ok)
    throw new Error(`POST ${url} failed: ${response.status} ${raw.slice(0, 1200)}`);
  return JSON.parse(raw);
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

function stripSlash(value) {
  return String(value ?? "").replace(/\/+$/, "");
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
    // Arguments can also supply everything needed.
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
