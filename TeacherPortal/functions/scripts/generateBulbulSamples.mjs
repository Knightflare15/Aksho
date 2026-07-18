import { execFileSync } from "node:child_process";
import { mkdir, writeFile } from "node:fs/promises";
import path from "node:path";
import process from "node:process";

const projectId = process.env.FIREBASE_PROJECT_ID || "the-script-dea4f";
const apiKey = process.env.SARVAM_API_KEY || readFirebaseSecret();
const modelVersion = parseModelVersion();
const model = `bulbul:${modelVersion}`;
const outputDir = path.resolve(process.cwd(), "tmp", modelVersion === "v3" ? "bulbul-v3-samples" : "bulbul-samples");

const baseSamples = [
  {
    slug: "01-article-clue",
    text: "अच्छा try! Owl starts with a vowel sound, so we say an owl. अब तुम बोलो: an owl."
  },
  {
    slug: "02-gentle-correction",
    text: "Nice effort! Rat starts with a rrr sound, so use a rat. छोटा सा rule याद रखो: sound pe focus."
  },
  {
    slug: "03-game-buddy",
    text: "Route pe answer mat guess karo. Pehle first sound suno, फिर article choose karo. You are close."
  },
  {
    slug: "04-call-turn",
    text: "हाँ, space example lete hain. An astronaut, because astronaut starts with a vowel sound."
  }
];

const speakersByModel = {
  v2: ["anushka", "vidya", "abhilash", "karun"],
  v3: ["shubh", "ritu", "rahul", "kavya"]
};

const samples = baseSamples.map((sample, index) => ({
  ...sample,
  slug: `${sample.slug.replace(/^(\d+)-/, `$1-${modelVersion}-`)}`,
  speaker: speakersByModel[modelVersion][index]
}));

await mkdir(outputDir, { recursive: true });

let totalChars = 0;
for (const sample of samples) {
  totalChars += sample.text.length;
  const startedAt = Date.now();
  const response = await fetch("https://api.sarvam.ai/text-to-speech", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "api-subscription-key": apiKey
    },
    body: JSON.stringify(buildPayload(sample))
  });

  const raw = await response.text();
  if (!response.ok) {
    throw new Error(`${sample.slug}: HTTP ${response.status} ${raw.slice(0, 500)}`);
  }

  const payload = JSON.parse(raw);
  const audioBase64 = payload.audios?.[0];
  if (!audioBase64) {
    throw new Error(`${sample.slug}: Sarvam returned no audio.`);
  }

  const wavPath = path.join(outputDir, `${sample.slug}.wav`);
  await writeFile(wavPath, Buffer.from(audioBase64, "base64"));
  console.log(`${sample.slug}.wav model=${model} speaker=${sample.speaker} chars=${sample.text.length} latencyMs=${Date.now() - startedAt}`);
  console.log(`  text: ${sample.text}`);
}

const priceInrPer10kChars = modelVersion === "v3" ? 30 : 15;
const estimatedInr = (totalChars / 10000) * priceInrPer10kChars;
console.log(`\nWrote ${samples.length} samples to ${outputDir}`);
console.log(`Total chars=${totalChars}; estimated Bulbul ${modelVersion} cost=INR ${estimatedInr.toFixed(2)} before taxes/discounts.`);

function buildPayload(sample) {
  const payload = {
    text: sample.text,
    target_language_code: "hi-IN",
    speaker: sample.speaker,
    pace: 0.92,
    speech_sample_rate: modelVersion === "v3" ? 24000 : 22050,
    model,
    output_audio_codec: "wav"
  };

  if (modelVersion === "v2") {
    return {
      ...payload,
      pitch: 0,
      loudness: 1,
      enable_preprocessing: true,
      enable_cached_responses: true
    };
  }

  return {
    ...payload,
    temperature: 0.55
  };
}

function parseModelVersion() {
  const arg = process.argv.find(value => value.startsWith("--model="));
  const value = (arg?.split("=")[1] || process.env.BULBUL_MODEL || "v2").trim().toLowerCase();
  if (value === "v2" || value === "bulbul:v2") {
    return "v2";
  }
  if (value === "v3" || value === "bulbul:v3") {
    return "v3";
  }
  throw new Error(`Unsupported Bulbul model "${value}". Use --model=v2 or --model=v3.`);
}

function readFirebaseSecret() {
  const command = process.platform === "win32" ? (process.env.ComSpec || "cmd.exe") : "npx";
  const args = process.platform === "win32"
    ? ["/d", "/s", "/c", `npx firebase-tools functions:secrets:access SARVAM_API_KEY --project ${projectId}`]
    : ["firebase-tools", "functions:secrets:access", "SARVAM_API_KEY", "--project", projectId];
  const value = execFileSync(command, args, {
    encoding: "utf8",
    stdio: ["ignore", "pipe", "inherit"]
  }).trim();
  if (!value) {
    throw new Error("Firebase returned an empty SARVAM_API_KEY secret.");
  }
  return value;
}
