# Kurspro Integration Discovery

## Scope and authorization

This discovery was performed on `https://kurspro.net` using a user-controlled,
manually authenticated browser session and only data visible to that account. The
inspection was read-only: it did not join a lesson, submit or change Kurspro data,
replay credentials, export cookies, or call the ExamFollower collector API.

Observed pages:

- Login: `GET /o/login.php`
- Weekly class schedule: `GET /o/index.php?a=4`
- Remote education sessions: `GET /o/index.php?a=51`
- Remote education attendance: `GET /o/index.php?a=65`
- Digital agenda: `GET /o/index.php?a=86`
- Privacy notice: `GET /o/index.php?a=60`

Student, teacher, institution, and class names were not retained. No cookie,
authorization value, CAPTCHA value, Zoom meeting identifier, or Zoom password was
copied into this report.

## Login and session model

The login page uses a classic `POST /o/login.php` form with
`application/x-www-form-urlencoded` content. The observed input names were `xk`,
`xu`, `dogum_tarihi`, and `vc`. The page includes an image CAPTCHA served from
`/o/cp/cp.php` and a corresponding `vc` input. No OAuth, OIDC, or SAML redirect was
observed.

The authenticated application is server-rendered PHP. No bearer-token signal,
local-storage entry, session-storage entry, or DOM CSRF field was observed. No
script-readable cookie name was exposed. Together with the authenticated HTML and
login redirects, this indicates a server-side cookie session, but the cookie name
and its `SameSite`, `Secure`, and `HttpOnly` flags could not be verified without
reading browser network/cookie internals and therefore remain unknown.

When the previous session was no longer accepted, an authenticated `GET` redirected
to `/o/login.php`. The exact status code (`302`, `401`, or another redirect status),
session lifetime, renewal behavior, device binding, and logout invalidation were not
captured. A logout route exists at `/o/logout.php`, but it was not invoked because
this checkpoint is read-only.

Authentication options for a future connector:

| Option | Assessment |
|---|---|
| Programmatic username/password login | Not recommended. It would require handling sensitive identity data and CAPTCHA, and is unsuitable for unattended automation. |
| Exported session/cookie | High theft and expiry risk; requires repeated user intervention and must not be the default design. |
| OAuth/OIDC token | No such flow was observed. Reassess only if Kurspro documents one. |
| Official API key/token | Preferred if Kurspro can provide one; none was found during this discovery. |
| Manual browser login and session reuse | Acceptable for controlled discovery, but not a reliable unattended connector architecture. |

## Schedule page flow

`GET /o/index.php?a=4` returns a weekly class schedule as server-rendered HTML.
The table exposes weekday, lesson sequence/time range, lesson/subject, instructor,
and classroom columns. The current page has no daily/future/past selector, date
range input, form, or pagination control. A separate program-selection link uses
the query names `a` and `p1`; the meaning and stability of `p1` were not verified.

`GET /o/index.php?a=51` is the remote education session list. It was empty for the
authorized account at discovery time. `GET /o/index.php?a=65` is the remote
education attendance view and exposed an empty HTML table with attendance status,
date, session, education, instructor, and lesson columns. The digital agenda at
`GET /o/index.php?a=86` also returned server-rendered HTML and no usable lesson
records.

Page reloads and route changes continued to return HTML. No mutation control was
used.

## Candidate data endpoint

No official API or authenticated JSON/XHR endpoint was found. The inspected pages
contained no `fetch`, jQuery AJAX, or XHR endpoint candidate for schedule data. The
only verified source is server-rendered HTML:

| Purpose | Method and path | Query names | Content type | Pagination/date parameters |
|---|---|---|---|---|
| Weekly program | `GET /o/index.php` | `a` (`a=4`); `p1` appears on a program-selection link | `text/html` | None observed |
| Remote sessions | `GET /o/index.php` | `a` (`a=51`) | `text/html` | None observed |
| Remote attendance | `GET /o/index.php` | `a` (`a=65`) | `text/html` | None observed |
| Digital agenda | `GET /o/index.php` | `a` (`a=86`) | `text/html` | None observed |

Authenticated pages rendered successfully in the browser. The exact successful
HTTP status and response headers were not captured. An unauthorized/expired
session navigated to `/o/login.php`. No rate-limit marker was observed, and no
repeated or bulk requests were made.

## Request contract

The verified read-only request contract is a browser-authenticated `GET` to
`/o/index.php` with the route selector `a`. Authentication is inferred to be the
existing server-side browser session. Header values were not inspected or stored.
No request body is used by the observed schedule routes.

Because no supported token mechanism was found, this request contract is not yet a
safe contract for an unattended Python connector.

## Response contract

All inspected data views return `text/html`. The weekly program is an HTML table;
its rows have no anchor, stable row ID, `data-*` identity attribute, or embedded
Zoom link. Remote-session and attendance tables were empty, so their populated row
shape could not be validated. No raw HTML or response body is included here.

Observed field availability:

| Kurspro concept | Availability |
|---|---|
| Natural lesson/event ID | Not found |
| Lesson title/subject | Directly available in the weekly table |
| Start time | Weekday and local wall-clock time are visible; calendar date is missing |
| End time/duration | End wall-clock time is visible; date and timezone are missing |
| Timezone | Not found |
| Zoom join URL | Not found; only a classroom label indicating Zoom was visible |
| Lesson status | Not found |
| Cancellation flag | Not found |
| Instructor | Directly available but unnecessary for the current backend contract |
| Course/program name | Available in a page heading, but it may contain personal/institutional context |
| Class/group | Available in a page heading, but unnecessary for the current backend contract |
| Last-updated timestamp | Not found |

## Field mapping

| Backend field | Proposed Kurspro mapping | Status |
|---|---|---|
| `studentId` | Connector configuration only | Ready by design; never infer from Kurspro |
| `title` | Weekly table lesson/subject | Directly available |
| `startAtUtc` | Weekday plus start wall-clock time, anchored to a real calendar date and converted from a verified timezone | Blocked: date anchor and timezone are absent |
| `endAtUtc` | Same date anchor plus end wall-clock time, converted from the verified timezone | Blocked: date anchor and timezone are absent |
| `joinUrl` | HTTPS URL whose host is `zoom.us` or a subdomain | Blocked: no URL was present |
| `source` | Constant `Kurspro` | Ready by design |
| `externalId` | Natural Kurspro event/session ID | Blocked: no stable ID was present |
| `lockRequired` | Constant `true`, subject to product approval | Design recommendation only |

An external-ID fallback must not include mutable start/end clock times. If `p1` is
confirmed to be a stable program identifier and lesson sequence is stable, a
versioned hash of `Kurspro + program ID + academic term + weekday + lesson sequence`
could represent a recurring weekly slot. This is only a design candidate. It must
not be implemented until `p1` semantics and row stability are verified. No safe
fallback exists yet for one-off remote sessions.

## Timezone behavior

The weekly schedule displays Turkish weekday names and wall-clock times but does not
declare a timezone or include an offset. It is plausible that the institution uses
`Europe/Istanbul`, but that is an inference and must not be encoded as fact. UTC
conversion is blocked until Kurspro documentation, an explicit page value, or a
vendor-confirmed timezone establishes the source zone. Daylight-saving and
cross-midnight behavior were not observable.

## Zoom URL extraction

The weekly schedule exposes a classroom text label indicating Zoom, not a clickable
Zoom URL. No `zoom.us` anchor was present. The remote-session list contained no
records, so the field or link that would hold a join URL could not be discovered.
Meeting IDs and `pwd` parameters were neither read nor stored.

A connector must reject non-HTTPS and non-Zoom hosts and must never construct a join
URL from the classroom label.

## External ID strategy

No natural lesson or event identifier was present in the weekly HTML, row
attributes, or links. The remote-session page had no sample row from which to
identify an event ID or series/parent ID. A natural server identifier remains the
required strategy for one-off sessions and updates. The recurring-slot hash above
is a conditional fallback for weekly timetable rows only.

## Update and cancellation behavior

The current data did not permit comparison of two versions of the same event.
Consequently, the following remain unknown:

- Whether an event ID survives a time change
- Whether cancelled lessons disappear or expose a status field
- Whether a Zoom link is added later
- Whether one Zoom meeting is reused by multiple lesson records
- Whether recurring lessons expose parent/series IDs

The current backend only upserts and cannot represent disappearance or explicit
cancellation. A future connector must not infer cancellation merely because a row
is absent. Cancellation support requires a verified Kurspro status signal and a
separate approved backend design.

## Error and session-expiration behavior

An expired or rejected session redirects the browser to `/o/login.php`. No JSON
error envelope was observed. CAPTCHA makes automatic reauthentication unsuitable.
The connector would need to stop with a permanent authentication/configuration
error rather than loop, retry login, or ask for credentials. Exact unauthorized
status, session duration, renewal endpoint, and rate-limit headers remain unknown.

## Security considerations

- Do not store a Kurspro username, password, date of birth, CAPTCHA value, cookie,
  or browser profile in the repository or bot configuration.
- Do not transfer a browser session into Python code.
- Do not bypass CAPTCHA or other access controls.
- Keep requests read-only, sequential, and narrowly scoped if discovery is repeated.
- Mask student/instructor/institution data and complete Zoom URLs in diagnostics.
- The privacy page contained no explicit automation prohibition signal, but the
  public `robots.txt` could not be retrieved in the available browser environment.
  This is not evidence of permission. Vendor terms and API authorization still need
  confirmation before implementing automation.

## Recommended connector approach

**Approach D — do not implement a Kurspro connector at this checkpoint.**

The only verified source is server-rendered HTML, login includes CAPTCHA, there is
no supported authentication token, populated remote-session data is unavailable,
and the mandatory timezone, Zoom URL, and stable external ID cannot be established.
An HTML parser would therefore be both fragile and unable to satisfy the backend
contract safely.

Preferred next options, in order:

1. Ask Kurspro/vendor support for a documented read-only API and supported token.
2. Repeat this read-only discovery when the authorized account has a populated
   remote-education session, without joining it.
3. If no supported Kurspro mechanism exists, proceed with an Email connector design
   that obtains Zoom invitations through an authorized mailbox integration.

## Known unknowns

- Official API availability and permitted automation terms
- Cookie attributes, session duration, renewal, and device binding
- Exact unauthorized and successful HTTP status codes/headers
- Source timezone
- Populated remote-session row and detail-page contract
- Natural event/session and recurring-series identifiers
- Zoom join URL location
- Update, reschedule, and cancellation semantics
- Pagination and date filtering for populated session history
- Rate-limit policy

## Implementation checkpoint

No connector, authentication automation, backend call, dependency, migration, or
application code was created. This report is the only working-tree change and has
not been committed or pushed. Implementation should remain blocked until the
timezone, Zoom URL, stable ID, authorization method, and permitted-use questions are
resolved.
