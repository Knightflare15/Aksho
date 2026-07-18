import assert from "node:assert/strict";
import test from "node:test";
import {
  assessPronunciationWithAzureRest,
  buildPronunciationAssessmentHeader,
  inspectPcmWav,
  normalizeAzurePronunciationAssessment,
} from "../azurePronunciationRest.js";

test("pronunciation header keeps prosody off and enables phoneme detail", () => {
  const decoded = JSON.parse(Buffer.from(buildPronunciationAssessmentHeader("cat"), "base64").toString("utf8"));
  assert.equal(decoded.ReferenceText, "cat");
  assert.equal(decoded.Granularity, "Phoneme");
  assert.equal(decoded.PhonemeAlphabet, "IPA");
  assert.equal(decoded.NBestPhonemeCount, 5);
  assert.equal("EnableProsodyAssessment" in decoded, false);
});

test("accepts only the 16 kHz mono PCM format used by Unity", () => {
  const wav = pcmWav(16000, 1, 1600);
  assert.equal(inspectPcmWav(wav).durationSeconds, 0.1);
  assert.throws(() => inspectPcmWav(pcmWav(24000, 1, 2400)), /16 kHz/);
  assert.throws(() => inspectPcmWav(pcmWav(16000, 2, 1600)), /mono/);
});

test("normalizes direct REST scores and nested SDK-era scores", () => {
  assert.deepEqual(normalizeAzurePronunciationAssessment({
    AccuracyScore: 81,
    PronScore: 76,
    NBestPhonemes: [{ Phoneme: "k", Score: 92 }],
  }), {
    AccuracyScore: 81,
    FluencyScore: undefined,
    CompletenessScore: undefined,
    PronScore: 76,
    ProsodyScore: undefined,
    ErrorType: undefined,
    NBestPhonemes: [{ Phoneme: "k", Score: 92 }],
  });
  const nested = normalizeAzurePronunciationAssessment({
    AccuracyScore: 10,
    PronunciationAssessment: { AccuracyScore: 88, ErrorType: "None" },
  });
  assert.equal(nested.AccuracyScore, 88);
  assert.equal(nested.ErrorType, "None");
});

test("retries a throttled REST request without mixing request headers or audio", async () => {
  let calls = 0;
  const wav = pcmWav(16000, 1, 1600);
  const result = await assessPronunciationWithAzureRest(wav, {
    subscriptionKey: "secret",
    region: "centralindia",
    language: "en-IN",
    referenceText: "cat",
    timeoutMs: 2000,
    maximumAttempts: 2,
    random: () => 0,
    fetchImpl: async (_url, init) => {
      calls++;
      assert.equal(new Headers(init?.headers).get("ocp-apim-subscription-key"), "secret");
      assert.equal((init?.body as Uint8Array).byteLength, wav.byteLength);
      return calls === 1
        ? new Response("", { status: 429, headers: { "retry-after": "0" } })
        : Response.json({ RecognitionStatus: "Success" });
    },
  });
  assert.equal(calls, 2);
  assert.equal(result.RecognitionStatus, "Success");
});

function pcmWav(sampleRate: number, channels: number, samplesPerChannel: number): Uint8Array {
  const pcmBytes = samplesPerChannel * channels * 2;
  const wav = Buffer.alloc(44 + pcmBytes);
  wav.write("RIFF", 0, 4, "ascii");
  wav.writeUInt32LE(36 + pcmBytes, 4);
  wav.write("WAVE", 8, 4, "ascii");
  wav.write("fmt ", 12, 4, "ascii");
  wav.writeUInt32LE(16, 16);
  wav.writeUInt16LE(1, 20);
  wav.writeUInt16LE(channels, 22);
  wav.writeUInt32LE(sampleRate, 24);
  wav.writeUInt32LE(sampleRate * channels * 2, 28);
  wav.writeUInt16LE(channels * 2, 32);
  wav.writeUInt16LE(16, 34);
  wav.write("data", 36, 4, "ascii");
  wav.writeUInt32LE(pcmBytes, 40);
  return wav;
}
