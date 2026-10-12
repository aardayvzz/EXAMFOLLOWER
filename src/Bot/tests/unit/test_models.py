from __future__ import annotations

from datetime import datetime, timedelta, timezone
from uuid import UUID

import pytest
from pydantic import ValidationError

from examfollower_collector.models import (
    CollectedZoomLesson,
    LessonSourceName,
    to_backend_payload,
)

STUDENT_ID = UUID("11111111-1111-1111-1111-111111111111")


def lesson(**overrides: object) -> CollectedZoomLesson:
    start = datetime(2026, 10, 13, 18, tzinfo=timezone(timedelta(hours=3)))
    values: dict[str, object] = {
        "student_id": STUDENT_ID,
        "title": " Matematik Canlı Ders ",
        "start_at_utc": start,
        "end_at_utc": start + timedelta(hours=1),
        "join_url": "https://tedu.zoom.us/j/123456789?pwd=preserved",
        "source": LessonSourceName.KURSPRO,
        "external_id": " lesson-123 ",
        "lock_required": True,
    }
    values.update(overrides)
    return CollectedZoomLesson.model_validate(values)


def test_payload_uses_exact_backend_field_names() -> None:
    payload = to_backend_payload(lesson())
    assert set(payload) == {
        "studentId",
        "title",
        "startAtUtc",
        "endAtUtc",
        "joinUrl",
        "source",
        "externalId",
        "lockRequired",
    }
    assert payload["studentId"] == str(STUDENT_ID)
    assert payload["title"] == "Matematik Canlı Ders"
    assert payload["externalId"] == "lesson-123"


def test_payload_times_are_converted_to_utc_with_z_suffix() -> None:
    payload = to_backend_payload(lesson())
    assert payload["startAtUtc"] == "2026-10-13T15:00:00Z"
    assert payload["endAtUtc"] == "2026-10-13T16:00:00Z"


def test_non_zoom_url_is_rejected() -> None:
    with pytest.raises(ValidationError):
        lesson(join_url="https://example.test/j/123")


def test_http_zoom_url_is_rejected() -> None:
    with pytest.raises(ValidationError):
        lesson(join_url="http://zoom.us/j/123")


def test_zoom_url_credentials_are_rejected() -> None:
    with pytest.raises(ValidationError):
        lesson(join_url="https://user:password@zoom.us/j/123")


def test_join_url_is_hidden_from_model_repr() -> None:
    value = lesson()
    assert "zoom.us" not in repr(value)
    assert "pwd=" not in repr(value)
