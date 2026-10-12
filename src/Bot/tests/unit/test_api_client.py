from __future__ import annotations

import json
from datetime import UTC, datetime, timedelta
from uuid import UUID

import httpx
import pytest

from examfollower_collector.api_client import (
    COLLECTOR_API_KEY_HEADER,
    CollectorApiClient,
    CollectorApiError,
    CollectorAuthenticationError,
)
from examfollower_collector.config import CollectorSettings
from examfollower_collector.models import CollectedZoomLesson

API_KEY = "collector-test-key-that-is-long-enough"
LESSON_ID = UUID("22222222-2222-2222-2222-222222222222")


def settings(*, retries: int = 0) -> CollectorSettings:
    return CollectorSettings(
        environment="Production",
        api_base_url="https://api.example.test",
        collector_api_key=API_KEY,
        max_retry_attempts=retries,
    )


def lesson() -> CollectedZoomLesson:
    start = datetime(2026, 10, 13, 15, tzinfo=UTC)
    return CollectedZoomLesson.model_validate(
        {
            "student_id": "11111111-1111-1111-1111-111111111111",
            "title": "Matematik Canlı Ders",
            "start_at_utc": start,
            "end_at_utc": start + timedelta(hours=1),
            "join_url": "https://tedu.zoom.us/j/123?pwd=sensitive",
            "source": "Kurspro",
            "external_id": "lesson-123",
            "lock_required": True,
        }
    )


def success(status_code: int, created: bool) -> httpx.Response:
    return httpx.Response(
        status_code,
        json={"created": created, "lesson": {"id": str(LESSON_ID)}},
    )


@pytest.mark.asyncio
async def test_api_key_is_sent_only_in_expected_header() -> None:
    observed: list[httpx.Request] = []

    def handler(request: httpx.Request) -> httpx.Response:
        observed.append(request)
        return success(201, True)

    http_client = httpx.AsyncClient(transport=httpx.MockTransport(handler))
    client = CollectorApiClient(settings(), http_client=http_client)
    await client.upsert_lesson(lesson())
    await http_client.aclose()

    request = observed[0]
    assert request.headers[COLLECTOR_API_KEY_HEADER] == API_KEY
    assert API_KEY not in str(request.url)
    assert request.url.query == b""


@pytest.mark.asyncio
@pytest.mark.parametrize(("status", "created"), [(200, False), (201, True)])
async def test_success_status_is_interpreted(status: int, created: bool) -> None:
    transport = httpx.MockTransport(lambda _: success(status, created))
    async with httpx.AsyncClient(transport=transport) as http_client:
        client = CollectorApiClient(settings(), http_client=http_client)
        result = await client.upsert_lesson(lesson())
    assert result.created is created
    assert result.status_code == status
    assert result.lesson_id == LESSON_ID


@pytest.mark.asyncio
async def test_400_is_not_retried() -> None:
    calls = 0

    def handler(_: httpx.Request) -> httpx.Response:
        nonlocal calls
        calls += 1
        return httpx.Response(400)

    async with httpx.AsyncClient(transport=httpx.MockTransport(handler)) as http_client:
        client = CollectorApiClient(settings(retries=3), http_client=http_client)
        with pytest.raises(CollectorApiError, match="Collector API request failed"):
            await client.upsert_lesson(lesson())
    assert calls == 1


@pytest.mark.asyncio
async def test_401_is_not_retried_and_is_authentication_error() -> None:
    calls = 0

    def handler(_: httpx.Request) -> httpx.Response:
        nonlocal calls
        calls += 1
        return httpx.Response(401)

    async with httpx.AsyncClient(transport=httpx.MockTransport(handler)) as http_client:
        client = CollectorApiClient(settings(retries=3), http_client=http_client)
        with pytest.raises(CollectorAuthenticationError):
            await client.upsert_lesson(lesson())
    assert calls == 1


@pytest.mark.asyncio
async def test_request_body_is_json_not_log_or_query_data() -> None:
    observed_body: dict[str, object] = {}

    def handler(request: httpx.Request) -> httpx.Response:
        observed_body.update(json.loads(request.content))
        return success(201, True)

    async with httpx.AsyncClient(transport=httpx.MockTransport(handler)) as http_client:
        client = CollectorApiClient(settings(), http_client=http_client)
        await client.upsert_lesson(lesson())
    assert observed_body["source"] == "Kurspro"
