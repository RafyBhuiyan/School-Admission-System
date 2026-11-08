# School Admission System

A school admission application for parents and administrators, built in the order defined by the revised implementation plan. Admission will be first-come, first-served with PostgreSQL transactions protecting limited seats.

## Current status

Step 1 scaffolding is implemented and verified locally. The first commit and public-repository visibility verification remain pending; see [docs/PROGRESS.md](docs/PROGRESS.md). Admission endpoints, persistence, and parent/admin workflows are not implemented yet. Docker Compose will be added in steps 22–24.

## Technology

- .NET 10 controller-based Web API (satisfies .NET 8+).
- xUnit test project referencing the API.
- React 19, TypeScript, and Vite (satisfies React 18+).
- PostgreSQL and EF Core are planned for steps 2–3.

## Structure

```text
SchoolAdmission.slnx
Backend/
  SchoolAdmission.Api/
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
dotnet run --project Backend/SchoolAdmission.Api --launch-profile http
```

The API listens on `http://localhost:8080`. Its development OpenAPI document is at `http://localhost:8080/openapi/v1.json`. The root path returns 404 until endpoints are added.

In a separate terminal:

```powershell
cd Frontend
npm ci
npm run dev
```

Open `http://localhost:5173` for the React starter. It currently has no API integration.

## Configuration

`.env.example` defines safe placeholders and the planned configuration names. Neither .NET nor Vite automatically loads a repository-root `.env` file in this scaffold. The launch profile sets the API URL; database and frontend API configuration will be wired in the corresponding steps.

| Variable | Intended purpose | Status |
| --- | --- | --- |
| `ASPNETCORE_URLS` | API listening address; default local URL is `http://localhost:8080` | Supported by ASP.NET Core |
| `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD` | Local PostgreSQL container settings | Planned for Docker Compose |
| `ConnectionStrings__DefaultConnection` | Backend PostgreSQL connection string | Planned for step 2 |
| `HOLD_DURATION_SECONDS` | Hold lifetime, default 600 seconds | Planned for lifecycle implementation |
| `API_BASE_URL` | Browser-reachable backend address | Planned for frontend integration/runtime configuration |

## Verification

```powershell
dotnet build SchoolAdmission.slnx --configuration Release
dotnet test SchoolAdmission.slnx --configuration Release --no-build
cd Frontend
npm run build
npm run lint
```

The test project is an empty scaffold: test discovery is not evidence that admission behavior works. Meaningful PostgreSQL integration and concurrency tests are scheduled for steps 13–15. Verified results are recorded in [docs/PROGRESS.md](docs/PROGRESS.md).

See [ASSUMPTIONS.md](ASSUMPTIONS.md) for implementation decisions and [AI_USAGE.md](AI_USAGE.md) for assistance disclosure.
