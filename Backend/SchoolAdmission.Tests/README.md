# Backend tests

This is an xUnit test project referencing `SchoolAdmission.Api`, not a second web application.

`Integration/FoundationTests.cs` contains 33 foundation test cases using `WebApplicationFactory` and the actual request pipeline, including invalid hold-duration and expiration settings. Test-only probe controllers are registered solely in the test host and are excluded from API documentation. No probe endpoints are included in the deployed API.

Coverage includes startup configuration errors, scoped PostgreSQL `AppDbContext` registration, allowed/disallowed CORS origins, automatic DTO and JSON validation, sanitized exceptions, fallback 404s, and development-only Swagger/OpenAPI.

The foundation tests supply synthetic configuration and do not open a database connection. They run without PostgreSQL or production credentials.

`Integration/DatabaseSchemaTests.cs` adds 11 schema cases against real PostgreSQL. `Integration/ClassApiTests.cs` adds 23 HTTP cases for class creation, list/details, exact response fields, validation, 400/404/409 errors, duplicate-name races, and mixed-status seat summaries.

`Integration/SeatSummaryQueryTests.cs` adds 17 service/query cases: every status with past/future expiry, the exact expiry boundary at microsecond precision, full/over-capacity data, per-class counts with application history, and fresh wall-clock time inside an existing transaction. Every case compares list and detail results. Only the three exact-boundary cases replace the query's clock expression with a fixed parameter; the counts and comparisons still execute in PostgreSQL. The transaction regression uses the real clock and verifies that reads leave overdue rows stored as `HELD`.

`Integration/HoldCreationTests.cs` adds 30 cases for the creation contract, configurable duration, header/body validation, duplicates/full classes, reapplication/history, cleanup scope, and rollback. The last-seat race uses two independent in-process API hosts sharing PostgreSQL. A separate test queues eight applications for one student. Both check database occupancy after completion. A lock-wait regression expires the old hold after the request transaction starts and verifies fresh-time cleanup and the new hold's lifetime. These tests observe blocked PostgreSQL sessions before releasing the class lock; polling has a 15-second timeout.

`Integration/HoldRetrievalTests.cs` adds 16 cases covering all four statuses with past/future expiry, the exact expiry boundary, fresh database time inside an existing transaction, unchanged stored data, missing/malformed IDs, creation-to-retrieval flow, public access, and OpenAPI. Responses include database `serverTime`, omit the student identifier, and disable caching. Boundary cases share a test-only fixed-clock interceptor with the summary tests; live-clock cases do not use it.

`Integration/HoldExpirationTests.cs` adds six cases for repeat/empty batches, overdue-only transitions, unchanged timestamps, oldest-first bounded processing, skipped locks/status changes, concurrent batches, and real hosted workers. Two hosts expire rows across cycles without HTTP requests. Most endpoint tests remove only the expiration hosted-service registration to keep seeded states deterministic; expiration tests enable it explicitly.

`Integration/HoldConfirmationTests.cs` adds 28 cases covering confirmation, idempotent retries, historical confirmed expiry, ownership before mutation, validation, committed overdue expiry, exact boundaries, and OpenAPI. Eight requests queue on an observed row lock before returning the same success. Another case expires the hold while confirmation waits. A test-only interceptor pauses the real expiration service after it locks its selected row, then verifies the queued confirmation observes the worker's committed EXPIRED state. Boundary tests freeze only the database clock expression and check persisted results in a separate context.

`Integration/HoldCancellationTests.cs` adds 26 cases covering 204 with no body, stable retries, history retention, released-seat reapplication by the same/different student, ownership/header validation, missing/malformed IDs, committed overdue expiry, microsecond boundaries, eight queued cancellations, and OpenAPI. Controlled row-lock tests exercise confirmation winning before cancellation, cancellation winning before confirmation, and expiration winning before cancellation. The competing operation must observe the committed terminal state and return 409.

Set `SCHEMA_TEST_CONNECTION_STRING` to a test-server account with `CREATE DATABASE` permission. Each test class gets a uniquely named database, applies the migration, and drops its own database afterward. API and query cases reset only that owned database between cases. Database-backed cases are explicitly skipped when the variable is absent. GitHub Actions provides a PostgreSQL 18 service and runs all 190 cases. See [database verification](../../docs/DATABASE.md#real-postgresql-schema-checks) for setup.

Current races cover allocation, confirmation/cancellation, and expiration contention; broader load coverage and deployment to separate processes remain untested.

Run from the repository root:

```powershell
dotnet test SchoolAdmission.slnx --configuration Release
```
