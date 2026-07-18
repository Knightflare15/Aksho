#!/usr/bin/env python3
"""Batch-evaluate labelled pronunciation WAV assets with Allosaurus."""

from __future__ import annotations

import argparse
import csv
import json
import unicodedata
from dataclasses import asdict, dataclass
from pathlib import Path

import phonetic_tester as tester


ARPABET_TO_ALLOSAURUS = {
    "AA": ["a", "ɑ", "ɒ"],
    "AE": ["æ", "a"],
    "AH": ["ʌ", "ə", "ɐ", "a"],
    "AO": ["ɔ", "ɒ", "ɑ", "o"],
    "AW": ["aʊ", "aw", "au"],
    "AY": ["aɪ", "ai", "aj"],
    "B": ["b"],
    "CH": ["tʃ", "t͡ʃ"],
    "D": ["d"],
    "DH": ["ð"],
    "EH": ["ɛ", "e", "æ"],
    "ER": ["ɝ", "ɚ", "ər", "r", "ɹ"],
    "EY": ["eɪ", "ei", "ej", "e"],
    "F": ["f"],
    "G": ["g", "ɡ"],
    "HH": ["h"],
    "IH": ["ɪ", "i"],
    "IY": ["i"],
    "JH": ["dʒ", "d͡ʒ", "ʒ"],
    "K": ["k"],
    "L": ["l"],
    "M": ["m"],
    "N": ["n"],
    "NG": ["ŋ"],
    "OW": ["oʊ", "ou", "ow", "o"],
    "OY": ["ɔɪ", "oi", "oj"],
    "P": ["p"],
    "R": ["r", "ɹ", "ɻ"],
    "S": ["s"],
    "SH": ["ʃ"],
    "T": ["t"],
    "TH": ["θ"],
    "UH": ["ʊ", "u"],
    "UW": ["u"],
    "V": ["v"],
    "W": ["w"],
    "Y": ["j"],
    "Z": ["z"],
    "ZH": ["ʒ"],
}


@dataclass
class AllosaurusPhone:
    phone: str
    normalized: str
    start: float | None
    duration: float | None


@dataclass
class AllosaurusTarget:
    phone: str
    candidates: list[str]
    matched: bool
    first_seen: float | None
    heard: str


def normalize_phone(phone: str) -> str:
    decomposed = unicodedata.normalize("NFD", phone.strip().lower())
    without_marks = "".join(ch for ch in decomposed if unicodedata.category(ch) != "Mn")
    return (
        without_marks.replace("ː", "")
        .replace("ˑ", "")
        .replace("ˈ", "")
        .replace("ˌ", "")
        .replace("͡", "")
    )


def candidates_for_arpabet(phone: str) -> list[str]:
    values = ARPABET_TO_ALLOSAURUS.get(phone, [phone.lower()])
    return sorted({normalize_phone(value) for value in values})


def parse_allosaurus_output(output: str) -> list[AllosaurusPhone]:
    phones: list[AllosaurusPhone] = []
    for raw_line in output.splitlines():
        line = raw_line.strip()
        if not line:
            continue

        parts = line.split()
        if len(parts) >= 3:
            try:
                start = float(parts[0])
                duration = float(parts[1])
                phone = " ".join(parts[2:])
            except ValueError:
                start = None
                duration = None
                phone = parts[-1]
        else:
            start = None
            duration = None
            phone = line

        phones.append(AllosaurusPhone(phone=phone, normalized=normalize_phone(phone), start=start, duration=duration))
    return phones


def ordered_match(word: str, phones: list[AllosaurusPhone]) -> list[AllosaurusTarget]:
    targets = tester.WORD_PHONES.get(tester.normalize_word(word), tester.build_word_segments(word))
    search_from = 0
    results: list[AllosaurusTarget] = []
    for target_phone in targets:
        candidates = candidates_for_arpabet(target_phone)
        matched_index = None
        for index in range(search_from, len(phones)):
            if phones[index].normalized in candidates:
                matched_index = index
                break

        if matched_index is None:
            results.append(
                AllosaurusTarget(
                    phone=target_phone,
                    candidates=candidates,
                    matched=False,
                    first_seen=None,
                    heard="",
                )
            )
            continue

        heard_phone = phones[matched_index]
        results.append(
            AllosaurusTarget(
                phone=target_phone,
                candidates=candidates,
                matched=True,
                first_seen=heard_phone.start,
                heard=heard_phone.phone,
            )
        )
        search_from = matched_index + 1

    return results


def main() -> int:
    parser = argparse.ArgumentParser(description="Evaluate labelled spell WAV assets against Allosaurus.")
    parser.add_argument(
        "--dir",
        type=Path,
        default=tester.repo_root() / "Assets" / "Audio" / "Pronunciations" / "Spells",
        help="Directory containing labelled WAV files.",
    )
    parser.add_argument("--lang", default="stan1293", help="Allosaurus language inventory. Use 'ipa' for universal inventory.")
    parser.add_argument("--model", default="latest", help="Allosaurus model name.")
    parser.add_argument("--emit", type=float, default=1.0, help="Allosaurus emit setting; larger emits more phones.")
    parser.add_argument("--topk", type=int, default=1)
    parser.add_argument("--quiet", action="store_true", help="Only print the final summary.")
    parser.add_argument("--csv", type=Path, default=Path("Tools/Wav2Vec2PhoneticTester/asset-eval-allosaurus.csv"))
    parser.add_argument("--json", type=Path, default=Path("Tools/Wav2Vec2PhoneticTester/asset-eval-allosaurus.json"))
    args = parser.parse_args()

    from argparse import Namespace

    from allosaurus.app import read_recognizer
    from allosaurus.model import resolve_model_name

    wavs = sorted(args.dir.glob("*.wav"))
    if not wavs:
        raise SystemExit(f"No WAV files found in {args.dir}")

    config = Namespace(
        model=resolve_model_name(args.model),
        device_id=-1,
        lang=args.lang,
        approximate=False,
        prior=None,
    )
    recognizer = read_recognizer(config)

    rows = []
    reports = []
    for wav_path in wavs:
        word = wav_path.stem.upper()
        output = recognizer.recognize(str(wav_path), args.lang, args.topk, args.emit, timestamp=True)
        phones = parse_allosaurus_output(output)
        target = ordered_match(word, phones)

        total = len(target)
        matched = sum(1 for segment in target if segment.matched)
        coverage = matched / total if total else 0.0
        observed = " ".join(phone.phone for phone in phones)
        missed = " ".join(segment.phone for segment in target if not segment.matched)

        row = {
            "word": word,
            "status": "ok",
            "matched": matched,
            "total": total,
            "coverage": round(coverage, 4),
            "model": config.model,
            "lang": args.lang,
            "emit": args.emit,
            "observed": observed,
            "missed": missed,
            "path": str(wav_path),
        }
        rows.append(row)
        reports.append(
            {
                **row,
                "raw": output,
                "phones": [asdict(phone) for phone in phones],
                "target": [asdict(segment) for segment in target],
            }
        )
        if not args.quiet:
            print(f"{word:<12} {matched:>2}/{total:<2} {coverage:>6.0%} ok       {observed}")

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
    print(f"Files: {len(rows)}  usable: {len(rows)}")
    print(f"Exact: {exact}  partial: {partial}  zero: {zero}  average coverage: {average:.1%}")
    print(f"CSV: {args.csv}")
    print(f"JSON: {args.json}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
