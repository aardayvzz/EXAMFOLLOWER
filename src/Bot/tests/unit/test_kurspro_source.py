from __future__ import annotations

import inspect
from datetime import time
from html.parser import HTMLParser
from pathlib import Path

import pytest

from examfollower_collector.config import CollectorSettings
from examfollower_collector.models import (
    create_kurspro_external_id,
    normalize_lesson_title,
)
from examfollower_collector.sources.kurspro import (
    ManualLoginRequired,
    ScheduleParseError,
    ensure_authenticated_schedule_url,
    is_allowed_kurspro_url,
    is_login_url,
    parse_weekly_schedule,
    perform_manual_login,
)

FIXTURE = (
    Path(__file__).parents[1]
    / "fixtures"
    / "kurspro_weekly_schedule.synthetic.html"
)
VALID_KEY = "x" * 32


class TableReader(HTMLParser):
    def __init__(self) -> None:
        super().__init__()
        self.headers: list[str] = []
        self.rows: list[list[str]] = []
        self._row: list[str] | None = None
        self._cell: list[str] | None = None
        self._header = False

    def handle_starttag(
        self,
        tag: str,
        attrs: list[tuple[str, str | None]],
    ) -> None:
        del attrs
        if tag == "tr":
            self._row = []
        elif tag in {"th", "td"}:
            self._cell = []
            self._header = tag == "th"

    def handle_data(self, data: str) -> None:
        if self._cell is not None:
            self._cell.append(data)

    def handle_endtag(self, tag: str) -> None:
        if tag in {"th", "td"} and self._cell is not None and self._row is not None:
            value = "".join(self._cell).strip()
            if self._header:
                self.headers.append(value)
            else:
                self._row.append(value)
            self._cell = None
        elif tag == "tr" and self._row is not None:
            if self._row:
                self.rows.append(self._row)
            self._row = None


def configured(**overrides: object) -> CollectorSettings:
    values: dict[str, object] = {
        "environment": "Production",
        "api_base_url": "https://api.example.test",
        "collector_api_key": VALID_KEY,
    }
    values.update(overrides)
    return CollectorSettings(**values)  # type: ignore[arg-type]


def test_profile_directory_inside_repository_is_rejected() -> None:
    repository = Path(__file__).resolve().parents[4]
    with pytest.raises(ValueError, match="outside the repository"):
        configured(
            kurspro_enabled=True,
            kurspro_student_id="11111111-1111-1111-1111-111111111111",
            kurspro_profile_directory=repository / "unsafe-profile",
        )


@pytest.mark.parametrize(
    "url",
    [
        "http://kurspro.net",
        "https://evil.example",
        "https://kurspro.net.evil.example",
        "https://user:pass@kurspro.net",
    ],
)
def test_wrong_kurspro_domain_is_rejected(url: str) -> None:
    with pytest.raises(ValueError, match="exactly"):
        configured(kurspro_base_url=url)
    assert not is_allowed_kurspro_url(url)


def test_login_redirect_is_detected_as_manual_login_required_signal() -> None:
    assert is_login_url("https://kurspro.net/o/login.php")
    assert not is_login_url("https://kurspro.net/o/index.php?a=4")
    with pytest.raises(ManualLoginRequired):
        ensure_authenticated_schedule_url("https://kurspro.net/o/login.php")


def test_login_command_has_no_credential_or_captcha_parameters() -> None:
    parameters = inspect.signature(perform_manual_login).parameters
    forbidden = {"username", "password", "captcha", "cookie", "token"}
    assert forbidden.isdisjoint(parameters)
    assert forbidden.isdisjoint(CollectorSettings.model_fields)


def test_synthetic_weekly_table_is_parsed() -> None:
    reader = TableReader()
    reader.feed(FIXTURE.read_text(encoding="utf-8"))
    parsed = parse_weekly_schedule(reader.headers, reader.rows)
    assert parsed.is_complete
    assert len(parsed.lessons) == 3
    assert parsed.lessons[0].title == "Matematik"


@pytest.mark.parametrize(
    ("day_name", "expected"),
    [
        ("Pazartesi", 1),
        ("Salı", 2),
        ("Çarşamba", 3),
        ("Perşembe", 4),
        ("Cuma", 5),
        ("Cumartesi", 6),
        ("Pazar", 7),
    ],
)
def test_turkish_weekdays_map_to_iso(day_name: str, expected: int) -> None:
    parsed = parse_weekly_schedule(
        ["Gün", "Ders Saati", "Ders"],
        [[day_name, "18:00 - 19:00", "Sentetik Ders"]],
    )
    assert parsed.lessons[0].iso_day_of_week == expected


def test_turkish_time_text_with_dots_is_parsed() -> None:
    parsed = parse_weekly_schedule(
        ["Gün", "Ders Saati", "Ders"],
        [["Pazartesi", "2. Ders 19.10 - 20.00", "Türkçe"]],
    )
    assert parsed.lessons[0].start_local_time == time(19, 10)
    assert parsed.lessons[0].end_local_time == time(20)


def test_title_normalization_is_unicode_preserving_and_stable() -> None:
    assert normalize_lesson_title("  İleri   Türkçe  ") == "i̇leri türkçe"
    assert normalize_lesson_title("IŞIK") == "işik"


def test_external_id_is_deterministic() -> None:
    first = create_kurspro_external_id(" Matematik ", 1, time(18), time(19))
    second = create_kurspro_external_id("matematik", 1, time(18), time(19))
    assert first == second
    assert first.startswith("kurspro-weekly-v1:")
    assert len(first.rsplit(":", 1)[1]) == 64


def test_time_change_changes_external_id() -> None:
    first = create_kurspro_external_id("Matematik", 1, time(18), time(19))
    second = create_kurspro_external_id("Matematik", 1, time(18, 30), time(19, 30))
    assert first != second


def test_unknown_row_produces_partial_snapshot() -> None:
    parsed = parse_weekly_schedule(
        ["Gün", "Ders Saati", "Ders"],
        [
            ["Pazartesi", "18:00 - 19:00", "Matematik"],
            ["Bilinmeyen", "tanınmayan", "satır"],
        ],
    )
    assert not parsed.is_complete
    assert len(parsed.lessons) == 1


def test_empty_table_is_not_accepted_for_upload() -> None:
    with pytest.raises(ScheduleParseError, match="no usable"):
        parse_weekly_schedule(["Gün", "Ders Saati", "Ders"], [])


def test_fixture_is_explicitly_synthetic_and_contains_no_session_material() -> None:
    content = FIXTURE.read_text(encoding="utf-8")
    lowered = content.casefold()
    assert "synthetic test fixture".casefold() in lowered
    for forbidden in ("cookie=", "authorization:", "sessionid", "type=\"hidden\"", "token="):
        assert forbidden not in lowered


def test_parser_does_not_log_or_return_raw_html_or_cookie_data(
    caplog: pytest.LogCaptureFixture,
) -> None:
    marker = "COOKIE-SESSION-MUST-NOT-APPEAR"
    parsed = parse_weekly_schedule(
        ["Gün", "Ders Saati", "Ders", "Not"],
        [["Pazartesi", "18:00 - 19:00", "Matematik", marker]],
    )
    assert marker not in repr(parsed)
    assert marker not in caplog.text
