from __future__ import annotations

from collections.abc import Awaitable, Callable

import httpx
import pytest

from examfollower_collector.retry import RetryPolicy


class SleepRecorder:
    def __init__(self) -> None:
        self.delays: list[float] = []

    async def __call__(self, delay: float) -> None:
        self.delays.append(delay)


def response_sequence(
    statuses: list[int],
) -> tuple[Callable[[], Awaitable[httpx.Response]], list[int]]:
    calls: list[int] = []

    async def request() -> httpx.Response:
        calls.append(1)
        return httpx.Response(statuses.pop(0))

    return request, calls


@pytest.mark.asyncio
async def test_429_is_retried_without_real_waiting() -> None:
    sleep = SleepRecorder()
    request, calls = response_sequence([429, 200])
    result = await RetryPolicy(3, sleep=sleep, jitter=lambda _a, _b: 0).execute(request)
    assert result.status_code == 200
    assert len(calls) == 2
    assert sleep.delays == [0.5]


@pytest.mark.asyncio
async def test_numeric_retry_after_is_honored_and_bounded() -> None:
    sleep = SleepRecorder()
    calls = 0

    async def request() -> httpx.Response:
        nonlocal calls
        calls += 1
        if calls == 1:
            return httpx.Response(429, headers={"Retry-After": "120"})
        return httpx.Response(200)

    await RetryPolicy(1, sleep=sleep, maximum_delay_seconds=30).execute(request)
    assert sleep.delays == [30]


@pytest.mark.asyncio
@pytest.mark.parametrize("status", [500, 502, 503, 504])
async def test_server_error_is_retried(status: int) -> None:
    request, calls = response_sequence([status, 200])
    await RetryPolicy(1, sleep=SleepRecorder(), jitter=lambda _a, _b: 0).execute(request)
    assert len(calls) == 2


@pytest.mark.asyncio
async def test_network_timeout_is_retried() -> None:
    calls = 0

    async def request() -> httpx.Response:
        nonlocal calls
        calls += 1
        if calls == 1:
            raise httpx.ReadTimeout("timed out")
        return httpx.Response(200)

    result = await RetryPolicy(1, sleep=SleepRecorder()).execute(request)
    assert result.status_code == 200
    assert calls == 2


@pytest.mark.asyncio
async def test_maximum_retry_count_is_not_exceeded() -> None:
    request, calls = response_sequence([503, 503, 503, 200])
    result = await RetryPolicy(2, sleep=SleepRecorder()).execute(request)
    assert result.status_code == 503
    assert len(calls) == 3
