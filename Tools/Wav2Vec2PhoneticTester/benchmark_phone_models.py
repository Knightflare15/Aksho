#!/usr/bin/env python3
"""Benchmark candidate phone-recognition models for accuracy and speed."""

from __future__ import annotations

import argparse
import csv
import json
import time
from dataclasses import asdict
from pathlib import Path

import hf_ctc_tester as hf_ctc
import phonetic_tester as tester


DEFAULT_CANDIDATES = [
    {
        "label": "wavlm-large-phoneme",
        "kind": "hf_ctc",
        "backend": "wavlm",
        "model": "speech31/wavlm-large-english-phoneme",
        "approx_model_mb": 3579,
        "notes": "Best current WavLM CTC phone checkpoint found; already benchmarked well.",
    },
    {
        "label": "wavlm-base-plus-fr-it-phonemizer",
        "kind": "hf_ctc",
        "backend": "wavlm-base-plus-fr-it",
        "model": "hugofara/wavlm-base-plus-phonemizer-fr-it",
        "approx_model_mb": 361,
        "notes": "Runnable WavLM base+ phonemizer head found by user. Trained for French/Italian, so English spell accuracy must be measured, not assumed.",
    },
    {
        "label": "wav2vec2-large-phoneme-v2",
        "kind": "hf_ctc",
        "backend": "wav2vec2",
        "model": "speech31/wav2vec2-large-english-phoneme-v2",
        "approx_model_mb": 1204,
        "notes": "English phoneme CTC model; likely slower than base but smaller than WavLM large checkpoint bundle.",
    },
    {
        "label": "facebook-wav2vec2-lv60-espeak",
        "kind": "hf_ctc",
        "backend": "wav2vec2-phoneme",
        "model": "facebook/wav2vec2-lv-60-espeak-cv-ft",
        "approx_model_mb": 1200,
        "notes": "Original HF Wav2Vec2Phoneme-style checkpoint from the phoneme-recognition filter; espeak phoneme vocabulary.",
    },
    {
        "label": "facebook-wav2vec2-xlsr53-espeak",
        "kind": "hf_ctc",
        "backend": "wav2vec2-phoneme",
        "model": "facebook/wav2vec2-xlsr-53-espeak-cv-ft",
        "approx_model_mb": 1200,
        "notes": "Cross-lingual Wav2Vec2Phoneme checkpoint from the HF phoneme-recognition filter; espeak phoneme vocabulary.",
    },
    {
        "label": "slplab-large-robust-l2-english-phoneme",
        "kind": "hf_ctc",
        "backend": "wav2vec2",
        "model": "slplab/wav2vec2-large-robust-L2-english-phoneme-recognition",
        "approx_model_mb": 1200,
        "notes": "High-download English phoneme-recognition checkpoint from the HF phoneme-recognition search.",
    },
    {
        "label": "bookbot-ljspeech-gruut",
        "kind": "hf_ctc",
        "backend": "wav2vec2-phoneme",
        "model": "bookbot/wav2vec2-ljspeech-gruut",
        "approx_model_mb": 360,
        "notes": "Small Wav2Vec2Phoneme/gruut checkpoint from the HF phoneme-recognition filter; trained on LJSpeech.",
    },
    {
        "label": "peacockery-hubert-base-phoneme-en",
        "kind": "hf_ctc",
        "backend": "hubert",
        "model": "Peacockery/hubert-base-phoneme-en",
        "approx_model_mb": 360,
        "notes": "English HuBERT phoneme checkpoint from the HF phoneme-recognition filter.",
    },
    {
        "label": "ct-phoneme-scorer-v2-wav2vec2",
        "kind": "hf_ctc",
        "backend": "wav2vec2",
        "model": "ct-vikramanantha/phoneme-scorer-v2-wav2vec2",
        "approx_model_mb": 360,
        "notes": "Small phoneme scorer candidate from the HF phoneme-recognition filter; may be scoring-oriented rather than free CTC decoding.",
    },
    {
        "label": "wav2vec2-large-timit-ipa",
        "kind": "hf_ctc",
        "backend": "wav2vec2",
        "model": "speech31/wav2vec2-large-TIMIT-IPA",
        "approx_model_mb": 1204,
        "notes": "Large wav2vec2 model trained for TIMIT IPA output.",
    },
    {
        "label": "wav2vec2-base-timit-arpa39",
        "kind": "hf_ctc",
        "backend": "wav2vec2",
        "model": "mostafaashahin/wav2vec2-base-timit-phoneme-arpa-39-v2",
        "approx_model_mb": 360,
        "notes": "Smallest packaged wav2vec2 phoneme CTC candidate; likely best efficiency candidate.",
    },
    {
        "label": "wav2vec2-xlsr50k-english-phoneme",
        "kind": "hf_ctc",
        "backend": "wav2vec2",
        "model": "slplab/wav2vec2_xlsr50k_english_phoneme",
        "approx_model_mb": 1204,
        "notes": "XLSR English phoneme CTC candidate.",
    },
    {
        "label": "wavlm-base-plus",
        "kind": "not_direct",
        "backend": "wavlm-base-plus",
        "model": "microsoft/wavlm-base-plus",
        "approx_model_mb": 360,
        "notes": "Encoder only: no phoneme CTC/transducer head, so it cannot emit phones without fine-tuning.",
    },
    {
        "label": "zipa-large-crctc-int8",
        "kind": "zipa_onnx",
        "backend": "zipa",
        "model": "anyspeech/zipa-large-crctc-ns-800k",
        "approx_model_mb": 310,
        "precision": "int8",
        "notes": "ZIPA large CR-CTC ONNX int8 checkpoint; separate ONNX/lhotse inference path.",
    },
]


def evaluate_candidate(candidate: dict, wavs: list[Path]) -> tuple[dict, list[dict]]:
    row = {
        "label": candidate["label"],
        "kind": candidate["kind"],
        "backend": candidate["backend"],
        "model": candidate["model"],
        "status": "skipped",
        "files": 0,
        "exact": 0,
        "partial": 0,
        "zero": 0,
        "average_coverage": "",
        "load_seconds": "",
        "eval_seconds": "",
        "total_seconds": "",
        "avg_seconds_per_file": "",
        "approx_model_mb": candidate.get("approx_model_mb", ""),
        "notes": candidate.get("notes", ""),
    }

    if candidate["kind"] not in {"hf_ctc", "zipa_onnx"}:
        row["status"] = candidate["kind"]
        return row, []

    reports: list[dict] = []
    started = time.perf_counter()
    try:
        load_started = time.perf_counter()
        if candidate["kind"] == "hf_ctc":
            torch, processor, model = hf_ctc.load_ctc(candidate["model"])
            zipa = None
        else:
            import zipa_onnx_tester as zipa_onnx

            torch = processor = model = None
            zipa = zipa_onnx.load_zipa(candidate["model"], candidate.get("precision", "int8"))
        load_seconds = time.perf_counter() - load_started

        eval_started = time.perf_counter()
        coverages: list[float] = []
        for wav_path in wavs:
            word = wav_path.stem.upper()
            audio = tester.read_wav(wav_path)
            if candidate["kind"] == "hf_ctc":
                phones, decoded = hf_ctc.decode_phones_loaded(audio, torch, processor, model)
            else:
                phones, decoded = zipa_onnx.decode_phones_loaded(audio, zipa)
            target = hf_ctc.ordered_match(word, phones)
            total = len(target)
            matched = sum(1 for segment in target if segment.matched)
            coverage = matched / total if total else 0.0
            coverages.append(coverage)
            reports.append(
                {
                    "label": candidate["label"],
                    "word": word,
                    "matched": matched,
                    "total": total,
                    "coverage": round(coverage, 4),
                    "observed": decoded,
                    "missed": " ".join(segment.phone for segment in target if not segment.matched),
                    "path": str(wav_path),
                    "phones": [asdict(phone) for phone in phones],
                    "target": [asdict(segment) for segment in target],
                }
            )

        eval_seconds = time.perf_counter() - eval_started
        total_seconds = time.perf_counter() - started
        row.update(
            {
                "status": "ok",
                "files": len(wavs),
                "exact": sum(1 for value in coverages if value >= 0.999),
                "partial": sum(1 for value in coverages if 0 < value < 0.999),
                "zero": sum(1 for value in coverages if value <= 0),
                "average_coverage": round(sum(coverages) / len(coverages), 4) if coverages else 0.0,
                "load_seconds": round(load_seconds, 3),
                "eval_seconds": round(eval_seconds, 3),
                "total_seconds": round(total_seconds, 3),
                "avg_seconds_per_file": round(eval_seconds / len(wavs), 3) if wavs else "",
            }
        )
    except Exception as exc:
        row["status"] = "error"
        row["notes"] = f"{candidate.get('notes', '')} ERROR: {type(exc).__name__}: {exc}".strip()

    return row, reports


def select_candidates(names: list[str]) -> list[dict]:
    if not names or names == ["default"]:
        return DEFAULT_CANDIDATES

    by_label = {candidate["label"]: candidate for candidate in DEFAULT_CANDIDATES}
    selected = []
    for name in names:
        if name not in by_label:
            raise SystemExit(f"Unknown candidate '{name}'. Choices: {', '.join(by_label)}")
        selected.append(by_label[name])
    return selected


def main() -> int:
    parser = argparse.ArgumentParser(description="Benchmark phone recognizer candidates on labelled spell WAVs.")
    parser.add_argument(
        "--dir",
        type=Path,
        default=tester.repo_root() / "Assets" / "Audio" / "Pronunciations" / "Spells",
        help="Directory containing labelled WAV files.",
    )
    parser.add_argument("--limit", type=int, default=0, help="Optional file limit for smoke tests.")
    parser.add_argument("--candidate", action="append", default=[], help="Candidate label to run. Repeatable.")
    parser.add_argument("--skip-existing", action="store_true", help="Skip candidate rows already present in the CSV.")
    parser.add_argument("--replace-existing", action="store_true", help="Replace existing rows for selected candidates.")
    parser.add_argument("--csv", type=Path, default=Path("Tools/Wav2Vec2PhoneticTester/phone-model-benchmark.csv"))
    parser.add_argument("--json", type=Path, default=Path("Tools/Wav2Vec2PhoneticTester/phone-model-benchmark.json"))
    args = parser.parse_args()

    wavs = sorted(args.dir.glob("*.wav"))
    if args.limit > 0:
        wavs = wavs[: args.limit]
    if not wavs:
        raise SystemExit(f"No WAV files found in {args.dir}")

    existing_labels: set[str] = set()
    existing_rows: list[dict] = []
    if (args.skip_existing or args.replace_existing) and args.csv.exists():
        with args.csv.open("r", newline="", encoding="utf-8") as handle:
            reader = csv.DictReader(handle)
            existing_rows = list(reader)
            existing_labels = {row["label"] for row in existing_rows}

    rows = existing_rows[:]
    reports: list[dict] = []
    if args.json.exists() and (args.skip_existing or args.replace_existing):
        try:
            reports = json.loads(args.json.read_text(encoding="utf-8"))
        except json.JSONDecodeError:
            reports = []

    candidates = select_candidates(args.candidate or ["default"])
    if args.replace_existing:
        selected_labels = {candidate["label"] for candidate in candidates}
        rows = [row for row in rows if row.get("label") not in selected_labels]
        reports = [report for report in reports if report.get("label") not in selected_labels]
        existing_labels -= selected_labels

    for candidate in candidates:
        if candidate["label"] in existing_labels:
            print(f"Skipping existing {candidate['label']}")
            continue
        print(f"Benchmarking {candidate['label']} ({candidate['model']})...")
        row, candidate_reports = evaluate_candidate(candidate, wavs)
        rows.append(row)
        reports.extend(candidate_reports)
        print(
            f"  {row['status']} exact={row['exact']} partial={row['partial']} "
            f"zero={row['zero']} avg={row['average_coverage']} "
            f"eval_s={row['eval_seconds']}"
        )

    args.csv.parent.mkdir(parents=True, exist_ok=True)
    with args.csv.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(rows[0].keys()))
        writer.writeheader()
        writer.writerows(rows)

    args.json.write_text(json.dumps(reports, indent=2, ensure_ascii=False), encoding="utf-8")
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
