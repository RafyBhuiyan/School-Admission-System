# School Admission System

A school admission application with class management, temporary seat holds, and PostgreSQL transactions protecting limited seats. The backend supports creating classes, viewing seat availability, applying for a hold, and checking its status.

Built with ASP.NET Core (.NET 10), EF Core, PostgreSQL, and a React/TypeScript frontend. A background worker automatically expires overdue holds. The frontend is currently a Vite starter; confirmation, cancellation, and My Applications are not implemented yet.

## Run locally

Install the .NET 10 SDK, PostgreSQL 18, and Node.js 22.12+ with npm. Create a local PostgreSQL database and account, then run from the repository root:

```powershell
dotnet restore SchoolAdmission.slnx
dotnet tool restore
$env:ConnectionStrings__DefaultConnection = "Host=localhost;Port=5432;Database=school_admission;Username=school_admission;Password=replace-with-your-local-password"
dotnet ef database update --project Backend/SchoolAdmission.Api
dotnet run --project Backend/SchoolAdmission.Api --launch-profile http
```

The API runs at `http://localhost:8080`. Development documentation is available at [Swagger UI](http://localhost:8080/swagger/index.html). Migrations are applied explicitly; API startup does not apply them.

Start the frontend in another terminal:

```powershell
cd Frontend
npm ci
npm run dev
```

Open `http://localhost:5173`. API integration and Docker Compose startup are not available yet.

## Configuration

Export backend settings as environment variables. The repository-root `.env` file is not loaded automatically; `.env.example` documents the settings.

| Variable | Purpose |
| --- | --- |
| `ConnectionStrings__DefaultConnection` | Required PostgreSQL connection string |
| `HOLD_DURATION_SECONDS` | Positive integer hold lifetime; defaults to 600 seconds |
| `Expiration__IntervalMilliseconds` | Expiration polling interval, 100–60000 ms; defaults to 1000 |
| `Expiration__BatchSize` | Maximum holds processed per cycle, 1–1000; defaults to 100 |
| `Cors__AllowedOrigins__0` | Allowed frontend origin; defaults to `http://localhost:5173` |
| `ASPNETCORE_URLS` | Listening address when running without the local launch profile |

Configuration is validated at startup. Additional allowed origins use `Cors__AllowedOrigins__1`, `Cors__AllowedOrigins__2`, and so on.

## API

| Method | Path | Purpose |
| --- | --- | --- |
| POST | `/classes` | Create a class and capacity |
| GET | `/classes` | List classes and seat availability |
| GET | `/classes/{classId}` | Retrieve one class and its seat summary |
| POST | `/classes/{classId}/holds` | Create a temporary hold; requires `X-Student-Id` |
| GET | `/holds/{holdId}` | Retrieve current status, expiry, and database server time |

Hold creation accepts one trimmed student identifier of up to 64 characters. A database row lock protects capacity and duplicate checks. Retrieval reports an overdue hold as `EXPIRED` immediately, even if its stored status is still `HELD`.

This demo has no authentication. The student header identifies a student, and hold status is publicly accessible by UUID. Use synthetic identifiers during development. See [API reference](docs/API.md) for response formats and errors, and [assumptions](ASSUMPTIONS.md) for design choices.

## Tests

Use a separate PostgreSQL test account with `CREATE DATABASE` permission:

```powershell
$env:SCHEMA_TEST_CONNECTION_STRING = "Host=localhost;Port=5432;Database=postgres;Username=schema_test;Password=replace-with-your-test-password"
dotnet test SchoolAdmission.slnx --configuration Release
```

Tests create and remove their own databases. Without this variable, database tests are skipped. Coverage includes validation, migrations, expiry boundaries, duplicate applications, and competing requests for the last seat.

GitHub Actions runs backend checks and frontend lint/build on pushes and pull requests. Further details: [test coverage](Backend/SchoolAdmission.Tests/README.md), [database setup](docs/DATABASE.md), [CI](docs/CI.md), [implementation status](docs/PROGRESS.md), and [AI usage](AI_USAGE.md).
