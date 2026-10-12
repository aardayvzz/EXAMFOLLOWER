from __future__ import annotations

import asyncio
from collections.abc import Sequence
from typing import Protocol, runtime_checkable

from examfollower_collector.models import CollectedZoomLesson


class SourceError(Exception):
    """Safe base error for a lesson source."""


class TemporarySourceError(SourceError):
    """A source failure that may succeed in a later cycle."""


class PermanentSourceError(SourceError):
    """A source configuration or authentication failure."""


@runtime_checkable
class LessonSource(Protocol):
    @property
    def name(self) -> str: ...

    async def collect_lessons(
        self,
        cancellation: asyncio.Event,
    ) -> Sequence[CollectedZoomLesson]: ...
