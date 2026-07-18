#!/usr/bin/env python3
"""Run Hugging Face CTC phone recognizers such as WavLM and HuBERT."""

from __future__ import annotations

import argparse
import importlib.util
import json
import os
import sys
import unicodedata
import warnings
from dataclasses import asdict, dataclass
from pathlib import Path

import numpy as np
import phonetic_tester as tester

os.environ.setdefault("HF_HUB_DISABLE_PROGRESS_BARS", "1")
os.environ.setdefault("HF_HUB_DISABLE_SYMLINKS_WARNING", "1")
os.environ.setdefault("TRANSFORMERS_NO_ADVISORY_WARNINGS", "1")
warnings.filterwarnings("ignore", message="You are sending unauthenticated requests to the HF Hub.*")
warnings.filterwarnings("ignore", message="`huggingface_hub` cache-system uses symlinks.*")


DEFAULT_MODELS = {
    "wavlm": "speech31/wavlm-large-english-phoneme",
    "wavlm-base-plus-fr-it": "hugofara/wavlm-base-plus-phonemizer-fr-it",
    "hubert": "addy88/hubert-base-timit-demo-colab",
    "wav2vec2": "mostafaashahin/wav2vec2-base-timit-phoneme-arpa-39-v2",
}


ARPABET_TO_CTC = {
    "AA": ["ɑ", "a", "aa"],
    "AE": ["æ", "a", "ae"],
    "AH": ["ʌ", "ə", "ah"],
    "AO": ["ɔ", "ɑ", "ao"],
    "AW": ["aw", "aʊ", "au"],
    "AY": ["aj", "aɪ", "ai", "ay"],
    "B": ["b"],
    "CH": ["t͡ʃ", "tʃ", "ch"],
    "D": ["d"],
    "DH": ["ð", "dh"],
    "EH": ["ɛ", "e", "eh"],
    "ER": ["ɹ̩", "ɚ", "ɝ", "əɹ", "er", "r"],
    "EY": ["ej", "eɪ", "ei", "ey"],
    "F": ["f"],
    "G": ["ɡ", "g"],
    "HH": ["h", "hh"],
    "IH": ["ɪ", "i", "ih"],
    "IY": ["i", "iy"],
    "JH": ["d͡ʒ", "dʒ", "jh"],
    "K": ["k"],
    "L": ["l"],
    "M": ["m"],
    "N": ["n"],
    "NG": ["ŋ", "ng"],
    "OW": ["ow", "oʊ", "ou"],
    "OY": ["oj", "ɔɪ", "oi", "oy"],
    "P": ["p"],
    "R": ["ɹ", "r"],
    "S": ["s"],
    "SH": ["ʃ", "sh"],
    "T": ["t"],
    "TH": ["θ", "th"],
    "UH": ["ʊ", "u", "uh"],
    "UW": ["u", "uw"],
    "V": ["v"],
    "W": ["w"],
    "Y": ["j", "y"],
    "Z": ["z"],
    "ZH": ["ʒ", "zh"],
}


@dataclass
class CtcPhone:
    phone: str
    normalized: str
    start: float | None
    duration: float | None
    confidence: float | None


@dataclass
class CtcTarget:
    phone: str
    candidates: list[str]
    matched: bool
    first_seen: float | None
    heard: str
    confidence: float | None


def resolve_model(backend: str, model: str | None) -> str:
    if model:
        return model
    if backend not in DEFAULT_MODELS:
        raise ValueError(f"Unknown backend '{backend}'. Choose one of: {', '.join(DEFAULT_MODELS)}")
    return DEFAULT_MODELS[backend]


def normalize_phone(phone: str) -> str:
    phone = phone.strip().lower()
    phone = phone.replace("ː", "").replace("ˑ", "").replace("ˈ", "").replace("ˌ", "")
    phone = phone.replace("͡", "")
    decomposed = unicodedata.normalize("NFD", phone)
    return "".join(ch for ch in decomposed if unicodedata.category(ch) != "Mn")


def candidates_for_arpabet(phone: str) -> list[str]:
    values = ARPABET_TO_CTC.get(phone, [phone.lower()])
    return sorted({normalize_phone(value) for value in values})


def canonical_phone(phone: str) -> str:
    phone = normalize_phone(phone)
    aliases = {
        "æ": "ae",
        "ɑ": "aa",
        "ɒ": "aa",
        "ʌ": "ah",
        "ə": "ah",
        "ɔ": "ao",
        "ɛ": "eh",
        "ɪ": "ih",
        "i": "iy",
        "ʊ": "uh",
        "u": "uw",
        "ʃ": "sh",
        "ʒ": "zh",
        "θ": "th",
        "ð": "dh",
        "ŋ": "ng",
        "ɹ": "r",
        "ɾ": "r",
        "tʃ": "ch",
        "dʒ": "jh",
    }
    return aliases.get(phone, phone)


def same_pair(left: str, right: str, a: str, b: str) -> bool:
    return (left == a and right == b) or (left == b and right == a)


def is_vowel_phone(phone: str) -> bool:
    return canonical_phone(phone).upper() in {
        "AA", "AE", "AH", "AO", "AW", "AY", "EH", "ER", "EY", "IH", "IY", "OW", "OY", "UH", "UW",
    } or canonical_phone(phone) in {"aa", "ae", "ah", "ao", "aw", "ay", "eh", "er", "ey", "ih", "iy", "ow", "oy", "uh", "uw"}


def phone_similarity(target_phone: str, candidates: list[str], heard_phone: str) -> float:
    heard = normalize_phone(heard_phone)
    if not heard:
        return 0.0
    if heard in candidates:
        return 1.0

    target = canonical_phone(target_phone).lower()
    heard = canonical_phone(heard).lower()
    if target == heard:
        return 1.0

    if same_pair(target, heard, "t", "d") or same_pair(target, heard, "k", "g") or same_pair(target, heard, "p", "b"):
        return 0.72
    if same_pair(target, heard, "f", "v") or same_pair(target, heard, "s", "z"):
        return 0.72
    if same_pair(target, heard, "sh", "zh") or same_pair(target, heard, "ch", "jh"):
        return 0.72
    if same_pair(target, heard, "th", "t") or same_pair(target, heard, "dh", "d"):
        return 0.62
    if same_pair(target, heard, "th", "f") or same_pair(target, heard, "dh", "z") or same_pair(target, heard, "ng", "n"):
        return 0.62
    if is_vowel_phone(target) and is_vowel_phone(heard):
        return 0.58
    if same_pair(target, heard, "r", "l") or same_pair(target, heard, "w", "y"):
        return 0.45
    return 0.0


def closest_phone(target_phone: str, candidates: list[str], phones: list[CtcPhone], start_index: int) -> tuple[int | None, float]:
    best_index = None
    best_score = 0.0
    for index in range(start_index, min(len(phones), start_index + 4)):
        phone = phones[index]
        similarity = phone_similarity(target_phone, candidates, phone.phone)
        confidence = phone.confidence if phone.confidence is not None else 0.75
        score = similarity * (0.75 + 0.25 * confidence)
        if score > best_score:
            best_index = index
            best_score = score
    return best_index, best_score


def ordered_match(word: str, phones: list[CtcPhone]) -> list[CtcTarget]:
    targets = tester.WORD_PHONES.get(tester.normalize_word(word), tester.build_word_segments(word))
    search_from = 0
    results: list[CtcTarget] = []
    for target_phone in targets:
        candidates = candidates_for_arpabet(target_phone)
        matched_index = None
        for index in range(search_from, len(phones)):
            if phones[index].normalized in candidates:
                matched_index = index
                break

        if matched_index is None:
            close_index, close_score = closest_phone(target_phone, candidates, phones, search_from)
            heard_phone = phones[close_index] if close_index is not None and close_score >= 0.35 else None
            results.append(
                CtcTarget(
                    phone=target_phone,
                    candidates=candidates,
                    matched=False,
                    first_seen=heard_phone.start if heard_phone is not None else None,
                    heard=heard_phone.phone if heard_phone is not None else "",
                    confidence=close_score if heard_phone is not None else None,
                )
            )
            continue

        heard_phone = phones[matched_index]
        results.append(
            CtcTarget(
                phone=target_phone,
                candidates=candidates,
                matched=True,
                first_seen=heard_phone.start,
                heard=heard_phone.phone,
                confidence=heard_phone.confidence,
            )
        )
        search_from = matched_index + 1

    return results


def load_ctc(model_name: str):
    import torch
    from huggingface_hub import hf_hub_download
    from transformers import AutoFeatureExtractor, AutoModelForCTC, AutoProcessor, Wav2Vec2PhonemeCTCTokenizer, Wav2Vec2Processor
    from transformers.utils import logging

    logging.set_verbosity_error()

    if model_name == "hugofara/wavlm-base-plus-phonemizer-fr-it":
        module_path = hf_hub_download(model_name, "wavlm_phoneme_fr_it.py")
        spec = importlib.util.spec_from_file_location("wavlm_phoneme_fr_it", module_path)
        if spec is None or spec.loader is None:
            raise RuntimeError(f"Could not load custom model module from {module_path}")
        module = importlib.util.module_from_spec(spec)
        sys.modules["wavlm_phoneme_fr_it"] = module
        spec.loader.exec_module(module)
        processor = AutoProcessor.from_pretrained(model_name)
        model = module.WavLMPhonemeFrIt.from_pretrained(model_name)
        model._phoneme_language = "en"
        model.eval()
        return torch, processor, model

    if "espeak-cv-ft" in model_name or model_name in {
        "bookbot/wav2vec2-ljspeech-gruut",
    }:
        vocab_path = hf_hub_download(model_name, "vocab.json")
        tokenizer = Wav2Vec2PhonemeCTCTokenizer(
            vocab_path,
            do_phonemize=False,
            unk_token="[UNK]",
            pad_token="[PAD]",
            word_delimiter_token="|",
        )
        feature_extractor = AutoFeatureExtractor.from_pretrained(model_name)
        processor = Wav2Vec2Processor(feature_extractor=feature_extractor, tokenizer=tokenizer)
        model = AutoModelForCTC.from_pretrained(model_name)
        model.eval()
        return torch, processor, model

    processor = AutoProcessor.from_pretrained(model_name)
    model = AutoModelForCTC.from_pretrained(model_name)
    model.eval()
    return torch, processor, model


def token_from_id(processor, token_id: int) -> str:
    tokenizer = getattr(processor, "tokenizer", processor)
    if hasattr(tokenizer, "convert_ids_to_tokens"):
        token = tokenizer.convert_ids_to_tokens(int(token_id))
        if token is not None:
            return str(token)
    vocab = getattr(tokenizer, "get_vocab", lambda: {})()
    inverse = {value: key for key, value in vocab.items()}
    return inverse.get(int(token_id), str(token_id))


def special_token_ids(processor) -> set[int]:
    tokenizer = getattr(processor, "tokenizer", processor)
    ids: set[int] = set()
    for name in ("pad_token_id", "unk_token_id", "bos_token_id", "eos_token_id"):
        value = getattr(tokenizer, name, None)
        if value is not None:
            ids.add(int(value))
    return ids


def decode_phones(audio: np.ndarray, model_name: str) -> tuple[list[CtcPhone], str]:
    torch, processor, model = load_ctc(model_name)
    return decode_phones_loaded(audio, torch, processor, model)


def decode_phones_loaded(audio: np.ndarray, torch, processor, model) -> tuple[list[CtcPhone], str]:
    inputs = processor(audio, sampling_rate=tester.SAMPLE_RATE, return_tensors="pt")
    phoneme_language = getattr(model, "_phoneme_language", None)
    if phoneme_language is not None:
        inputs["language"] = phoneme_language
    with torch.no_grad():
        logits = model(**inputs).logits[0]
        probabilities = torch.softmax(logits, dim=-1)
        token_ids = torch.argmax(probabilities, dim=-1).cpu().tolist()
        confidences = torch.max(probabilities, dim=-1).values.cpu().tolist()

    ignored = special_token_ids(processor)
    frame_seconds = (audio.size / float(tester.SAMPLE_RATE)) / max(1, len(token_ids))
    phones: list[CtcPhone] = []
    previous: int | None = None
    active_start = 0
    active_confidences: list[float] = []

    def flush(token_id: int | None, end_frame: int) -> None:
        if token_id is None or token_id in ignored:
            return
        token = token_from_id(processor, token_id)
        if token in {"|", " ", ""} or token.startswith("[") or token.startswith("<"):
            return
        normalized = normalize_phone(token)
        if not normalized:
            return
        start = active_start * frame_seconds
        duration = max(frame_seconds, (end_frame - active_start) * frame_seconds)
        confidence = sum(active_confidences) / len(active_confidences) if active_confidences else None
        phones.append(
            CtcPhone(
                phone=token,
                normalized=normalized,
                start=start,
                duration=duration,
                confidence=confidence,
            )
        )

    for frame_index, token_id in enumerate(token_ids):
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


def print_report(word: str, backend: str, model_name: str, wav_path: Path, phones: list[CtcPhone], target: list[CtcTarget]) -> None:
    print()
    print(f"HF CTC model: {backend}  {model_name}")
    print(f"Analyzed WAV: {wav_path}")
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
    parser = argparse.ArgumentParser(description="Run a Hugging Face WavLM/HuBERT CTC phone recognizer.")
    parser.add_argument("--backend", choices=sorted(DEFAULT_MODELS), default="wavlm")
    parser.add_argument("--model", default=None, help="Override Hugging Face model id.")
    parser.add_argument("--word", default="")
    parser.add_argument("--wav", type=Path, required=True)
    parser.add_argument("--json", type=Path, default=None)
    args = parser.parse_args(argv)

    model_name = resolve_model(args.backend, args.model)
    audio = tester.read_wav(args.wav)
    phones, decoded = decode_phones(audio, model_name)
    target = ordered_match(args.word, phones) if args.word else []
    print_report(args.word, args.backend, model_name, args.wav, phones, target)

    if args.json:
        args.json.parent.mkdir(parents=True, exist_ok=True)
        args.json.write_text(
            json.dumps(
                {
                    "backend": args.backend,
                    "model": model_name,
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
    # Some Torch/Transformers builds on Windows leave non-daemon cleanup threads
    # alive after inference. Exit hard after normal cleanup so CLI/batch runs do
    # not hang after printing the completed report.
    import os

    code = main(sys.argv[1:])
    sys.stdout.flush()
    sys.stderr.flush()
    os._exit(code)
