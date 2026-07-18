import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import process from "node:process";
import { fileURLToPath } from "node:url";
import * as speechsdk from "microsoft-cognitiveservices-speech-sdk";

const functionsDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const repoRoot = path.resolve(functionsDir, "..", "..");
const envPath = path.join(functionsDir, ".env");
const spellsDir = path.join(repoRoot, "Assets", "Audio", "Pronunciations", "Spells");
const defaultWords = [
  "BAD", "CAN", "DAD", "DOT", "FIN",
  "GET", "HIT", "HOT", "KID", "MAD",
  "MAN", "MEN", "PET", "POP", "PUP",
  "RED", "SIT", "TAP", "TEN", "WET"
];

loadEnv(envPath);

const key = process.env.AZURE_SPEECH_KEY ?? "";
const region = process.env.AZURE_SPEECH_REGION ?? "centralindia";
const voice = process.env.AZURE_SPEECH_TTS_VOICE ?? "en-US-JennyNeural";
const wordsArg = process.argv.find((arg) => arg.startsWith("--words="));
const force = process.argv.includes("--force");
const words = wordsArg
  ? wordsArg.slice("--words=".length).split(",").map((word) => word.trim().toUpperCase()).filter(Boolean)
  : defaultWords;

if (!key || key.includes("replace-with")) {
  throw new Error(`Set AZURE_SPEECH_KEY in ${envPath}`);
}

fs.mkdirSync(spellsDir, { recursive: true });
console.log(`Generating ${words.length} pronunciation clips`);
console.log(`Region: ${region}`);
console.log(`Voice: ${voice}`);

for (const word of words) {
  const wavPath = path.join(spellsDir, `${word}.wav`);
  const metaPath = `${wavPath}.meta`;
  if (!force && fs.existsSync(wavPath)) {
    console.log(`${word}: exists`);
    ensureMeta(metaPath);
    continue;
  }

  await synthesizeWord(word, wavPath);
  ensureMeta(metaPath);
  console.log(`${word}: wrote ${path.relative(repoRoot, wavPath)}`);
}

async function synthesizeWord(word, wavPath) {
  const speechConfig = speechsdk.SpeechConfig.fromSubscription(key, region);
  speechConfig.speechSynthesisVoiceName = voice;
  speechConfig.speechSynthesisOutputFormat = speechsdk.SpeechSynthesisOutputFormat.Riff16Khz16BitMonoPcm;

  const audioConfig = speechsdk.AudioConfig.fromAudioFileOutput(wavPath);
  const synthesizer = new speechsdk.SpeechSynthesizer(speechConfig, audioConfig);
  try {
    const result = await new Promise((resolve, reject) => {
      synthesizer.speakTextAsync(word, resolve, reject);
    });
    if (result.reason !== speechsdk.ResultReason.SynthesizingAudioCompleted) {
      throw new Error(`TTS failed for ${word}: reason=${result.reason} ${result.errorDetails ?? ""}`.trim());
    }
  } finally {
    synthesizer.close();
  }
}

function ensureMeta(metaPath) {
  if (fs.existsSync(metaPath)) {
    return;
  }

  const guid = crypto.randomBytes(16).toString("hex");
  fs.writeFileSync(metaPath, unityAudioMeta(guid));
}

function unityAudioMeta(guid) {
  return `fileFormatVersion: 2
guid: ${guid}
AudioImporter:
  externalObjects: {}
  serializedVersion: 8
  defaultSettings:
    serializedVersion: 2
    loadType: 0
    sampleRateSetting: 0
    sampleRateOverride: 44100
    compressionFormat: 1
    quality: 1
    conversionMode: 0
    preloadAudioData: 0
  platformSettingOverrides: {}
  forceToMono: 0
  normalize: 1
  loadInBackground: 0
  ambisonic: 0
  3D: 1
  userData: 
  assetBundleName: 
  assetBundleVariant: 
`;
}

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
