import { spawn } from "node:child_process";
import fs from "node:fs/promises";
import path from "node:path";
import process from "node:process";
import { fileURLToPath } from "node:url";
import { initializeApp } from "firebase-admin/app";
import { FieldValue, getFirestore } from "firebase-admin/firestore";
import { getStorage } from "firebase-admin/storage";

const functionsDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const repoRoot = path.resolve(functionsDir, "..", "..");
const realisticDir = path.join(repoRoot, "Assets", "Audio", "Pronunciations", "Realistic");
const args = parseArgs(process.argv.slice(2));

const projectId = args.projectId ?? "the-script-dea4f";
const bucketName = args.bucket ?? `${projectId}.firebasestorage.app`;
const schoolId = args.schoolId ?? "smoke-school";
const classId = args.classId ?? "smoke-class";
const studentId = args.studentId ?? "smoke-student";
const limit = Number.parseInt(args.limit ?? "3", 10);
const only = args.only
  ? new Set(args.only.split(",").map((value) => normalizeTarget(value)).filter(Boolean))
  : null;
const timeoutMs = Number.parseInt(args.timeoutMs ?? "150000", 10);

initializeApp({ projectId, storageBucket: bucketName });

const db = getFirestore();
const bucket = getStorage().bucket(bucketName);

const selectedFiles = await selectRealisticFiles();
if (selectedFiles.length === 0) {
  throw new Error(`No realistic .m4a files matched in ${realisticDir}`);
}

console.log("Realistic pronunciation cloud smoke test");
console.log(`Project: ${projectId}`);
console.log(`Bucket: ${bucketName}`);
console.log(`Student path: schools/${schoolId}/students/${studentId}`);
console.log(`Files: ${selectedFiles.length}`);
console.log("");

await ensureSmokeStudent();

const runId = `realistic-smoke-${new Date().toISOString().replace(/[^0-9]/g, "").slice(0, 14)}`;
const results = [];
for (const sourcePath of selectedFiles) {
  const targetText = targetFromFile(sourcePath);
  const jobId = `${runId}-${targetText.toLowerCase()}`;
  const sourceRecordId = jobId;
  const objectName = `schools/${schoolId}/students/${studentId}/pronunciationAudio/${jobId}.wav`;
  const wavPath = path.join(process.env.TMP ?? process.env.TEMP ?? functionsDir, `${jobId}.wav`);

  console.log(`${targetText}: converting ${path.basename(sourcePath)} -> wav`);
  await convertToWav(sourcePath, wavPath);
  const wavBytes = await fs.readFile(wavPath);

  console.log(`${targetText}: uploading ${wavBytes.length} bytes`);
  await bucket.file(objectName).save(wavBytes, {
    contentType: "audio/wav",
    resumable: false,
    metadata: {
      cacheControl: "no-store",
      metadata: {
        smokeTest: "true",
        targetText
      }
    }
  });

  const nowIso = new Date().toISOString();
  const audioStoragePath = `gs://${bucketName}/${objectName}`;
  const sourceRef = db.doc(`schools/${schoolId}/students/${studentId}/wordCastEvents/${sourceRecordId}`);
  const jobRef = db.doc(`schools/${schoolId}/students/${studentId}/analysisJobs/${jobId}`);

  await sourceRef.set({
    id: sourceRecordId,
    schoolId,
    classId,
    studentId,
    word: targetText,
    targetText,
    sourceAudioFile: path.relative(repoRoot, sourcePath).replace(/\\/g, "/"),
    audioStoragePath,
    serverAnalysisJobId: jobId,
    serverAnalysisStatus: "pending",
    serverAnalysisRequestedAtUtc: nowIso,
    smokeTest: true,
    createdAtUtc: nowIso,
    createdAt: FieldValue.serverTimestamp(),
    updatedAt: FieldValue.serverTimestamp()
  }, { merge: true });

  await jobRef.create({
    jobId,
    schoolId,
    classId,
    studentId,
    missionId: "realistic-smoke",
    analysisKind: "pronunciation",
    status: "pending",
    sourceCollection: "wordCastEvents",
    sourceRecordId,
    targetText,
    audioStoragePath,
    analysisMode: "HybridServerAssist",
    onDeviceAnalysisProvider: "smoke-realistic",
    smokeTest: true,
    createdAtUtc: nowIso,
    createdAt: FieldValue.serverTimestamp(),
    updatedAt: FieldValue.serverTimestamp()
  });

  console.log(`${targetText}: waiting for deployed dispatchAnalysisJob`);
  const result = await waitForJob(jobRef);
  results.push({ targetText, jobId, ...result });
  printResult(targetText, jobId, result);

  await fs.unlink(wavPath).catch(() => undefined);
}

console.log("");
console.log("Summary");
for (const result of results) {
  const score = result.score == null ? "" : ` score=${result.score}`;
  console.log(`${result.targetText}: ${result.status}${score} job=${result.jobId}`);
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

  return m4aFiles.slice(0, Math.max(1, Number.isFinite(limit) ? limit : 3));
}

async function ensureSmokeStudent() {
  await db.doc(`schools/${schoolId}`).set({
    id: schoolId,
    name: "Smoke Test School",
    smokeTest: true,
    updatedAt: FieldValue.serverTimestamp(),
    createdAt: FieldValue.serverTimestamp()
  }, { merge: true });

  await db.doc(`schools/${schoolId}/classes/${classId}`).set({
    id: classId,
    schoolId,
    name: "Smoke Test Class",
    studentIds: FieldValue.arrayUnion(studentId),
    smokeTest: true,
    updatedAt: FieldValue.serverTimestamp(),
    createdAt: FieldValue.serverTimestamp()
  }, { merge: true });

  await db.doc(`schools/${schoolId}/students/${studentId}`).set({
    id: studentId,
    schoolId,
    classId,
    name: "Smoke Test Student",
    smokeTest: true,
    updatedAt: FieldValue.serverTimestamp(),
    createdAt: FieldValue.serverTimestamp()
  }, { merge: true });
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

async function waitForJob(jobRef) {
  const started = Date.now();
  while (Date.now() - started < timeoutMs) {
    await delay(2500);
    const snap = await jobRef.get();
    if (!snap.exists) {
      continue;
    }

    const data = snap.data() ?? {};
    if (data.status === "complete" || data.status === "failed") {
      const insight = data.result?.serverPronunciationInsight ?? {};
      const azure = data.result?.azureResult ?? {};
      return {
        status: String(data.status),
        score: numberOrNull(insight.overallScore ?? azure.NBest?.[0]?.PronunciationAssessment?.PronScore),
        recognizedText: String(insight.recognizedText ?? azure.DisplayText ?? ""),
        error: String(data.error ?? "")
      };
    }

    process.stdout.write(".");
  }

  throw new Error(`Timed out after ${timeoutMs}ms waiting for ${jobRef.path}`);
}

function printResult(targetText, jobId, result) {
  const score = result.score == null ? "n/a" : result.score.toFixed(1);
  console.log("");
  console.log(`${targetText}: ${result.status.toUpperCase()} score=${score} heard="${result.recognizedText}" job=${jobId}`);
  if (result.error) {
    console.log(`${targetText}: error=${result.error}`);
  }
}

function targetFromFile(filePath) {
  return normalizeTarget(path.basename(filePath, path.extname(filePath)));
}

function normalizeTarget(value) {
  return String(value ?? "").trim().toUpperCase();
}

function numberOrNull(value) {
  const number = Number(value);
  return Number.isFinite(number) ? number : null;
}

function delay(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function parseArgs(argv) {
  const parsed = {};
  for (const arg of argv) {
    if (!arg.startsWith("--")) {
      continue;
    }

    const equalsIndex = arg.indexOf("=");
    if (equalsIndex < 0) {
      parsed[arg.slice(2)] = "true";
    } else {
      parsed[arg.slice(2, equalsIndex)] = arg.slice(equalsIndex + 1);
    }
  }

  return parsed;
}
