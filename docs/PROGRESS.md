# Implementation status

## Available

- Validated configuration, CORS, Problem Details, and development Swagger/OpenAPI.
- EF Core entities, migration, constraints, and indexes for classes and applications.
- Class creation, listing, and detail queries with consistent seat summaries.
- Hold creation with a PostgreSQL class-row lock, overdue-hold cleanup, duplicate/capacity checks, and configurable duration.
- Public hold retrieval with effective expiry, database server time, and no student identifier in the response.
- Automatic expiration in bounded batches, using fresh scoped services, database time, conditional updates, and skipped row locks.
- Simulated-payment confirmation with ownership checks, hold locking, fresh-time expiry, and idempotent retries.

Cancellation, My Applications, frontend workflows, and Docker Compose packaging remain unimplemented. The React application is a starter.

## Verification

Verified locally on 10 November 2026 against PostgreSQL 18:

| Check | Result |
| --- | --- |
| Release build | Passed with zero warnings or errors |
| Backend suite | 164 passed, zero failed, zero skipped |
| Foundation | 33 cases covering configuration, validation, CORS, errors, and Swagger |
| Schema | 11 cases covering mappings, constraints, indexes, and migration rollback/reapply |
| Class API | 23 cases covering response contracts, validation, errors, and duplicate-name races |
| Seat summaries | 17 cases covering status/expiry boundaries, capacity, class isolation, and fresh database time |
| Hold creation | 30 cases covering headers, duration, conflicts, history, cleanup, rollback, and concurrent allocation |
| Hold retrieval | 16 cases covering every status, exact expiry boundaries, live database time, read-only behavior, errors, and public response fields |
| Expiration | 6 cases covering empty/repeated batches, state/timestamp preservation, batch limits/order, locked rows, concurrent batches, and hosted workers without HTTP requests |
| Confirmation | 28 cases covering the exact response, ownership/header/body checks, retries, committed expiry, microsecond boundaries, concurrent confirmations, lock waits, and expiration-worker contention |
| Migration snapshot | No pending model changes; no migration needed |
| Whitespace/diff check | Passed |

The last-seat test uses two independent in-process API hosts sharing PostgreSQL: one request returns 201 and the other 409, with one persisted active hold. Eight queued requests for the same student produce one hold and seven conflicts. Tests observe database lock waits before releasing the competing requests. This is not a separate-process deployment or load test.

Exact-boundary tests freeze only the scalar clock input; PostgreSQL executes the production query's comparisons. Separate tests use the unmodified live clock inside existing transactions to detect regressions to transaction-start time. Hold retrieval uses the same captured time for effective status and `serverTime`, reports overdue rows as `EXPIRED`, and leaves stored data unchanged.

Hosted-worker tests enable the production background service in two application hosts, with one row per batch. They observe persisted expiry across cycles without HTTP clients. Other endpoint tests disable only that hosted service to preserve controlled stored-state assertions. Worker batches skip locked rows and keep confirmed/cancelled states and historical timestamps intact.

Eight queued confirmations returned the same successful result with one stored admission and unchanged occupancy. A hold made overdue while confirmation waited for its row lock was persisted as EXPIRED before the 409 response. A controlled expiration-worker lock race also returned 409 after the worker committed expiry. Ownership mismatches leave stored state unchanged, including overdue HELD rows.

The suite used fixture-owned databases in a disposable PostgreSQL container, which was removed with its volume after verification. Latest results are saved to the ignored `Backend/SchoolAdmission.Tests/TestResults/confirmation.trx`. No hosted CI run was performed during this verification. Cancellation races and deployment checks remain outstanding.
