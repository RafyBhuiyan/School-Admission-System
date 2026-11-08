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

## Business rules

The detailed assumptions required by step 4 will be recorded after the database model is implemented. The revised plan remains the baseline; no admission behavior has been implemented or verified at step 2.
