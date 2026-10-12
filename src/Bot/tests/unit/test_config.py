from __future__ import annotations

import pytest
from pydantic import ValidationError

from examfollower_collector.config import CollectorSettings

VALID_KEY = "x" * 32


def settings(**overrides: object) -> CollectorSettings:
    values: dict[str, object] = {
        "environment": "Production",
        "api_base_url": "https://api.example.test",
        "collector_api_key": VALID_KEY,
    }
    values.update(overrides)
    return CollectorSettings(**values)  # type: ignore[arg-type]


def test_missing_api_key_fails(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.delenv("EXAMFOLLOWER_COLLECTOR_API_KEY", raising=False)
    monkeypatch.setenv("EXAMFOLLOWER_API_BASE_URL", "https://api.example.test")
    with pytest.raises(ValidationError):
        CollectorSettings()


def test_short_api_key_is_rejected() -> None:
    secret = "too-short"
    with pytest.raises(ValidationError) as error:
        settings(collector_api_key=secret)
    assert secret not in str(error.value)


def test_secret_is_not_exposed_by_repr() -> None:
    secret = "a-very-sensitive-key-value-123456"
    configured = settings(collector_api_key=secret)
    assert secret not in repr(configured)
    assert "**********" in str(configured.collector_api_key)


def test_production_http_base_url_is_rejected() -> None:
    with pytest.raises(ValidationError):
        settings(api_base_url="http://localhost:5000")


def test_development_loopback_http_is_accepted() -> None:
    configured = settings(
        environment="Development",
        api_base_url="http://127.0.0.1:5000/",
    )
    assert configured.api_base_url == "http://127.0.0.1:5000"


def test_invalid_timezone_is_rejected() -> None:
    with pytest.raises(ValidationError):
        settings(time_zone="Not/A-Time-Zone")
