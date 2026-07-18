#!/usr/bin/env python3
"""Run Allosaurus against live or WAV audio using the best local settings."""

from __future__ import annotations

import argparse
import json
import sys
import tempfile
from dataclasses import asdict
from pathlib import Path

import numpy as np

import evaluate_allosaurus_assets as allosaurus_eval
import phonetic_tester as tester


if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
if hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")

DEFAULT_ALLOSAURUS_MODEL = "eng2102"
DEFAULT_ALLOSAURUS_LANG = "eng"
DEFAULT_ALLOSAURUS_EMIT = 0.75


def load_recognizer(model_name: str, lang: str):
    from argparse import Namespace

    from allosaurus.app import read_recognizer
    from allosaurus.bin.download_model import download_model
    from allosaurus.model import resolve_model_name

    resolved = resolve_model_name(model_name)
    if resolved == "none":
        print(f"Allosaurus model '{model_name}' is not installed; downloading it now...")
        download_model(model_name)
        resolved = resolve_model_name(model_name)

    if resolved == "none":
        raise RuntimeError(f"Allosaurus model '{model_name}' could not be resolved.")

    config = Namespace(model=resolved, device_id=-1, lang=lang, approximate=False, prior=None)
    return resolved, read_recognizer(config)


def save_audio_for_allosaurus(
    audio: np.ndarray,
    source_wav: Path | None,
    attempt_dir: Path | None,
    word: str,
    suffix: str,
) -> tuple[Path, bool]:
    saved = tester.save_attempt_audio(attempt_dir, audio, word, source_wav, suffix)
    if saved is not None:
        return saved, False

    handle = tempfile.NamedTemporaryFile(prefix="allosaurus-", suffix=".wav", delete=False)
    path = Path(handle.name)
    handle.close()
    tester.write_wav(path, audio, tester.SAMPLE_RATE)
    return path, True


def print_report(
    word: str,
    phones: list[allosaurus_eval.AllosaurusPhone],
    target: list[allosaurus_eval.AllosaurusTarget],
    model: str,
    lang: str,
    emit: float,
    audio_path: Path,
    temporary_audio: bool,
    trim_info: tester.TrimInfo,
    top_k: int,
) -> None:
    print()
    print(f"Allosaurus model: {model}  lang={lang}  emit={emit:0.2f}")
    print(
        "Audio window: "
        f"kept {trim_info.trimmed_seconds:0.2f}s "
        f"from {trim_info.original_seconds:0.2f}s "
        f"(peak {trim_info.peak:0.3f})"
    )
    if temporary_audio:
        print("Analyzed WAV: temporary audio file, deleted after analysis")
    else:
        print(f"Analyzed WAV: {audio_path}")
    print()
    print("Observed phones")
    print("---------------")
    for phone in phones[:top_k]:
        if phone.start is None:
            print(f"{phone.phone}")
        else:
            duration = phone.duration if phone.duration is not None else 0.0
            print(f"{phone.start:5.2f}s +{duration:0.3f}s  {phone.phone}")
    if len(phones) > top_k:
        print(f"... {len(phones) - top_k} more phones")

    if word:
        matched = sum(1 for segment in target if segment.matched)
        total = len(target)
        score = matched / total if total else 0.0
        print()
        print(f"Target coverage for {tester.normalize_word(word)}: {matched}/{total} ({score:.0%})")
        print("--------------------------------")
        for segment in target:
            status = "yes" if segment.matched else "no "
            candidates = "/".join(segment.candidates)
            heard = segment.heard or "--"
            first = "--" if segment.first_seen is None else f"{segment.first_seen:0.2f}s"
            print(
                f"{segment.phone:>3} -> {candidates:<12} "
                f"matched={status} first={first:>6} heard={heard}"
            )


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description="Run Allosaurus against live or WAV audio.")
    parser.add_argument("--word", default="", help="Expected game word, e.g. CAT, SHIP, THIN.")
    parser.add_argument("--wav", type=Path, help="Analyze a WAV file instead of recording the microphone.")
    parser.add_argument("--seconds", type=float, default=2.5, help="Seconds to record when --wav is not supplied.")
    parser.add_argument("--model", default=DEFAULT_ALLOSAURUS_MODEL, help="Allosaurus model name.")
    parser.add_argument("--lang", default=DEFAULT_ALLOSAURUS_LANG, help="Allosaurus language inventory.")
    parser.add_argument("--emit", type=float, default=DEFAULT_ALLOSAURUS_EMIT, help="Allosaurus emit setting.")
    parser.add_argument("--top-k", type=int, default=120, help="Maximum phones to print.")
    parser.add_argument("--json", type=Path, help="Optional path to save the full report as JSON.")
    parser.add_argument("--live", action="store_true", help="Listen until speech is detected, then stop after silence.")
    parser.add_argument("--live-max-seconds", type=float, default=5.0, help="Maximum duration for one live utterance.")
    parser.add_argument("--live-silence-seconds", type=float, default=0.6, help="Silence needed to end a live utterance.")
    parser.add_argument("--live-timeout", type=float, default=30.0, help="Maximum time to wait for speech in live mode.")
    parser.add_argument("--voice-threshold", type=float, default=0.015, help="Peak threshold for live voice detection.")
    parser.add_argument("--trim-to-voice", action="store_true", help="Trim to detected voice before Allosaurus recognition.")
    parser.add_argument("--save-trimmed-dir", type=Path, help="Optional folder to save the analyzed WAV for this attempt.")
    args = parser.parse_args(argv)

    source_wav = args.wav.resolve() if args.wav else None
    if source_wav:
        original_audio = tester.read_wav(source_wav)
    elif args.live:
        original_audio = tester.record_live_utterance(
            max_seconds=args.live_max_seconds,
            silence_seconds=args.live_silence_seconds,
            timeout_seconds=args.live_timeout,
            threshold=args.voice_threshold,
        )
    else:
        original_audio = tester.record_audio(args.seconds)
    if original_audio.size == 0:
        raise SystemExit("No audio samples were captured.")

    if args.trim_to_voice:
        audio, trim_info = tester.trim_to_voice(original_audio, tester.SAMPLE_RATE)
        if audio.size == 0:
            rejected_path = tester.save_attempt_audio(args.save_trimmed_dir, original_audio, args.word, source_wav, "no_voice_full")
            if rejected_path is not None:
                print(f"Saved rejected full capture to {rejected_path}")
            raise SystemExit("No clear voice activity was detected.")
        suffix = "trimmed"
    else:
        duration = original_audio.size / float(tester.SAMPLE_RATE)
        audio = original_audio
        trim_info = tester.TrimInfo(
            original_seconds=duration,
            trimmed_seconds=duration,
            start_seconds=0.0,
            end_seconds=duration,
            voiced_seconds=duration,
            peak=float(np.max(np.abs(audio))) if audio.size > 0 else 0.0,
        )
        suffix = "full"

    audio_path, temporary_audio = save_audio_for_allosaurus(audio, source_wav, args.save_trimmed_dir, args.word, suffix)
    model_name, recognizer = load_recognizer(args.model, args.lang)
    try:
        output = recognizer.recognize(str(audio_path), args.lang, topk=1, emit=args.emit, timestamp=True)
    finally:
        if temporary_audio:
            try:
                audio_path.unlink()
            except OSError:
                pass
    phones = allosaurus_eval.parse_allosaurus_output(output)
    target = allosaurus_eval.ordered_match(args.word, phones) if args.word else []

    print_report(args.word, phones, target, model_name, args.lang, args.emit, audio_path, temporary_audio, trim_info, args.top_k)

    if args.json:
        payload = {
            "word": tester.normalize_word(args.word),
            "engine": "allosaurus",
            "model": model_name,
            "lang": args.lang,
            "emit": args.emit,
            "duration_seconds": trim_info.trimmed_seconds,
            "trim": asdict(trim_info),
            "analyzed_wav": "" if temporary_audio else str(audio_path),
            "raw": output,
            "phones": [asdict(phone) for phone in phones],
            "target": [asdict(segment) for segment in target],
        }
        args.json.parent.mkdir(parents=True, exist_ok=True)
        args.json.write_text(json.dumps(payload, indent=2, ensure_ascii=False), encoding="utf-8")
        print(f"\nSaved JSON report to {args.json}")

    print()
    print("Accuracy note: Allosaurus is a free phone decoder; expect extra/noisy phones.")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main(sys.argv[1:]))
    except (RuntimeError, ValueError) as exc:
        print(f"Error: {exc}", file=sys.stderr)
        raise SystemExit(1)
