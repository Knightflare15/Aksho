"""Concurrency gates for heavyweight, process-local model initialization."""

from collections.abc import Callable
from threading import Lock


class ModelLoadGate:
    """Runs a loader once while allowing a failed load to be retried later.

    FastAPI executes synchronous route functions in worker threads. A plain
    ``if model is None`` check therefore allows several cold requests to load
    the same checkpoint at once, which can exhaust CPU or GPU memory.
    """

    def __init__(self) -> None:
        self._lock = Lock()

    def ensure(self, is_ready: Callable[[], bool], load: Callable[[], None]) -> None:
        if is_ready():
            return

        with self._lock:
            if is_ready():
                return
            load()
