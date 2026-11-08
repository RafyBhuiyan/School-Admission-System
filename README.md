# School Admission System

A school admission application for parents and administrators, built in the order defined by the revised implementation plan. Admission will be first-come, first-served with PostgreSQL transactions protecting limited seats.

## Current status

Steps 1–5 are complete locally. The class creation, list, and detail endpoints now work with validation, duplicate-name conflicts, and seat summaries. All 55 backend test cases pass, including 23 class API cases against real PostgreSQL. Hold/payment/cancellation endpoints and the parent/admin UI workflows are still planned. See [docs/PROGRESS.md](docs/PROGRESS.md) for verified scope. Docker Compose will be added in steps 22–24.

## Technology

- .NET 10 controller-based Web API (satisfies .NET 8+).
- xUnit test project referencing the API.
- React 19, TypeScript, and Vite (satisfies React 18+).
- EF Core with the Npgsql PostgreSQL provider, configured entities, and an initial migration.

## Structure

```text
SchoolAdmission.slnx
Backend/
  SchoolAdmission.Api/
    Configuration/
    Controllers/
    DTOs/{Requests,Responses}/
    Models/Entities/
    Enums/
    Data/{Configurations,Migrations}/
    Services/
    BackgroundServices/
    Validators/
    Middleware/
    Common/
  SchoolAdmission.Tests/
    Unit/
    Integration/
    Concurrency/
Frontend/
  src/
  public/
docs/PROGRESS.md
```

Controllers will delegate business rules and transactions to services that use `AppDbContext` directly. A repository abstraction is not planned.

## Local development

Prerequisites for this scaffold: .NET 10 SDK and Node.js 22.12+ with npm. Docker-only startup will be documented after packaging is implemented.

Run from the repository root:

```powershell
dotnet restore SchoolAdmission.slnx
dotnet build SchoolAdmission.slnx --no-restore
$env:ConnectionStrings__DefaultConnection = "Host=localhost;Port=5432;Database=school_admission;Username=school_admission;Password=replace-with-a-local-development-password"
dotnet run --project Backend/SchoolAdmission.Api --launch-profile http
```

Replace the connection values with your local PostgreSQL settings. API startup validates configuration without automatically opening a database connection or applying migrations. Missing or malformed database configuration prevents startup with a useful message that omits connection-string values.

The API listens on `http://localhost:8080`. In development, Swagger UI is at `http://localhost:8080/swagger/index.html` and the OpenAPI document is at `http://localhost:8080/openapi/v1.json`. Both are disabled in production. Swagger documents the three class operations. The root path returns a 404 Problem Details response.

In a separate terminal:

```powershell
cd Frontend
npm ci
npm run dev
```

Open `http://localhost:5173` for the React starter. It currently has no API integration.

## Configuration

`.env.example` defines safe placeholders. Neither .NET nor Vite automatically loads a repository-root `.env` file. Export the variables into the backend process environment (PowerShell example above). ASP.NET Core's environment configuration maps double underscores to configuration sections. The local HTTP launch profile sets the listening URL and development environment.

| Variable | Intended purpose | Status |
| --- | --- | --- |
| `ASPNETCORE_URLS` | API listening address; default local URL is `http://localhost:8080` | Supported by ASP.NET Core |
| `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD` | Local PostgreSQL container settings | Planned for Docker Compose |
| `ConnectionStrings__DefaultConnection` | Backend PostgreSQL connection string; must contain `Host` and `Database` | Required and validated at startup |
| `Cors__AllowedOrigins__0` | Allowed frontend origin, default `http://localhost:5173` | Configured and validated at startup |
| `HOLD_DURATION_SECONDS` | Hold lifetime, default 600 seconds | Planned for lifecycle implementation |
| `API_BASE_URL` | Browser-reachable backend address | Planned for frontend integration/runtime configuration |

Additional explicit origins can be supplied through `Cors__AllowedOrigins__1`, `Cors__AllowedOrigins__2`, etc. Origins must use HTTP(S), with no wildcard, credentials, path, or trailing slash. The CORS policy allows `GET`, `POST`, `DELETE`, `Content-Type`, and `X-Student-Id`; preflight is handled automatically. Credentials are not enabled.

## Errors and validation

All controllers use `[ApiController]` conventions, including automatic HTTP 400 responses for invalid DTOs and JSON. The class request trims its name before Data Annotations validation and requires a positive JSON integer capacity. Errors use `application/problem+json`, with an HTTP status, title, request path, and `traceId`; validation errors also contain an `errors` object. Unhandled exceptions produce a generic 500 response in development and production. Exception messages, stack traces, and query strings are not returned. Unexpected-error logs record the exception type and matching request trace ID without exception-message values.

## Class endpoints

| Method | Path | Success |
| --- | --- | --- |
| POST | `/classes` | 201 with `id`, `name`, `totalSeats`, and a `Location` header |
| GET | `/classes` | 200 with all classes and seat summaries; `[]` when empty |
| GET | `/classes/{classId}` | 200 with the class and seat summary |

List/details include `id`, `name`, `totalSeats`, `heldSeats`, `confirmedSeats`, and `availableSeats`. Unknown IDs return 404; malformed input returns 400; duplicate trimmed class names return 409. See [docs/API.md](docs/API.md) for request/response examples and count semantics.

## Database model and migrations

The model has `AdmissionClasses` and `AdmissionHolds` with UUID keys, UTC timestamps, `VARCHAR(64)` student IDs, a required class foreign key, positive-capacity validation, a unique class name, and a partial unique index protecting `HELD`/`CONFIRMED` applications. See [docs/DATABASE.md](docs/DATABASE.md) for the ERD, mappings, indexes, and schema verification.

With your local PostgreSQL connection configured:

```powershell
dotnet tool restore
dotnet ef database update --project Backend/SchoolAdmission.Api
```

The local EF CLI is pinned to the same patch version as EF Core. Migrations are applied explicitly at this stage. Concurrency-safe allocation is scheduled for step 7.

## Verification

```powershell
dotnet build SchoolAdmission.slnx --configuration Release
dotnet test SchoolAdmission.slnx --configuration Release --no-build
cd Frontend
npm run build
npm run lint
```

The 21 foundation cases exercise the real ASP.NET Core pipeline using an in-process test host. Eleven schema cases and 23 class API cases use isolated PostgreSQL databases. They cover the migrated schema, class creation/reads, validation and error contracts, simultaneous duplicate names, and mixed-status seat summaries. Set `SCHEMA_TEST_CONNECTION_STRING` to a test account with `CREATE DATABASE` permission to run the database-backed cases; otherwise they are explicitly skipped. See [database test instructions](docs/DATABASE.md#real-postgresql-schema-checks). Admission lifecycle integration and seat-allocation concurrency tests remain scheduled for steps 13–15. Verified results are recorded in [docs/PROGRESS.md](docs/PROGRESS.md).

See [ASSUMPTIONS.md](ASSUMPTIONS.md) for implementation decisions and [AI_USAGE.md](AI_USAGE.md) for assistance disclosure.

## Continuous integration

GitHub Actions runs backend restore/build/tests and frontend install/lint/build
checks on pushes and pull requests. See [docs/CI.md](docs/CI.md) for GitHub Desktop
setup steps, required merge checks, current test limitations, and when to add deployment.
