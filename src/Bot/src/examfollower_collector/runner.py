from __future__ import annotations

import argparse
import asyncio
import logging
from collections.abc import Sequence

from pydantic import ValidationError

from examfollower_collector.api_client import CollectorApiClient, CollectorAuthenticationError
from examfollower_collector.config import CollectorSettings
from examfollower_collector.logging_config import configure_logging
from examfollower_collector.sources.base import LessonSource, PermanentSourceError
from examfollower_collector.sync_service import SyncService

LOGGER = logging.getLogger(__name__)


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="ExamFollower lesson collector")
    modes = parser.add_mutually_exclusive_group(required=True)
    modes.add_argument("--once", action="store_true", help="run one synchronization cycle")
    modes.add_argument("--service", action="store_true", help="run synchronization continuously")
    return parser


async def run_once(
    settings: CollectorSettings,
    *,
    sources: Sequence[LessonSource] = (),
    api_client: CollectorApiClient | None = None,
) -> int:
    if api_client is not None:
        service = SyncService(sources, api_client)
        await service.sync_once()
        return 0

    async with CollectorApiClient(settings) as owned_client:
        service = SyncService(sources, owned_client)
        await service.sync_once()
    return 0


async def run_service(
    settings: CollectorSettings,
    *,
    sources: Sequence[LessonSource] = (),
) -> int:
    cancellation = asyncio.Event()
    async with CollectorApiClient(settings) as client:
        service = SyncService(sources, client)
        while not cancellation.is_set():
            await service.sync_once(cancellation)
            try:
                await asyncio.wait_for(
                    cancellation.wait(),
                    timeout=settings.sync_interval_seconds,
                )
            except TimeoutError:
                continue
    return 0


def main(argv: Sequence[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    try:
        settings = CollectorSettings()  # type: ignore[call-arg]
    except ValidationError:
        configure_logging()
        LOGGER.critical("Collector configuration is invalid")
        return 2

    configure_logging(secrets=(settings.collector_api_key.get_secret_value(),))
    LOGGER.info("Collector starting mode=%s", "service" if args.service else "once")
    try:
        return asyncio.run(
            run_service(settings) if args.service else run_once(settings)
        )
    except CollectorAuthenticationError:
        LOGGER.critical("Collector authentication configuration failed")
        return 3
    except PermanentSourceError:
        LOGGER.critical("Lesson source configuration failed")
        return 4
    except KeyboardInterrupt:
        LOGGER.info("Collector stopped")
        return 0
