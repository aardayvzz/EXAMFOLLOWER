from __future__ import annotations

from dataclasses import dataclass
from typing import Any
from uuid import UUID

import httpx

from examfollower_collector.config import CollectorSettings
from examfollower_collector.models import (
    CollectedZoomLesson,
    RecurringLessonSnapshot,
    recurring_snapshot_payload,
    to_backend_payload,
)
from examfollower_collector.retry import RetryPolicy

COLLECTOR_UPSERT_PATH = "/api/collector/zoom-lessons/upsert"
COLLECTOR_API_KEY_HEADER = "X-Collector-Api-Key"
RECURRING_LESSON_SNAPSHOT_PATH = "/api/collector/recurring-lessons/snapshot"


@dataclass(frozen=True, slots=True)
class UploadResult:
    created: bool
    lesson_id: UUID
    status_code: int


@dataclass(frozen=True, slots=True)
class SnapshotUploadResult:
    created_count: int
    updated_count: int
    reactivated_count: int
    deactivated_count: int
    unchanged_count: int


class CollectorApiError(Exception):
    def __init__(self, status_code: int | None = None) -> None:
        self.status_code = status_code
        super().__init__("Collector API request failed")


class CollectorAuthenticationError(CollectorApiError):
    def __init__(self, status_code: int) -> None:
        super().__init__(status_code)
        self.args = ("Collector API authentication failed",)


class CollectorApiClient:
    def __init__(
        self,
        settings: CollectorSettings,
        *,
        http_client: httpx.AsyncClient | None = None,
        retry_policy: RetryPolicy | None = None,
    ) -> None:
        self._settings = settings
        self._owns_client = http_client is None
        self._http_client = http_client or httpx.AsyncClient()
        self._retry_policy = retry_policy or RetryPolicy(settings.max_retry_attempts)

    async def __aenter__(self) -> CollectorApiClient:
        return self

    async def __aexit__(self, *_: object) -> None:
        await self.aclose()

    async def aclose(self) -> None:
        if self._owns_client:
            await self._http_client.aclose()

    async def upsert_lesson(self, lesson: CollectedZoomLesson) -> UploadResult:
        url = f"{self._settings.api_base_url}{COLLECTOR_UPSERT_PATH}"
        payload = to_backend_payload(lesson)

        async def send() -> httpx.Response:
            return await self._http_client.post(
                url,
                headers={
                    COLLECTOR_API_KEY_HEADER:
                        self._settings.collector_api_key.get_secret_value()
                },
                json=payload,
                timeout=self._settings.http_timeout_seconds,
            )

        try:
            response = await self._retry_policy.execute(send)
        except (httpx.TimeoutException, httpx.NetworkError) as error:
            raise CollectorApiError() from error
        if response.status_code in {401, 403}:
            raise CollectorAuthenticationError(response.status_code)
        if response.status_code not in {200, 201}:
            raise CollectorApiError(response.status_code)

        return self._parse_success(response)

    async def send_recurring_lesson_snapshot(
        self,
        snapshot: RecurringLessonSnapshot,
    ) -> SnapshotUploadResult:
        url = f"{self._settings.api_base_url}{RECURRING_LESSON_SNAPSHOT_PATH}"

        async def send() -> httpx.Response:
            return await self._http_client.post(
                url,
                headers={
                    COLLECTOR_API_KEY_HEADER:
                        self._settings.collector_api_key.get_secret_value()
                },
                json=recurring_snapshot_payload(snapshot),
                timeout=self._settings.http_timeout_seconds,
            )

        try:
            response = await self._retry_policy.execute(send)
        except (httpx.TimeoutException, httpx.NetworkError) as error:
            raise CollectorApiError() from error
        if response.status_code in {401, 403}:
            raise CollectorAuthenticationError(response.status_code)
        if response.status_code != 200:
            raise CollectorApiError(response.status_code)
        try:
            body: Any = response.json()
            return SnapshotUploadResult(
                created_count=int(body["createdCount"]),
                updated_count=int(body["updatedCount"]),
                reactivated_count=int(body["reactivatedCount"]),
                deactivated_count=int(body["deactivatedCount"]),
                unchanged_count=int(body["unchangedCount"]),
            )
        except (KeyError, TypeError, ValueError) as error:
            raise CollectorApiError(response.status_code) from error

    @staticmethod
    def _parse_success(response: httpx.Response) -> UploadResult:
        try:
            body: Any = response.json()
            created = body["created"]
            lesson_id = UUID(body["lesson"]["id"])
            if not isinstance(created, bool):
                raise TypeError
        except (KeyError, TypeError, ValueError) as error:
            raise CollectorApiError(response.status_code) from error
        return UploadResult(
            created=created,
            lesson_id=lesson_id,
            status_code=response.status_code,
        )
