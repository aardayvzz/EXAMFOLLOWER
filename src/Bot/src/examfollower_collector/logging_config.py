from __future__ import annotations

import logging
import re
from collections.abc import Iterable

_JWT_PATTERN = re.compile(r"\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\b")
_URL_PATTERN = re.compile(r"https://(?:[A-Za-z0-9-]+\.)*zoom\.us/\S+", re.IGNORECASE)


class SensitiveDataFilter(logging.Filter):
    def __init__(self, secrets: Iterable[str] = ()) -> None:
        super().__init__()
        self._secrets = tuple(secret for secret in secrets if secret)

    def filter(self, record: logging.LogRecord) -> bool:
        message = record.getMessage()
        for secret in self._secrets:
            message = message.replace(secret, "[REDACTED]")
        message = _JWT_PATTERN.sub("[REDACTED]", message)
        message = _URL_PATTERN.sub("[REDACTED]", message)
        record.msg = message
        record.args = ()
        return True


def configure_logging(*, secrets: Iterable[str] = ()) -> None:
    handler = logging.StreamHandler()
    handler.addFilter(SensitiveDataFilter(secrets))
    handler.setFormatter(logging.Formatter("%(asctime)s %(levelname)s %(name)s %(message)s"))
    logging.basicConfig(level=logging.INFO, handlers=[handler], force=True)
