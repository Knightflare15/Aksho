import io
import json
import base64
import tempfile
import urllib.request
import urllib.error
from pathlib import Path
from typing import Any

import numpy as np
import soundfile as sf
from fastapi import FastAPI, HTTPException, Request, UploadFile

from .config import (
    DEVICE,
    DIRECT_AUDIO_UPLOADS_PER_MINUTE,
    DIRECT_TTS_REQUESTS_PER_MINUTE,
    ENABLE_DIRECT_AUDIO_UPLOAD_ENDPOINT,
    ENABLE_DIRECT_TTS_ENDPOINT,
    ENABLE_LOCAL_PATH_ANALYSIS,
    FIREBASE_STORAGE_BUCKET,
    LOCAL_TTS_ALLOWED_MODEL_IDS,
    LOCAL_TTS_DEFAULT_LANGUAGE,
    LOCAL_TTS_ENABLED,
    LOCAL_TTS_ENGINE,
    LOCAL_TTS_MODEL_BY_LANGUAGE,
    LOCAL_TTS_MODEL_ID,
    LOCAL_AUDIO_ROOT,
    MAX_AUDIO_UPLOAD_BYTES,
    MODEL_ID,
    SAMPLE_RATE,
    SILERO_REPO_DIR,
    SILERO_TTS_SPEAKER_BY_LANGUAGE,
    TRANSLATION_EXTERNAL_URL,
    TRANSLATION_FALLBACK_ENABLED,
    TRANSLATION_PROVIDER_NAME,
    TTS_EXTERNAL_URL,
    TTS_PROVIDER_NAME,
)
from .auth import authorize_student_scope, register_firebase_authentication
from .direct_endpoint_policy import DirectEndpointPolicy
from .local_path_policy import (
    LocalAudioPathRejected,
    LocalAudioRootNotConfigured,
    resolve_local_wav_path,
)
from .model_loading import ModelLoadGate
from .schemas import AnalyzeJobRequest, AnalyzePathRequest, TranslateRequest, TtsRequest
from .storage_policy import (
    StorageBucketNotConfigured,
    StoragePathRejected,
    split_and_validate_owned_pronunciation_path,
)

try:
    from google.cloud import firestore, storage
except Exception:  # pragma: no cover - local installs may omit GCP deps.
    firestore = None
    storage = None

try:
    import torch
    from transformers import AutoModelForCTC, AutoProcessor, AutoTokenizer, VitsModel
except Exception:  # pragma: no cover - health endpoint reports unavailable.
    torch = None
    AutoModelForCTC = None
    AutoProcessor = None
    AutoTokenizer = None
    VitsModel = None
from .phonetics import (
    build_pronunciation_insight,
    expected_phonemes_for,
    normalize_phoneme_text,
    resample_audio,
    score_pronunciation,
)

app = FastAPI(title="The Script WavLM Worker", version="0.1.0")
register_firebase_authentication(app)
processor = None
model = None
tts_tokenizer = None
tts_model = None
tts_model_id = ""
silero_tts_model = None
wavlm_model_load_gate = ModelLoadGate()
hf_tts_model_load_gate = ModelLoadGate()
silero_tts_model_load_gate = ModelLoadGate()
direct_tts_policy = DirectEndpointPolicy(
    name="TTS",
    enabled=ENABLE_DIRECT_TTS_ENDPOINT,
    requests_per_minute=DIRECT_TTS_REQUESTS_PER_MINUTE,
    allowed_roles={"student", "parent", "teacher", "admin"},
)
direct_audio_upload_policy = DirectEndpointPolicy(
    name="audio analysis",
    enabled=ENABLE_DIRECT_AUDIO_UPLOAD_ENDPOINT,
    requests_per_minute=DIRECT_AUDIO_UPLOADS_PER_MINUTE,
    allowed_roles={"student", "parent", "teacher", "admin"},
)


def assert_owned_pronunciation_storage_path(
    storage_path: str,
    school_id: str,
    student_id: str,
) -> tuple[str, str]:
    try:
        return split_and_validate_owned_pronunciation_path(
            storage_path,
            FIREBASE_STORAGE_BUCKET,
            school_id,
            student_id,
        )
    except StorageBucketNotConfigured as exc:
        raise HTTPException(status_code=503, detail=str(exc)) from exc
    except StoragePathRejected as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc


def local_tts_health_state() -> tuple[str, bool]:
    """Describe optional local TTS without making disabled config mandatory."""
    if not LOCAL_TTS_ENABLED:
        return tts_model_id, True
    if LOCAL_TTS_ENGINE in {"silero", "torchhub", "torch-hub"}:
        return "silero:v4_indic", bool(SILERO_REPO_DIR and Path(SILERO_REPO_DIR).is_dir())
    try:
        return tts_model_id or local_tts_model_id(LOCAL_TTS_DEFAULT_LANGUAGE, ""), True
    except HTTPException:
        return tts_model_id, False


@app.get("/health")
def health() -> dict[str, Any]:
    local_tts_model, local_tts_configuration_valid = local_tts_health_state()
    return {
        "ok": True,
        "modelId": MODEL_ID,
        "device": DEVICE,
        "modelLoaded": model is not None,
        "gcpAvailable": firestore is not None and storage is not None,
        "translationConfigured": bool(TRANSLATION_EXTERNAL_URL),
        "translationFallbackEnabled": TRANSLATION_FALLBACK_ENABLED,
        "ttsConfigured": bool(TTS_EXTERNAL_URL) or LOCAL_TTS_ENABLED,
        "ttsExternalConfigured": bool(TTS_EXTERNAL_URL),
        "ttsLocalEnabled": LOCAL_TTS_ENABLED,
        "ttsLocalEngine": LOCAL_TTS_ENGINE,
        "ttsLocalModelId": local_tts_model,
        "ttsLocalConfigurationValid": local_tts_configuration_valid,
        "ttsLocalModelLoaded": (silero_tts_model is not None) if LOCAL_TTS_ENGINE == "silero" else (tts_model is not None),
    }


@app.post("/translate")
def translate(request: TranslateRequest) -> dict[str, Any]:
    text = (request.text or "").strip()
    if not text:
        raise HTTPException(status_code=400, detail="text is required.")
    if len(text) > 2000:
        raise HTTPException(status_code=413, detail="text must be at most 2000 characters.")

    if TRANSLATION_EXTERNAL_URL:
        payload = {
            "text": text,
            "sourceLanguage": request.sourceLanguage,
            "targetLanguage": request.targetLanguage,
            "ttsBackend": request.ttsBackend or "",
            "voice": request.voice or "",
        }
        return post_json_or_audio(TRANSLATION_EXTERNAL_URL, payload, TRANSLATION_PROVIDER_NAME)

    if not TRANSLATION_FALLBACK_ENABLED:
        raise HTTPException(
            status_code=503,
            detail="No translation backend is configured. Set TRANSLATION_EXTERNAL_URL or enable TRANSLATION_FALLBACK_ENABLED for development.",
        )

    return {
        "translation": text,
        "providerName": TRANSLATION_PROVIDER_NAME,
        "fallback": True,
        "translationConfigured": False,
    }


@app.post("/tts")
def tts(request: TtsRequest, http_request: Request) -> dict[str, Any]:
    direct_tts_policy.authorize(http_request)
    text = (request.text or "").strip()
    if not text:
        raise HTTPException(status_code=400, detail="text is required.")
    if len(text) > 1000:
        raise HTTPException(status_code=413, detail="text must be at most 1000 characters.")

    payload = {
        "text": text,
        "language": request.language,
        "ttsBackend": request.ttsBackend or "",
        "voice": request.voice or "",
    }
    if TTS_EXTERNAL_URL and not wants_local_tts(request.ttsBackend):
        return post_json_or_audio(TTS_EXTERNAL_URL, payload, TTS_PROVIDER_NAME)

    if LOCAL_TTS_ENABLED:
        return synthesize_local_tts(text, request.language, request.ttsBackend or "")

    raise HTTPException(
        status_code=503,
        detail="No TTS backend is configured. Set TTS_EXTERNAL_URL or enable LOCAL_TTS_ENABLED with a Hugging Face VITS/MMS model.",
    )


@app.post("/analyze/upload")
async def analyze_upload(http_request: Request, file: UploadFile, targetText: str = "") -> dict[str, Any]:
    direct_audio_upload_policy.authorize(http_request)
    if len(targetText or "") > 240:
        raise HTTPException(status_code=413, detail="targetText must be at most 240 characters.")
    suffix = Path(file.filename or "audio.wav").suffix.lower() or ".wav"
    if suffix != ".wav":
        raise HTTPException(status_code=415, detail="Only PCM WAV uploads are supported.")
    audio_bytes = await file.read(MAX_AUDIO_UPLOAD_BYTES + 1)
    if len(audio_bytes) > MAX_AUDIO_UPLOAD_BYTES:
        raise HTTPException(status_code=413, detail=f"Audio upload exceeds {MAX_AUDIO_UPLOAD_BYTES} bytes.")
    with tempfile.NamedTemporaryFile(delete=False, suffix=".wav") as tmp:
        tmp.write(audio_bytes)
        tmp_path = tmp.name

    try:
        return analyze_wav_path(tmp_path, targetText)
    finally:
        Path(tmp_path).unlink(missing_ok=True)


@app.post("/analyze/path")
def analyze_path(request: AnalyzePathRequest) -> dict[str, Any]:
    if not ENABLE_LOCAL_PATH_ANALYSIS:
        raise HTTPException(status_code=404, detail="Local path analysis is disabled.")
    try:
        path = resolve_local_wav_path(request.wavPath, LOCAL_AUDIO_ROOT)
    except LocalAudioRootNotConfigured as exc:
        raise HTTPException(status_code=503, detail=str(exc)) from exc
    except LocalAudioPathRejected as exc:
        raise HTTPException(status_code=403, detail=str(exc)) from exc
    except FileNotFoundError as exc:
        raise HTTPException(status_code=404, detail=str(exc)) from exc
    return analyze_wav_path(str(path), request.targetText or "")


@app.post("/analyze/job")
def analyze_job(request: AnalyzeJobRequest, http_request: Request) -> dict[str, Any]:
    authorize_student_scope(http_request, request.schoolId, request.studentId)
    if firestore is None or storage is None:
        raise HTTPException(status_code=503, detail="Google Cloud clients are not installed.")

    db = firestore.Client()
    job_ref = db.document(
        f"schools/{request.schoolId}/students/{request.studentId}/analysisJobs/{request.jobId}"
    )
    snapshot = job_ref.get()
    if not snapshot.exists:
        raise HTTPException(status_code=404, detail="Analysis job not found.")

    job = snapshot.to_dict() or {}
    if job.get("analysisKind") != "pronunciation":
        raise HTTPException(status_code=400, detail="Only pronunciation WavLM jobs are supported by this worker.")

    job_ref.set({"status": "processing", "updatedAt": firestore.SERVER_TIMESTAMP}, merge=True)
    audio_storage_path = str(job.get("audioStoragePath") or "")
    target_text = str(job.get("targetText") or "")

    if not audio_storage_path:
        failure = {
            "status": "failed",
            "error": "No audioStoragePath was attached to this analysis job.",
            "updatedAt": firestore.SERVER_TIMESTAMP,
        }
        job_ref.set(failure, merge=True)
        return {"ok": False, **failure}
    if len(target_text) > 240:
        raise HTTPException(status_code=400, detail="Pronunciation target text exceeds 240 characters.")
    storage_bucket, storage_object_name = assert_owned_pronunciation_storage_path(
        audio_storage_path,
        request.schoolId,
        request.studentId,
    )

    try:
        wav_path = download_storage_object(storage_bucket, storage_object_name)
        result = analyze_wav_path(wav_path, target_text)
        server_insight = build_pronunciation_insight(result)
        job_ref.set({
            "status": "complete",
            "providerName": "WavLM Cloud Run",
            "result": result,
            "updatedAt": firestore.SERVER_TIMESTAMP,
        }, merge=True)
        update_source_record(db, job, {
            "serverAnalysisStatus": "complete",
            "serverPronunciationInsight": server_insight,
            "updatedAt": firestore.SERVER_TIMESTAMP,
        })
        return {"ok": True, "jobId": request.jobId, "result": result}
    except Exception as exc:
        job_ref.set({
            "status": "failed",
            "error": str(exc),
            "updatedAt": firestore.SERVER_TIMESTAMP,
        }, merge=True)
        update_source_record(db, job, {
            "serverAnalysisStatus": "failed",
            "serverAnalysisError": str(exc),
            "updatedAt": firestore.SERVER_TIMESTAMP,
        })
        raise
    finally:
        if "wav_path" in locals():
            Path(wav_path).unlink(missing_ok=True)
        if audio_storage_path:
            try:
                delete_storage_object(storage_bucket, storage_object_name)
            except Exception:
                # Analysis status is already durable; cleanup can be retried by a
                # lifecycle rule without turning a learner result into a failure.
                pass


def analyze_wav_path(path: str, target_text: str = "") -> dict[str, Any]:
    ensure_model_loaded()
    audio, sample_rate = sf.read(path)
    if audio.ndim > 1:
        audio = np.mean(audio, axis=1)
    if sample_rate != SAMPLE_RATE:
        audio = resample_audio(audio, sample_rate, SAMPLE_RATE)

    inputs = processor(audio, sampling_rate=SAMPLE_RATE, return_tensors="pt")
    inputs = {key: value.to(DEVICE) for key, value in inputs.items()}
    with torch.no_grad():
        logits = model(**inputs).logits
        predicted_ids = torch.argmax(logits, dim=-1)
        transcription = processor.batch_decode(predicted_ids)[0]
        model_confidence = float(torch.softmax(logits, dim=-1).max(dim=-1).values.mean().cpu())

    expected = expected_phonemes_for(target_text)
    observed = normalize_phoneme_text(transcription)
    pronunciation = score_pronunciation(expected, observed, model_confidence)

    return {
        "targetText": target_text,
        "phonemeText": transcription,
        "expectedPhonemes": expected,
        "observedPhonemes": observed,
        "score": pronunciation["score"],
        "modelConfidence": model_confidence,
        "alignment": pronunciation["alignment"],
        "phonemeIssues": pronunciation["issues"],
        "message": pronunciation["message"],
    }

def wants_local_tts(tts_backend: str | None) -> bool:
    value = (tts_backend or "").strip().lower()
    return value in {"local", "hf", "huggingface"} or value.startswith("hf:")


def local_tts_model_id(language: str | None, requested_backend: str | None) -> str:
    backend = (requested_backend or "").strip()
    if backend.lower().startswith("hf:"):
        candidate = backend[3:].strip()
        if candidate:
            if candidate not in LOCAL_TTS_ALLOWED_MODEL_IDS:
                raise HTTPException(status_code=400, detail="Requested TTS model is not allow-listed.")
            return candidate
    if backend and "/" in backend:
        if backend not in LOCAL_TTS_ALLOWED_MODEL_IDS:
            raise HTTPException(status_code=400, detail="Requested TTS model is not allow-listed.")
        return backend
    if LOCAL_TTS_MODEL_ID:
        return LOCAL_TTS_MODEL_ID

    language_key = normalize_language_code(language or LOCAL_TTS_DEFAULT_LANGUAGE)
    candidate = LOCAL_TTS_MODEL_BY_LANGUAGE.get(
        language_key,
        LOCAL_TTS_MODEL_BY_LANGUAGE.get(normalize_language_code(LOCAL_TTS_DEFAULT_LANGUAGE), "facebook/mms-tts-hin"),
    )
    if candidate not in LOCAL_TTS_ALLOWED_MODEL_IDS:
        raise HTTPException(status_code=503, detail="No allow-listed local TTS model is configured for this language.")
    return candidate


def normalize_language_code(language: str) -> str:
    cleaned = (language or "").strip().lower().replace("_", "-")
    if not cleaned:
        return "hi"
    return cleaned.split("-", 1)[0]


def synthesize_local_tts(text: str, language: str, requested_backend: str) -> dict[str, Any]:
    if wants_silero_tts(requested_backend):
        return synthesize_silero_tts(text, language, requested_backend)

    tokenizer, speech_model, loaded_model_id = ensure_tts_model_loaded(
        local_tts_model_id(language, requested_backend)
    )
    inputs = tokenizer(text, return_tensors="pt")
    inputs = {key: value.to(DEVICE) for key, value in inputs.items()}

    with torch.no_grad():
        output = speech_model(**inputs)

    waveform = output.waveform
    if hasattr(waveform, "detach"):
        waveform = waveform.detach().cpu().numpy()
    waveform = np.asarray(waveform, dtype=np.float32).squeeze()
    if waveform.ndim != 1 or waveform.size == 0:
        raise HTTPException(status_code=502, detail="Local TTS produced an empty waveform.")

    sample_rate = int(getattr(speech_model.config, "sampling_rate", SAMPLE_RATE) or SAMPLE_RATE)
    wav_bytes = encode_wav_bytes(waveform, sample_rate)
    return {
        "audioBase64": base64.b64encode(wav_bytes).decode("ascii"),
        "audioContentType": "audio/wav",
        "providerName": f"Local Hugging Face TTS ({loaded_model_id})",
        "language": normalize_language_code(language),
    }


def wants_silero_tts(tts_backend: str | None) -> bool:
    value = (tts_backend or "").strip().lower()
    if value in {"silero", "torchhub", "torch-hub"}:
        return True
    if value.startswith("hf:"):
        return False
    return LOCAL_TTS_ENGINE in {"silero", "torchhub", "torch-hub"}


def synthesize_silero_tts(text: str, language: str, requested_backend: str) -> dict[str, Any]:
    ensure_silero_tts_loaded()
    sample_rate = 48000
    speaker = silero_speaker_for(language, requested_backend)
    try:
        waveform = silero_tts_model.apply_tts(
            text=text,
            speaker=speaker,
            sample_rate=sample_rate,
        )
    except Exception as exc:
        raise HTTPException(status_code=502, detail=f"Silero TTS synthesis failed: {exc}") from exc

    if hasattr(waveform, "detach"):
        waveform = waveform.detach().cpu().numpy()
    waveform = np.asarray(waveform, dtype=np.float32).squeeze()
    if waveform.ndim != 1 or waveform.size == 0:
        raise HTTPException(status_code=502, detail="Silero TTS produced an empty waveform.")

    wav_bytes = encode_wav_bytes(waveform, sample_rate)
    return {
        "audioBase64": base64.b64encode(wav_bytes).decode("ascii"),
        "audioContentType": "audio/wav",
        "providerName": f"Local Silero TTS ({speaker})",
        "language": normalize_language_code(language),
    }


def ensure_silero_tts_loaded() -> None:
    global silero_tts_model

    def load() -> None:
        global silero_tts_model
        if torch is None:
            raise HTTPException(status_code=503, detail="Silero TTS requires PyTorch.")
        if not SILERO_REPO_DIR or not Path(SILERO_REPO_DIR).is_dir():
            raise HTTPException(
                status_code=503,
                detail="SILERO_REPO_DIR must point to a reviewed, image-baked Silero repository.",
            )

        try:
            loaded_model, _ = torch.hub.load(
                repo_or_dir=SILERO_REPO_DIR,
                model="silero_tts",
                language="indic",
                speaker="v4_indic",
                source="local",
            )
            loaded_model.to(DEVICE)
        except Exception as exc:
            raise HTTPException(status_code=503, detail=f"Silero TTS model could not be loaded: {exc}") from exc
        silero_tts_model = loaded_model

    silero_tts_model_load_gate.ensure(lambda: silero_tts_model is not None, load)


def silero_speaker_for(language: str, requested_backend: str) -> str:
    backend = (requested_backend or "").strip()
    if backend.lower().startswith("silero:"):
        candidate = backend.split(":", 1)[1].strip()
        if candidate in set(SILERO_TTS_SPEAKER_BY_LANGUAGE.values()):
            return candidate
        raise HTTPException(status_code=400, detail="Requested Silero speaker is not allow-listed.")
    return SILERO_TTS_SPEAKER_BY_LANGUAGE.get(
        normalize_language_code(language),
        SILERO_TTS_SPEAKER_BY_LANGUAGE.get(normalize_language_code(LOCAL_TTS_DEFAULT_LANGUAGE), "hindi_female"),
    )


def ensure_tts_model_loaded(model_id: str) -> tuple[Any, Any, str]:
    global tts_tokenizer, tts_model, tts_model_id

    def is_ready() -> bool:
        return tts_tokenizer is not None and tts_model is not None and tts_model_id == model_id

    def load() -> None:
        global tts_tokenizer, tts_model, tts_model_id
        if AutoTokenizer is None or VitsModel is None or torch is None:
            raise HTTPException(status_code=503, detail="Local TTS dependencies are not installed.")

        loaded_tokenizer = AutoTokenizer.from_pretrained(model_id)
        loaded_model = VitsModel.from_pretrained(model_id)
        loaded_model.to(DEVICE)
        loaded_model.eval()
        tts_tokenizer = loaded_tokenizer
        tts_model = loaded_model
        tts_model_id = model_id

    hf_tts_model_load_gate.ensure(is_ready, load)
    return tts_tokenizer, tts_model, tts_model_id


def encode_wav_bytes(waveform: np.ndarray, sample_rate: int) -> bytes:
    buffer = io.BytesIO()
    sf.write(buffer, waveform, sample_rate, format="WAV", subtype="PCM_16")
    return buffer.getvalue()


def post_json_or_audio(url: str, payload: dict[str, Any], provider_name: str) -> dict[str, Any]:
    body = json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(
        url,
        data=body,
        headers={
            "Content-Type": "application/json",
            "Accept": "application/json, audio/wav",
        },
        method="POST",
    )
    try:
        with urllib.request.urlopen(req, timeout=60) as response:
            content_type = response.headers.get("Content-Type", "")
            data = response.read()
    except urllib.error.HTTPError as exc:
        detail = exc.read().decode("utf-8", errors="replace")
        raise HTTPException(status_code=exc.code, detail=detail or str(exc)) from exc
    except urllib.error.URLError as exc:
        raise HTTPException(status_code=502, detail=f"External provider unavailable: {exc}") from exc

    if "audio" in content_type.lower() or looks_like_wav(data):
        return {
            "audioBase64": base64.b64encode(data).decode("ascii"),
            "audioContentType": content_type or "audio/wav",
            "providerName": provider_name,
        }

    try:
        parsed = json.loads(data.decode("utf-8"))
    except Exception as exc:
        raise HTTPException(status_code=502, detail=f"External provider returned non-JSON/non-audio data: {exc}") from exc

    if isinstance(parsed, dict):
        parsed.setdefault("providerName", provider_name)
        return parsed

    raise HTTPException(status_code=502, detail="External provider response must be a JSON object or audio/wav.")


def looks_like_wav(data: bytes) -> bool:
    return (
        len(data) >= 12 and
        data[0:4] == b"RIFF" and
        data[8:12] == b"WAVE"
    )


def update_source_record(db: Any, job: dict[str, Any], payload: dict[str, Any]) -> None:
    school_id = str(job.get("schoolId") or "")
    student_id = str(job.get("studentId") or "")
    source_collection = str(job.get("sourceCollection") or "")
    source_record_id = str(job.get("sourceRecordId") or "")
    if not school_id or not student_id or not source_collection or not source_record_id:
        return

    db.document(
        f"schools/{school_id}/students/{student_id}/{source_collection}/{source_record_id}"
    ).set(payload, merge=True)


def ensure_model_loaded() -> None:
    global processor, model

    def load() -> None:
        global processor, model
        if AutoProcessor is None or AutoModelForCTC is None or torch is None:
            raise HTTPException(status_code=503, detail="WavLM dependencies are not installed.")

        loaded_processor = AutoProcessor.from_pretrained(MODEL_ID)
        loaded_model = AutoModelForCTC.from_pretrained(MODEL_ID)
        loaded_model.to(DEVICE)
        loaded_model.eval()
        processor = loaded_processor
        model = loaded_model

    wavlm_model_load_gate.ensure(lambda: processor is not None and model is not None, load)


def download_storage_object(bucket_name: str, blob_name: str) -> str:
    client = storage.Client()
    bucket = client.bucket(bucket_name)
    blob = bucket.blob(blob_name)
    suffix = Path(blob_name).suffix or ".wav"
    tmp = tempfile.NamedTemporaryFile(delete=False, suffix=suffix)
    tmp.close()
    blob.download_to_filename(tmp.name)
    return tmp.name


def delete_storage_object(bucket_name: str, blob_name: str) -> None:
    client = storage.Client()
    client.bucket(bucket_name).blob(blob_name).delete()
