from __future__ import annotations

from pathlib import Path
from typing import Literal, Self
from urllib.parse import urlsplit
from uuid import UUID
from zoneinfo import ZoneInfo, ZoneInfoNotFoundError

from pydantic import Field, SecretStr, field_validator, model_validator
from pydantic_settings import BaseSettings, SettingsConfigDict


class CollectorSettings(BaseSettings):
    """Environment-only configuration with secret-safe representation."""

    model_config = SettingsConfigDict(
        env_prefix="EXAMFOLLOWER_",
        case_sensitive=False,
        extra="ignore",
        hide_input_in_errors=True,
    )

    environment: Literal["Production", "Development"] = "Production"
    api_base_url: str
    collector_api_key: SecretStr = Field(repr=False)
    sync_interval_seconds: int = Field(default=21600, ge=60, le=86400)
    http_timeout_seconds: float = Field(default=15, ge=1, le=120)
    max_retry_attempts: int = Field(default=3, ge=0, le=10)
    time_zone: str = "Europe/Istanbul"
    kurspro_enabled: bool = False
    kurspro_base_url: str = "https://kurspro.net"
    kurspro_student_id: UUID | None = None
    kurspro_profile_directory: Path | None = None
    kurspro_time_zone: str = "Europe/Istanbul"
    kurspro_weekly_program_path: str = "/o/index.php?a=4"

    @field_validator("environment", mode="before")
    @classmethod
    def canonicalize_environment(cls, value: object) -> object:
        if isinstance(value, str):
            normalized = value.strip().casefold()
            if normalized == "production":
                return "Production"
            if normalized == "development":
                return "Development"
        return value

    @field_validator("collector_api_key")
    @classmethod
    def validate_api_key(cls, value: SecretStr) -> SecretStr:
        if len(value.get_secret_value().encode("utf-8")) < 32:
            raise ValueError("collector API key must contain at least 32 bytes")
        return value

    @field_validator("time_zone")
    @classmethod
    def validate_time_zone(cls, value: str) -> str:
        candidate = value.strip()
        try:
            ZoneInfo(candidate)
        except ZoneInfoNotFoundError as error:
            # The embeddable Windows Python runtime does not include an IANA
            # timezone database. Keep the documented default usable without
            # adding an otherwise unnecessary runtime dependency.
            if candidate in {"Europe/Istanbul", "UTC"}:
                return candidate
            raise ValueError("time zone is not supported") from error
        return candidate

    @model_validator(mode="after")
    def validate_base_url(self) -> Self:
        candidate = self.api_base_url.strip().rstrip("/")
        parsed = urlsplit(candidate)
        if (
            parsed.scheme not in {"http", "https"}
            or not parsed.hostname
            or parsed.username is not None
            or parsed.password is not None
            or parsed.query
            or parsed.fragment
        ):
            raise ValueError("API base URL is invalid")

        if parsed.scheme == "http":
            is_loopback = parsed.hostname.casefold() in {
                "localhost",
                "127.0.0.1",
                "::1",
            }
            if self.environment != "Development" or not is_loopback:
                raise ValueError(
                    "HTTP API base URL is allowed only for Development loopback hosts"
                )

        self.api_base_url = candidate

        kurspro = self.kurspro_base_url.strip().rstrip("/")
        parsed_kurspro = urlsplit(kurspro)
        if (
            parsed_kurspro.scheme != "https"
            or parsed_kurspro.hostname != "kurspro.net"
            or parsed_kurspro.port is not None
            or parsed_kurspro.username is not None
            or parsed_kurspro.password is not None
            or parsed_kurspro.path not in {"", "/"}
            or parsed_kurspro.query
            or parsed_kurspro.fragment
        ):
            raise ValueError("Kurspro base URL must be exactly https://kurspro.net")
        if self.kurspro_time_zone != "Europe/Istanbul":
            raise ValueError("Kurspro time zone must be Europe/Istanbul")
        if self.kurspro_weekly_program_path != "/o/index.php?a=4":
            raise ValueError("Kurspro weekly program path is not supported")

        if self.kurspro_enabled:
            if self.kurspro_student_id is None or self.kurspro_student_id.int == 0:
                raise ValueError(
                    "Kurspro student ID is required when the connector is enabled"
                )
            if self.kurspro_profile_directory is None:
                raise ValueError(
                    "Kurspro profile directory is required when the connector is enabled"
                )
            profile = self.kurspro_profile_directory.expanduser().resolve()
            repository_root = Path(__file__).resolve().parents[4]
            if profile == repository_root or repository_root in profile.parents:
                raise ValueError("Kurspro browser profile must be outside the repository")
            self.kurspro_profile_directory = profile

        self.kurspro_base_url = kurspro
        return self
