#!/usr/bin/env python3
"""Probe the checked-in Charsiu wav2vec2 phoneme model from the desktop.

This is intentionally a lab tool: it shows model phones and rough target
coverage so the pronunciation UI can be calibrated against actual audio.
"""

from __future__ import annotations

import argparse
import json
import math
import queue
import sys
import time
import wave
from dataclasses import asdict, dataclass
from datetime import datetime
from pathlib import Path

import numpy as np


SAMPLE_RATE = 16_000
DEFAULT_MIN_CONFIDENCE = 0.55
DEFAULT_MIN_PHONE_DURATION = 0.03
DEFAULT_FORCED_MIN_CONFIDENCE = 0.15
MIN_VOICED_SECONDS = 0.16

DEFAULT_LABELS = [
    "sil",
    "AA",
    "AE",
    "AH",
    "AO",
    "AW",
    "AY",
    "B",
    "CH",
    "D",
    "DH",
    "EH",
    "ER",
    "EY",
    "F",
    "G",
    "HH",
    "IH",
    "IY",
    "JH",
    "K",
    "L",
    "M",
    "N",
    "NG",
    "OW",
    "OY",
    "P",
    "R",
    "S",
    "SH",
    "T",
    "TH",
    "UH",
    "UW",
    "V",
    "W",
    "Y",
    "Z",
    "ZH",
    "SIL",
    "SPN",
]

DIGRAPHS = [
    "SH",
    "TH",
    "CH",
    "WH",
    "PH",
    "CK",
    "NG",
    "QU",
    "EE",
    "EA",
    "AI",
    "AY",
    "OA",
    "OO",
    "OW",
    "OU",
    "AR",
    "OR",
    "ER",
    "IR",
    "UR",
]

PHONE_CANDIDATES = {
    "SH": ["SH"],
    "TH": ["TH", "DH"],
    "CH": ["CH"],
    "WH": ["W", "HH"],
    "PH": ["F"],
    "CK": ["K"],
    "NG": ["NG"],
    "QU": ["K", "W"],
    "EE": ["IY"],
    "EA": ["IY", "EH"],
    "AI": ["EY"],
    "AY": ["EY"],
    "OA": ["OW"],
    "OO": ["UW", "UH"],
    "OW": ["AW", "OW"],
    "OU": ["AW", "OW"],
    "AR": ["AA", "R", "ER"],
    "OR": ["AO", "R", "ER"],
    "ER": ["ER"],
    "IR": ["ER", "IH", "R"],
    "UR": ["ER", "UH", "R"],
    "A": ["AE", "AA", "AH", "EY"],
    "B": ["B"],
    "C": ["K", "S"],
    "D": ["D"],
    "E": ["EH", "IY"],
    "F": ["F"],
    "G": ["G", "JH"],
    "H": ["HH"],
    "I": ["IH", "AY"],
    "J": ["JH"],
    "K": ["K"],
    "L": ["L"],
    "M": ["M"],
    "N": ["N"],
    "O": ["AO", "OW", "AA"],
    "P": ["P"],
    "Q": ["K"],
    "R": ["R"],
    "S": ["S", "Z"],
    "T": ["T"],
    "U": ["AH", "UH", "UW", "Y"],
    "V": ["V"],
    "W": ["W"],
    "X": ["K", "S"],
    "Y": ["Y", "IY", "AY"],
    "Z": ["Z"],
}

WORD_PHONES = {
    "ANT": ["AE", "N", "T"],
    "APPLE": ["AE", "P", "AH", "L"],
    "ARM": ["AA", "R"],
    "BAG": ["B", "AE", "G"],
    "BALL": ["B", "AO", "L"],
    "BARK": ["B", "AA", "R", "K"],
    "BAT": ["B", "AE", "T"],
    "BED": ["B", "EH", "D"],
    "BELL": ["B", "EH", "L"],
    "BIRD": ["B", "ER", "D"],
    "BOAT": ["B", "OW", "T"],
    "BOOK": ["B", "UH", "K"],
    "BOX": ["B", "AA", "K", "S"],
    "BROOM": ["B", "R", "UW", "M"],
    "BUG": ["B", "AH", "G"],
    "BUS": ["B", "AH", "S"],
    "CAP": ["K", "AE", "P"],
    "CAR": ["K", "AA", "R"],
    "CAT": ["K", "AE", "T"],
    "COW": ["K", "AW"],
    "CRAB": ["K", "R", "AE", "B"],
    "CUP": ["K", "AH", "P"],
    "DOG": ["D", "AO", "G"],
    "DOLL": ["D", "AA", "L"],
    "DOOR": ["D", "AO", "R"],
    "DRUM": ["D", "R", "AH", "M"],
    "DUCK": ["D", "AH", "K"],
    "EAR": ["IY", "R"],
    "EGG": ["EH", "G"],
    "EYE": ["AY"],
    "FAN": ["F", "AE", "N"],
    "FISH": ["F", "IH", "SH"],
    "FOX": ["F", "AA", "K", "S"],
    "GOAT": ["G", "OW", "T"],
    "GRAPES": ["G", "R", "EY", "P", "S"],
    "GUM": ["G", "AH", "M"],
    "HAT": ["HH", "AE", "T"],
    "HEN": ["HH", "EH", "N"],
    "HOP": ["HH", "AA", "P"],
    "ICE": ["AY", "S"],
    "INK": ["IH", "NG", "K"],
    "JAM": ["JH", "AE", "M"],
    "JAR": ["JH", "AA", "R"],
    "JUG": ["JH", "AH", "G"],
    "KEY": ["K", "IY"],
    "KING": ["K", "IH", "NG"],
    "KITE": ["K", "AY", "T"],
    "LEG": ["L", "EH", "G"],
    "LION": ["L", "AY", "AH", "N"],
    "LOG": ["L", "AO", "G"],
    "MAP": ["M", "AE", "P"],
    "MAT": ["M", "AE", "T"],
    "MOON": ["M", "UW", "N"],
    "MOP": ["M", "AA", "P"],
    "MUG": ["M", "AH", "G"],
    "NEST": ["N", "EH", "S", "T"],
    "NET": ["N", "EH", "T"],
    "NOSE": ["N", "OW", "Z"],
    "NUT": ["N", "AH", "T"],
    "OWL": ["AW", "L"],
    "OX": ["AA", "K", "S"],
    "PAN": ["P", "AE", "N"],
    "PEG": ["P", "EH", "G"],
    "PEN": ["P", "EH", "N"],
    "PIG": ["P", "IH", "G"],
    "PIN": ["P", "IH", "N"],
    "POT": ["P", "AA", "T"],
    "QUAIL": ["K", "W", "EY", "L"],
    "QUEEN": ["K", "W", "IY", "N"],
    "QUILT": ["K", "W", "IH", "L", "T"],
    "RAIN": ["R", "EY", "N"],
    "RAT": ["R", "AE", "T"],
    "RING": ["R", "IH", "NG"],
    "RUG": ["R", "AH", "G"],
    "SHIP": ["SH", "IH", "P"],
    "SOCK": ["S", "AA", "K"],
    "SPOON": ["S", "P", "UW", "N"],
    "STAR": ["S", "T", "AA", "R"],
    "STONE": ["S", "T", "OW", "N"],
    "SUN": ["S", "AH", "N"],
    "THIN": ["TH", "IH", "N"],
    "TIGER": ["T", "AY", "G", "ER"],
    "TOP": ["T", "AA", "P"],
    "TOY": ["T", "OY"],
    "TREE": ["T", "R", "IY"],
    "UMBRELLA": ["AH", "M", "B", "R", "EH", "L", "AH"],
    "UNICORN": ["Y", "UW", "N", "IH", "K", "AO", "R", "N"],
    "UP": ["AH", "P"],
    "VAN": ["V", "AE", "N"],
    "VASE": ["V", "EY", "S"],
    "WALL": ["W", "AO", "L"],
    "WATCH": ["W", "AA", "CH"],
    "WEB": ["W", "EH", "B"],
    "XRAY": ["EH", "K", "S", "R", "EY"],
    "XYLOPHONE": ["Z", "AY", "L", "AH", "F", "OW", "N"],
    "YAK": ["Y", "AE", "K"],
    "YOYO": ["Y", "OW", "Y", "OW"],
    "ZEBRA": ["Z", "IY", "B", "R", "AH"],
    "ZIP": ["Z", "IH", "P"],
}


@dataclass
class PhoneSpan:
    phone: str
    start: float
    end: float
    confidence: float


@dataclass
class TargetSegment:
    spelling: str
    candidates: list[str]
    matched: bool
    first_seen: float | None
    best_confidence: float


@dataclass
class TrimInfo:
    original_seconds: float
    trimmed_seconds: float
    start_seconds: float
    end_seconds: float
    voiced_seconds: float
    peak: float


def repo_root() -> Path:
    current = Path(__file__).resolve().parent
    for candidate in [current, *current.parents]:
        if (candidate / "Assets").is_dir() and (candidate / "Tools").is_dir():
            return candidate
    return Path(__file__).resolve().parents[2]


def default_model_path() -> Path:
    model_root = repo_root() / "Assets" / "MLModels"
    full_model = model_root / "charsiu-en-w2v2-fc-10ms"
    if full_model.is_dir():
        return full_model
    return model_root / "charsiu-en-w2v2-tiny-fc-10ms"


def normalize_word(value: str) -> str:
    return "".join(ch.upper() for ch in value if ch.isalpha())


def build_word_segments(word: str) -> list[str]:
    word = normalize_word(word)
    segments: list[str] = []
    index = 0
    while index < len(word):
        if index == len(word) - 1 and word[index] == "E" and any(ch in "AEIOU" for ch in word[:-1]):
            break

        chunk = ""
        for digraph in DIGRAPHS:
            if word.startswith(digraph, index):
                chunk = digraph
                break
        if not chunk:
            chunk = word[index]
        segments.append(chunk)
        index += len(chunk)
    return segments


def read_wav(path: Path) -> np.ndarray:
    with wave.open(str(path), "rb") as wav:
        channels = wav.getnchannels()
        sample_width = wav.getsampwidth()
        sample_rate = wav.getframerate()
        frames = wav.readframes(wav.getnframes())

    if sample_width == 1:
        audio = (np.frombuffer(frames, dtype=np.uint8).astype(np.float32) - 128.0) / 128.0
    elif sample_width == 2:
        audio = np.frombuffer(frames, dtype="<i2").astype(np.float32) / 32768.0
    elif sample_width == 4:
        audio = np.frombuffer(frames, dtype="<i4").astype(np.float32) / 2147483648.0
    else:
        raise ValueError(f"Unsupported WAV sample width: {sample_width} bytes")

    if channels > 1:
        audio = audio.reshape(-1, channels).mean(axis=1)

    return resample(audio, sample_rate, SAMPLE_RATE)


def write_wav(path: Path, audio: np.ndarray, sample_rate: int = SAMPLE_RATE) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    clipped = np.clip(audio, -1.0, 1.0)
    pcm = (clipped * 32767.0).astype("<i2")
    with wave.open(str(path), "wb") as wav:
        wav.setnchannels(1)
        wav.setsampwidth(2)
        wav.setframerate(sample_rate)
        wav.writeframes(pcm.tobytes())


def safe_filename(value: str) -> str:
    value = normalize_word(value) or "ATTEMPT"
    return "".join(ch if ch.isalnum() or ch in {"-", "_"} else "_" for ch in value)


def save_attempt_audio(
    directory: Path | None,
    audio: np.ndarray,
    word: str,
    source_wav: Path | None,
    suffix: str,
) -> Path | None:
    if directory is None:
        return None

    label = safe_filename(word or (source_wav.stem if source_wav else "ATTEMPT"))
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S-%f")
    path = directory / f"{stamp}_{label}_{suffix}.wav"
    write_wav(path, audio, SAMPLE_RATE)
    return path


def record_audio(seconds: float) -> np.ndarray:
    try:
        import sounddevice as sd
    except ImportError as exc:
        raise RuntimeError(
            "Live recording needs the optional 'sounddevice' package. "
            "Install requirements.txt or pass --wav."
        ) from exc

    duration = max(0.1, seconds)
    frames = int(duration * SAMPLE_RATE)
    for remaining in range(3, 0, -1):
        print(f"Recording starts in {remaining}...", flush=True)
        time.sleep(1.0)

    print(f"Recording {duration:.1f}s at {SAMPLE_RATE} Hz. Speak now.")
    recording = sd.rec(frames, samplerate=SAMPLE_RATE, channels=1, dtype="float32")
    started = time.monotonic()
    bar_width = 28
    while True:
        elapsed = min(duration, time.monotonic() - started)
        ratio = elapsed / duration if duration > 0 else 1.0
        filled = min(bar_width, int(round(ratio * bar_width)))
        bar = "#" * filled + "-" * (bar_width - filled)
        print(f"\rRecording [{bar}] {elapsed:0.1f}/{duration:0.1f}s", end="", flush=True)
        if elapsed >= duration:
            break
        time.sleep(0.05)
    print()
    sd.wait()
    return recording.reshape(-1)


def record_live_utterance(
    max_seconds: float = 5.0,
    silence_seconds: float = 0.6,
    timeout_seconds: float = 30.0,
    threshold: float = 0.015,
    pre_roll_seconds: float = 0.2,
) -> np.ndarray:
    try:
        import sounddevice as sd
    except ImportError as exc:
        raise RuntimeError(
            "Live recording needs the optional 'sounddevice' package. "
            "Install requirements.txt or pass --wav."
        ) from exc

    block_seconds = 0.03
    block_samples = max(1, int(SAMPLE_RATE * block_seconds))
    max_blocks = max(1, int(math.ceil(max_seconds / block_seconds)))
    silence_blocks = max(1, int(math.ceil(silence_seconds / block_seconds)))
    pre_roll_blocks = max(1, int(math.ceil(pre_roll_seconds / block_seconds)))
    blocks: queue.Queue[np.ndarray] = queue.Queue()

    def callback(indata, frames, time_info, status) -> None:
        blocks.put(indata[:, 0].copy())

    def meter(value: float, width: int = 20) -> str:
        filled = min(width, int(round(min(1.0, value) * width)))
        return "#" * filled + "-" * (width - filled)

    print("Live mode: listening. Speak when ready; I will stop after silence.", flush=True)
    chunks: list[np.ndarray] = []
    pre_roll: list[np.ndarray] = []
    recording = False
    quiet_blocks = 0
    total_blocks = 0
    started_at = time.monotonic()

    with sd.InputStream(
        samplerate=SAMPLE_RATE,
        channels=1,
        dtype="float32",
        blocksize=block_samples,
        callback=callback,
    ):
        while True:
            if not recording and timeout_seconds > 0 and time.monotonic() - started_at > timeout_seconds:
                print("\nNo voice detected before timeout.")
                return np.array([], dtype=np.float32)

            try:
                block = blocks.get(timeout=0.25)
            except queue.Empty:
                continue

            rms = float(np.sqrt(np.mean(block * block))) if block.size else 0.0
            peak = float(np.max(np.abs(block))) if block.size else 0.0
            voiced = peak >= threshold or rms >= threshold * 0.55

            if not recording:
                pre_roll.append(block)
                if len(pre_roll) > pre_roll_blocks:
                    pre_roll.pop(0)
                print(f"\rListening [{meter(peak / max(threshold, 1e-6))}] peak={peak:0.3f}", end="", flush=True)
                if voiced:
                    recording = True
                    chunks = list(pre_roll)
                    total_blocks = len(chunks)
                    quiet_blocks = 0
                    print("\nVoice detected. Recording...", flush=True)
                continue

            chunks.append(block)
            total_blocks += 1
            quiet_blocks = 0 if voiced else quiet_blocks + 1
            elapsed = total_blocks * block_seconds
            print(f"\rRecording [{meter(elapsed / max_seconds)}] {elapsed:0.1f}/{max_seconds:0.1f}s", end="", flush=True)

            if quiet_blocks >= silence_blocks and elapsed >= 0.25:
                print("\nSilence detected. Analyzing...", flush=True)
                break

            if total_blocks >= max_blocks:
                print("\nMax live recording length reached. Analyzing...", flush=True)
                break

    if not chunks:
        return np.array([], dtype=np.float32)
    return np.concatenate(chunks).astype(np.float32, copy=False)


def resample(audio: np.ndarray, source_rate: int, target_rate: int) -> np.ndarray:
    if source_rate == target_rate:
        return audio.astype(np.float32, copy=False)

    if audio.size == 0:
        return audio.astype(np.float32)

    duration = audio.size / float(source_rate)
    target_count = max(1, int(round(duration * target_rate)))
    source_positions = np.linspace(0, audio.size - 1, target_count)
    resampled = np.interp(source_positions, np.arange(audio.size), audio)
    return resampled.astype(np.float32)


def trim_to_voice(audio: np.ndarray, sample_rate: int) -> tuple[np.ndarray, TrimInfo]:
    if audio.size == 0:
        return audio, TrimInfo(0.0, 0.0, 0.0, 0.0, 0.0, 0.0)

    frame_samples = max(1, int(sample_rate * 0.02))
    frame_count = max(1, int(math.ceil(audio.size / frame_samples)))
    rms_values = np.zeros(frame_count, dtype=np.float32)
    for index in range(frame_count):
        start = index * frame_samples
        end = min(audio.size, start + frame_samples)
        frame = audio[start:end]
        if frame.size > 0:
            rms_values[index] = float(np.sqrt(np.mean(frame * frame)))

    noise_floor = float(np.percentile(rms_values, 20))
    threshold = max(0.008, noise_floor * 3.0)
    voiced_indices = np.where(rms_values >= threshold)[0]
    peak = float(np.max(np.abs(audio)))
    original_seconds = audio.size / float(sample_rate)

    if voiced_indices.size == 0 or peak < 0.015:
        info = TrimInfo(original_seconds, 0.0, 0.0, original_seconds, 0.0, peak)
        return np.array([], dtype=np.float32), info

    voiced_seconds = voiced_indices.size * frame_samples / float(sample_rate)
    if voiced_seconds < MIN_VOICED_SECONDS:
        info = TrimInfo(original_seconds, 0.0, 0.0, original_seconds, voiced_seconds, peak)
        return np.array([], dtype=np.float32), info

    padding = int(sample_rate * 0.12)
    start_sample = max(0, int(voiced_indices[0]) * frame_samples - padding)
    end_sample = min(audio.size, (int(voiced_indices[-1]) + 1) * frame_samples + padding)
    trimmed = audio[start_sample:end_sample].astype(np.float32, copy=False)
    info = TrimInfo(
        original_seconds=original_seconds,
        trimmed_seconds=trimmed.size / float(sample_rate),
        start_seconds=start_sample / float(sample_rate),
        end_seconds=end_sample / float(sample_rate),
        voiced_seconds=voiced_seconds,
        peak=peak,
    )
    return trimmed, info


def load_labels(model_dir: Path, label_count: int) -> list[str]:
    config_path = model_dir / "config.json"
    if config_path.exists():
        with config_path.open("r", encoding="utf-8") as handle:
            config = json.load(handle)
        id2label = config.get("id2label")
        if isinstance(id2label, dict) and len(id2label) == label_count:
            return [id2label[str(i)] for i in range(label_count)]

    tokenizer_vocab_path = repo_root() / "Tools" / "Wav2Vec2PhoneticTester" / "tokenizer_en_cmu" / "vocab.json"
    if tokenizer_vocab_path.exists():
        with tokenizer_vocab_path.open("r", encoding="utf-8") as handle:
            vocab = json.load(handle)
        if isinstance(vocab, dict) and len(vocab) == label_count:
            labels = [""] * label_count
            for label, index in vocab.items():
                labels[int(index)] = label
            return labels

    if len(DEFAULT_LABELS) == label_count:
        return DEFAULT_LABELS

    return [f"class_{index}" for index in range(label_count)]


def load_model(model_dir: Path):
    try:
        import torch
        from transformers import Wav2Vec2Config, Wav2Vec2Model
    except ImportError as exc:
        raise RuntimeError(
            "This tester needs torch and transformers. "
            "Install Tools/Wav2Vec2PhoneticTester/requirements.txt first."
        ) from exc

    class CharsiuFrameClassifier(torch.nn.Module):
        def __init__(self, config):
            super().__init__()
            self.wav2vec2 = Wav2Vec2Model(config)
            self.dropout = torch.nn.Dropout(getattr(config, "final_dropout", 0.0))
            self.lm_head = torch.nn.Linear(config.hidden_size, config.vocab_size)

        def forward(self, input_values):
            hidden = self.wav2vec2(input_values).last_hidden_state
            return self.lm_head(self.dropout(hidden))

    config = Wav2Vec2Config.from_pretrained(str(model_dir), local_files_only=True)
    model = CharsiuFrameClassifier(config)
    weights_path = model_dir / "pytorch_model.bin"
    state = torch.load(str(weights_path), map_location="cpu")
    if isinstance(state, dict) and "state_dict" in state:
        state = state["state_dict"]
    missing, unexpected = model.load_state_dict(state, strict=False)
    missing = [key for key in missing if not key.startswith("wav2vec2.masked_spec_embed")]
    if missing or unexpected:
        print(
            "Warning: checkpoint load mismatch. "
            f"missing={missing[:6]} unexpected={unexpected[:6]}",
            file=sys.stderr,
        )
    model.eval()
    return torch, model


def run_model(audio: np.ndarray, model_dir: Path):
    torch, model = load_model(model_dir)
    with torch.no_grad():
        inputs = torch.from_numpy(audio.astype(np.float32)).unsqueeze(0)
        logits = model(inputs)[0]
        probs = torch.softmax(logits, dim=-1).cpu().numpy()

    labels = load_labels(model_dir, probs.shape[-1])
    ids = probs.argmax(axis=-1)
    confidences = probs.max(axis=-1)
    return labels, ids, confidences, probs


def collapse_phones(labels: list[str], ids: np.ndarray, confidences: np.ndarray, duration: float) -> list[PhoneSpan]:
    if ids.size == 0:
        return []

    frame_seconds = duration / float(ids.size)
    spans: list[PhoneSpan] = []
    start_index = 0
    active_id = int(ids[0])
    confidence_values = [float(confidences[0])]

    for index in range(1, ids.size):
        phone_id = int(ids[index])
        if phone_id == active_id:
            confidence_values.append(float(confidences[index]))
            continue

        spans.append(
            PhoneSpan(
                labels[active_id],
                start_index * frame_seconds,
                index * frame_seconds,
                float(np.mean(confidence_values)),
            )
        )
        start_index = index
        active_id = phone_id
        confidence_values = [float(confidences[index])]

    spans.append(
        PhoneSpan(
            labels[active_id],
            start_index * frame_seconds,
            ids.size * frame_seconds,
            float(np.mean(confidence_values)),
        )
    )
    return spans


def compare_to_target(
    word: str,
    spans: list[PhoneSpan],
    minimum_confidence: float,
    minimum_duration: float,
) -> list[TargetSegment]:
    observed = [
        span
        for span in spans
        if span.confidence >= minimum_confidence
        and span.end - span.start >= minimum_duration
        and span.phone.upper() not in {"SIL", "SPN", "SILENCE"}
    ]
    result: list[TargetSegment] = []
    search_index = 0

    for spelling in build_word_segments(word):
        candidates = PHONE_CANDIDATES.get(spelling, [spelling])
        best_confidence = 0.0
        match: PhoneSpan | None = None
        for span in observed[search_index:]:
            if span.phone.upper() in candidates:
                best_confidence = max(best_confidence, span.confidence)
                if match is None:
                    match = span
                    search_index = observed.index(span) + 1
                    break

        result.append(
            TargetSegment(
                spelling=spelling,
                candidates=candidates,
                matched=match is not None,
                first_seen=match.start if match else None,
                best_confidence=best_confidence,
            )
        )

    return result


def expected_phone_sequence(word: str) -> list[str]:
    normalized = normalize_word(word)
    if normalized in WORD_PHONES:
        return WORD_PHONES[normalized]

    phones: list[str] = []
    for segment in build_word_segments(normalized):
        candidates = PHONE_CANDIDATES.get(segment, [segment])
        phones.append(candidates[0])
    return phones


def forced_align_target(
    word: str,
    labels: list[str],
    probs: np.ndarray,
    duration: float,
    minimum_confidence: float,
    minimum_duration: float,
) -> list[TargetSegment]:
    phones = expected_phone_sequence(word)
    if not phones or probs.size == 0:
        return []

    label_to_index = {label.upper(): index for index, label in enumerate(labels)}
    sil_index = label_to_index.get("[SIL]", label_to_index.get("SIL", 0))
    states = ["[SIL]", *phones, "[SIL]"]
    state_indices = [sil_index]
    for phone in phones:
        phone_index = label_to_index.get(phone.upper())
        if phone_index is None:
            phone_index = label_to_index.get("[UNK]", label_to_index.get("SPN", sil_index))
        state_indices.append(phone_index)
    state_indices.append(sil_index)

    frame_count = probs.shape[0]
    state_count = len(states)
    log_probs = np.log(np.maximum(probs[:, state_indices], 1e-8))
    scores = np.full((frame_count, state_count), -np.inf, dtype=np.float32)
    back = np.zeros((frame_count, state_count), dtype=np.int16)

    scores[0, 0] = log_probs[0, 0]
    if state_count > 1:
        scores[0, 1] = log_probs[0, 1]
        back[0, 1] = 0

    for frame in range(1, frame_count):
        max_state = min(state_count, frame + 2)
        for state in range(max_state):
            stay_score = scores[frame - 1, state]
            advance_score = scores[frame - 1, state - 1] if state > 0 else -np.inf
            if advance_score > stay_score:
                scores[frame, state] = advance_score + log_probs[frame, state]
                back[frame, state] = state - 1
            else:
                scores[frame, state] = stay_score + log_probs[frame, state]
                back[frame, state] = state

    final_state = state_count - 1 if scores[-1, state_count - 1] > scores[-1, state_count - 2] else state_count - 2
    path = np.zeros(frame_count, dtype=np.int16)
    path[-1] = final_state
    for frame in range(frame_count - 1, 0, -1):
        path[frame - 1] = back[frame, path[frame]]

    frame_seconds = duration / float(frame_count)
    result: list[TargetSegment] = []
    for phone_index, phone in enumerate(phones, start=1):
        frames = np.where(path == phone_index)[0]
        if frames.size == 0:
            result.append(TargetSegment(phone, [phone], False, None, 0.0))
            continue

        start = float(frames[0] * frame_seconds)
        end = float((frames[-1] + 1) * frame_seconds)
        confidence = float(np.mean(probs[frames, state_indices[phone_index]]))
        matched = confidence >= minimum_confidence and (end - start) >= minimum_duration
        result.append(TargetSegment(phone, [phone], matched, start, confidence))

    return result


def format_time(value: float | None) -> str:
    if value is None or math.isnan(value):
        return "--"
    return f"{value:5.2f}s"


def print_report(
    word: str,
    spans: list[PhoneSpan],
    target: list[TargetSegment],
    top_k: int,
    trim_info: TrimInfo,
    min_confidence: float,
    min_duration: float,
) -> None:
    print()
    print(
        "Audio trim: "
        f"kept {trim_info.trimmed_seconds:0.2f}s "
        f"from {trim_info.original_seconds:0.2f}s "
        f"(voice {trim_info.start_seconds:0.2f}-{trim_info.end_seconds:0.2f}s, "
        f"peak {trim_info.peak:0.3f})"
    )
    print(f"Match gate: confidence >= {min_confidence:0.2f}, duration >= {min_duration:0.2f}s, ordered")
    print()
    print("Observed phone spans")
    print("--------------------")
    for span in spans[:top_k]:
        print(f"{span.start:5.2f}-{span.end:5.2f}s  {span.phone:>5}  {span.confidence:0.2f}")
    if len(spans) > top_k:
        print(f"... {len(spans) - top_k} more spans")

    if word:
        matched = sum(1 for segment in target if segment.matched)
        total = len(target)
        score = matched / total if total else 0.0
        print()
        print(f"Target coverage for {normalize_word(word)}: {matched}/{total} ({score:.0%})")
        print("--------------------------------")
        for segment in target:
            status = "yes" if segment.matched else "no "
            candidates = "/".join(segment.candidates)
            print(
                f"{segment.spelling:>3} -> {candidates:<14} "
                f"matched={status} first={format_time(segment.first_seen)} conf={segment.best_confidence:0.2f}"
            )


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description="Run the Charsiu wav2vec2 phonetic model against live or WAV audio.")
    parser.add_argument("--word", default="", help="Expected game word, e.g. CAT, SHIP, THIN.")
    parser.add_argument("--wav", type=Path, help="Analyze a WAV file instead of recording the microphone.")
    parser.add_argument("--seconds", type=float, default=2.5, help="Seconds to record when --wav is not supplied.")
    parser.add_argument("--model", type=Path, default=default_model_path(), help="Path to the local Charsiu model folder.")
    parser.add_argument("--mode", choices=["forced", "free"], default="forced", help="forced aligns expected word phones; free uses raw recognized phone spans.")
    parser.add_argument("--min-confidence", type=float, default=None, help="Minimum phone confidence used for target coverage.")
    parser.add_argument("--min-phone-duration", type=float, default=DEFAULT_MIN_PHONE_DURATION, help="Minimum phone span duration used for target coverage.")
    parser.add_argument("--top-k", type=int, default=80, help="Maximum collapsed phone spans to print.")
    parser.add_argument("--json", type=Path, help="Optional path to save the full report as JSON.")
    parser.add_argument("--no-trim", action="store_true", help="Analyze the full recording without voice-activity trimming.")
    parser.add_argument("--save-trimmed-dir", type=Path, help="Optional folder to save the trimmed WAV for this attempt.")
    args = parser.parse_args(argv)

    model_dir = args.model.resolve()
    if not model_dir.exists():
        raise SystemExit(f"Model folder not found: {model_dir}")

    source_wav = args.wav.resolve() if args.wav else None
    audio = read_wav(source_wav) if source_wav else record_audio(args.seconds)
    if audio.size == 0:
        raise SystemExit("No audio samples were captured.")

    saved_trimmed_path: Path | None = None
    if args.no_trim:
        trim_info = TrimInfo(
            original_seconds=audio.size / float(SAMPLE_RATE),
            trimmed_seconds=audio.size / float(SAMPLE_RATE),
            start_seconds=0.0,
            end_seconds=audio.size / float(SAMPLE_RATE),
            voiced_seconds=audio.size / float(SAMPLE_RATE),
            peak=float(np.max(np.abs(audio))) if audio.size > 0 else 0.0,
        )
        saved_trimmed_path = save_attempt_audio(args.save_trimmed_dir, audio, args.word, source_wav, "full")
    else:
        original_audio = audio
        audio, trim_info = trim_to_voice(audio, SAMPLE_RATE)
        if audio.size == 0:
            rejected_path = save_attempt_audio(args.save_trimmed_dir, original_audio, args.word, source_wav, "no_voice_full")
            if rejected_path is not None:
                print(f"Saved rejected full capture to {rejected_path}")
            raise SystemExit(
                "No clear voice activity was detected. "
                "Move closer to the mic, speak after the prompt, or increase --seconds."
            )
        saved_trimmed_path = save_attempt_audio(args.save_trimmed_dir, audio, args.word, source_wav, "trimmed")
    if saved_trimmed_path is not None:
        print(f"Saved trimmed WAV to {saved_trimmed_path}")

    labels, ids, confidences, probs = run_model(audio, model_dir)
    duration = audio.size / float(SAMPLE_RATE)
    spans = collapse_phones(labels, ids, confidences, duration)
    min_confidence = args.min_confidence
    if min_confidence is None:
        min_confidence = DEFAULT_FORCED_MIN_CONFIDENCE if args.mode == "forced" else DEFAULT_MIN_CONFIDENCE
    if args.word and args.mode == "forced":
        target = forced_align_target(args.word, labels, probs, duration, min_confidence, args.min_phone_duration)
    else:
        target = compare_to_target(args.word, spans, min_confidence, args.min_phone_duration) if args.word else []

    print_report(args.word, spans, target, args.top_k, trim_info, min_confidence, args.min_phone_duration)

    if args.json:
        target_payload = []
        for segment in target:
            item = asdict(segment)
            if item["first_seen"] is None:
                item["first_seen"] = -1.0
            target_payload.append(item)
        payload = {
            "word": normalize_word(args.word),
            "duration_seconds": duration,
            "trim": asdict(trim_info),
            "trimmed_wav": str(saved_trimmed_path) if saved_trimmed_path else "",
            "mode": args.mode,
            "model": str(model_dir),
            "spans": [asdict(span) for span in spans],
            "target": target_payload,
        }
        args.json.parent.mkdir(parents=True, exist_ok=True)
        args.json.write_text(json.dumps(payload, indent=2), encoding="utf-8")
        print(f"\nSaved JSON report to {args.json}")

    print()
    print("Accuracy note: treat this as pronunciation evidence, not a pass/fail oracle.")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main(sys.argv[1:]))
    except (RuntimeError, ValueError) as exc:
        print(f"Error: {exc}", file=sys.stderr)
        raise SystemExit(1)
