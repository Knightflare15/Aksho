import fs from "node:fs";
import path from "node:path";
import process from "node:process";
import { performance } from "node:perf_hooks";
import { fileURLToPath } from "node:url";
import * as speechsdk from "microsoft-cognitiveservices-speech-sdk";

const functionsDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const repoRoot = path.resolve(functionsDir, "..", "..");
const envPath = path.join(functionsDir, ".env");
const spellsDir = path.join(repoRoot, "Assets", "Audio", "Pronunciations", "Spells");

loadEnv(envPath);

const key = process.env.AZURE_SPEECH_KEY ?? "";
const region = process.env.AZURE_SPEECH_REGION ?? "centralindia";
const language = process.env.AZURE_SPEECH_LANGUAGE ?? "en-IN";
const limitArg = process.argv.find((arg) => arg.startsWith("--limit="));
const onlyArg = process.argv.find((arg) => arg.startsWith("--only="));
const languageArg = process.argv.find((arg) => arg.startsWith("--language="));
const alphabetArg = process.argv.find((arg) => arg.startsWith("--alphabet="));
const configArg = process.argv.find((arg) => arg.startsWith("--config="));
const dirArg = process.argv.find((arg) => arg.startsWith("--dir="));
const referencesArg = process.argv.find((arg) => arg.startsWith("--references="));
const maxAudioArg = process.argv.find((arg) => arg.startsWith("--max-audio-seconds="));
const limit = limitArg ? Number.parseInt(limitArg.slice("--limit=".length), 10) : 0;
const configMode = configArg ? configArg.slice("--config=".length).toLowerCase() : "setter";
const phonemeAlphabet = alphabetArg ? alphabetArg.slice("--alphabet=".length).toUpperCase() : "IPA";
const inputDir = dirArg ? path.resolve(dirArg.slice("--dir=".length)) : spellsDir;
const referenceMap = loadReferenceMap(referencesArg ? referencesArg.slice("--references=".length) : "");
const requestedMaxAudioSeconds = maxAudioArg ? Number.parseFloat(maxAudioArg.slice("--max-audio-seconds=".length)) : 3600;
const maxAudioSeconds = Math.min(3600, Math.max(0, Number.isFinite(requestedMaxAudioSeconds) ? requestedMaxAudioSeconds : 3600));
const only = onlyArg
  ? new Set(onlyArg.slice("--only=".length).split(",").map((word) => word.trim().toUpperCase()).filter(Boolean))
  : null;
if (languageArg) {
  process.env.AZURE_SPEECH_LANGUAGE = languageArg.slice("--language=".length);
}

if (!key || key.includes("replace-with")) {
  throw new Error(`Set AZURE_SPEECH_KEY in ${envPath}`);
}

let wavFiles = fs.readdirSync(inputDir)
  .filter((name) => name.toLowerCase().endsWith(".wav"))
  .sort((a, b) => a.localeCompare(b))
  .map((name) => path.join(inputDir, name));

if (only) {
  wavFiles = wavFiles.filter((file) => only.has(targetFromFile(file)));
}
if (limit > 0) {
  wavFiles = wavFiles.slice(0, limit);
}

let selectedAudioSeconds = 0;
wavFiles = wavFiles.filter((wavPath) => {
  const seconds = wavDurationSeconds(wavPath);
  if (selectedAudioSeconds + seconds > maxAudioSeconds) {
    return false;
  }
  selectedAudioSeconds += seconds;
  return true;
});

console.log(`Azure pronunciation smoke test`);
console.log(`Region: ${region}`);
console.log(`Language: ${process.env.AZURE_SPEECH_LANGUAGE ?? language}`);
console.log(`Alphabet: ${phonemeAlphabet}`);
console.log(`Config: ${configMode}`);
console.log(`Directory: ${inputDir}`);
console.log(`Files: ${wavFiles.length}`);
console.log(`Input audio: ${selectedAudioSeconds.toFixed(2)}s (cap: ${maxAudioSeconds.toFixed(2)}s; hard max: 3600s)`);
console.log("");
console.log(["WORD".padEnd(12), "SCORE".padStart(6), "MS".padStart(6), "AUDIO".padStart(6), "HEARD".padEnd(18), "SYLLABLES".padEnd(18), "WEAK"].join("  "));
console.log("-".repeat(102));

const startedAt = new Date().toISOString();
const runStartedMs = performance.now();
const results = [];
for (const wavPath of wavFiles) {
  const target = targetFromFile(wavPath);
  const callStartedMs = performance.now();
  try {
    const azure = await assessPronunciation(wavPath, target);
    const latencyMs = Math.round(performance.now() - callStartedMs);
    const summary = summarizeAzure(target, wavPath, azure);
    summary.latencyMs = latencyMs;
    results.push(summary);
    console.log(formatSummary(summary));
  } catch (error) {
    const latencyMs = Math.round(performance.now() - callStartedMs);
    const failed = {
      target,
      file: wavPath,
      ok: false,
      latencyMs,
      error: shortError(error)
    };
    results.push(failed);
    console.log([target.padEnd(12), "ERR".padStart(6), latencyMs.toString().padStart(6), "".padStart(6), "".padEnd(18), "".padEnd(18), failed.error].join("  "));
  }
}
const totalElapsedMs = Math.round(performance.now() - runStartedMs);
const successfulResults = results.filter((result) => result.ok);
const latency = stats(successfulResults.map((result) => result.latencyMs));
const audio = stats(successfulResults.map((result) => result.audioSeconds * 1000));

const report = {
  startedAt,
  completedAt: new Date().toISOString(),
  region,
  language,
  files: results.length,
  inputAudioSeconds: selectedAudioSeconds,
  maxAudioSeconds,
  failures: results.filter((result) => !result.ok).length,
  totalElapsedMs,
  latencyMs: latency,
  audioMs: audio,
  results
};
const reportPath = path.join(repoRoot, "Temp", `azure-pronunciation-spells-${Date.now()}.json`);
fs.mkdirSync(path.dirname(reportPath), { recursive: true });
fs.writeFileSync(reportPath, JSON.stringify(report, null, 2));
console.log("");
console.log(`Latency ms: avg=${latency.avg} p50=${latency.p50} p95=${latency.p95} min=${latency.min} max=${latency.max}`);
console.log(`Audio ms:   avg=${audio.avg} p50=${audio.p50} p95=${audio.p95} min=${audio.min} max=${audio.max}`);
console.log(`Total wall time: ${totalElapsedMs} ms`);
console.log(`Report: ${reportPath}`);

function loadEnv(filePath) {
  if (!fs.existsSync(filePath)) {
    return;
  }

  const text = fs.readFileSync(filePath, "utf8");
  for (const rawLine of text.split(/\r?\n/)) {
    const line = rawLine.trim();
    if (!line || line.startsWith("#")) {
      continue;
    }

    const equalsIndex = line.indexOf("=");
    if (equalsIndex <= 0) {
      continue;
    }

    const name = line.slice(0, equalsIndex).trim();
    let value = line.slice(equalsIndex + 1).trim();
    if ((value.startsWith("\"") && value.endsWith("\"")) ||
        (value.startsWith("'") && value.endsWith("'"))) {
      value = value.slice(1, -1);
    }
    process.env[name] ??= value;
  }
}

function targetFromFile(filePath) {
  const name = path.basename(filePath);
  const referenced = referenceMap[name.toLowerCase()];
  return (referenced || path.basename(filePath, path.extname(filePath)))
    .replace(/[_-]+/g, " ")
    .trim()
    .toUpperCase();
}

function loadReferenceMap(filePath) {
  if (!filePath) {
    return {};
  }

  const parsed = JSON.parse(fs.readFileSync(path.resolve(filePath), "utf8"));
  const map = {};
  for (const [fileName, referenceText] of Object.entries(parsed)) {
    if (typeof referenceText === "string" && referenceText.trim()) {
      map[path.basename(fileName).toLowerCase()] = referenceText.trim();
    }
  }
  return map;
}

function wavDurationSeconds(filePath) {
  const data = fs.readFileSync(filePath);
  if (data.subarray(0, 4).toString("ascii") !== "RIFF" || data.subarray(8, 12).toString("ascii") !== "WAVE") {
    throw new Error(`Expected RIFF WAV: ${filePath}`);
  }

  let channels = 0;
  let sampleRate = 0;
  let bitsPerSample = 0;
  let audioBytes = 0;
  for (let offset = 12; offset + 8 <= data.length;) {
    const id = data.subarray(offset, offset + 4).toString("ascii");
    const size = data.readUInt32LE(offset + 4);
    const contentOffset = offset + 8;
    if (id === "fmt " && contentOffset + 16 <= data.length) {
      channels = data.readUInt16LE(contentOffset + 2);
      sampleRate = data.readUInt32LE(contentOffset + 4);
      bitsPerSample = data.readUInt16LE(contentOffset + 14);
    } else if (id === "data") {
      audioBytes = size;
    }
    offset = contentOffset + size + (size % 2);
  }

  const bytesPerSecond = sampleRate * channels * (bitsPerSample / 8);
  if (!Number.isFinite(bytesPerSecond) || bytesPerSecond <= 0 || audioBytes <= 0) {
    throw new Error(`Could not determine WAV duration: ${filePath}`);
  }
  return audioBytes / bytesPerSecond;
}

async function assessPronunciation(wavPath, referenceText) {
  const speechConfig = speechsdk.SpeechConfig.fromSubscription(key, region);
  speechConfig.speechRecognitionLanguage = process.env.AZURE_SPEECH_LANGUAGE ?? language;
  const audioConfig = speechsdk.AudioConfig.fromWavFileInput(fs.readFileSync(wavPath));
  const pronunciationConfig = configMode === "json"
    ? speechsdk.PronunciationAssessmentConfig.fromJSON(JSON.stringify({
        referenceText,
        gradingSystem: "HundredMark",
        granularity: "Phoneme",
        phonemeAlphabet,
        nBestPhonemeCount: 5,
        enableMiscue: true
      }))
    : new speechsdk.PronunciationAssessmentConfig(
        referenceText,
        speechsdk.PronunciationAssessmentGradingSystem.HundredMark,
        speechsdk.PronunciationAssessmentGranularity.Phoneme,
        true
      );
  if (configMode !== "json") {
    pronunciationConfig.phonemeAlphabet = phonemeAlphabet;
    pronunciationConfig.nbestPhonemeCount = 5;
  }

  const recognizer = new speechsdk.SpeechRecognizer(speechConfig, audioConfig);
  pronunciationConfig.applyTo(recognizer);
  try {
    const result = await new Promise((resolve, reject) => {
      recognizer.recognizeOnceAsync(resolve, reject);
    });
    const rawJson = result.properties.getProperty(speechsdk.PropertyId.SpeechServiceResponse_JsonResult);
    if (!rawJson) {
      throw new Error(`No Azure JSON result. reason=${result.reason}`);
    }
    return JSON.parse(rawJson);
  } finally {
    recognizer.close();
  }
}

function summarizeAzure(target, wavPath, azure) {
  const best = azure.NBest?.[0] ?? {};
  const assessment = best.PronunciationAssessment ?? {};
  const word = best.Words?.[0] ?? {};
  const phonemes = word.Phonemes ?? [];
  const segments = phonemes.map((phoneme, index) => {
    const segmentAssessment = phoneme.PronunciationAssessment ?? {};
    const confidence = clamp01((segmentAssessment.AccuracyScore ?? 0) / 100);
    return {
      index,
      expected: phoneme.Phoneme ?? "",
      heard: segmentAssessment.NBestPhonemes?.[0]?.Phoneme ?? "",
      confidence,
      status: confidence >= 0.75 ? "Matched" : confidence >= 0.45 ? "NeedsPractice" : "Missing",
      nbest: segmentAssessment.NBestPhonemes ?? []
    };
  });

  return {
    target,
    file: wavPath,
    ok: true,
    displayText: azure.DisplayText ?? "",
    lexical: best.Lexical ?? "",
    audioSeconds: ((azure.Duration ?? 0) / 10000000),
    score: clamp01((assessment.PronScore ?? assessment.AccuracyScore ?? word.PronunciationAssessment?.AccuracyScore ?? 0) / 100),
    accuracyScore: clamp01((assessment.AccuracyScore ?? 0) / 100),
    syllables: (word.Syllables ?? []).map((syllable) => syllable.Syllable).filter(Boolean),
    expectedPhonemes: segments.map((segment) => segment.expected),
    heardPhonemes: segments.map((segment) => segment.heard),
    weakSegments: segments.filter((segment) => segment.status !== "Matched"),
    segments,
    raw: azure
  };
}

function formatSummary(summary) {
  const weak = summary.weakSegments
    .slice(0, 4)
    .map((segment) => `${segment.expected || "?"}->${segment.heard || "?"}:${Math.round(segment.confidence * 100)}`)
    .join(", ");
  return [
    summary.target.padEnd(12),
    Math.round(summary.score * 100).toString().padStart(6),
    summary.latencyMs.toString().padStart(6),
    summary.audioSeconds.toFixed(2).padStart(6),
    (summary.displayText || summary.lexical || "").slice(0, 18).padEnd(18),
    summary.syllables.join("|").slice(0, 18).padEnd(18),
    weak || "ok"
  ].join("  ");
}

function stats(values) {
  const sorted = values
    .filter((value) => Number.isFinite(value))
    .sort((a, b) => a - b);
  if (sorted.length === 0) {
    return { count: 0, avg: 0, p50: 0, p95: 0, min: 0, max: 0 };
  }
  const sum = sorted.reduce((total, value) => total + value, 0);
  return {
    count: sorted.length,
    avg: Math.round(sum / sorted.length),
    p50: Math.round(percentile(sorted, 0.5)),
    p95: Math.round(percentile(sorted, 0.95)),
    min: Math.round(sorted[0]),
    max: Math.round(sorted[sorted.length - 1])
  };
}

function percentile(sorted, ratio) {
  const index = (sorted.length - 1) * ratio;
  const lower = Math.floor(index);
  const upper = Math.ceil(index);
  if (lower === upper) {
    return sorted[lower];
  }
  const weight = index - lower;
  return sorted[lower] * (1 - weight) + sorted[upper] * weight;
}

function clamp01(value) {
  if (!Number.isFinite(value)) {
    return 0;
  }
  return Math.max(0, Math.min(1, value));
}

function shortError(error) {
  return error instanceof Error ? error.message.slice(0, 240) : String(error).slice(0, 240);
}
