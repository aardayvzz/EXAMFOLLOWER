from __future__ import annotations

import asyncio
import logging
from datetime import UTC, datetime, timedelta
from typing import Any
from uuid import uuid4

import pytest

from examfollower_collector.api_client import UploadResult
from examfollower_collector.logging_config import SensitiveDataFilter
from examfollower_collector.models import CollectedZoomLesson
from examfollower_collector.runner import run_once
from examfollower_collector.sync_service import SyncService


def lesson(**overrides: object) -> CollectedZoomLesson:
    start = datetime(2026, 10, 13, 15, tzinfo=UTC)
    values: dict[str, object] = {
        "student_id": "11111111-1111-1111-1111-111111111111",
        "title": "Matematik Canlı Ders",
        "start_at_utc": start,
        "end_at_utc": start + timedelta(hours=1),
        "join_url": "https://tedu.zoom.us/j/123?pwd=sensitive",
        "source": "Kurspro",
        "external_id": "lesson-123",
        "lock_required": True,
    }
    values.update(overrides)
    return CollectedZoomLesson.model_validate(values)


class FakeSource:
    name = "Fake"

    def __init__(self, lessons: list[Any]) -> None:
        self._lessons = lessons

    async def collect_lessons(self, _: asyncio.Event) -> list[Any]:
        return self._lessons


class FakeClient:
    def __init__(self) -> None:
        self.lessons: list[CollectedZoomLesson] = []

    async def upsert_lesson(self, item: CollectedZoomLesson) -> UploadResult:
        self.lessons.append(item)
        return UploadResult(True, uuid4(), 201)


@pytest.mark.asyncio
async def test_identical_duplicates_are_uploaded_once() -> None:
    item = lesson()
    source = FakeSource([item, item])
    client = FakeClient()
    summary = await SyncService([source], client).sync_once()  # type: ignore[arg-type]
    assert len(client.lessons) == 1
    assert summary.collected_count == 2
    assert summary.conflict_count == 0


@pytest.mark.asyncio
async def test_conflicting_duplicates_are_not_uploaded() -> None:
    source = FakeSource([lesson(), lesson(title="Different title")])
    client = FakeClient()
    summary = await SyncService([source], client).sync_once()  # type: ignore[arg-type]
    assert client.lessons == []
    assert summary.conflict_count == 1
    assert summary.failed_count == 1


@pytest.mark.asyncio
async def test_invalid_lesson_does_not_block_valid_lesson() -> None:
    invalid = lesson().model_dump()
    invalid["join_url"] = "https://example.test/not-zoom"
    client = FakeClient()
    summary = await SyncService(
        [FakeSource([invalid, lesson(external_id="valid")])],  # type: ignore[list-item]
        client,  # type: ignore[arg-type]
    ).sync_once()
    assert len(client.lessons) == 1
    assert summary.failed_count == 1
    assert summary.uploaded_count == 1


def test_sensitive_filter_redacts_api_key_join_url_and_jwt() -> None:
    api_key = "secret-key-that-must-never-be-logged"
    zoom_url = "https://zoom.us/j/123?pwd=secret"
    token = "eyJheader.payload.signature"
    record = logging.LogRecord(
        "test",
        logging.INFO,
        __file__,
        1,
        "%s %s %s",
        (api_key, zoom_url, token),
        None,
    )
    SensitiveDataFilter([api_key]).filter(record)
    rendered = record.getMessage()
    assert api_key not in rendered
    assert zoom_url not in rendered
    assert token not in rendered


@pytest.mark.asyncio
async def test_once_with_no_sources_completes_safely() -> None:
    client = FakeClient()
    result = await run_once(None, sources=(), api_client=client)  # type: ignore[arg-type]
    assert result == 0
    assert client.lessons == []
