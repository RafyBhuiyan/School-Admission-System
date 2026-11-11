# Integration coverage

The integration suite runs the actual ASP.NET Core pipeline and EF Core provider against PostgreSQL 18. Database fixtures create uniquely named databases, apply migrations, and drop only their own databases. Foundation tests run without a database; database tests are explicitly skipped if `SCHEMA_TEST_CONNECTION_STRING` is missing. CI supplies the connection and PostgreSQL service.

## Endpoint coverage

| Operation | Verified responses | Primary test files |
| --- | --- | --- |
| POST `/classes` | 201, 400, 409 | `ClassApiTests`, `AdmissionWorkflowTests` |
| GET `/classes` | 200, including empty array | `ClassApiTests`, `AdmissionWorkflowTests` |
| GET `/classes/{classId}` | 200, 400, 404 | `ClassApiTests`, `AdmissionWorkflowTests` |
| POST `/classes/{classId}/holds` | 201, 400, 404, 409 | `HoldCreationTests`, `AdmissionWorkflowTests` |
| GET `/holds/{holdId}` | 200, 400, 404 | `HoldRetrievalTests`, `AdmissionWorkflowTests` |
| POST `/holds/{holdId}/confirm` | 200, 400, 403, 404, 409 | `HoldConfirmationTests`, `AdmissionWorkflowTests` |
| DELETE `/holds/{holdId}` | 204 with empty body, 400, 403, 404, 409 | `HoldCancellationTests`, `AdmissionWorkflowTests` |
| GET `/me/holds` | 200, including empty array, 400 | `StudentApplicationTests`, `AdmissionWorkflowTests` |

## Cross-cutting checks

- Synthetic non-empty IDs, leading zeros, case-sensitive matching, trimmed 64-character IDs, and rejection of missing/blank/overlong/multiple header values.
- Duplicate active/confirmed applications per class, independent classes, retained terminal history, and reapplication after cancellation/expiry.
- Exact expiry boundaries, fresh time after row-lock waits, committed EXPIRED state before overdue confirmation/cancellation returns 409, and ownership checks before mutation.
- Stable confirmation/cancellation retries and capacity derived from persisted statuses.
- A complete HTTP workflow with the real worker enabled, covering all eight operations for three identifier formats.
- A one-second hold that the worker expires without subsequent HTTP requests, followed by retrieval, rejected terminal-state mutations, and reapplication.
- PostgreSQL triggers deliberately rejecting allocation inserts and confirmation/cancellation updates. Responses must be sanitized 500 Problem Details; failed transactions must leave prior state intact. Removing the trigger permits a successful retry. Triggers exist only in the fixture-owned database and are removed during cleanup.

Existing race tests cover the last seat, duplicate applications, queued confirmations/cancellations, skipped locks, and competing lifecycle mutations. They use in-process hosts with separate database connections. Real-process persistence tests are described below. [Compose startup and volume persistence](CONTAINERS.md#clean-startup-and-volume-persistence) also passed locally and have a repeatable CI runner. Broader load and multi-process allocation concurrency remain outstanding.

## Capacity bursts

`CapacityConcurrencyTests` sends 100 distinct-student HTTP requests to a class with 10 seats. The two scenarios begin with no applications, or with three active holds and two confirmations. A shared start gate launches requests while the class row is locked; observed PostgreSQL blocking sessions confirm contention before the lock is released. The host uses a 20-connection pool to bound database resource use while all 100 HTTP requests remain in flight or queued.

Assertions check the exact number of 201/409 responses, unique successful hold IDs, response-to-database matches, persisted active/confirmed counts, distinct student counts, capacity invariants, and the class summary. The worker is disabled for these allocation tests; separate suites verify its behavior. Results and observed blocking counts are preserved in the [actual output](evidence/capacity-concurrency.txt), extracted from the local TRX report. This is a correctness test, not a throughput benchmark.

## Lifecycle and process restarts

`BackendRestartTests` starts the compiled API DLL as a separate OS process, with the real worker enabled in Production mode. Kestrel binds an ephemeral loopback port. Each process is terminated and awaited before a replacement starts against the same fixture database. The test helper drains output without publishing configuration values and cleans up only the processes it owns.

| Behavior | Coverage |
| --- | --- |
| Confirmation retries | Existing sequential/queued retry cases; retries after process restart preserve the original timestamp and occupancy |
| Exact expiry / lock waits | Fixed-clock microsecond cases plus live-clock mutations while requests are verifiably waiting for a row lock |
| Ownership | 403 without mutation across states; the same checks run against the restarted API process |
| History and class persistence | Full application timestamp/identity snapshots and class timestamps remain unchanged across restart |
| Terminal-state protection | A worker cycle in the restarted process expires a new overdue row while confirmed/cancelled/expired snapshots stay unchanged |
| Downtime expiry | A real five-second hold expires while the API is stopped, remains stored HELD until startup, then becomes EXPIRED without a student request |
| Released capacity | Another student obtains the freed seat after startup while the original expired row remains in history |

Tests use database polling with bounded timeouts. Historical confirmed/cancelled deadlines are advanced through the fixture to exercise overdue terminal rows without long waits; the downtime test uses the actual configured hold duration and unmodified PostgreSQL clock.

For commands and database-account setup, see [database verification](DATABASE.md#real-postgresql-schema-checks). [Test project notes](../Backend/SchoolAdmission.Tests/README.md) describe individual test groups; [verification results](PROGRESS.md) record the latest local run.
