# ExamFollower Collector Bot

This package is the secure, asynchronous foundation for collecting Zoom lesson records and
submitting them to the ExamFollower backend. It intentionally contains no Kurspro or email
connector yet and does not perform browser or email automation.

## Setup

From `src/Bot` on Windows PowerShell:

```powershell
python -m venv .venv
.\.venv\Scripts\python -m pip install --upgrade pip
.\.venv\Scripts\python -m pip install -e ".[dev]"
```

Configure the process environment using the names documented in `.env.example`. The collector
key must be at least 32 bytes. Never store a real key in `.env`, source control, command output,
or logs. Production API URLs must use HTTPS; loopback HTTP is accepted only in Development.

## Running

Run one cycle or start the sequential service loop:

```powershell
.\.venv\Scripts\python -m examfollower_collector --once
.\.venv\Scripts\python -m examfollower_collector --service
```

No sources are registered in this foundation, so either mode performs no network request until a
future source implementation supplies validated lessons.

## Quality checks

```powershell
.\.venv\Scripts\python -m ruff check .
.\.venv\Scripts\python -m mypy src
.\.venv\Scripts\python -m pytest
```

## Backend contract

The client sends an authenticated `POST` to `/api/collector/zoom-lessons/upsert`. The API key is
provided only in the `X-Collector-Api-Key` header. Request JSON uses the exact backend camel-case
fields `studentId`, `title`, `startAtUtc`, `endAtUtc`, `joinUrl`, `source`, `externalId`, and
`lockRequired`. Timestamps are converted to UTC and serialized with a `Z` suffix.
