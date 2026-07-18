#!/usr/bin/env python3
"""Run ZIPA ONNX phone-recognition checkpoints."""

from __future__ import annotations

import argparse
import json
import os
import sys
from dataclasses import asdict
from pathlib import Path

import numpy as np

import hf_ctc_tester as hf_ctc
import phonetic_tester as tester

os.environ.setdefault("HF_HUB_DISABLE_PROGRESS_BARS", "1")
os.environ.setdefault("HF_HUB_DISABLE_SYMLINKS_WARNING", "1")


DEFAULT_REPO = "anyspeech/zipa-large-crctc-ns-800k"
MODEL_FILES = {
    "int8": "model.int8.onnx",
    "fp16": "model.fp16.onnx",
    "fp32": "model.onnx",
}


def download_zpa_files(repo_id: str, precision: str) -> tuple[Path, Path]:
    from huggingface_hub import hf_hub_download

    model_path = hf_hub_download(repo_id, MODEL_FILES[precision])
    tokens_path = hf_hub_download(repo_id, "tokens.txt")
    return Path(model_path), Path(tokens_path)


def load_tokens(path: Path) -> dict[int, str]:
    tokens: dict[int, str] = {}
    for line in path.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line:
            continue
        parts = line.rsplit(maxsplit=1)
        if len(parts) != 2:
            continue
        token, token_id = parts
        try:
            tokens[int(token_id)] = token
        except ValueError:
            continue
    if not tokens:
        raise ValueError(f"No token/id pairs found in {path}")
    return tokens


def load_zipa(
    repo_id: str = DEFAULT_REPO,
    precision: str = "int8",
    model_path: Path | None = None,
    tokens_path: Path | None = None,
):
    import onnxruntime as ort
    from lhotse.features.kaldi.extractors import Fbank, FbankConfig

    if model_path is None or tokens_path is None:
        downloaded_model, downloaded_tokens = download_zpa_files(repo_id, precision)
        model_path = model_path or downloaded_model
        tokens_path = tokens_path or downloaded_tokens

    session = ort.InferenceSession(str(model_path), providers=["CPUExecutionProvider"])
    tokens = load_tokens(tokens_path)
    extractor = Fbank(FbankConfig(num_filters=80, dither=0.0, snip_edges=False))
    return {
        "session": session,
        "tokens": tokens,
        "extractor": extractor,
        "repo_id": repo_id,
        "precision": precision,
        "model_path": str(model_path),
        "tokens_path": str(tokens_path),
    }


def extract_features(audio: np.ndarray, extractor):
    import torch

    audio_tensor = torch.from_numpy(audio.astype(np.float32, copy=False))
    features = extractor.extract_batch([audio_tensor], sampling_rate=tester.SAMPLE_RATE)
    if isinstance(features, list):
        feature = features[0]
    else:
        feature = features[0]
    if feature.ndim == 2:
        feature = feature.unsqueeze(0)
    return feature.cpu().numpy().astype(np.float32, copy=False)


def softmax(values: np.ndarray) -> np.ndarray:
    shifted = values - np.max(values, axis=-1, keepdims=True)
    exps = np.exp(shifted)
    return exps / np.sum(exps, axis=-1, keepdims=True)


def decode_phones_loaded(audio: np.ndarray, loaded) -> tuple[list[hf_ctc.CtcPhone], str]:
    session = loaded["session"]
    tokens: dict[int, str] = loaded["tokens"]
    features = extract_features(audio, loaded["extractor"])
    feature_lens = np.array([features.shape[1]], dtype=np.int64)

    input_names = [item.name for item in session.get_inputs()]
    feed = {input_names[0]: features, input_names[1]: feature_lens}
    outputs = session.run(None, feed)
    logits = outputs[0]
    if logits.ndim == 3:
        logits = logits[0]

    probabilities = softmax(logits.astype(np.float32, copy=False))
    token_ids = np.argmax(probabilities, axis=-1).astype(np.int64)
    confidences = np.max(probabilities, axis=-1)
    frame_seconds = (audio.size / float(tester.SAMPLE_RATE)) / max(1, len(token_ids))

    phones: list[hf_ctc.CtcPhone] = []
    previous: int | None = None
    active_start = 0
    active_confidences: list[float] = []

    def flush(token_id: int | None, end_frame: int) -> None:
        if token_id is None or token_id == 0:
            return
        token = tokens.get(int(token_id), str(token_id))
        if token in {"<blk>", "<sos/eos>", "<unk>", "▁", "|", " ", ""}:
            return
        if token.startswith("<") and token.endswith(">"):
            return
        normalized = hf_ctc.normalize_phone(token)
        if not normalized:
            return
        start = active_start * frame_seconds
        duration = max(frame_seconds, (end_frame - active_start) * frame_seconds)
        confidence = sum(active_confidences) / len(active_confidences) if active_confidences else None
        phones.append(hf_ctc.CtcPhone(token, normalized, start, duration, confidence))

    for frame_index, token_id in enumerate(token_ids.tolist()):
        token_id = int(token_id)
        if previous is None:
            previous = token_id
            active_start = frame_index
            active_confidences = [float(confidences[frame_index])]
            continue
        if token_id == previous:
            active_confidences.append(float(confidences[frame_index]))
            continue
        flush(previous, frame_index)
        previous = token_id
        active_start = frame_index
        active_confidences = [float(confidences[frame_index])]

    flush(previous, len(token_ids))
    decoded = " ".join(phone.phone for phone in phones)
    return phones, decoded


def decode_phones(
    audio: np.ndarray,
    repo_id: str = DEFAULT_REPO,
    precision: str = "int8",
    model_path: Path | None = None,
    tokens_path: Path | None = None,
) -> tuple[list[hf_ctc.CtcPhone], str]:
    loaded = load_zipa(repo_id, precision, model_path, tokens_path)
    return decode_phones_loaded(audio, loaded)


def print_report(word: str, loaded, wav_path: Path, phones: list[hf_ctc.CtcPhone], target: list[hf_ctc.CtcTarget]) -> None:
    print()
    print(f"ZIPA ONNX model: {loaded['repo_id']}  precision={loaded['precision']}")
    print(f"Analyzed WAV: {wav_path}")
    print(f"Model file: {loaded['model_path']}")
    print()
    print("Observed phones")
    print("---------------")
    for phone in phones:
        if phone.start is None:
            print(f"{phone.phone}")
        else:
            confidence = phone.confidence if phone.confidence is not None else 0.0
            print(f"{phone.start:5.2f}s +{(phone.duration or 0.0):0.3f}s  {phone.phone:<4} {confidence:0.2f}")

    if not target:
        return

    total = len(target)
    matched = sum(1 for segment in target if segment.matched)
    score = matched / total if total else 0.0
    print()
    print(f"Target coverage for {tester.normalize_word(word)}: {matched}/{total} ({score:.0%})")
    print("--------------------------------")
    for segment in target:
        candidates = "/".join(segment.candidates)
        first = "--" if segment.first_seen is None else f"{segment.first_seen:5.2f}s"
        confidence = 0.0 if segment.confidence is None else segment.confidence
        print(
            f"{segment.phone:>3} -> {candidates:<12} "
            f"matched={'yes' if segment.matched else 'no '} "
            f"first={first:>6} conf={confidence:0.2f} heard={segment.heard or '--'}"
        )


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description="Run a ZIPA ONNX phone recognizer.")
    parser.add_argument("--repo", default=DEFAULT_REPO, help="Hugging Face model repo id.")
    parser.add_argument("--precision", choices=sorted(MODEL_FILES), default="int8")
    parser.add_argument("--model-path", type=Path, default=None, help="Local ONNX model path.")
    parser.add_argument("--tokens-path", type=Path, default=None, help="Local ZIPA tokens.txt path.")
    parser.add_argument("--word", default="")
    parser.add_argument("--wav", type=Path, required=True)
    parser.add_argument("--json", type=Path, default=None)
    args = parser.parse_args(argv)

    audio = tester.read_wav(args.wav)
    loaded = load_zipa(args.repo, args.precision, args.model_path, args.tokens_path)
    phones, decoded = decode_phones_loaded(audio, loaded)
    target = hf_ctc.ordered_match(args.word, phones) if args.word else []
    print_report(args.word, loaded, args.wav, phones, target)

    if args.json:
        args.json.parent.mkdir(parents=True, exist_ok=True)
        args.json.write_text(
            json.dumps(
                {
                    "backend": "zipa",
                    "model": args.repo,
                    "precision": args.precision,
                    "word": tester.normalize_word(args.word),
                    "wav": str(args.wav),
                    "decoded": decoded,
                    "phones": [asdict(phone) for phone in phones],
                    "target": [asdict(segment) for segment in target],
                },
                indent=2,
                ensure_ascii=False,
            ),
            encoding="utf-8",
        )

    return 0


if __name__ == "__main__":
    code = main(sys.argv[1:])
    sys.stdout.flush()
    sys.stderr.flush()
    os._exit(code)
