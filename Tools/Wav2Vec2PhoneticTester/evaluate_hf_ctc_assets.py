#!/usr/bin/env python3
"""Batch-evaluate labelled pronunciation WAV assets with HF CTC phone models."""

from __future__ import annotations

import argparse
import csv
import json
from dataclasses import asdict
from pathlib import Path

import hf_ctc_tester as hf_ctc
import phonetic_tester as tester


def main() -> int:
    parser = argparse.ArgumentParser(description="Evaluate labelled spell WAV assets against WavLM/HuBERT CTC models.")
    parser.add_argument(
        "--dir",
        type=Path,
        default=tester.repo_root() / "Assets" / "Audio" / "Pronunciations" / "Spells",
        help="Directory containing labelled WAV files.",
    )
    parser.add_argument("--backend", choices=sorted(hf_ctc.DEFAULT_MODELS), default="wavlm")
    parser.add_argument("--model", default=None, help="Override Hugging Face model id.")
    parser.add_argument("--limit", type=int, default=0, help="Optional limit for smoke tests.")
    parser.add_argument("--quiet", action="store_true", help="Only print the final summary.")
    parser.add_argument("--csv", type=Path, default=Path("Tools/Wav2Vec2PhoneticTester/asset-eval-hf-ctc.csv"))
    parser.add_argument("--json", type=Path, default=Path("Tools/Wav2Vec2PhoneticTester/asset-eval-hf-ctc.json"))
    args = parser.parse_args()

    model_name = hf_ctc.resolve_model(args.backend, args.model)
    wavs = sorted(args.dir.glob("*.wav"))
    if args.limit > 0:
        wavs = wavs[: args.limit]
    if not wavs:
        raise SystemExit(f"No WAV files found in {args.dir}")

    torch, processor, model = hf_ctc.load_ctc(model_name)
    rows = []
    reports = []
    for wav_path in wavs:
        word = wav_path.stem.upper()
        audio = tester.read_wav(wav_path)
        phones, decoded = hf_ctc.decode_phones_loaded(audio, torch, processor, model)
        target = hf_ctc.ordered_match(word, phones)

        total = len(target)
        matched = sum(1 for segment in target if segment.matched)
        coverage = matched / total if total else 0.0
        missed = " ".join(segment.phone for segment in target if not segment.matched)

        row = {
            "word": word,
            "status": "ok",
            "matched": matched,
            "total": total,
            "coverage": round(coverage, 4),
            "backend": args.backend,
            "model": model_name,
            "observed": decoded,
            "missed": missed,
            "path": str(wav_path),
        }
        rows.append(row)
        reports.append(
            {
                **row,
                "phones": [asdict(phone) for phone in phones],
                "target": [asdict(segment) for segment in target],
            }
        )
        if not args.quiet:
            print(f"{word:<12} {matched:>2}/{total:<2} {coverage:>6.0%} ok       {decoded}")

    args.csv.parent.mkdir(parents=True, exist_ok=True)
    with args.csv.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(rows[0].keys()))
        writer.writeheader()
        writer.writerows(rows)

    args.json.write_text(json.dumps(reports, indent=2, ensure_ascii=False), encoding="utf-8")

    exact = sum(1 for row in rows if row["coverage"] >= 0.999)
    partial = sum(1 for row in rows if 0 < row["coverage"] < 0.999)
    zero = sum(1 for row in rows if row["coverage"] <= 0)
    average = sum(float(row["coverage"]) for row in rows) / len(rows) if rows else 0.0
    print()
    print(f"Backend: {args.backend}  Model: {model_name}")
    print(f"Files: {len(rows)}  usable: {len(rows)}")
    print(f"Exact: {exact}  partial: {partial}  zero: {zero}  average coverage: {average:.1%}")
    print(f"CSV: {args.csv}")
    print(f"JSON: {args.json}")
    return 0


if __name__ == "__main__":
    import os
    import sys

    code = main()
    sys.stdout.flush()
    sys.stderr.flush()
    os._exit(code)
