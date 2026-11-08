# AI usage

## Tools and scope

OpenAI Codex assisted with the repository and solution scaffolding for step 1, following the user-supplied revised implementation plan. The user requested sequential implementation, starting with step 1, and a completion report after each step.

Assistance included inspecting the existing starters, converting the incorrectly scaffolded test Web API into an xUnit project, organizing the solution, preparing environment placeholders, and writing initial documentation.

## Verification and corrections

- Inspection found that `SchoolAdmission.Tests` was a second Web API. It was converted to a test project referencing the API.
- The PowerShell npm launcher failed by looking for a missing user-level npm installation. Verification used the working Node installation's `npm.cmd`; no global installation was changed.
- Actual build, lint, startup, and test-discovery results are recorded in `docs/PROGRESS.md`. There are no business tests at step 1, and no claim of concurrency correctness.
- Human review of the generated implementation and design decisions is pending; this document does not claim independent human verification.

Update this file as subsequent steps are implemented, including mistakes and the checks used to correct them.

## Step 2 assistance

Codex implemented the application foundations: PostgreSQL/EF Core configuration and scoped context registration, startup configuration validation, automatic API-controller validation, Problem Details and sanitized global exceptions, explicit frontend CORS, and development-only Swagger UI. It added 21 foundation test cases and updated the environment examples and documentation.

The implementation was checked against the official ASP.NET Core, Npgsql, and Swagger UI documentation. The first build exposed an incorrect JSON-writing overload and non-constant environment names in test attributes; both were corrected before tests were run successfully. An initial sandboxed test run could not write Windows Event Log entries on error paths. The authorized run outside the sandbox passed all 21 cases. No database connection or concurrency claim is made. Human review remains pending.
