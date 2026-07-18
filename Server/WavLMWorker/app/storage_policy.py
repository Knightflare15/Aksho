"""Storage-path validation for pronunciation analysis jobs."""

import re


class StorageBucketNotConfigured(ValueError):
    pass


class StoragePathRejected(ValueError):
    pass


def normalize_bucket_name(value: str) -> str:
    normalized = (value or "").strip()
    if normalized.startswith("gs://"):
        normalized = normalized[5:]
    normalized = normalized.strip("/")
    if not normalized or "/" in normalized or "\\" in normalized:
        return ""
    return normalized


def split_and_validate_owned_pronunciation_path(
    storage_path: str,
    configured_bucket: str,
    school_id: str,
    student_id: str,
) -> tuple[str, str]:
    bucket = normalize_bucket_name(configured_bucket)
    if not bucket:
        raise StorageBucketNotConfigured(
            "FIREBASE_STORAGE_BUCKET must name the single bucket used for pronunciation jobs."
        )

    value = (storage_path or "").strip()
    if value.startswith("gs://"):
        without_scheme = value[5:]
        requested_bucket, separator, object_name = without_scheme.partition("/")
        if not separator or not requested_bucket or not object_name:
            raise StoragePathRejected("audioStoragePath must include a bucket and object name.")
    else:
        requested_bucket = bucket
        object_name = value.lstrip("/")

    if normalize_bucket_name(requested_bucket) != bucket:
        raise StoragePathRejected("audioStoragePath must use the configured Firebase Storage bucket.")

    expected_prefix = f"schools/{school_id}/students/{student_id}/pronunciationAudio/"
    file_name = object_name[len(expected_prefix):] if object_name.startswith(expected_prefix) else ""
    if not file_name or "/" in file_name or re.fullmatch(r"[A-Za-z0-9_-]+[.]wav", file_name, re.IGNORECASE) is None:
        raise StoragePathRejected(
            "audioStoragePath must reference this student's pronunciationAudio WAV object."
        )
    return bucket, object_name
