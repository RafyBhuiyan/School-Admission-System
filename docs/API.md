# Class API

Implemented in step 5. Base URL: `http://localhost:8080`. Apply the initial migration to the configured PostgreSQL database before calling these endpoints; see [database setup](DATABASE.md#migration-commands).

These class endpoints require no authentication or `X-Student-Id` header in this demo.

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

EF Core translates the shared LINQ query into one SQL statement. The only handwritten SQL is `SELECT clock_timestamp() AS "Value"`, composed into that statement to capture database wall-clock time once. Overdue `HELD`, `EXPIRED`, and `CANCELLED` rows do not occupy seats. Reads do not mutate application status. Capacity-safe hold creation is scheduled for step 7; the availability floor does not enforce that invariant.

## Errors

| Status | Situation |
| --- | --- |
| 400 | Missing/blank/overlong name, missing/nonpositive/non-integer capacity, invalid JSON, or malformed class UUID |
| 404 | A valid UUID does not identify a class |
| 409 | Another class already has the trimmed name |

Errors use `application/problem+json` with `status`, `title`, `instance`, and `traceId`; validation errors also include `errors`. Unexpected database/infrastructure failures remain generic 500 responses. Database exceptions and constraint names are not exposed in the response.

Swagger UI is available in development at `http://localhost:8080/swagger/index.html`. Hold, payment, cancellation, and My Applications endpoints are still planned.
