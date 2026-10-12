# ExamFollower Collector Bot

This package collects approved lesson data and submits it to the ExamFollower backend. The
Kurspro connector reads only the weekly program through a user-controlled persistent browser
profile. It never accepts credentials, solves CAPTCHA, exports cookies, or automates login.

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

Before the first Kurspro sync, configure a browser profile directory outside this repository and
run `python -m examfollower_collector kurspro-login`. Complete login and CAPTCHA yourself, then
navigate to the weekly-program page. A normal collector cycle reuses that local profile. If the
session expires, the collector reports `ManualLoginRequired` and does not attempt an automatic
login or retry within that cycle.

## Quality checks

```powershell
.\.venv\Scripts\python -m ruff check .
.\.venv\Scripts\python -m mypy src
.\.venv\Scripts\python -m pytest
```

## Backend contract

The client sends collector-key authenticated requests to the Zoom upsert endpoint and to
`POST /api/collector/recurring-lessons/snapshot`. The API key is provided only in the
`X-Collector-Api-Key` header. Zoom request JSON uses the exact backend camel-case
fields `studentId`, `title`, `startAtUtc`, `endAtUtc`, `joinUrl`, `source`, `externalId`, and
`lockRequired`. Timestamps are converted to UTC and serialized with a `Z` suffix.
