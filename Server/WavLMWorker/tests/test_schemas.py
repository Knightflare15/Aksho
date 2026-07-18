import unittest
import tempfile
from pathlib import Path

from pydantic import ValidationError

from app.schemas import AnalyzeJobRequest, AnalyzePathRequest, TranslateRequest, TtsRequest
from app.local_path_policy import (
    LocalAudioPathRejected,
    LocalAudioRootNotConfigured,
    resolve_local_wav_path,
)
from app.auth import is_loopback_host


class WorkerRequestSchemaTests(unittest.TestCase):
    def test_analysis_job_requires_bounded_identity_fields(self) -> None:
        with self.assertRaises(ValidationError):
            AnalyzeJobRequest(schoolId="", studentId="student", jobId="job")
        with self.assertRaises(ValidationError):
            AnalyzeJobRequest(schoolId="school", studentId="s" * 161, jobId="job")

    def test_local_path_and_target_are_bounded_before_file_access(self) -> None:
        with self.assertRaises(ValidationError):
            AnalyzePathRequest(wavPath="x" * 2049, targetText="word")
        with self.assertRaises(ValidationError):
            AnalyzePathRequest(wavPath="attempt.wav", targetText="x" * 241)

    def test_language_and_provider_inputs_are_bounded(self) -> None:
        with self.assertRaises(ValidationError):
            TranslateRequest(text="hello", targetLanguage="x" * 33)
        with self.assertRaises(ValidationError):
            TtsRequest(text="x" * 1001)

    def test_normal_requests_keep_stable_defaults(self) -> None:
        translation = TranslateRequest(text="hello")
        speech = TtsRequest(text="hello")

        self.assertEqual("en", translation.sourceLanguage)
        self.assertEqual("hi", translation.targetLanguage)
        self.assertEqual("hi", speech.language)


class LocalPathPolicyTests(unittest.TestCase):
    def test_requires_an_explicit_existing_root(self) -> None:
        with self.assertRaises(LocalAudioRootNotConfigured):
            resolve_local_wav_path("attempt.wav", "")

    def test_accepts_only_wav_files_inside_the_configured_root(self) -> None:
        with tempfile.TemporaryDirectory() as root_value:
            root = Path(root_value)
            wav = root / "attempt.wav"
            text = root / "notes.txt"
            wav.write_bytes(b"RIFF0000WAVE")
            text.write_text("not audio", encoding="utf-8")

            self.assertEqual(wav.resolve(), resolve_local_wav_path(str(wav), str(root)))
            with self.assertRaises(LocalAudioPathRejected):
                resolve_local_wav_path(str(text), str(root))

    def test_rejects_paths_outside_the_configured_root(self) -> None:
        with tempfile.TemporaryDirectory() as root_value, tempfile.TemporaryDirectory() as outside_value:
            outside = Path(outside_value) / "attempt.wav"
            outside.write_bytes(b"RIFF0000WAVE")

            with self.assertRaises(LocalAudioPathRejected):
                resolve_local_wav_path(str(outside), root_value)


class AuthenticationPolicyTests(unittest.TestCase):
    def test_local_bypass_host_policy_accepts_only_loopback(self) -> None:
        self.assertTrue(is_loopback_host("127.0.0.1"))
        self.assertTrue(is_loopback_host("::1"))
        self.assertTrue(is_loopback_host("localhost"))
        self.assertFalse(is_loopback_host("10.0.0.7"))
        self.assertFalse(is_loopback_host("worker.example.com"))


if __name__ == "__main__":
    unittest.main()
