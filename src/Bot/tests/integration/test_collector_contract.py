from __future__ import annotations

import json
from datetime import UTC, datetime, timedelta

import httpx
import pytest

from examfollower_collector.api_client import (
    COLLECTOR_API_KEY_HEADER,
    COLLECTOR_UPSERT_PATH,
    CollectorApiClient,
)
from examfollower_collector.config import CollectorSettings
from examfollower_collector.models import CollectedZoomLesson


@pytest.mark.asyncio
async def test_backend_collector_endpoint_and_payload_contract() -> None:
    api_key = "integration-key-that-is-at-least-32-bytes"
    captured: list[httpx.Request] = []

    def handler(request: httpx.Request) -> httpx.Response:
        captured.append(request)
        return httpx.Response(
            201,
            json={
                "created": True,
                "lesson": {"id": "22222222-2222-2222-2222-222222222222"},
            },
        )

    settings = CollectorSettings(
        environment="Production",
        api_base_url="https://api.example.test",
        collector_api_key=api_key,
        max_retry_attempts=0,
    )
    start = datetime(2026, 10, 13, 15, tzinfo=UTC)
    lesson = CollectedZoomLesson.model_validate(
        {
            "student_id": "11111111-1111-1111-1111-111111111111",
            "title": "Matematik Canlı Ders",
            "start_at_utc": start,
            "end_at_utc": start + timedelta(hours=1),
            "join_url": "https://tedu.zoom.us/j/123456789?pwd=preserved",
            "source": "Kurspro",
            "external_id": "kurspro-lesson-123",
            "lock_required": True,
        }
    )

    async with httpx.AsyncClient(transport=httpx.MockTransport(handler)) as http_client:
        client = CollectorApiClient(settings, http_client=http_client)
        await client.upsert_lesson(lesson)

    request = captured[0]
    assert request.method == "POST"
    assert request.url.path == COLLECTOR_UPSERT_PATH
    assert request.headers[COLLECTOR_API_KEY_HEADER] == api_key
    assert json.loads(request.content) == {
        "studentId": "11111111-1111-1111-1111-111111111111",
        "title": "Matematik Canlı Ders",
        "startAtUtc": "2026-10-13T15:00:00Z",
        "endAtUtc": "2026-10-13T16:00:00Z",
        "joinUrl": "https://tedu.zoom.us/j/123456789?pwd=preserved",
        "source": "Kurspro",
        "externalId": "kurspro-lesson-123",
        "lockRequired": True,
    }
