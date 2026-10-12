from __future__ import annotations

import hashlib
import unicodedata
from datetime import UTC, datetime, time, timedelta
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


class RecurringLessonSnapshotItem(BaseModel):
    model_config = ConfigDict(extra="forbid", frozen=True)

    external_id: str = Field(min_length=1, max_length=200)
    title: str = Field(min_length=2, max_length=200)
    iso_day_of_week: int = Field(ge=1, le=7)
    start_local_time: time
    end_local_time: time
    notification_enabled: bool = True
    reminder_minutes_before: int = 0
    lock_required: bool = True

    @model_validator(mode="after")
    def validate_time_range(self) -> RecurringLessonSnapshotItem:
        start_seconds = _seconds(self.start_local_time)
        end_seconds = _seconds(self.end_local_time)
        if end_seconds <= start_seconds or end_seconds - start_seconds > 8 * 60 * 60:
            raise ValueError("recurring lesson time range is invalid")
        if self.reminder_minutes_before not in {0, 5, 10, 15, 30, 60}:
            raise ValueError("recurring lesson reminder is invalid")
        return self


class RecurringLessonSnapshot(BaseModel):
    model_config = ConfigDict(extra="forbid", frozen=True)

    student_id: UUID
    source: str = "Kurspro"
    time_zone_id: str = "Europe/Istanbul"
    collected_at_utc: datetime
    is_complete_snapshot: bool
    lessons: tuple[RecurringLessonSnapshotItem, ...] = Field(min_length=1, max_length=100)

    @field_validator("collected_at_utc")
    @classmethod
    def require_aware_collected_at(cls, value: datetime) -> datetime:
        if value.tzinfo is None or value.utcoffset() is None:
            raise ValueError("collected_at_utc must be timezone-aware")
        return value

    @model_validator(mode="after")
    def validate_contract(self) -> RecurringLessonSnapshot:
        if self.student_id.int == 0 or self.source != "Kurspro":
            raise ValueError("recurring lesson snapshot identity is invalid")
        if self.time_zone_id != "Europe/Istanbul":
            raise ValueError("recurring lesson snapshot timezone is invalid")
        identifiers = [lesson.external_id for lesson in self.lessons]
        if len(identifiers) != len(set(identifiers)):
            raise ValueError("recurring lesson snapshot contains duplicate external IDs")
        return self


def normalize_lesson_title(value: str) -> str:
    normalized = unicodedata.normalize("NFC", value.strip())
    return " ".join(normalized.split()).casefold()


def create_kurspro_external_id(
    title: str,
    iso_day_of_week: int,
    start_local_time: time,
    end_local_time: time,
) -> str:
    canonical = "|".join(
        (
            "kurspro-weekly-v1",
            normalize_lesson_title(title),
            str(iso_day_of_week),
            start_local_time.strftime("%H:%M:%S"),
            end_local_time.strftime("%H:%M:%S"),
        )
    )
    digest = hashlib.sha256(canonical.encode("utf-8")).hexdigest()
    return f"kurspro-weekly-v1:{digest}"


def recurring_snapshot_payload(snapshot: RecurringLessonSnapshot) -> dict[str, Any]:
    return {
        "studentId": str(snapshot.student_id),
        "source": snapshot.source,
        "timeZoneId": snapshot.time_zone_id,
        "collectedAtUtc": _format_utc(snapshot.collected_at_utc),
        "isCompleteSnapshot": snapshot.is_complete_snapshot,
        "lessons": [
            {
                "externalId": lesson.external_id,
                "title": lesson.title,
                "isoDayOfWeek": lesson.iso_day_of_week,
                "startLocalTime": lesson.start_local_time.strftime("%H:%M:%S"),
                "endLocalTime": lesson.end_local_time.strftime("%H:%M:%S"),
                "notificationEnabled": lesson.notification_enabled,
                "reminderMinutesBefore": lesson.reminder_minutes_before,
                "lockRequired": lesson.lock_required,
            }
            for lesson in snapshot.lessons
        ],
    }


def _seconds(value: time) -> int:
    return value.hour * 3600 + value.minute * 60 + value.second
