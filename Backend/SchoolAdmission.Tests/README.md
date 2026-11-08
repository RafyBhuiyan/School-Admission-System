# Backend tests

This is an xUnit test project referencing `SchoolAdmission.Api`, not a second web application.

`Integration/FoundationTests.cs` contains 21 foundation test cases using `WebApplicationFactory` and the actual request pipeline. Test-only probe controllers are registered solely in the test host and are excluded from API documentation. No probe endpoints are included in the deployed API.

Coverage includes startup configuration errors, scoped PostgreSQL `AppDbContext` registration, allowed/disallowed CORS origins, automatic DTO and JSON validation, sanitized exceptions, fallback 404s, and development-only Swagger/OpenAPI.

The tests supply synthetic configuration and do not open a database connection. They run without PostgreSQL or production credentials. The PostgreSQL integration and real concurrency suite will be added in steps 13–15; these foundation tests do not prove seat-allocation correctness.

Run from the repository root:

```powershell
dotnet test SchoolAdmission.slnx --configuration Release
```
