"""Environment-backed worker configuration.

Keep model/provider selection out of the HTTP composition module so images can
switch reviewed providers through deployment configuration rather than source
edits. No provider is allowed to accept an arbitrary model identifier at
request time.
"""

import os

try:
    import torch
except Exception:  # pragma: no cover - the health route reports availability.
    torch = None


def _bounded_integer(name: str, fallback: int, minimum: int, maximum: int) -> int:
    try:
        parsed = int(os.getenv(name, str(fallback)))
    except (TypeError, ValueError):
        parsed = fallback
    return max(minimum, min(parsed, maximum))


def _enabled(name: str, fallback: bool = False) -> bool:
    default = "1" if fallback else "0"
    return os.getenv(name, default).strip().lower() in {"1", "true", "yes"}


MODEL_ID = os.getenv("WAVLM_MODEL_ID", "speech31/wavlm-large-english-phoneme")
DEVICE = os.getenv("WAVLM_DEVICE", "cuda" if torch is not None and torch.cuda.is_available() else "cpu")
SAMPLE_RATE = 16000

TRANSLATION_EXTERNAL_URL = os.getenv("TRANSLATION_EXTERNAL_URL", "").strip()
TTS_EXTERNAL_URL = os.getenv("TTS_EXTERNAL_URL", "").strip()
TRANSLATION_PROVIDER_NAME = os.getenv("TRANSLATION_PROVIDER_NAME", "Text fallback").strip()
TTS_PROVIDER_NAME = os.getenv("TTS_PROVIDER_NAME", "External TTS").strip()
TRANSLATION_FALLBACK_ENABLED = _enabled("TRANSLATION_FALLBACK_ENABLED", True)

LOCAL_TTS_ENABLED = _enabled("LOCAL_TTS_ENABLED")
LOCAL_TTS_ENGINE = os.getenv("LOCAL_TTS_ENGINE", "silero").strip().lower()
LOCAL_TTS_MODEL_ID = os.getenv("LOCAL_TTS_MODEL_ID", "").strip()
LOCAL_TTS_DEFAULT_LANGUAGE = os.getenv("LOCAL_TTS_DEFAULT_LANGUAGE", "hi").strip()
LOCAL_TTS_ALLOWED_MODEL_IDS = {
    value.strip()
    for value in os.getenv("LOCAL_TTS_ALLOWED_MODEL_IDS", "").split(",")
    if value.strip()
}
if LOCAL_TTS_MODEL_ID:
    LOCAL_TTS_ALLOWED_MODEL_IDS.add(LOCAL_TTS_MODEL_ID)

SILERO_REPO_DIR = os.getenv("SILERO_REPO_DIR", "").strip()
GOOGLE_CLOUD_PROJECT = os.getenv("GOOGLE_CLOUD_PROJECT", "").strip()
FIREBASE_STORAGE_BUCKET = os.getenv("FIREBASE_STORAGE_BUCKET", "").strip()
ALLOW_UNAUTHENTICATED_LOCAL_DEV = _enabled("ALLOW_UNAUTHENTICATED_LOCAL_DEV")
ENABLE_LOCAL_PATH_ANALYSIS = _enabled("ENABLE_LOCAL_PATH_ANALYSIS")
ENABLE_DIRECT_TTS_ENDPOINT = _enabled("ENABLE_DIRECT_TTS_ENDPOINT")
ENABLE_DIRECT_AUDIO_UPLOAD_ENDPOINT = _enabled("ENABLE_DIRECT_AUDIO_UPLOAD_ENDPOINT")
LOCAL_AUDIO_ROOT = os.getenv("LOCAL_AUDIO_ROOT", "").strip()
MAX_AUDIO_UPLOAD_BYTES = _bounded_integer(
    "MAX_AUDIO_UPLOAD_BYTES",
    5 * 1024 * 1024,
    64 * 1024,
    10 * 1024 * 1024,
)
DIRECT_TTS_REQUESTS_PER_MINUTE = _bounded_integer(
    "DIRECT_TTS_REQUESTS_PER_MINUTE",
    12,
    1,
    120,
)
DIRECT_AUDIO_UPLOADS_PER_MINUTE = _bounded_integer(
    "DIRECT_AUDIO_UPLOADS_PER_MINUTE",
    6,
    1,
    60,
)

LOCAL_TTS_MODEL_BY_LANGUAGE = {
    "bn": "facebook/mms-tts-ben",
    "hi": "facebook/mms-tts-hin",
    "gu": "facebook/mms-tts-guj",
    "kn": "facebook/mms-tts-kan",
    "ml": "facebook/mms-tts-mal",
    "mr": "facebook/mms-tts-mar",
    "pa": "facebook/mms-tts-pan",
    "ta": "facebook/mms-tts-tam",
    "te": "facebook/mms-tts-tel",
    "ur": "facebook/mms-tts-urd",
}

SILERO_TTS_SPEAKER_BY_LANGUAGE = {
    "bn": "bengali_female",
    "gu": "gujarati_female",
    "hi": "hindi_female",
    "kn": "kannada_female",
    "ml": "malayalam_female",
    "ta": "tamil_female",
    "te": "telugu_female",
}
