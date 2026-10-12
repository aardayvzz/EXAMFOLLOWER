from __future__ import annotations

import asyncio
import re
from collections.abc import Callable, Sequence
from dataclasses import dataclass
from datetime import UTC, datetime, time
from urllib.parse import urljoin, urlsplit

from playwright.async_api import BrowserContext, Page, Route, TimeoutError, async_playwright

from examfollower_collector.config import CollectorSettings
from examfollower_collector.models import (
    RecurringLessonSnapshot,
    RecurringLessonSnapshotItem,
    create_kurspro_external_id,
)
from examfollower_collector.sources.base import PermanentSourceError, SourceError

Clock = Callable[[], datetime]
_TIME_RANGE = re.compile(
    r"(?P<start>\d{1,2}[:.]\d{2})(?::\d{2})?\s*[-–—]\s*"
    r"(?P<end>\d{1,2}[:.]\d{2})(?::\d{2})?"
)
_DAYS = {
    "pazartesi": 1,
    "salı": 2,
    "sali": 2,
    "çarşamba": 3,
    "carsamba": 3,
    "perşembe": 4,
    "persembe": 4,
    "cuma": 5,
    "cumartesi": 6,
    "pazar": 7,
}


class ManualLoginRequired(SourceError):
    """The local persistent browser session is no longer authenticated."""


class ScheduleParseError(SourceError):
    """The weekly schedule cannot safely produce a snapshot."""


@dataclass(frozen=True, slots=True)
class ParsedSchedule:
    lessons: tuple[RecurringLessonSnapshotItem, ...]
    is_complete: bool


def is_allowed_kurspro_url(url: str) -> bool:
    parsed = urlsplit(url)
    return (
        parsed.scheme == "https"
        and parsed.hostname == "kurspro.net"
        and parsed.port is None
        and parsed.username is None
        and parsed.password is None
    )


def is_login_url(url: str) -> bool:
    parsed = urlsplit(url)
    return is_allowed_kurspro_url(url) and parsed.path.casefold() == "/o/login.php"


def ensure_authenticated_schedule_url(url: str) -> None:
    if is_login_url(url):
        raise ManualLoginRequired("Manual Kurspro login is required")
    if not is_allowed_kurspro_url(url):
        raise PermanentSourceError("Kurspro navigation left the approved domain")


def parse_weekly_schedule(
    headers: Sequence[str],
    rows: Sequence[Sequence[str]],
) -> ParsedSchedule:
    normalized_headers = [_normalize_cell(value).casefold() for value in headers]
    if (
        len(normalized_headers) < 3
        or not any(value in {"gün", "gun"} for value in normalized_headers)
        or not any("ders" in value or "saat" in value for value in normalized_headers)
    ):
        raise ScheduleParseError("Kurspro weekly schedule structure is not recognized")

    lessons: list[RecurringLessonSnapshotItem] = []
    complete = True
    current_day: int | None = None
    for raw_cells in rows:
        cells = [_normalize_cell(value) for value in raw_cells if value.strip()]
        if not cells:
            continue

        first_day = _DAYS.get(cells[0].casefold())
        if first_day is not None:
            current_day = first_day
            cells = cells[1:]
        if current_day is None:
            complete = False
            continue

        time_index = next(
            (index for index, value in enumerate(cells) if _TIME_RANGE.search(value)),
            None,
        )
        if time_index is None or time_index + 1 >= len(cells):
            complete = False
            continue
        match = _TIME_RANGE.search(cells[time_index])
        assert match is not None
        try:
            start = _parse_time(match.group("start"))
            end = _parse_time(match.group("end"))
            title = cells[time_index + 1]
            lessons.append(
                RecurringLessonSnapshotItem(
                    external_id=create_kurspro_external_id(
                        title,
                        current_day,
                        start,
                        end,
                    ),
                    title=title,
                    iso_day_of_week=current_day,
                    start_local_time=start,
                    end_local_time=end,
                    notification_enabled=True,
                    reminder_minutes_before=0,
                    lock_required=True,
                )
            )
        except ValueError:
            complete = False

    if not lessons:
        raise ScheduleParseError("Kurspro weekly schedule contains no usable lessons")
    if len(lessons) > 100:
        raise ScheduleParseError("Kurspro weekly schedule exceeds the supported row limit")
    if len({lesson.external_id for lesson in lessons}) != len(lessons):
        raise ScheduleParseError("Kurspro weekly schedule contains duplicate lesson rows")
    return ParsedSchedule(tuple(lessons), complete)


class KursproWeeklyScheduleSource:
    name = "Kurspro"

    def __init__(
        self,
        settings: CollectorSettings,
        *,
        clock: Clock | None = None,
    ) -> None:
        if not settings.kurspro_enabled:
            raise PermanentSourceError("Kurspro connector is not enabled")
        self._settings = settings
        self._clock = clock or (lambda: datetime.now(UTC))

    async def collect_snapshot(
        self,
        cancellation: asyncio.Event | None = None,
    ) -> RecurringLessonSnapshot:
        cancellation = cancellation or asyncio.Event()
        if cancellation.is_set():
            raise asyncio.CancelledError
        assert self._settings.kurspro_profile_directory is not None
        assert self._settings.kurspro_student_id is not None
        schedule_url = urljoin(
            f"{self._settings.kurspro_base_url}/",
            self._settings.kurspro_weekly_program_path.lstrip("/"),
        )

        async with async_playwright() as playwright:
            context = await playwright.chromium.launch_persistent_context(
                str(self._settings.kurspro_profile_directory),
                channel="chrome",
                headless=True,
                accept_downloads=False,
            )
            try:
                page = context.pages[0] if context.pages else await context.new_page()
                await _secure_context(context, page, login_mode=False)
                await page.goto(schedule_url, wait_until="domcontentloaded")
                ensure_authenticated_schedule_url(page.url)
                headers, rows = await _read_schedule_table(page)
                parsed = parse_weekly_schedule(headers, rows)
                return RecurringLessonSnapshot(
                    student_id=self._settings.kurspro_student_id,
                    collected_at_utc=self._clock(),
                    is_complete_snapshot=parsed.is_complete,
                    lessons=parsed.lessons,
                )
            finally:
                await context.close()


async def perform_manual_login(
    settings: CollectorSettings,
    *,
    timeout_seconds: float = 300,
) -> None:
    if not settings.kurspro_enabled or settings.kurspro_profile_directory is None:
        raise PermanentSourceError("Kurspro connector configuration is incomplete")
    login_url = urljoin(f"{settings.kurspro_base_url}/", "o/login.php")
    expected_path = settings.kurspro_weekly_program_path

    async with async_playwright() as playwright:
        context = await playwright.chromium.launch_persistent_context(
            str(settings.kurspro_profile_directory),
            channel="chrome",
            headless=False,
            accept_downloads=False,
        )
        try:
            page = context.pages[0] if context.pages else await context.new_page()
            await _secure_context(context, page, login_mode=True)
            await page.goto(login_url, wait_until="domcontentloaded")
            try:
                await page.wait_for_url(
                    lambda url: is_allowed_kurspro_url(url)
                    and urlsplit(url).path == "/o/index.php"
                    and urlsplit(url).query == urlsplit(expected_path).query,
                    timeout=timeout_seconds * 1000,
                )
            except TimeoutError as error:
                raise ManualLoginRequired(
                    "Manual login timed out; start the command again"
                ) from error
        finally:
            await context.close()


async def _secure_context(
    context: BrowserContext,
    page: Page,
    *,
    login_mode: bool,
) -> None:
    async def route_handler(route: Route) -> None:
        request = route.request
        if not is_allowed_kurspro_url(request.url):
            await route.abort()
            return
        if not login_mode and request.method != "GET":
            await route.abort()
            return
        if (
            not login_mode
            and request.resource_type in {"image", "media", "font"}
        ):
            await route.abort()
            return
        await route.continue_()

    await context.route("**/*", route_handler)

    def close_popup(page: Page) -> None:
        asyncio.create_task(page.close())

    page.on("popup", close_popup)


async def _read_schedule_table(page: Page) -> tuple[list[str], list[list[str]]]:
    tables = page.locator("table")
    for table_index in range(await tables.count()):
        table = tables.nth(table_index)
        row_locators = table.locator("tr")
        if await row_locators.count() < 2:
            continue
        headers = await row_locators.nth(0).locator("th,td").all_inner_texts()
        try:
            parse_weekly_schedule(headers, [["Pazartesi", "18:00-19:00", "Probe"]])
        except ScheduleParseError:
            continue
        rows = [
            await row_locators.nth(index).locator("td").all_inner_texts()
            for index in range(1, await row_locators.count())
        ]
        return headers, rows
    raise ScheduleParseError("Kurspro weekly schedule table was not found")


def _normalize_cell(value: str) -> str:
    return " ".join(value.strip().split())


def _parse_time(value: str) -> time:
    normalized = value.replace(".", ":")
    parsed = datetime.strptime(normalized, "%H:%M").time()
    return parsed.replace(second=0, microsecond=0)
