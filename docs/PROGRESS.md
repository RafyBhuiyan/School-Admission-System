# Implementation status

## Available

- Validated configuration, CORS, Problem Details, and development Swagger/OpenAPI.
- EF Core entities, migration, constraints, and indexes for classes and applications.
- Class creation, listing, and detail queries with consistent seat summaries.
- Hold creation with a PostgreSQL class-row lock, overdue-hold cleanup, duplicate/capacity checks, and configurable duration.
- Public hold retrieval with effective expiry, database server time, and no student identifier in the response.
- Automatic expiration in bounded batches, using fresh scoped services, database time, conditional updates, and skipped row locks.
- Simulated-payment confirmation with ownership checks, hold locking, fresh-time expiry, and idempotent retries.
- Cancellation with ownership checks, terminal-state protection, retained history, and released capacity for reapplication.
- Student application listing with header-based filtering, class names, effective expiry, and retained history across classes.
- Frontend runtime configuration and shared typed API client for all eight operations, exposed through a React provider/hook.
- Student identification with 17-digit validation, localStorage restoration, automatic application retrieval, and Change/Forget actions that clear stale state and pending requests.

Class administration/apply/payment/cancellation UI controls and Docker Compose packaging remain unimplemented. The portal currently supports student identification and read-only application history.

## Verification

Verified locally on 10 November 2026 against PostgreSQL 18:

| Check | Result |
| --- | --- |
| Release build | Passed with zero warnings or errors |
| Backend suite | 220 passed, zero failed, zero skipped |
| Foundation | 33 cases covering configuration, validation, CORS, errors, and Swagger |
| Schema | 11 cases covering mappings, constraints, indexes, and migration rollback/reapply |
| Class API | 23 cases covering response contracts, validation, errors, and duplicate-name races |
| Seat summaries | 17 cases covering status/expiry boundaries, capacity, class isolation, and fresh database time |
| Hold creation | 30 cases covering headers, duration, conflicts, history, cleanup, rollback, and concurrent allocation |
| Hold retrieval | 16 cases covering every status, exact expiry boundaries, live database time, read-only behavior, errors, and public response fields |
| Expiration | 6 cases covering empty/repeated batches, state/timestamp preservation, batch limits/order, locked rows, concurrent batches, and hosted workers without HTTP requests |
| Confirmation | 28 cases covering the exact response, ownership/header/body checks, retries, committed expiry, microsecond boundaries, concurrent confirmations, lock waits, and expiration-worker contention |
| Cancellation | 26 cases covering 204/empty body, retries, history, seat reuse, ownership/header checks, error paths, exact expiry, lock waits, queued retries, and confirmation/expiration races |
| Student applications | 19 cases covering exact fields, all statuses, class/history joins, ordering, student isolation, empty results, header validation, expiry boundaries, live transaction time, lifecycle flow, and OpenAPI |
| HTTP workflows/database failures | 7 cases covering all eight operations with the real worker, identifier formats, elapsed-time expiry/reapplication, and atomic rollback/sanitized errors after rejected database writes |
| Capacity bursts | 2 cases sending 100 distinct-student requests to a 10-seat class, empty and partially occupied; persisted results match available capacity |
| Backend restart | 2 cases terminating and restarting real API processes, checking persisted history/timestamps, retries/ownership, terminal-state protection, and worker catch-up after downtime |
| Frontend API/configuration | 30 passed, zero failed/skipped; all eight methods, headers, error normalization, cancellation/timeouts, and runtime changes through a real HTTP server |
| Student session | 19 passed, zero failed/skipped; strict validation, persistence/reopen, switching/forgetting, aborted/stale requests, storage failures, retries, and cross-tab restoration |
| Frontend build/lint | Passed; production bundle includes a separate runtime config file |
| Migration snapshot | No pending model changes; no migration needed |
| Whitespace/diff check | Passed |

The last-seat test uses two independent in-process API hosts sharing PostgreSQL: one request returns 201 and the other 409, with one persisted active hold. Eight queued requests for the same student produce one hold and seven conflicts. Tests observe database lock waits before releasing the competing requests. This is not a separate-process deployment or load test.

Exact-boundary tests freeze only the scalar clock input; PostgreSQL executes the production query's comparisons. Separate tests use the unmodified live clock inside existing transactions to detect regressions to transaction-start time. Hold retrieval uses the same captured time for effective status and `serverTime`, reports overdue rows as `EXPIRED`, and leaves stored data unchanged.

Hosted-worker tests enable the production background service in two application hosts, with one row per batch. They observe persisted expiry across cycles without HTTP clients. Other endpoint tests disable only that hosted service to preserve controlled stored-state assertions. Worker batches skip locked rows and keep confirmed/cancelled states and historical timestamps intact.

Eight queued confirmations returned the same successful result with one stored admission and unchanged occupancy. A hold made overdue while confirmation waited for its row lock was persisted as EXPIRED before the 409 response. A controlled expiration-worker lock race also returned 409 after the worker committed expiry. Ownership mismatches leave stored state unchanged, including overdue HELD rows.

Cancellation tests reallocate a released seat both to the original student and to another student, with the old row retained. Eight queued cancellations return 204 and retain one history row. Controlled races let confirmation, cancellation, or expiration acquire the lock first, then verify a competing operation returns 409 and cannot overwrite the committed terminal state. Expiry during a cancellation lock wait is committed as EXPIRED before returning 409, without setting a cancellation timestamp.

Student-list tests verify that header case and leading zeros are preserved, query parameters cannot override the selected student, history is retained across classes, and reads leave stored rows unchanged. Exact-boundary cases share one captured clock value across rows. A complete HTTP flow creates two applications, confirms one, cancels the other, and checks their final statuses in the list. OpenAPI covers all eight operations.

The [integration coverage audit](TESTING.md) maps success/error responses for every endpoint and the cross-cutting identity/lifecycle checks. Additional HTTP workflows run with the production worker enabled and exercise synthetic, leading-zero, and 64-character IDs. A real one-second hold expires without a follow-up HTTP request. Test-only PostgreSQL triggers reject inserts/updates to verify rollback of allocation cleanup and lifecycle mutations; dropping the trigger permits a successful retry. These tests passed without production-code changes.

The capacity tests observed 20 blocked allocation requests before releasing the class lock in each scenario. For the empty class, 10 requests returned 201 and 90 returned 409. With three HELD and two CONFIRMED rows present, five returned 201 and 95 returned 409. Both ended with 10 occupied seats, 10 rows, 10 distinct students, and zero availability. Successful response IDs and expiries matched persisted rows. The tests use one in-process API host and a pool bounded to 20 connections; all 100 HTTP requests launch through a shared start gate. The expiration worker is disabled for deterministic allocation counts with 600-second holds.

Aggregate metrics were extracted directly from the successful TRX results into [preserved concurrency output](evidence/capacity-concurrency.txt). The raw report remains ignored to avoid committing machine-specific logs. No production-code or schema changes were required.

Restart tests launch the compiled API in Production mode on ephemeral loopback ports, terminate the owned OS process, and start a new one against the same database. Snapshots include IDs, student IDs, statuses, and all application timestamps. Class names/capacity/timestamps, list/details, ownership checks, and idempotent confirmation/cancellation remain correct after restart. Historical confirmed/cancelled deadlines are advanced directly in the fixture to avoid waiting ten minutes. An offline-seeded overdue HELD row proves a restarted worker cycle runs without changing the pre-existing terminal rows.

A separate five-second hold expires while the API is stopped. Before restart it remains stored as HELD; after restart, database-only polling observes EXPIRED before any student request. Another student then obtains the released seat, while the expired application remains historical and cannot be confirmed/cancelled. Existing microsecond-boundary, post-lock-time, ownership, and retry suites passed alongside these additions. No production or schema changes were required.

The suite used fixture-owned databases in a disposable PostgreSQL container, which was removed with its volume after verification. Latest results are saved to the ignored `Backend/SchoolAdmission.Tests/TestResults/lifecycle-restart.trx`. No hosted CI run was performed during this verification. Broader load, multi-process allocation concurrency, and container deployment checks remain outstanding.

The frontend loads `config.json` before rendering and initializes the shared API provider. Its client sends student IDs only in headers, omits credentials, disables caching/redirects, supports aborts and a 15-second timeout, and normalizes Problem Details without exposing raw failure bodies. Mutations are not automatically retried. All 49 frontend tests passed; build and lint also passed. The backend was unchanged and its 220-case result above is from the preceding backend verification.

Browser verification used the in-app browser and a local synthetic API fixture: short input was rejected, a valid leading-zero identifier loaded history, reload/tab reopen restored it automatically, changing students cleared old history, and forgetting cleared both open tabs and remained cleared after reload. A [synthetic preview](evidence/student-portal.png) was saved with only the final four identifier digits visible. The temporary fixture/server were stopped and runtime configuration restored to the normal API address. Live .NET/browser integration, mobile viewport checks, and a full browser-process restart were not performed.
