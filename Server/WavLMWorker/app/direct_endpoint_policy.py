"""Opt-in authorization and rate limiting for direct compute endpoints."""

from collections import defaultdict, deque
from collections.abc import Collection
from threading import Lock
from time import monotonic

from fastapi import HTTPException, Request


class SlidingWindowRateLimiter:
    """Small per-process guardrail for authenticated direct requests.

    Production deployments should still enforce fleet-wide quotas at their
    gateway. This limiter prevents a single process from accepting an
    unbounded burst and is deliberately independent of model code.
    """

    def __init__(self, window_seconds: float = 60.0, maximum_keys: int = 10_000) -> None:
        self.window_seconds = max(1.0, float(window_seconds))
        self.maximum_keys = max(100, int(maximum_keys))
        self._events: dict[str, deque[float]] = defaultdict(deque)
        self._lock = Lock()

    def allow(self, key: str, limit: int, now: float | None = None) -> bool:
        safe_limit = max(1, int(limit))
        current = monotonic() if now is None else float(now)
        cutoff = current - self.window_seconds

        with self._lock:
            events = self._events[key]
            while events and events[0] <= cutoff:
                events.popleft()
            if len(events) >= safe_limit:
                return False
            events.append(current)
            self._prune_empty_or_old(cutoff)
            return True

    def _prune_empty_or_old(self, cutoff: float) -> None:
        if len(self._events) <= self.maximum_keys:
            return
        for key in list(self._events):
            events = self._events[key]
            while events and events[0] <= cutoff:
                events.popleft()
            if not events:
                self._events.pop(key, None)
            if len(self._events) <= self.maximum_keys:
                break


class DirectEndpointPolicy:
    def __init__(
        self,
        *,
        name: str,
        enabled: bool,
        requests_per_minute: int,
        allowed_roles: Collection[str],
        limiter: SlidingWindowRateLimiter | None = None,
    ) -> None:
        self.name = name
        self.enabled = bool(enabled)
        self.requests_per_minute = max(1, int(requests_per_minute))
        self.allowed_roles = {str(role) for role in allowed_roles}
        self.limiter = limiter or SlidingWindowRateLimiter()

    def authorize(self, request: Request) -> None:
        if not self.enabled:
            raise HTTPException(status_code=404, detail=f"The direct {self.name} endpoint is disabled.")

        claims = getattr(request.state, "firebase_claims", {}) or {}
        role = str(claims.get("role") or "")
        if role == "local_development":
            host = request.client.host if request.client is not None else "loopback"
            subject = f"local:{host}"
        else:
            if role not in self.allowed_roles:
                raise HTTPException(status_code=403, detail=f"This account cannot use direct {self.name}.")
            subject = str(claims.get("sub") or claims.get("user_id") or "").strip()
            if not subject:
                raise HTTPException(status_code=403, detail="The verified identity has no stable subject claim.")

        if not self.limiter.allow(f"{self.name}:{subject}", self.requests_per_minute):
            raise HTTPException(
                status_code=429,
                detail=f"Direct {self.name} request limit reached. Try again in a minute.",
                headers={"Retry-After": "60"},
            )
