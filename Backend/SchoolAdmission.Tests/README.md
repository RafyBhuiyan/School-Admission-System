# Backend tests

xUnit tests for the real ASP.NET Core request pipeline, EF Core mappings and PostgreSQL transactions. The project references `SchoolAdmission.Api`; test-only probe controllers are registered only inside the test host.

## Run

From the repository root, use a test-server account with `CREATE DATABASE` permission:

```powershell
$env:SCHEMA_TEST_CONNECTION_STRING = "Host=localhost;Port=5432;Database=postgres;Username=schema_test;Password=replace-with-your-test-password"
dotnet test SchoolAdmission.slnx --configuration Release
```

Each fixture creates a uniquely named database, applies the migration and drops only its own database afterward. Without the connection variable, PostgreSQL cases are explicitly skipped. Foundation and health-probe tests run without a database. GitHub Actions supplies disposable PostgreSQL 18 and synthetic credentials.

## Coverage

Files below are in `Integration/` unless marked otherwise.

| Test group | Cases | Main checks |
| --- | ---: | --- |
| `FoundationTests` | 33 | Configuration, DI, CORS, validation, safe errors, development Swagger |
| `Unit/ApiHealthProbeTests` | 24 | Listener precedence, IPv6, overrides and internal/public URL separation |
| `DatabaseSchemaTests` | 11 | Migration, types, constraints, indexes and rollback/reapply |
| `ClassApiTests` | 23 | Creation/list/details, response fields, validation and duplicate races |
| `SeatSummaryQueryTests` | 17 | Status counts, exact expiry, fresh database time and unchanged stored rows |
| `HoldCreationTests` | 30 | Capacity, duplicates, headers, rollback, reapplication and lock waits |
| `HoldRetrievalTests` | 16 | Effective expiry, public reads, server time and response shape |
| `HoldExpirationTests` | 6 | Batches, skipped locks, contention and real hosted workers |
| `HoldConfirmationTests` | 28 | Ownership, retries, expiry and competing lifecycle writes |
| `HoldCancellationTests` | 26 | Empty 204, retries, ownership, released capacity and terminal states |
| `StudentApplicationTests` | 19 | Student filtering, history, ordering and expiry |
| `AdmissionWorkflowTests` | 7 | Full HTTP workflows, real-duration expiry and failed-write rollback |
| `CapacityConcurrencyTests` | 2 | 100 requests for 10 seats, empty and partially occupied classes |
| `BackendRestartTests` | 2 | Persisted history, stable retries and worker catch-up after downtime |
| `AdminAccessTests` | 4 | Missing/wrong keys, server configuration and protected creation |
| `AdminApplicationsTests` | 6 | Access, filters, pagination, effective expiry and no-store |
| `MultiInstanceConcurrencyTests` | 2 | Two real API processes sharing capacity under contention |
| **Total** | **256** | |

## How the race checks work

Requests wait behind a class/hold row lock. Tests observe blocked sessions in PostgreSQL before releasing it, then compare responses with persisted rows. This exercises actual lock contention rather than relying on task timing. The two-process allocation tests send 50 requests to each API and check both summaries against the same database.

Restart and multi-instance tests launch the built API DLL on ephemeral loopback ports, with the real worker enabled. They need the source checkout, build output and installed .NET runtime. The helper drains process output and stops only processes it started.

Exact-boundary cases replace only the database clock expression with a fixed input. Other lock-wait cases use the real PostgreSQL clock. Failure tests install rejecting triggers only in fixture-owned databases, verify rollback and sanitized errors, then remove the triggers.

## Focused runs

```powershell
dotnet test SchoolAdmission.slnx --configuration Release --filter Category=PostgreSQLCapacityConcurrency --logger "trx;LogFileName=capacity.trx"
dotnet test SchoolAdmission.slnx --configuration Release --filter Category=PostgreSQLMultiInstance --logger "trx;LogFileName=multi-instance.trx"
```

The latest complete local run passed **all 256 cases**, with zero skips, against disposable PostgreSQL 18. This includes the administrator register and two-process allocation cases. The [repository audit](../../docs/REPOSITORY-AUDIT.md) records the verification scope.

See [coverage and reproduction](../../docs/TESTING.md), [database account setup](../../docs/DATABASE.md#real-postgresql-schema-checks), and the recorded [single-host](../../docs/evidence/capacity-concurrency.txt) / [two-process](../../docs/evidence/multi-instance-concurrency.txt) output. These concurrency checks establish correctness under contention; broader load testing remains outstanding.
