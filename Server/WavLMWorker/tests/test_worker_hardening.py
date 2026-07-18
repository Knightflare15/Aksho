import threading
import time
import unittest
from types import SimpleNamespace
from unittest.mock import patch

from fastapi import HTTPException

from app.direct_endpoint_policy import DirectEndpointPolicy, SlidingWindowRateLimiter
from app.model_loading import ModelLoadGate
from app.storage_policy import (
    StorageBucketNotConfigured,
    StoragePathRejected,
    split_and_validate_owned_pronunciation_path,
)


def request_with_claims(**claims: str) -> SimpleNamespace:
    return SimpleNamespace(
        state=SimpleNamespace(firebase_claims=claims),
        client=SimpleNamespace(host="127.0.0.1"),
    )


class DirectEndpointPolicyTests(unittest.TestCase):
    def test_disabled_endpoint_is_hidden(self) -> None:
        policy = DirectEndpointPolicy(
            name="TTS",
            enabled=False,
            requests_per_minute=12,
            allowed_roles={"student"},
        )

        with self.assertRaises(HTTPException) as context:
            policy.authorize(request_with_claims(role="student", sub="learner-1"))

        self.assertEqual(404, context.exception.status_code)

    def test_enabled_endpoint_requires_an_allowed_stable_identity(self) -> None:
        policy = DirectEndpointPolicy(
            name="TTS",
            enabled=True,
            requests_per_minute=12,
            allowed_roles={"student"},
        )

        for claims in (
            {"role": "teacher", "sub": "teacher-1"},
            {"role": "student"},
        ):
            with self.subTest(claims=claims), self.assertRaises(HTTPException) as context:
                policy.authorize(request_with_claims(**claims))
            self.assertEqual(403, context.exception.status_code)

    def test_enabled_endpoint_applies_a_per_identity_limit(self) -> None:
        policy = DirectEndpointPolicy(
            name="audio analysis",
            enabled=True,
            requests_per_minute=1,
            allowed_roles={"student"},
        )
        request = request_with_claims(role="student", sub="learner-1")

        policy.authorize(request)
        with self.assertRaises(HTTPException) as context:
            policy.authorize(request)

        self.assertEqual(429, context.exception.status_code)
        self.assertEqual("60", context.exception.headers["Retry-After"])

    def test_sliding_window_reopens_after_expiry(self) -> None:
        limiter = SlidingWindowRateLimiter(window_seconds=60)

        self.assertTrue(limiter.allow("learner", 1, now=0))
        self.assertFalse(limiter.allow("learner", 1, now=59.9))
        self.assertTrue(limiter.allow("learner", 1, now=60))


class StoragePolicyTests(unittest.TestCase):
    path = "schools/school-1/students/student-1/pronunciationAudio/attempt_7.wav"

    def test_accepts_relative_and_matching_bucket_paths(self) -> None:
        expected = ("project.appspot.com", self.path)

        self.assertEqual(
            expected,
            split_and_validate_owned_pronunciation_path(
                self.path,
                "project.appspot.com",
                "school-1",
                "student-1",
            ),
        )
        self.assertEqual(
            expected,
            split_and_validate_owned_pronunciation_path(
                f"gs://project.appspot.com/{self.path}",
                "gs://project.appspot.com",
                "school-1",
                "student-1",
            ),
        )

    def test_rejects_an_unconfigured_or_different_bucket(self) -> None:
        with self.assertRaises(StorageBucketNotConfigured):
            split_and_validate_owned_pronunciation_path(
                self.path,
                "",
                "school-1",
                "student-1",
            )
        with self.assertRaises(StoragePathRejected):
            split_and_validate_owned_pronunciation_path(
                f"gs://attacker.example/{self.path}",
                "project.appspot.com",
                "school-1",
                "student-1",
            )

    def test_rejects_cross_student_and_nested_objects(self) -> None:
        for path in (
            "schools/school-1/students/student-2/pronunciationAudio/attempt.wav",
            "schools/school-1/students/student-1/pronunciationAudio/nested/attempt.wav",
            "schools/school-1/students/student-1/pronunciationAudio/attempt.mp3",
        ):
            with self.subTest(path=path), self.assertRaises(StoragePathRejected):
                split_and_validate_owned_pronunciation_path(
                    path,
                    "project.appspot.com",
                    "school-1",
                    "student-1",
                )


class ModelLoadGateTests(unittest.TestCase):
    def test_concurrent_cold_requests_load_once(self) -> None:
        gate = ModelLoadGate()
        state = {"ready": False, "loads": 0}
        state_lock = threading.Lock()

        def load() -> None:
            with state_lock:
                state["loads"] += 1
            time.sleep(0.03)
            state["ready"] = True

        threads = [
            threading.Thread(target=lambda: gate.ensure(lambda: state["ready"], load))
            for _ in range(8)
        ]
        for thread in threads:
            thread.start()
        for thread in threads:
            thread.join(timeout=1)

        self.assertTrue(all(not thread.is_alive() for thread in threads))
        self.assertEqual(1, state["loads"])


class HealthRouteTests(unittest.TestCase):
    def test_disabled_local_tts_health_never_resolves_a_model(self) -> None:
        from app import main as worker_main

        with (
            patch.object(worker_main, "LOCAL_TTS_ENABLED", False),
            patch.object(worker_main, "tts_model_id", ""),
            patch.object(
                worker_main,
                "local_tts_model_id",
                side_effect=AssertionError("disabled TTS must not resolve a model"),
            ),
        ):
            result = worker_main.health()

        self.assertTrue(result["ok"])
        self.assertFalse(result["ttsLocalEnabled"])
        self.assertEqual("", result["ttsLocalModelId"])
        self.assertTrue(result["ttsLocalConfigurationValid"])


if __name__ == "__main__":
    unittest.main()
