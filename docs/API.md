# Admission API

Base URL: `http://localhost:8080`. Apply the initial migration to the configured PostgreSQL database before calling these endpoints; see [database setup](DATABASE.md#migration-commands).

Class creation requires `X-Admin-Key`, matching the private server setting `Admin__AccessKey` (at least 32 characters). Missing/incorrect keys return 401; missing/invalid server configuration returns 503 and disables creation. `GET /admin/session` validates that credential and returns 204 with `Cache-Control: no-store`. Class listing/details remain public. Hold creation requires the student identifier header; it is not authentication. CORS permits `X-Admin-Key` only from the configured origins. Never put this key in public `config.json` or browser storage.

## Create a class

`POST /classes` with `Content-Type: application/json`:

```json
{
  "name": "Class 6 - Morning Shift",
  "totalSeats": 30
}
```

Example `201 Created` response:

```json
{
  "id": "10eeb2c5-54c4-4e4c-9f2c-dc197d5db560",
  "name": "Class 6 - Morning Shift",
  "totalSeats": 30
}
```

The `Location` header points to `/classes/{id}`. Names are trimmed before validation, required, and limited to 200 characters. Capacity must be a JSON integer from 1 through 2147483647; quoted numbers are rejected. Duplicate names return 409, including simultaneous creates. Name uniqueness is case-sensitive.

## List or retrieve a class

- `GET /classes` returns a JSON array, or `[]` when there are no classes. Results are ordered by name, then ID.
- `GET /classes/{classId}` returns one summary. The ID must be a UUID.

Both endpoints use the same summary query. Each item contains exactly these fields:

| Field | Meaning |
| --- | --- |
| `id` | Class UUID |
| `name` | Class/shift name |
| `totalSeats` | Configured capacity |
| `heldSeats` | `HELD` rows whose expiry is strictly after the captured PostgreSQL `clock_timestamp()` |
| `confirmedSeats` | `CONFIRMED` rows, regardless of their historical expiry |
| `availableSeats` | Total minus active holds and confirmed admissions, with a defensive floor of zero |

EF Core translates the shared LINQ query into one SQL statement. The only handwritten SQL in this summary query is `SELECT clock_timestamp() AS "Value"`, composed into that statement to capture database wall-clock time once. Overdue `HELD`, `EXPIRED`, and `CANCELLED` rows do not occupy seats. Reads do not mutate application status. Hold creation enforces capacity through its transaction; the availability floor alone does not enforce that invariant.

## Create a temporary hold

`POST /classes/{classId}/holds` with one `X-Student-Id: student-1` header. No body is needed; an empty JSON object `{}` is also accepted. If a JSON body is supplied, malformed JSON, non-object values other than `null`, and unknown fields return 400. The class and student identity come only from the route and header.

Example `201 Created` response:

```json
{
  "holdId": "334b0c66-a05e-413b-b7d8-52baf2ac3158",
  "classId": "10eeb2c5-54c4-4e4c-9f2c-dc197d5db560",
  "studentId": "student-1",
  "status": "HELD",
  "expiresAt": "2026-11-09T12:10:00Z"
}
```

The header is trimmed, required, and limited to 64 characters after trimming. Case and leading zeros are preserved. Synthetic IDs are accepted; only the future React input enforces 17 digits. Multiple header values are rejected. The response contains the five fields above.

`HOLD_DURATION_SECONDS` defaults to 600 when unset. Invalid, nonpositive, or over-32-bit integer settings fail startup. Creation uses a read-committed transaction and a parameterized `SELECT ... FOR UPDATE` on the class row. After locking, it captures PostgreSQL `clock_timestamp()`, conditionally expires overdue `HELD` rows in that class, checks the student's existing active/confirmed application, counts occupied seats, and inserts only if capacity remains. Cleanup, the new hold, and the captured-time-plus-duration expiry commit together. EF Core handles the cleanup, checks, and insertion.

Expired/cancelled applications remain in history, and reapplication creates a new hold ID. Duplicate active/confirmed applications and full classes return 409. Failed allocation rolls back the transaction, including its cleanup. Overdue rows immediately stop occupying seats in summaries; a background worker also persists their EXPIRED status without a student request. A student may apply to different classes.

## Retrieve a hold

`GET /holds/{holdId}` requires no student header. Example `200 OK` response:

```json
{
  "holdId": "334b0c66-a05e-413b-b7d8-52baf2ac3158",
  "classId": "10eeb2c5-54c4-4e4c-9f2c-dc197d5db560",
  "status": "HELD",
  "expiresAt": "2026-11-09T12:10:00Z",
  "serverTime": "2026-11-09T12:05:00Z"
}
```

Allowed statuses are `HELD`, `CONFIRMED`, `EXPIRED`, and `CANCELLED`. If a stored `HELD` row has `expiresAt <= serverTime`, the response reports `EXPIRED` immediately. Other statuses remain unchanged, regardless of their historical expiry. A single EF query captures `clock_timestamp()` for both the expiry comparison and `serverTime`. Timestamps are UTC.

Reads preserve the stored status and all timestamps. The response omits the student ID and uses `Cache-Control: no-store, no-cache` so clients can poll for fresh state. A supplied student header does not restrict access: this is a public status endpoint, and UUIDs are not authentication. Unknown holds return 404; malformed UUIDs return 400.

## Confirm a hold

`POST /holds/{holdId}/confirm` requires one `X-Student-Id` header with the same trimming and 64-character rules as creation. No body is needed; `{}` or `null` is accepted. Malformed JSON, non-object values, and unknown body fields return 400. Payment is simulated as successful; no payment details are accepted.

Example `200 OK` response:

```json
{
  "holdId": "334b0c66-a05e-413b-b7d8-52baf2ac3158",
  "status": "CONFIRMED"
}
```

The transaction locks the hold and verifies ownership before any status change. A valid identifier belonging to another student returns 403, including for overdue or terminal holds. Unknown holds return 404. An owned, already-confirmed hold returns the same successful result without changing its confirmation timestamp, even after its historical expiry time.

For a HELD row, the service reads fresh `clock_timestamp()` after acquiring the lock. If expiry is at or before that instant, it persists EXPIRED, commits, and then returns 409. Otherwise it saves CONFIRMED and `confirmed_at`, commits, and returns 200. EXPIRED/CANCELLED holds return 409. Confirmation preserves historical timestamps and total occupancy. The hold lock serializes concurrent confirmations and prevents the expiration worker from overwriting the result.

## Cancel a hold

`DELETE /holds/{holdId}` requires one `X-Student-Id` header, using the same trimming and 64-character rules as creation/confirmation. No request body is needed. Success is `204 No Content` with an empty response body.

Cancellation locks the hold and verifies ownership before checking status or changing state. A valid non-owning identifier returns 403; unknown holds return 404 and malformed UUIDs return 400. An owned CANCELLED row returns 204 without changing its original cancellation timestamp, even after historical expiry. CONFIRMED or EXPIRED rows return 409.

For a HELD row, fresh database time is read after the row lock. If `expires_at <= clock_timestamp()`, the service saves EXPIRED, commits, and returns 409 without setting `cancelled_at`. Otherwise it saves CANCELLED and `cancelled_at`, commits, and returns 204. Rows are retained with their original creation/expiry timestamps. A cancelled seat becomes available for another student or a new application by the same student. Confirmation, cancellation, and expiration coordinate through the hold row lock.

## List a student's applications

`GET /me/holds` requires one trimmed, non-empty `X-Student-Id` header of at most 64 characters. Example `200 OK` response:

```json
[
  {
    "holdId": "334b0c66-a05e-413b-b7d8-52baf2ac3158",
    "classId": "10eeb2c5-54c4-4e4c-9f2c-dc197d5db560",
    "className": "Class 6 - Morning Shift",
    "status": "HELD",
    "expiresAt": "2026-11-10T12:10:00Z"
  }
]
```

Each item contains exactly these five fields. The list includes all applications for that identifier across classes, including cancelled/expired history. Results are ordered by creation time descending, then hold UUID ascending for ties. No applications returns `[]`. Matching preserves case and leading zeros; query parameters do not select a student. Invalid or multiple header values return 400.

A single EF query filters by student and joins class names, using one captured database clock value for expiry. An overdue HELD row is returned as EXPIRED immediately; terminal statuses and original expiry timestamps remain unchanged. Listing does not persist state changes. Responses disable caching and omit student IDs. The header identifies the student but does not authenticate them.

## Automatic expiration

The expiration worker runs one bounded batch per interval, using database time. Defaults are 100 rows every 1000 ms, configurable with `Expiration__BatchSize` (1–1000) and `Expiration__IntervalMilliseconds` (100–60000). Locked rows are skipped and revisited on a later cycle. Only overdue HELD rows change; history and timestamps are retained. Failed batches are retried on the next cycle. Retrieval and seat summaries remain authoritative between cycles.

## Errors

| Status | Situation |
| --- | --- |
| 400 | Invalid class input/UUID/JSON, missing/blank/overlong/multiple student headers, or unsupported hold-body fields |
| 403 | A valid student identifier does not own the hold being confirmed or cancelled |
| 404 | A valid UUID does not identify the requested class or hold |
| 409 | Duplicate class name, no remaining seat, duplicate application, or a hold that cannot be confirmed/cancelled |

Errors use `application/problem+json` with `status`, `title`, `instance`, and `traceId`; validation errors also include `errors`. Unexpected database/infrastructure failures remain generic 500 responses. Database exceptions and constraint names are not exposed in the response.

Swagger UI is available in development at `http://localhost:8080/swagger/index.html` and documents all eight API operations.
