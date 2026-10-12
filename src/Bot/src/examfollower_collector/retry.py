from __future__ import annotations

import asyncio
import logging
import random
from collections.abc import Awaitable, Callable

import httpx

RequestFactory = Callable[[], Awaitable[httpx.Response]]
SleepFunction = Callable[[float], Awaitable[None]]
JitterFunction = Callable[[float, float], float]


class RetryPolicy:
    """Retry only transient collector failures with bounded backoff."""

    _RETRYABLE_STATUS_CODES = frozenset({408, 429, 500, 502, 503, 504})

    def __init__(
        self,
        max_retry_attempts: int,
        *,
        sleep: SleepFunction = asyncio.sleep,
        jitter: JitterFunction = random.uniform,
        base_delay_seconds: float = 0.5,
        maximum_delay_seconds: float = 30.0,
        logger: logging.Logger | None = None,
    ) -> None:
        if not 0 <= max_retry_attempts <= 10:
            raise ValueError("max_retry_attempts must be between 0 and 10")
        self._max_retry_attempts = max_retry_attempts
        self._sleep = sleep
        self._jitter = jitter
        self._base_delay_seconds = base_delay_seconds
        self._maximum_delay_seconds = maximum_delay_seconds
        self._logger = logger or logging.getLogger(__name__)

    async def execute(self, request_factory: RequestFactory) -> httpx.Response:
        for retry_number in range(self._max_retry_attempts + 1):
            try:
                response = await request_factory()
            except (httpx.TimeoutException, httpx.NetworkError):
                if retry_number >= self._max_retry_attempts:
                    raise
                await self._wait(retry_number + 1, None)
                continue

            if (
                response.status_code not in self._RETRYABLE_STATUS_CODES
                or retry_number >= self._max_retry_attempts
            ):
                return response

            await self._wait(retry_number + 1, response)

        raise RuntimeError("unreachable retry state")

    async def _wait(
        self,
        retry_number: int,
        response: httpx.Response | None,
    ) -> None:
        delay = self._retry_after(response)
        if delay is None:
            exponential = self._base_delay_seconds * (2 ** (retry_number - 1))
            jittered = exponential + self._jitter(0, exponential * 0.2)
            delay = min(jittered, self._maximum_delay_seconds)

        status_code = response.status_code if response is not None else "network"
        self._logger.warning(
            "Transient collector request failure; retry=%s status=%s delay_seconds=%.3f",
            retry_number,
            status_code,
            delay,
        )
        await self._sleep(delay)

    def _retry_after(self, response: httpx.Response | None) -> float | None:
        if response is None:
            return None
        raw_value = response.headers.get("Retry-After")
        if raw_value is None:
            return None
        try:
            seconds = float(raw_value)
        except ValueError:
            return None
        if seconds < 0:
            return None
        return min(seconds, self._maximum_delay_seconds)
