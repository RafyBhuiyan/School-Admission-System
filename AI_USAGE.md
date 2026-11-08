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
