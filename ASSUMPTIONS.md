# Assumptions and decisions

## Step 1

- Retain the existing `Backend` and `Frontend` directory names; the plan permits different folder names.
- Retain .NET 10 and React 19 from the existing starters. Both meet the plan's minimum versions. `global.json` selects a stable .NET 10 SDK with feature-band roll-forward.
- Use one API project and one xUnit test project in the root solution. Services will use `AppDbContext` directly, without boilerplate repositories.
- `.env.example` is a configuration contract for later steps. It contains placeholders, and is not automatically loaded by the scaffold.
- The user initialized the GitHub remote at `RafyBhuiyan/School-Admission-System`. Connectivity is verified; public visibility has not been verified because the separate API check was declined.

## Step 2

- The backend requires `ConnectionStrings__DefaultConnection` at startup. It validates the PostgreSQL connection-string syntax and requires explicit `Host` and `Database` values, without exposing configuration values in validation errors.
- `AppDbContext` is scoped to the request/DI scope. Step 2 registers the PostgreSQL provider but does not connect, run migrations, or create tables. Database connectivity is not yet a readiness guarantee.
- The frontend origin defaults to `http://localhost:5173`; deployment can override or extend it through indexed `Cors__AllowedOrigins__*` environment variables. Wildcard origins and cookie credentials are not enabled.
- Swagger UI and the OpenAPI document are available only in development. The existing OpenAPI generator supplies the document; the Swagger UI package provides the interactive viewer.
- Controllers use Data Annotations and automatic API-controller validation. Input formatting errors, unhandled exceptions, and empty error responses use Problem Details with a request trace ID. Business-state outcomes and student-header validation will be implemented with their endpoints.
- Foundation tests use the actual HTTP pipeline with test-only controllers and synthetic configuration. They require no live PostgreSQL service and do not establish concurrency correctness.

## Step 3

- Database table names are `AdmissionClasses` and `AdmissionHolds`, with snake_case columns. One class represents a complete grade/shift category.
- Entities generate UUIDs with `Guid.NewGuid()`. Class names are limited to 200 characters and use the database's default case-sensitive uniqueness.
- Student IDs use `VARCHAR(64)` and preserve leading zeros/case. Request trimming and backend/UI validation will be implemented at their scheduled endpoint/UI steps.
- Status is stored as uppercase text, backed by a C# enum and an allowed-values database check. There is no PostgreSQL custom enum type or additional status table.
- UTC timestamps use `TIMESTAMPTZ`. Insert-time `created_at`/class `updated_at` defaults use `clock_timestamp()`. No application-clock expiry logic or timestamp-update trigger has been added.
- Class deletion is restricted while application history references it. No class-deletion endpoint is specified.
- The active/confirmed unique index is based only on stored status; overdue `HELD` cleanup must precede reapplication in the step 7 transaction.
- Migrations are explicit commands at this stage. The repository's EF CLI and EF packages are aligned at 10.0.8. Docker migration orchestration remains a later packaging step.
- Real schema tests create/drop a randomly named database on the configured test server, using an account with `CREATE DATABASE` permission. They verify schema behavior and do not assert concurrent allocation safety.

## Business rules

The detailed business assumptions required by step 4 are next. The revised plan remains the baseline; no admission endpoint behavior has been implemented or verified at step 3.
