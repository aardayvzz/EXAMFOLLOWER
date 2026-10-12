from __future__ import annotations

import asyncio
import logging
from collections.abc import Callable, Sequence
from dataclasses import dataclass
from datetime import UTC, datetime

from pydantic import ValidationError

from examfollower_collector.api_client import (
    CollectorApiClient,
    CollectorApiError,
    CollectorAuthenticationError,
)
from examfollower_collector.models import CollectedZoomLesson
from examfollower_collector.sources.base import (
    LessonSource,
    PermanentSourceError,
    SourceError,
)

Clock = Callable[[], datetime]


@dataclass(frozen=True, slots=True)
class SyncSummary:
    source_count: int
    collected_count: int
    uploaded_count: int
    created_count: int
    updated_count: int
    failed_count: int
    conflict_count: int
    started_at_utc: datetime
    completed_at_utc: datetime


class SyncService:
    def __init__(
        self,
        sources: Sequence[LessonSource],
        api_client: CollectorApiClient,
        *,
        clock: Clock | None = None,
        logger: logging.Logger | None = None,
    ) -> None:
        self._sources = tuple(sources)
        self._api_client = api_client
        self._clock = clock or (lambda: datetime.now(UTC))
        self._logger = logger or logging.getLogger(__name__)
        self._lock = asyncio.Lock()

    async def sync_once(self, cancellation: asyncio.Event | None = None) -> SyncSummary:
        cancellation = cancellation or asyncio.Event()
        async with self._lock:
            return await self._sync_once(cancellation)

    async def _sync_once(self, cancellation: asyncio.Event) -> SyncSummary:
        started = self._clock()
        collected_count = 0
        failed_count = 0
        conflict_count = 0
        candidates: list[CollectedZoomLesson] = []

        if not self._sources:
            self._logger.info("No lesson sources are configured")

        for source in self._sources:
            if cancellation.is_set():
                break
            try:
                raw_lessons = await source.collect_lessons(cancellation)
            except PermanentSourceError:
                self._logger.critical(
                    "Lesson source configuration failed source=%s",
                    source.name,
                )
                raise
            except SourceError:
                failed_count += 1
                self._logger.error("Lesson source failed source=%s", source.name)
                continue

            collected_count += len(raw_lessons)
            self._logger.info(
                "Lesson source completed source=%s collected=%s",
                source.name,
                len(raw_lessons),
            )
            for raw_lesson in raw_lessons:
                try:
                    candidates.append(CollectedZoomLesson.model_validate(raw_lesson))
                except ValidationError:
                    failed_count += 1
                    self._logger.warning(
                        "Collected lesson validation failed source=%s",
                        source.name,
                    )

        unique: dict[tuple[object, object, str], CollectedZoomLesson] = {}
        conflicts: set[tuple[object, object, str]] = set()
        for lesson in candidates:
            key = (lesson.student_id, lesson.source, lesson.external_id)
            existing = unique.get(key)
            if existing is None:
                unique[key] = lesson
            elif existing != lesson:
                conflicts.add(key)

        for conflict_key in conflicts:
            unique.pop(conflict_key, None)
        conflict_count += len(conflicts)
        failed_count += len(conflicts)
        if conflicts:
            self._logger.warning("Conflicting collected lessons skipped count=%s", len(conflicts))

        uploaded_count = 0
        created_count = 0
        updated_count = 0
        for lesson in unique.values():
            if cancellation.is_set():
                break
            try:
                result = await self._api_client.upsert_lesson(lesson)
            except CollectorAuthenticationError:
                self._logger.critical("Collector authentication configuration failed")
                raise
            except CollectorApiError as error:
                failed_count += 1
                self._logger.error(
                    "Collector upload failed status=%s",
                    error.status_code if error.status_code is not None else "network",
                )
                continue
            uploaded_count += 1
            if result.created:
                created_count += 1
            else:
                updated_count += 1

        completed = self._clock()
        summary = SyncSummary(
            source_count=len(self._sources),
            collected_count=collected_count,
            uploaded_count=uploaded_count,
            created_count=created_count,
            updated_count=updated_count,
            failed_count=failed_count,
            conflict_count=conflict_count,
            started_at_utc=started,
            completed_at_utc=completed,
        )
        self._logger.info(
            "Sync cycle completed sources=%s collected=%s uploaded=%s created=%s "
            "updated=%s failed=%s conflicts=%s duration_seconds=%.3f",
            summary.source_count,
            summary.collected_count,
            summary.uploaded_count,
            summary.created_count,
            summary.updated_count,
            summary.failed_count,
            summary.conflict_count,
            max(0.0, (completed - started).total_seconds()),
        )
        return summary
