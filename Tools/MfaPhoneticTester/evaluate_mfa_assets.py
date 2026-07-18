#!/usr/bin/env python3
"""Batch-evaluate labelled pronunciation WAV assets with Montreal Forced Aligner."""

from __future__ import annotations

import argparse
import csv
import json
import shutil
import subprocess
import sys
from dataclasses import asdict, dataclass
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
W2V_TOOL = REPO_ROOT / "Tools" / "Wav2Vec2PhoneticTester"
sys.path.insert(0, str(W2V_TOOL))

import phonetic_tester as tester  # noqa: E402


TRANSCRIPT_OVERRIDES = {
    "XRAY": "X RAY",
    "YOYO": "YO YO",
}

PHONE_EQUIVALENTS = {
    "AA": {"AA", "AO"},
    "AO": {"AO", "AA"},
}


@dataclass
class MfaPhone:
    phone: str
    start: float
    end: float


@dataclass
class MfaTarget:
    phone: str
    candidates: list[str]
    matched: bool
    first_seen: float | None
    heard: str


def normalize_mfa_phone(phone: str) -> str:
    return "".join(ch for ch in phone.upper().strip() if not ch.isdigit())


def prepare_corpus(source_dir: Path, corpus_dir: Path) -> list[Path]:
    corpus_dir.mkdir(parents=True, exist_ok=True)
    wavs = sorted(source_dir.glob("*.wav"))
    if not wavs:
        raise SystemExit(f"No WAV files found in {source_dir}")

    for wav_path in wavs:
        word = wav_path.stem.upper()
        shutil.copy2(wav_path, corpus_dir / wav_path.name)
        (corpus_dir / f"{word}.txt").write_text(TRANSCRIPT_OVERRIDES.get(word, word), encoding="utf-8")
    return wavs


def parse_phone_csv(csv_path: Path) -> list[MfaPhone]:
    if not csv_path.exists():
        return []

    phones: list[MfaPhone] = []
    with csv_path.open("r", encoding="utf-8", newline="") as handle:
        for row in csv.DictReader(handle):
            if row.get("Type") != "phones":
                continue
            label = normalize_mfa_phone(row.get("Label", ""))
            if not label or label in {"SIL", "SP", "SPN"}:
                continue
            phones.append(MfaPhone(phone=label, start=float(row["Begin"]), end=float(row["End"])))
    return phones


def ordered_match(word: str, phones: list[MfaPhone]) -> list[MfaTarget]:
    targets = tester.WORD_PHONES.get(tester.normalize_word(word), tester.build_word_segments(word))
    search_from = 0
    results: list[MfaTarget] = []
    for target_phone in targets:
        candidates = PHONE_EQUIVALENTS.get(target_phone, {target_phone})
        matched_index = None
        for index in range(search_from, len(phones)):
            if phones[index].phone in candidates:
                matched_index = index
                break

        if matched_index is None:
            results.append(MfaTarget(target_phone, sorted(candidates), False, None, ""))
            continue

        heard_phone = phones[matched_index]
        results.append(MfaTarget(target_phone, sorted(candidates), True, heard_phone.start, heard_phone.phone))
        search_from = matched_index + 1

    return results


def run_mfa(args: argparse.Namespace) -> None:
    output_dir = args.output_dir
    output_dir.mkdir(parents=True, exist_ok=True)
    command = [
        args.conda,
        "run",
        "-n",
        args.conda_env,
        "mfa",
        "align",
        str(args.corpus_dir),
        args.dictionary,
        args.acoustic_model,
        str(output_dir),
        "--output_format",
        "csv",
        "--clean",
        "--single_speaker",
        "--overwrite",
        "--num_jobs",
        str(args.num_jobs),
    ]
    if not args.quiet:
        print("Running:", " ".join(command))
    subprocess.run(command, cwd=REPO_ROOT, check=True)


def main() -> int:
    parser = argparse.ArgumentParser(description="Evaluate labelled spell WAV assets with MFA forced alignment.")
    parser.add_argument("--dir", type=Path, default=REPO_ROOT / "Assets" / "Audio" / "Pronunciations" / "Spells")
    parser.add_argument("--corpus-dir", type=Path, default=REPO_ROOT / "Tools" / "MfaPhoneticTester" / "spell-corpus")
    parser.add_argument("--output-dir", type=Path, default=REPO_ROOT / "Tools" / "MfaPhoneticTester" / "spell-output")
    parser.add_argument("--csv", type=Path, default=REPO_ROOT / "Tools" / "MfaPhoneticTester" / "asset-eval-mfa-english-us-arpa.csv")
    parser.add_argument("--json", type=Path, default=REPO_ROOT / "Tools" / "MfaPhoneticTester" / "asset-eval-mfa-english-us-arpa.json")
    parser.add_argument("--conda", default=str(Path.home() / "scoop" / "apps" / "miniconda3" / "current" / "Scripts" / "conda.exe"))
    parser.add_argument("--conda-env", default="mfa-phonetic")
    parser.add_argument("--dictionary", default="english_us_arpa")
    parser.add_argument("--acoustic-model", default="english_us_arpa")
    parser.add_argument("--num-jobs", type=int, default=3)
    parser.add_argument("--skip-align", action="store_true", help="Only parse an existing MFA output directory.")
    parser.add_argument("--quiet", action="store_true")
    args = parser.parse_args()

    wavs = prepare_corpus(args.dir, args.corpus_dir)
    if not args.skip_align:
        run_mfa(args)

    rows = []
    reports = []
    for wav_path in wavs:
        word = wav_path.stem.upper()
        phone_csv = args.output_dir / f"{word}.csv"
        phones = parse_phone_csv(phone_csv)
        target = ordered_match(word, phones)
        total = len(target)
        matched = sum(1 for segment in target if segment.matched)
        coverage = matched / total if total else 0.0
        observed = " ".join(phone.phone for phone in phones)
        missed = " ".join(segment.phone for segment in target if not segment.matched)
        status = "ok" if phones else "no_alignment"

        row = {
            "word": word,
            "status": status,
            "matched": matched,
            "total": total,
            "coverage": round(coverage, 4),
            "observed": observed,
            "missed": missed,
            "path": str(wav_path),
        }
        rows.append(row)
        reports.append({**row, "phones": [asdict(phone) for phone in phones], "target": [asdict(segment) for segment in target]})
        if not args.quiet:
            print(f"{word:<12} {matched:>2}/{total:<2} {coverage:>6.0%} {status:<12} {observed}")

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
    print(f"Files: {len(rows)}  usable: {len(ok_rows)}  no_alignment: {len(rows) - len(ok_rows)}")
    print(f"Exact: {exact}  partial: {partial}  zero: {zero}  average coverage: {average:.1%}")
    print(f"CSV: {args.csv}")
    print(f"JSON: {args.json}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
