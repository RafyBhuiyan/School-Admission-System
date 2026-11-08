# Admission API

Class operations were implemented in step 5 and hold creation in step 7. Base URL: `http://localhost:8080`. Apply the initial migration to the configured PostgreSQL database before calling these endpoints; see [database setup](DATABASE.md#migration-commands).

Class creation/list/details require no authentication or `X-Student-Id` header in this demo. Hold creation requires the student identifier header; it is not authentication.

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

Expired/cancelled applications remain in history, and reapplication creates a new hold ID. Duplicate active/confirmed applications and full classes return 409. Failed allocation rolls back the transaction, including its cleanup. Overdue rows still do not occupy seats in summaries; the background worker planned for step 9 will persist expiry independently. A student may apply to different classes.

## Errors

| Status | Situation |
| --- | --- |
| 400 | Invalid class input/UUID/JSON, missing/blank/overlong/multiple student headers, or unsupported hold-body fields |
| 404 | A valid UUID does not identify a class |
| 409 | Duplicate class name, no remaining seat, or an existing active/confirmed application for this student and class |

Errors use `application/problem+json` with `status`, `title`, `instance`, and `traceId`; validation errors also include `errors`. Unexpected database/infrastructure failures remain generic 500 responses. Database exceptions and constraint names are not exposed in the response.

Swagger UI is available in development at `http://localhost:8080/swagger/index.html`. Hold retrieval, payment, cancellation, and My Applications endpoints are still planned.
