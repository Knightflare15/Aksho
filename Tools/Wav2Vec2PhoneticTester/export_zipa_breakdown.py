#!/usr/bin/env python3
"""Export per-word ZIPA expected/heard phone breakdown from benchmark JSON."""

from __future__ import annotations

import argparse
import csv
import json
from pathlib import Path


def segment_score(target: dict) -> float:
    if target.get("matched"):
        return 1.0
    try:
        return float(target.get("confidence") or 0.0)
    except (TypeError, ValueError):
        return 0.0


def main() -> int:
    parser = argparse.ArgumentParser(description="Export ZIPA spell phone breakdown CSV.")
    parser.add_argument("--json", type=Path, default=Path("Tools/Wav2Vec2PhoneticTester/phone-model-benchmark.json"))
    parser.add_argument("--csv", type=Path, default=Path("Tools/Wav2Vec2PhoneticTester/zipa-spell-phonetic-breakdown.csv"))
    parser.add_argument("--label", default="zipa-large-crctc-int8")
    args = parser.parse_args()

    reports = json.loads(args.json.read_text(encoding="utf-8"))
    rows = [report for report in reports if report.get("label") == args.label]
    args.csv.parent.mkdir(parents=True, exist_ok=True)

    fields = [
        "word",
        "coverage",
        "close_score",
        "matched",
        "total",
        "expected_phones",
        "observed_phones",
        "target_breakdown",
        "missed",
        "wav_path",
    ]
    with args.csv.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=fields)
        writer.writeheader()
        for report in rows:
            targets = report.get("target") or []
            phones = report.get("phones") or []
            scores = [segment_score(target) for target in targets]
            close_score = sum(scores) / len(scores) if scores else 0.0
            parts = []
            for target, score in zip(targets, scores):
                phone = target.get("phone") or ""
                heard = target.get("heard") or "--"
                status = "match" if target.get("matched") else "close" if score >= 0.35 else "miss"
                parts.append(f"{phone}->{heard}:{status}:{score:.2f}")

            writer.writerow(
                {
                    "word": report.get("word", ""),
                    "coverage": report.get("coverage", ""),
                    "close_score": round(close_score, 4),
                    "matched": report.get("matched", ""),
                    "total": report.get("total", ""),
                    "expected_phones": " ".join(target.get("phone") or "" for target in targets),
                    "observed_phones": " ".join(phone.get("phone") or "" for phone in phones),
                    "target_breakdown": " | ".join(parts),
                    "missed": report.get("missed", ""),
                    "wav_path": report.get("path", ""),
                }
            )

    print(f"CSV: {args.csv}")
    print(f"Rows: {len(rows)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
