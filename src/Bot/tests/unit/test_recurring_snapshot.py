from __future__ import annotations

from datetime import UTC, datetime, time
from uuid import UUID

import httpx
import pytest

from examfollower_collector.api_client import (
    CollectorApiClient,
    CollectorApiError,
    CollectorAuthenticationError,
)
from examfollower_collector.config import CollectorSettings
from examfollower_collector.models import (
    RecurringLessonSnapshot,
    RecurringLessonSnapshotItem,
    recurring_snapshot_payload,
)

API_KEY = "collector-test-key-that-is-long-enough"


def settings(*, retries: int = 0) -> CollectorSettings:
    return CollectorSettings(
        environment="Production",
        api_base_url="https://api.example.test",
        collector_api_key=API_KEY,
        max_retry_attempts=retries,
    )


def snapshot() -> RecurringLessonSnapshot:
    return RecurringLessonSnapshot(
        student_id=UUID("11111111-1111-1111-1111-111111111111"),
        collected_at_utc=datetime(2026, 10, 12, 18, tzinfo=UTC),
        is_complete_snapshot=True,
        lessons=(
            RecurringLessonSnapshotItem(
                external_id="kurspro-weekly-v1:" + "a" * 64,
                title="Matematik",
                iso_day_of_week=1,
                start_local_time=time(18),
                end_local_time=time(19),
            ),
        ),
    )


def test_snapshot_json_matches_backend_contract() -> None:
    payload = recurring_snapshot_payload(snapshot())
    assert payload == {
        "studentId": "11111111-1111-1111-1111-111111111111",
        "source": "Kurspro",
        "timeZoneId": "Europe/Istanbul",
        "collectedAtUtc": "2026-10-12T18:00:00Z",
        "isCompleteSnapshot": True,
        "lessons": [
            {
                "externalId": "kurspro-weekly-v1:" + "a" * 64,
                "title": "Matematik",
                "isoDayOfWeek": 1,
                "startLocalTime": "18:00:00",
                "endLocalTime": "19:00:00",
                "notificationEnabled": True,
                "reminderMinutesBefore": 0,
                "lockRequired": True,
            }
        ],
    }


@pytest.mark.asyncio
async def test_snapshot_400_is_not_retried() -> None:
    calls = 0

    def handler(_: httpx.Request) -> httpx.Response:
        nonlocal calls
        calls += 1
        return httpx.Response(400)

    async with httpx.AsyncClient(transport=httpx.MockTransport(handler)) as http:
        client = CollectorApiClient(settings(retries=3), http_client=http)
        with pytest.raises(CollectorApiError):
            await client.send_recurring_lesson_snapshot(snapshot())
    assert calls == 1


@pytest.mark.asyncio
@pytest.mark.parametrize("status", [401, 403])
async def test_snapshot_authentication_failure_is_not_retried(status: int) -> None:
    calls = 0

    def handler(_: httpx.Request) -> httpx.Response:
        nonlocal calls
        calls += 1
        return httpx.Response(status)

    async with httpx.AsyncClient(transport=httpx.MockTransport(handler)) as http:
        client = CollectorApiClient(settings(retries=3), http_client=http)
        with pytest.raises(CollectorAuthenticationError):
            await client.send_recurring_lesson_snapshot(snapshot())
    assert calls == 1


@pytest.mark.asyncio
async def test_snapshot_500_uses_existing_retry_policy() -> None:
    calls = 0

    def handler(_: httpx.Request) -> httpx.Response:
        nonlocal calls
        calls += 1
        if calls == 1:
            return httpx.Response(500)
        return httpx.Response(
            200,
            json={
                "createdCount": 1,
                "updatedCount": 0,
                "reactivatedCount": 0,
                "deactivatedCount": 0,
                "unchangedCount": 0,
            },
        )

    async with httpx.AsyncClient(transport=httpx.MockTransport(handler)) as http:
        client = CollectorApiClient(settings(retries=1), http_client=http)
        result = await client.send_recurring_lesson_snapshot(snapshot())
    assert calls == 2
    assert result.created_count == 1
