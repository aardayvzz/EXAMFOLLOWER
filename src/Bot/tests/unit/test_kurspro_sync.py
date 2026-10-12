from __future__ import annotations

import asyncio

import pytest

from examfollower_collector.kurspro_sync import KursproSyncService
from examfollower_collector.sources.kurspro import (
    ManualLoginRequired,
    ScheduleParseError,
)


class FailingSource:
    def __init__(self, error: Exception) -> None:
        self.error = error
        self.calls = 0

    async def collect_snapshot(self, _: asyncio.Event | None = None) -> None:
        self.calls += 1
        raise self.error


class RecordingClient:
    def __init__(self) -> None:
        self.calls = 0

    async def send_recurring_lesson_snapshot(self, _: object) -> None:
        self.calls += 1


@pytest.mark.asyncio
async def test_manual_login_required_is_not_retried_or_uploaded() -> None:
    source = FailingSource(ManualLoginRequired())
    client = RecordingClient()
    result = await KursproSyncService(
        source,  # type: ignore[arg-type]
        client,  # type: ignore[arg-type]
    ).sync_once()
    assert result.manual_login_required
    assert source.calls == 1
    assert client.calls == 0


@pytest.mark.asyncio
async def test_empty_or_unexpected_table_is_not_uploaded() -> None:
    source = FailingSource(ScheduleParseError())
    client = RecordingClient()
    result = await KursproSyncService(
        source,  # type: ignore[arg-type]
        client,  # type: ignore[arg-type]
    ).sync_once()
    assert not result.uploaded
    assert source.calls == 1
    assert client.calls == 0
