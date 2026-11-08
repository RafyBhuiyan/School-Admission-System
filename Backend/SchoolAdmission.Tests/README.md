# Backend tests

This is an xUnit test project referencing `SchoolAdmission.Api`, not a second web application.

`Integration/FoundationTests.cs` contains 21 foundation test cases using `WebApplicationFactory` and the actual request pipeline. Test-only probe controllers are registered solely in the test host and are excluded from API documentation. No probe endpoints are included in the deployed API.

Coverage includes startup configuration errors, scoped PostgreSQL `AppDbContext` registration, allowed/disallowed CORS origins, automatic DTO and JSON validation, sanitized exceptions, fallback 404s, and development-only Swagger/OpenAPI.

The foundation tests supply synthetic configuration and do not open a database connection. They run without PostgreSQL or production credentials.

`Integration/DatabaseSchemaTests.cs` adds 11 schema cases against real PostgreSQL. `Integration/ClassApiTests.cs` adds 23 HTTP cases for class creation, list/details, exact response fields, validation, 400/404/409 errors, duplicate-name races, and mixed-status seat summaries. Set `SCHEMA_TEST_CONNECTION_STRING` to a test-server account with `CREATE DATABASE` permission. Each test class gets a uniquely named database, applies the migration, and drops its own database afterward. Class API cases reset only that owned database between cases. Database-backed cases are explicitly skipped when the variable is absent. GitHub Actions provides a PostgreSQL 18 service and runs all 55 cases. See [database verification](../../docs/DATABASE.md#real-postgresql-schema-checks) for setup.

Admission lifecycle integration and concurrency tests will be added in steps 13–15; the current tests do not prove seat-allocation correctness.

Run from the repository root:

```powershell
dotnet test SchoolAdmission.slnx --configuration Release
```
