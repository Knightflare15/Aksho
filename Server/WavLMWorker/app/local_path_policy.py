"""Fail-closed policy for the development-only local WAV endpoint."""

from pathlib import Path


class LocalAudioRootNotConfigured(ValueError):
    pass


class LocalAudioPathRejected(ValueError):
    pass


def resolve_local_wav_path(requested_path: str, allowed_root: str) -> Path:
    if not (allowed_root or "").strip():
        raise LocalAudioRootNotConfigured("LOCAL_AUDIO_ROOT is required when local path analysis is enabled.")

    root = Path(allowed_root).expanduser().resolve()
    if not root.is_dir():
        raise LocalAudioRootNotConfigured("LOCAL_AUDIO_ROOT must reference an existing directory.")

    candidate = Path(requested_path or "").expanduser().resolve()
    try:
        candidate.relative_to(root)
    except ValueError as exc:
        raise LocalAudioPathRejected("WAV path must stay inside LOCAL_AUDIO_ROOT.") from exc

    if candidate.suffix.lower() != ".wav":
        raise LocalAudioPathRejected("Only WAV files are accepted.")
    if not candidate.is_file():
        raise FileNotFoundError("WAV path not found.")
    return candidate
