import re
from typing import Any

import numpy as np
from fastapi import HTTPException

EXPECTED_PHONEMES: dict[str, list[str]] = {
    "ANT": ["æ", "n", "t"],
    "APPLE": ["æ", "p", "l"],
    "ARM": ["ɑ", "r", "m"],
    "BAG": ["b", "æ", "g"],
    "BALL": ["b", "ɔ", "l"],
    "BAT": ["b", "æ", "t"],
    "BED": ["b", "ɛ", "d"],
    "BELL": ["b", "ɛ", "l"],
    "BIRD": ["b", "ɝ", "d"],
    "BOAT": ["b", "oʊ", "t"],
    "BOOK": ["b", "ʊ", "k"],
    "BOX": ["b", "ɑ", "k", "s"],
    "BROOM": ["b", "r", "u", "m"],
    "BUS": ["b", "ʌ", "s"],
    "CAP": ["k", "æ", "p"],
    "CAR": ["k", "ɑ", "r"],
    "CAT": ["k", "æ", "t"],
    "COW": ["k", "aʊ"],
    "CUP": ["k", "ʌ", "p"],
    "DOG": ["d", "ɔ", "g"],
    "DOLL": ["d", "ɑ", "l"],
    "DOOR": ["d", "ɔ", "r"],
    "DRUM": ["d", "r", "ʌ", "m"],
    "DUCK": ["d", "ʌ", "k"],
    "EAR": ["ɪ", "r"],
    "EGG": ["ɛ", "g"],
    "EYE": ["aɪ"],
    "FAN": ["f", "æ", "n"],
    "FISH": ["f", "ɪ", "ʃ"],
    "FOX": ["f", "ɑ", "k", "s"],
    "GOAT": ["g", "oʊ", "t"],
    "GRAPES": ["g", "r", "eɪ", "p", "s"],
    "GUM": ["g", "ʌ", "m"],
    "HAT": ["h", "æ", "t"],
    "HEN": ["h", "ɛ", "n"],
    "ICE": ["aɪ", "s"],
    "INK": ["ɪ", "ŋ", "k"],
    "JAM": ["dʒ", "æ", "m"],
    "JAR": ["dʒ", "ɑ", "r"],
    "JUG": ["dʒ", "ʌ", "g"],
    "KEY": ["k", "i"],
    "KING": ["k", "ɪ", "ŋ"],
    "KITE": ["k", "aɪ", "t"],
    "LEG": ["l", "ɛ", "g"],
    "LION": ["l", "aɪ", "ə", "n"],
    "LOG": ["l", "ɑ", "g"],
    "MAT": ["m", "æ", "t"],
    "MOON": ["m", "u", "n"],
    "MOP": ["m", "ɑ", "p"],
    "MUG": ["m", "ʌ", "g"],
    "NEST": ["n", "ɛ", "s", "t"],
    "NET": ["n", "ɛ", "t"],
    "NOSE": ["n", "oʊ", "z"],
    "NUT": ["n", "ʌ", "t"],
    "OWL": ["aʊ", "l"],
    "OX": ["ɑ", "k", "s"],
    "PAN": ["p", "æ", "n"],
    "PEG": ["p", "ɛ", "g"],
    "PEN": ["p", "ɛ", "n"],
    "PIG": ["p", "ɪ", "g"],
    "PIN": ["p", "ɪ", "n"],
    "POT": ["p", "ɑ", "t"],
    "QUAIL": ["k", "w", "eɪ", "l"],
    "QUILT": ["k", "w", "ɪ", "l", "t"],
    "RAIN": ["r", "eɪ", "n"],
    "RAT": ["r", "æ", "t"],
    "RING": ["r", "ɪ", "ŋ"],
    "RUG": ["r", "ʌ", "g"],
    "SOCK": ["s", "ɑ", "k"],
    "SPOON": ["s", "p", "u", "n"],
    "STAR": ["s", "t", "ɑ", "r"],
    "SUN": ["s", "ʌ", "n"],
    "TIGER": ["t", "aɪ", "g", "ɝ"],
    "TIN": ["t", "ɪ", "n"],
    "THIN": ["θ", "ɪ", "n"],
    "TOP": ["t", "ɑ", "p"],
    "TOY": ["t", "ɔɪ"],
    "TREE": ["t", "r", "i"],
    "UMBRELLA": ["ʌ", "m", "b", "r", "ɛ", "l", "ə"],
    "UNICORN": ["j", "u", "n", "ɪ", "k", "ɔ", "r", "n"],
    "VAN": ["v", "æ", "n"],
    "VASE": ["v", "eɪ", "s"],
    "WALL": ["w", "ɔ", "l"],
    "WATCH": ["w", "ɑ", "tʃ"],
    "XRAY": ["ɛ", "k", "s", "r", "eɪ"],
    "XYLOPHONE": ["z", "aɪ", "l", "ə", "f", "oʊ", "n"],
    "YAK": ["j", "æ", "k"],
    "YOYO": ["j", "oʊ", "j", "oʊ"],
    "ZEBRA": ["z", "i", "b", "r", "ə"],
}

PHONEME_ALIASES = {
    "ɡ": "g",
    "ɹ": "r",
    "ɻ": "r",
    "ɫ": "l",
    "ɚ": "ɝ",
    "ɹ̩": "ɝ",
    "er": "ɝ",
    "ah": "ɑ",
    "aa": "ɑ",
    "ae": "æ",
    "eh": "ɛ",
    "ih": "ɪ",
    "iy": "i",
    "uh": "ʊ",
    "uw": "u",
    "ow": "oʊ",
    "aw": "aʊ",
    "ay": "aɪ",
    "ey": "eɪ",
    "oy": "ɔɪ",
    "sh": "ʃ",
    "ch": "tʃ",
    "jh": "dʒ",
    "ng": "ŋ",
}

PHONEME_SCAN_PATTERNS = [
    ("d͡ʒ", "dʒ"),
    ("t͡ʃ", "tʃ"),
    ("ɹ̩", "ɝ"),
    ("ow", "oʊ"),
    ("ou", "oʊ"),
    ("ej", "eɪ"),
    ("ei", "eɪ"),
    ("aj", "aɪ"),
    ("ai", "aɪ"),
    ("aw", "aʊ"),
    ("au", "aʊ"),
    ("ɔj", "ɔɪ"),
    ("ɔi", "ɔɪ"),
    ("dʒ", "dʒ"),
    ("tʃ", "tʃ"),
    ("oʊ", "oʊ"),
    ("eɪ", "eɪ"),
    ("aɪ", "aɪ"),
    ("aʊ", "aʊ"),
    ("ɔɪ", "ɔɪ"),
    ("ʃ", "ʃ"),
    ("θ", "θ"),
    ("ð", "ð"),
    ("ŋ", "ŋ"),
    ("æ", "æ"),
    ("ɑ", "ɑ"),
    ("ɔ", "ɔ"),
    ("ʊ", "ʊ"),
    ("ʌ", "ʌ"),
    ("ɪ", "ɪ"),
    ("ɛ", "ɛ"),
    ("ə", "ə"),
    ("ɝ", "ɝ"),
    ("ɚ", "ɝ"),
    ("ɹ", "r"),
    ("ɻ", "r"),
    ("ɡ", "g"),
    ("i", "i"),
    ("u", "u"),
    ("b", "b"),
    ("p", "p"),
    ("d", "d"),
    ("t", "t"),
    ("g", "g"),
    ("k", "k"),
    ("f", "f"),
    ("v", "v"),
    ("s", "s"),
    ("z", "z"),
    ("m", "m"),
    ("n", "n"),
    ("l", "l"),
    ("r", "r"),
    ("h", "h"),
    ("w", "w"),
    ("j", "j"),
]

VOWELS = {"æ", "ɑ", "ɔ", "ʊ", "ʌ", "ɪ", "ɛ", "i", "u", "ə", "ɝ", "eɪ", "oʊ", "aɪ", "aʊ", "ɔɪ"}
CONSONANT_GROUPS = [
    {"p", "b"},
    {"t", "d"},
    {"k", "g"},
    {"f", "v"},
    {"s", "z"},
    {"ʃ", "tʃ", "dʒ"},
    {"θ", "ð"},
    {"m", "n", "ŋ"},
]

def resample_audio(audio: np.ndarray, source_rate: int, target_rate: int) -> np.ndarray:
    if source_rate <= 0:
        raise HTTPException(status_code=400, detail="WAV file has an invalid sample rate.")
    if audio.size == 0:
        raise HTTPException(status_code=400, detail="WAV file is empty.")

    duration = audio.shape[0] / float(source_rate)
    target_length = max(1, int(round(duration * target_rate)))
    source_positions = np.linspace(0.0, duration, num=audio.shape[0], endpoint=False)
    target_positions = np.linspace(0.0, duration, num=target_length, endpoint=False)
    return np.interp(target_positions, source_positions, audio).astype(np.float32)


def expected_phonemes_for(target_text: str) -> list[str]:
    word = normalize_word(target_text)
    if not word:
        return []
    if word in EXPECTED_PHONEMES:
        return EXPECTED_PHONEMES[word]
    return approximate_expected_phonemes(word)


def approximate_expected_phonemes(word: str) -> list[str]:
    chunks: list[str] = []
    i = 0
    while i < len(word):
        tail = word[i:]
        if tail.startswith("TH"):
            chunks.append("θ")
            i += 2
        elif tail.startswith("SH"):
            chunks.append("ʃ")
            i += 2
        elif tail.startswith("CH"):
            chunks.append("tʃ")
            i += 2
        elif tail.startswith("PH"):
            chunks.append("f")
            i += 2
        elif tail.startswith("CK"):
            chunks.append("k")
            i += 2
        elif tail.startswith("NG"):
            chunks.append("ŋ")
            i += 2
        elif tail.startswith("QU"):
            chunks.extend(["k", "w"])
            i += 2
        elif tail.startswith(("EE", "EA")):
            chunks.append("i")
            i += 2
        elif tail.startswith(("AI", "AY")):
            chunks.append("eɪ")
            i += 2
        elif tail.startswith("OA"):
            chunks.append("oʊ")
            i += 2
        elif tail.startswith("OO"):
            chunks.append("u")
            i += 2
        elif tail.startswith(("OW", "OU")):
            chunks.append("aʊ")
            i += 2
        else:
            phoneme = {
                "A": "æ", "B": "b", "C": "k", "D": "d", "E": "ɛ", "F": "f",
                "G": "g", "H": "h", "I": "ɪ", "J": "dʒ", "K": "k", "L": "l",
                "M": "m", "N": "n", "O": "ɑ", "P": "p", "Q": "k", "R": "r",
                "S": "s", "T": "t", "U": "ʌ", "V": "v", "W": "w", "X": "k",
                "Y": "j", "Z": "z",
            }.get(word[i])
            if phoneme:
                chunks.append(phoneme)
            i += 1

    if len(word) > 1 and word.endswith("E") and chunks:
        chunks = chunks[:-1]
    return chunks


def normalize_phoneme_text(value: str) -> list[str]:
    cleaned = re.sub(r"[|/,_]+", " ", value or "").strip().lower()
    tokens: list[str] = []
    for chunk in cleaned.split():
        tokens.extend(split_phoneme_chunk(chunk))
    return [normalize_phoneme(token) for token in tokens if token]


def normalize_phoneme(value: str) -> str:
    token = re.sub(r"[0-9ˈˌ.]+", "", value.strip().lower())
    return PHONEME_ALIASES.get(token, token)


def split_phoneme_chunk(value: str) -> list[str]:
    token = re.sub(r"[0-9ˈˌ.]+", "", value.strip().lower())
    token = token.replace("ː", "")
    if not token:
        return []
    if token in PHONEME_ALIASES:
        return [PHONEME_ALIASES[token]]

    phones: list[str] = []
    index = 0
    while index < len(token):
        matched = False
        for pattern, phone in PHONEME_SCAN_PATTERNS:
            if token.startswith(pattern, index):
                phones.append(phone)
                index += len(pattern)
                matched = True
                break
        if not matched:
            index += 1
    return phones


def normalize_word(value: str) -> str:
    return "".join(c.upper() for c in value or "" if c.isalpha())


def score_pronunciation(expected: list[str], observed: list[str], model_confidence: float) -> dict[str, Any]:
    if not expected:
        return {
            "score": round(float(model_confidence), 4),
            "alignment": [],
            "issues": [],
            "message": "WavLM heard phonemes, but no target phoneme profile was available.",
        }

    alignment = align_phonemes(expected, observed)
    matched = sum(1 for item in alignment if item["status"] == "matched")
    close = sum(1 for item in alignment if item["status"] == "close")
    substitutions = sum(1 for item in alignment if item["status"] == "substituted")
    missing = sum(1 for item in alignment if item["status"] == "missing")
    extra = sum(1 for item in alignment if item["status"] == "extra")

    raw_points = matched + close * 0.72 + substitutions * 0.22
    score = raw_points / max(1, len(expected))
    score -= min(0.35, extra * 0.08)
    score = max(0.0, min(1.0, score))

    issues = [
        item for item in alignment
        if item["status"] in {"close", "substituted", "missing", "extra"}
    ]
    message = pronunciation_message(score, issues)
    return {
        "score": round(score, 4),
        "alignment": alignment,
        "issues": issues,
        "message": message,
    }


def align_phonemes(expected: list[str], observed: list[str]) -> list[dict[str, Any]]:
    rows = len(expected) + 1
    cols = len(observed) + 1
    dp = [[0.0 for _ in range(cols)] for _ in range(rows)]
    back: list[list[str]] = [["" for _ in range(cols)] for _ in range(rows)]

    for i in range(1, rows):
        dp[i][0] = i
        back[i][0] = "missing"
    for j in range(1, cols):
        dp[0][j] = j * 0.75
        back[0][j] = "extra"

    for i in range(1, rows):
        for j in range(1, cols):
            sim = phoneme_similarity(expected[i - 1], observed[j - 1])
            candidates = [
                (dp[i - 1][j - 1] + (1.0 - sim), "pair"),
                (dp[i - 1][j] + 1.0, "missing"),
                (dp[i][j - 1] + 0.75, "extra"),
            ]
            dp[i][j], back[i][j] = min(candidates, key=lambda item: item[0])

    alignment: list[dict[str, Any]] = []
    i = len(expected)
    j = len(observed)
    while i > 0 or j > 0:
        step = back[i][j]
        if step == "pair":
            expected_phone = expected[i - 1]
            observed_phone = observed[j - 1]
            sim = phoneme_similarity(expected_phone, observed_phone)
            status = "matched" if sim >= 0.98 else "close" if sim >= 0.55 else "substituted"
            alignment.append({
                "expected": expected_phone,
                "observed": observed_phone,
                "status": status,
                "confidence": round(sim, 4),
            })
            i -= 1
            j -= 1
        elif step == "missing":
            alignment.append({
                "expected": expected[i - 1],
                "observed": "",
                "status": "missing",
                "confidence": 0.0,
            })
            i -= 1
        else:
            alignment.append({
                "expected": "",
                "observed": observed[j - 1],
                "status": "extra",
                "confidence": 0.0,
            })
            j -= 1

    alignment.reverse()
    return alignment


def phoneme_similarity(left: str, right: str) -> float:
    left = normalize_phoneme(left)
    right = normalize_phoneme(right)
    if left == right:
        return 1.0
    if left in VOWELS and right in VOWELS:
        return 0.58
    for group in CONSONANT_GROUPS:
        if left in group and right in group:
            return 0.62
    if left in right or right in left:
        return 0.5
    return 0.0


def pronunciation_message(score: float, issues: list[dict[str, Any]]) -> str:
    if not issues and score >= 0.9:
        return "Pronunciation matched the expected phonemes."
    if score >= 0.75:
        return "Pronunciation was close; review the highlighted sound."
    if score >= 0.45:
        return "Some target sounds were unclear or different."
    return "Several target sounds were missing or different; ask the student to try again slowly."


def build_pronunciation_insight(result: dict[str, Any]) -> dict[str, Any]:
    segments = build_segments(result)
    return {
        "providerName": "WavLM Cloud Run",
        "targetWord": result.get("targetText", ""),
        "confirmedWord": "",
        "rawRecognizedText": result.get("phonemeText", ""),
        "voskConfirmedWord": False,
        "attemptedTarget": True,
        "score": result.get("score", 0),
        "hintKey": "TryAgain" if result.get("score", 0) < 0.65 else "GreatTry",
        "message": result.get("message", ""),
        "segments": segments,
        "syllableBeats": [],
    }


def build_segments(result: dict[str, Any]) -> list[dict[str, Any]]:
    segments = []
    for item in result.get("alignment", []):
        expected = str(item.get("expected") or "")
        observed = str(item.get("observed") or "")
        status = str(item.get("status") or "")
        if not expected:
            continue
        segments.append({
            "spelling": expected,
            "friendlySound": expected,
            "heardSound": observed,
            "beatIndex": 0,
            "status": "Matched" if status == "matched" else "Missing" if status == "missing" else "NeedsPractice",
            "confidence": float(item.get("confidence") or 0.0),
        })
    return segments
