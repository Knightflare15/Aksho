"""Firebase authentication and tenant-scope policy for the worker."""

import ipaddress
from typing import Any

from fastapi import FastAPI, HTTPException, Request
from fastapi.responses import JSONResponse
from google.auth.transport.requests import Request as GoogleAuthRequest
from google.oauth2 import id_token as google_id_token

from .config import ALLOW_UNAUTHENTICATED_LOCAL_DEV, GOOGLE_CLOUD_PROJECT


def is_loopback_host(host: str | None) -> bool:
    value = (host or "").strip()
    if value == "testclient":
        return True
    try:
        return ipaddress.ip_address(value).is_loopback
    except ValueError:
        return value.lower() == "localhost"


def register_firebase_authentication(app: FastAPI) -> None:
    @app.middleware("http")
    async def require_firebase_identity(request: Request, call_next: Any) -> Any:
        if request.url.path == "/health":
            return await call_next(request)

        if ALLOW_UNAUTHENTICATED_LOCAL_DEV:
            client_host = request.client.host if request.client is not None else ""
            if not is_loopback_host(client_host):
                return JSONResponse(
                    status_code=403,
                    content={"detail": "The local authentication bypass accepts loopback clients only."},
                )
            request.state.firebase_claims = {"role": "local_development"}
            return await call_next(request)

        if not GOOGLE_CLOUD_PROJECT:
            return JSONResponse(
                status_code=503,
                content={"detail": "GOOGLE_CLOUD_PROJECT is required for token verification."},
            )

        authorization = request.headers.get("Authorization", "")
        scheme, _, token = authorization.partition(" ")
        if scheme.lower() != "bearer" or not token.strip():
            return JSONResponse(status_code=401, content={"detail": "A Firebase bearer token is required."})

        try:
            claims = google_id_token.verify_firebase_token(
                token.strip(),
                GoogleAuthRequest(),
                audience=GOOGLE_CLOUD_PROJECT,
            )
        except Exception:
            return JSONResponse(
                status_code=401,
                content={"detail": "Firebase bearer token is invalid or expired."},
            )

        role = str((claims or {}).get("role") or "")
        if role not in {"student", "parent", "teacher", "admin"}:
            return JSONResponse(status_code=403, content={"detail": "This account cannot use the speech worker."})

        request.state.firebase_claims = claims or {}
        return await call_next(request)


def authorize_student_scope(request: Request, school_id: str, student_id: str) -> None:
    if ALLOW_UNAUTHENTICATED_LOCAL_DEV:
        return
    claims = getattr(request.state, "firebase_claims", {}) or {}
    role = str(claims.get("role") or "")
    if str(claims.get("schoolId") or "") != school_id:
        raise HTTPException(status_code=403, detail="Cross-school access is not allowed.")
    if role == "student" and str(claims.get("studentId") or "") != student_id:
        raise HTTPException(status_code=403, detail="Students can analyze only their own audio.")
    if role == "parent" and student_id not in list(claims.get("studentIds") or []):
        raise HTTPException(status_code=403, detail="Parents can analyze only linked student audio.")
    if role == "teacher":
        raise HTTPException(status_code=403, detail="Teacher accounts cannot dispatch student audio jobs directly.")
