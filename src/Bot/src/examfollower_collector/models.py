from __future__ import annotations

from datetime import UTC, datetime, timedelta
from enum import StrEnum
from typing import Any
from urllib.parse import urlsplit
from uuid import UUID

from pydantic import BaseModel, ConfigDict, Field, HttpUrl, field_validator, model_validator


class LessonSourceName(StrEnum):
    KURSPRO = "Kurspro"
    EMAIL = "Email"


class CollectedZoomLesson(BaseModel):
    model_config = ConfigDict(extra="forbid", frozen=True, revalidate_instances="always")

    student_id: UUID
    title: str = Field(min_length=2, max_length=200)
    start_at_utc: datetime
    end_at_utc: datetime
    join_url: HttpUrl = Field(repr=False)
    source: LessonSourceName
    external_id: str = Field(min_length=1, max_length=200)
    lock_required: bool

    @field_validator("student_id")
    @classmethod
    def validate_student_id(cls, value: UUID) -> UUID:
        if value.int == 0:
            raise ValueError("student ID cannot be empty")
        return value

    @field_validator("title", "external_id", mode="before")
    @classmethod
    def strip_bounded_text(cls, value: object) -> object:
        return value.strip() if isinstance(value, str) else value

    @field_validator("start_at_utc", "end_at_utc")
    @classmethod
    def require_aware_datetime(cls, value: datetime) -> datetime:
        if value.tzinfo is None or value.utcoffset() is None:
            raise ValueError("datetime must be timezone-aware")
        return value

    @field_validator("join_url")
    @classmethod
    def validate_zoom_url(cls, value: HttpUrl) -> HttpUrl:
        parsed = urlsplit(str(value))
        host = (parsed.hostname or "").casefold()
        if parsed.scheme.casefold() != "https":
            raise ValueError("Zoom URL must use HTTPS")
        if parsed.username is not None or parsed.password is not None:
            raise ValueError("Zoom URL cannot contain credentials")
        if host != "zoom.us" and not host.endswith(".zoom.us"):
            raise ValueError("Zoom URL host is not supported")
        return value

    @model_validator(mode="after")
    def validate_schedule(self) -> CollectedZoomLesson:
        duration = self.end_at_utc - self.start_at_utc
        if duration <= timedelta(0):
            raise ValueError("lesson end must be after lesson start")
        if duration > timedelta(hours=8):
            raise ValueError("lesson duration cannot exceed eight hours")
        return self


def to_backend_payload(lesson: CollectedZoomLesson) -> dict[str, Any]:
    """Map one validated lesson to the exact backend collector contract."""

    return {
        "studentId": str(lesson.student_id),
        "title": lesson.title,
        "startAtUtc": _format_utc(lesson.start_at_utc),
        "endAtUtc": _format_utc(lesson.end_at_utc),
        "joinUrl": str(lesson.join_url),
        "source": lesson.source.value,
        "externalId": lesson.external_id,
        "lockRequired": lesson.lock_required,
    }


def _format_utc(value: datetime) -> str:
    return value.astimezone(UTC).isoformat().replace("+00:00", "Z")
