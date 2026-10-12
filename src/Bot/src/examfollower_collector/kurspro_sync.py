from __future__ import annotations

import asyncio
import logging
from dataclasses import dataclass

from examfollower_collector.api_client import (
    CollectorApiClient,
    CollectorApiError,
    CollectorAuthenticationError,
    SnapshotUploadResult,
)
from examfollower_collector.sources.kurspro import (
    KursproWeeklyScheduleSource,
    ManualLoginRequired,
    ScheduleParseError,
)


@dataclass(frozen=True, slots=True)
class KursproSyncResult:
    uploaded: bool
    manual_login_required: bool
    response: SnapshotUploadResult | None = None


class KursproSyncService:
    def __init__(
        self,
        source: KursproWeeklyScheduleSource,
        api_client: CollectorApiClient,
        *,
        logger: logging.Logger | None = None,
    ) -> None:
        self._source = source
        self._api_client = api_client
        self._logger = logger or logging.getLogger(__name__)
        self._lock = asyncio.Lock()

    async def sync_once(
        self,
        cancellation: asyncio.Event | None = None,
    ) -> KursproSyncResult:
        async with self._lock:
            try:
                snapshot = await self._source.collect_snapshot(cancellation)
            except ManualLoginRequired:
                self._logger.warning(
                    "Kurspro session requires a new manual browser login"
                )
                return KursproSyncResult(False, True)
            except ScheduleParseError:
                self._logger.error(
                    "Kurspro weekly schedule could not be parsed safely"
                )
                return KursproSyncResult(False, False)

            try:
                response = await self._api_client.send_recurring_lesson_snapshot(
                    snapshot
                )
            except CollectorAuthenticationError:
                self._logger.critical(
                    "Collector authentication configuration failed"
                )
                raise
            except CollectorApiError as error:
                self._logger.error(
                    "Kurspro snapshot upload failed status=%s",
                    error.status_code if error.status_code is not None else "network",
                )
                return KursproSyncResult(False, False)

            self._logger.info(
                "Kurspro snapshot synchronized lessons=%s complete=%s",
                len(snapshot.lessons),
                snapshot.is_complete_snapshot,
            )
            return KursproSyncResult(True, False, response)
