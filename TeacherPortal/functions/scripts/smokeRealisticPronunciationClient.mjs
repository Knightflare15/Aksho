import { spawn } from "node:child_process";
import fs from "node:fs/promises";
import path from "node:path";
import process from "node:process";
import { fileURLToPath } from "node:url";

const functionsDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const portalDir = path.resolve(functionsDir, "..");
const repoRoot = path.resolve(portalDir, "..");
const realisticDir = path.join(repoRoot, "Assets", "Audio", "Pronunciations", "Realistic");
const args = parseArgs(process.argv.slice(2));
const env = await readDotEnv(path.join(portalDir, ".env.local"));

const apiKey = args.apiKey ?? env.VITE_FIREBASE_API_KEY;
const projectId = args.projectId ?? env.VITE_FIREBASE_PROJECT_ID ?? "the-script-dea4f";
const bucketName = args.bucket ?? env.VITE_FIREBASE_STORAGE_BUCKET ?? `${projectId}.firebasestorage.app`;
const functionsBaseUrl = stripSlash(args.functionsBaseUrl ?? `https://us-central1-${projectId}.cloudfunctions.net`);
const email = args.email ?? "aryan.raj@axxela.in";
const password = args.password ?? "12345678";
const limit = Number.parseInt(args.limit ?? "2", 10);
const only = args.only
  ? new Set(args.only.split(",").map((value) => normalizeTarget(value)).filter(Boolean))
  : null;
const timeoutMs = Number.parseInt(args.timeoutMs ?? "150000", 10);

if (!apiKey) {
  throw new Error("Missing Firebase API key. Set VITE_FIREBASE_API_KEY in TeacherPortal/.env.local or pass --apiKey=...");
}

const selectedFiles = await selectRealisticFiles();
if (selectedFiles.length === 0) {
  throw new Error(`No realistic .m4a files matched in ${realisticDir}`);
}

console.log("Realistic pronunciation client smoke test");
console.log(`Project: ${projectId}`);
console.log(`Bucket: ${bucketName}`);
console.log(`Functions: ${functionsBaseUrl}`);
console.log(`Email: ${email}`);
console.log(`Files: ${selectedFiles.length}`);
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

const runId = args.runId ?? `realistic-client-${new Date().toISOString().replace(/[^0-9]/g, "").slice(0, 14)}`;
const results = [];
for (const sourcePath of selectedFiles) {
  const targetText = targetFromFile(sourcePath);
  const eventId = `${runId}-${targetText.toLowerCase()}`;
  const objectName = `schools/${schoolId}/students/${studentId}/pronunciationAudio/${eventId}.wav`;
  const wavPath = path.join(process.env.TMP ?? process.env.TEMP ?? functionsDir, `${eventId}.wav`);

  console.log(`${targetText}: converting ${path.basename(sourcePath)} -> wav`);
  await convertToWav(sourcePath, wavPath);
  const wavBytes = await fs.readFile(wavPath);

  console.log(`${targetText}: uploading as logged-in student`);
  await uploadWav(objectName, wavBytes, signIn.idToken);

  const audioStoragePath = `gs://${bucketName}/${objectName}`;
  console.log(`${targetText}: calling submitWordCast`);
  await callSubmitWordCast({
    eventId,
    schoolId,
    classId,
    studentId,
    missionId: "realistic-client-smoke",
    word: targetText,
    success: true,
    responseSeconds: 1,
    audioStoragePath,
    audioContentType: "audio/wav",
    rawAudioUploaded: true,
    serverAnalysisJobId: eventId,
    serverAnalysisStatus: "pending",
    serverAnalysisRequestedAtUtc: new Date().toISOString(),
    onDeviceAnalysisProvider: "smoke-realistic-client",
    analysisMode: "HybridServerAssist",
    createdAtUtc: new Date().toISOString(),
    smokeTest: true
  }, signIn.idToken);

  console.log(`${targetText}: waiting for analysis job`);
  const result = await waitForJob(schoolId, studentId, eventId, signIn.idToken);
  results.push({ targetText, eventId, ...result });
  printResult(targetText, eventId, result);

  await fs.unlink(wavPath).catch(() => undefined);
}

console.log("");
console.log("Summary");
for (const result of results) {
  const score = result.score == null ? "" : ` score=${result.score}`;
  console.log(`${result.targetText}: ${result.status}${score} event=${result.eventId}`);
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
  const url = firestoreDocUrl(`users/${uid}`);
  const response = await fetchWithRetry(url, {
    headers: { Authorization: `Bearer ${idToken}` }
  });
  if (!response.ok) {
    throw new Error(`Could not read users/${uid}: ${response.status} ${await response.text()}`);
  }
  return await response.json();
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

async function callSubmitWordCast(payload, idToken) {
  const response = await postJson(`${functionsBaseUrl}/submitWordCast`, { data: payload }, idToken);
  if (!response.result?.id) {
    throw new Error(`submitWordCast returned no id: ${JSON.stringify(response)}`);
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
  if (idToken) {
    headers.Authorization = `Bearer ${idToken}`;
  }

  const response = await fetchWithRetry(url, {
    method: "POST",
    headers,
    body: JSON.stringify(body)
  });
  if (!response.ok) {
    throw new Error(`POST ${url} failed: ${response.status} ${await response.text()}`);
  }
  return await response.json();
}

async function fetchWithRetry(url, options, attempts = 3) {
  let lastError = null;
  for (let attempt = 1; attempt <= attempts; attempt++) {
    try {
      return await fetch(url, options);
    } catch (error) {
      lastError = error;
      if (attempt === attempts) {
        break;
      }
      await delay(750 * attempt);
    }
  }

  throw lastError;
}

async function selectRealisticFiles() {
  const files = await fs.readdir(realisticDir);
  let m4aFiles = files
    .filter((name) => name.toLowerCase().endsWith(".m4a"))
    .map((name) => path.join(realisticDir, name))
    .sort((a, b) => a.localeCompare(b));

  if (only) {
    m4aFiles = m4aFiles.filter((file) => only.has(targetFromFile(file)));
  }

  return m4aFiles.slice(0, Math.max(1, Number.isFinite(limit) ? limit : 2));
}

function convertToWav(inputPath, outputPath) {
  return new Promise((resolve, reject) => {
    const child = spawn("ffmpeg", [
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

    let stderr = "";
    child.stderr.on("data", (chunk) => {
      stderr += chunk.toString();
    });
    child.on("error", reject);
    child.on("close", (code) => {
      if (code === 0) {
        resolve();
      } else {
        reject(new Error(`ffmpeg failed with exit code ${code}: ${stderr.trim()}`));
      }
    });
  });
}

function firestoreDocUrl(documentPath) {
  return `https://firestore.googleapis.com/v1/projects/${encodeURIComponent(projectId)}/databases/(default)/documents/${documentPath.split("/").map(encodeURIComponent).join("/")}`;
}

function printResult(targetText, eventId, result) {
  const score = result.score == null ? "n/a" : result.score.toFixed(1);
  console.log("");
  console.log(`${targetText}: ${result.status.toUpperCase()} score=${score} heard="${result.recognizedText}" event=${eventId}`);
  if (result.error) {
    console.log(`${targetText}: error=${result.error}`);
  }
}

function targetFromFile(filePath) {
  return normalizeTarget(path.basename(filePath, path.extname(filePath)));
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

function normalizeTarget(value) {
  return String(value ?? "").trim().toUpperCase();
}

function numberOrNull(value) {
  const number = Number(value);
  return Number.isFinite(number) ? number : null;
}

function stripSlash(value) {
  return String(value ?? "").replace(/\/+$/, "");
}

function delay(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
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
    if (equalsIndex < 0) {
      parsed[arg.slice(2)] = "true";
    } else {
      parsed[arg.slice(2, equalsIndex)] = arg.slice(equalsIndex + 1);
    }
  }
  return parsed;
}
