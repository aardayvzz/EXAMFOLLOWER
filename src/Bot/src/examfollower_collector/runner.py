from __future__ import annotations

import argparse
import asyncio
import logging
from collections.abc import Sequence

from pydantic import ValidationError

from examfollower_collector.api_client import CollectorApiClient, CollectorAuthenticationError
from examfollower_collector.config import CollectorSettings
from examfollower_collector.kurspro_sync import KursproSyncService
from examfollower_collector.logging_config import configure_logging
from examfollower_collector.sources.base import LessonSource, PermanentSourceError
from examfollower_collector.sources.kurspro import (
    KursproWeeklyScheduleSource,
    ManualLoginRequired,
    perform_manual_login,
)
from examfollower_collector.sync_service import SyncService

LOGGER = logging.getLogger(__name__)


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="ExamFollower lesson collector")
    parser.add_argument(
        "command",
        nargs="?",
        choices=("kurspro-login",),
        help="open the visible manual Kurspro login browser",
    )
    modes = parser.add_mutually_exclusive_group(required=False)
    modes.add_argument("--once", action="store_true", help="run one synchronization cycle")
    modes.add_argument("--service", action="store_true", help="run synchronization continuously")
    return parser


async def run_once(
    settings: CollectorSettings,
    *,
    sources: Sequence[LessonSource] = (),
    api_client: CollectorApiClient | None = None,
    kurspro_source: KursproWeeklyScheduleSource | None = None,
) -> int:
    if api_client is not None:
        service = SyncService(sources, api_client)
        await service.sync_once()
        if kurspro_source is not None:
            await KursproSyncService(kurspro_source, api_client).sync_once()
        return 0

    async with CollectorApiClient(settings) as owned_client:
        service = SyncService(sources, owned_client)
        await service.sync_once()
        if kurspro_source is not None:
            await KursproSyncService(kurspro_source, owned_client).sync_once()
    return 0


async def run_service(
    settings: CollectorSettings,
    *,
    sources: Sequence[LessonSource] = (),
    kurspro_source: KursproWeeklyScheduleSource | None = None,
) -> int:
    cancellation = asyncio.Event()
    async with CollectorApiClient(settings) as client:
        service = SyncService(sources, client)
        kurspro_service = (
            KursproSyncService(kurspro_source, client)
            if kurspro_source is not None
            else None
        )
        while not cancellation.is_set():
            await service.sync_once(cancellation)
            if kurspro_service is not None:
                await kurspro_service.sync_once(cancellation)
            try:
                await asyncio.wait_for(
                    cancellation.wait(),
                    timeout=settings.sync_interval_seconds,
                )
            except TimeoutError:
                continue
    return 0


def main(argv: Sequence[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    if args.command is None and not args.once and not args.service:
        parser.error("choose --once, --service, or kurspro-login")
    if args.command is not None and (args.once or args.service):
        parser.error("kurspro-login cannot be combined with a synchronization mode")
    try:
        settings = CollectorSettings()  # type: ignore[call-arg]
    except ValidationError:
        configure_logging()
        LOGGER.critical("Collector configuration is invalid")
        return 2

    configure_logging(secrets=(settings.collector_api_key.get_secret_value(),))
    mode = args.command or ("service" if args.service else "once")
    LOGGER.info("Collector starting mode=%s", mode)
    try:
        if args.command == "kurspro-login":
            asyncio.run(perform_manual_login(settings))
            LOGGER.info("Manual Kurspro browser login completed")
            return 0
        kurspro_source = (
            KursproWeeklyScheduleSource(settings)
            if settings.kurspro_enabled
            else None
        )
        return asyncio.run(
            run_service(settings, kurspro_source=kurspro_source)
            if args.service
            else run_once(settings, kurspro_source=kurspro_source)
        )
    except CollectorAuthenticationError:
        LOGGER.critical("Collector authentication configuration failed")
        return 3
    except PermanentSourceError:
        LOGGER.critical("Lesson source configuration failed")
        return 4
    except ManualLoginRequired:
        LOGGER.error(
            "Manual Kurspro login is required; run kurspro-login again"
        )
        return 5
    except KeyboardInterrupt:
        LOGGER.info("Collector stopped")
        return 0
