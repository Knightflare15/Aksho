"""Validated request contracts for the worker's public HTTP boundary."""

from pydantic import BaseModel, Field


class AnalyzeJobRequest(BaseModel):
    schoolId: str = Field(min_length=1, max_length=160)
    studentId: str = Field(min_length=1, max_length=160)
    jobId: str = Field(min_length=1, max_length=160)


class AnalyzePathRequest(BaseModel):
    wavPath: str = Field(min_length=1, max_length=2048)
    targetText: str | None = Field(default=None, max_length=240)


class TranslateRequest(BaseModel):
    text: str = Field(min_length=1, max_length=2000)
    sourceLanguage: str = Field(default="en", min_length=2, max_length=32)
    targetLanguage: str = Field(default="hi", min_length=2, max_length=32)
    ttsBackend: str | None = Field(default=None, max_length=200)
    voice: str | None = Field(default=None, max_length=100)


class TtsRequest(BaseModel):
    text: str = Field(min_length=1, max_length=1000)
    language: str = Field(default="hi", min_length=2, max_length=32)
    ttsBackend: str | None = Field(default=None, max_length=200)
    voice: str | None = Field(default=None, max_length=100)
