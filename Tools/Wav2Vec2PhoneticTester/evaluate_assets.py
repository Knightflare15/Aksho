#!/usr/bin/env python3
"""Batch-evaluate labelled pronunciation WAV assets with the phoneme tester."""

from __future__ import annotations

import argparse
import csv
import json
from dataclasses import asdict
from pathlib import Path

import numpy as np

import phonetic_tester as tester


def classify_audio(audio: np.ndarray, model_dir: Path, loaded: tuple):
    torch, model = loaded
    with torch.no_grad():
        inputs = torch.from_numpy(audio.astype(np.float32)).unsqueeze(0)
        logits = model(inputs)[0]
        probs = torch.softmax(logits, dim=-1).cpu().numpy()

    labels = tester.load_labels(model_dir, probs.shape[-1])
    ids = probs.argmax(axis=-1)
    confidences = probs.max(axis=-1)
    return labels, ids, confidences, probs


def main() -> int:
    parser = argparse.ArgumentParser(description="Evaluate labelled spell WAV assets against the Charsiu phoneme model.")
    parser.add_argument(
        "--dir",
        type=Path,
        default=tester.repo_root() / "Assets" / "Audio" / "Pronunciations" / "Spells",
        help="Directory containing labelled WAV files.",
    )
    parser.add_argument("--model", type=Path, default=tester.default_model_path(), help="Path to the local Charsiu model folder.")
    parser.add_argument("--mode", choices=["forced", "free"], default="forced")
    parser.add_argument("--min-confidence", type=float, default=None)
    parser.add_argument("--min-phone-duration", type=float, default=tester.DEFAULT_MIN_PHONE_DURATION)
    parser.add_argument("--csv", type=Path, default=Path("Tools/Wav2Vec2PhoneticTester/asset-eval.csv"))
    parser.add_argument("--json", type=Path, default=Path("Tools/Wav2Vec2PhoneticTester/asset-eval.json"))
    parser.add_argument("--trimmed-dir", type=Path, help="Optional folder to save every trimmed labelled WAV.")
    parser.add_argument("--no-trim", action="store_true", help="Evaluate the entire WAV file instead of trimming to voice first.")
    args = parser.parse_args()

    wavs = sorted(args.dir.glob("*.wav"))
    if not wavs:
        raise SystemExit(f"No WAV files found in {args.dir}")

    model_dir = args.model.resolve()
    loaded = tester.load_model(model_dir)
    rows = []
    reports = []
    min_confidence = args.min_confidence
    if min_confidence is None:
        min_confidence = tester.DEFAULT_FORCED_MIN_CONFIDENCE if args.mode == "forced" else tester.DEFAULT_MIN_CONFIDENCE

    for wav_path in wavs:
        word = wav_path.stem.upper()
        audio = tester.read_wav(wav_path)
        if args.no_trim:
            duration = audio.size / float(tester.SAMPLE_RATE) if audio.size else 0.0
            peak = float(np.max(np.abs(audio))) if audio.size else 0.0
            trimmed = audio
            trim_info = tester.TrimInfo(duration, duration, 0.0, duration, duration, peak)
        else:
            trimmed, trim_info = tester.trim_to_voice(audio, tester.SAMPLE_RATE)
        status = "ok"
        spans = []
        target = []
        if trimmed.size == 0:
            status = "no_voice"
        else:
            if args.trimmed_dir:
                tester.write_wav(args.trimmed_dir / f"{word}_trimmed.wav", trimmed, tester.SAMPLE_RATE)
            labels, ids, confidences, probs = classify_audio(trimmed, model_dir, loaded)
            duration = trimmed.size / float(tester.SAMPLE_RATE)
            spans = tester.collapse_phones(labels, ids, confidences, duration)
            if args.mode == "forced":
                target = tester.forced_align_target(word, labels, probs, duration, min_confidence, args.min_phone_duration)
            else:
                target = tester.compare_to_target(word, spans, min_confidence, args.min_phone_duration)

        total = len(target) if target else len(tester.build_word_segments(word))
        matched = sum(1 for segment in target if segment.matched)
        coverage = matched / total if total else 0.0
        observed = " ".join(
            span.phone.upper()
            for span in spans
            if span.phone.upper() not in {"SIL", "SPN", "SILENCE"}
            and span.confidence >= min_confidence
            and span.end - span.start >= args.min_phone_duration
        )
        missed = " ".join(segment.spelling for segment in target if not segment.matched)

        row = {
            "word": word,
            "status": status,
            "matched": matched,
            "total": total,
            "coverage": round(coverage, 4),
            "trim_mode": "none" if args.no_trim else "voice",
            "trimmed_seconds": round(trim_info.trimmed_seconds, 4),
            "voiced_seconds": round(trim_info.voiced_seconds, 4),
            "peak": round(trim_info.peak, 4),
            "observed": observed,
            "missed": missed,
            "path": str(wav_path),
        }
        rows.append(row)
        reports.append(
            {
                **row,
                "trim": asdict(trim_info),
                "spans": [asdict(span) for span in spans],
                "target": [asdict(segment) for segment in target],
            }
        )
        print(f"{word:<12} {matched:>2}/{total:<2} {coverage:>6.0%} {status:<8} {observed}")

    args.csv.parent.mkdir(parents=True, exist_ok=True)
    with args.csv.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(rows[0].keys()))
        writer.writeheader()
        writer.writerows(rows)

    args.json.write_text(json.dumps(reports, indent=2), encoding="utf-8")

    ok_rows = [row for row in rows if row["status"] == "ok"]
    exact = sum(1 for row in ok_rows if row["coverage"] >= 0.999)
    partial = sum(1 for row in ok_rows if 0 < row["coverage"] < 0.999)
    zero = sum(1 for row in ok_rows if row["coverage"] <= 0)
    average = sum(float(row["coverage"]) for row in ok_rows) / len(ok_rows) if ok_rows else 0.0
    print()
    print(f"Files: {len(rows)}  usable: {len(ok_rows)}  no_voice: {len(rows) - len(ok_rows)}")
    print(f"Exact: {exact}  partial: {partial}  zero: {zero}  average coverage: {average:.1%}")
    print(f"CSV: {args.csv}")
    print(f"JSON: {args.json}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
