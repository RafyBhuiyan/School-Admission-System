# Continuous integration

The workflow in
`.github/workflows/ci.yml` runs on every branch push and pull request. It can
also be started manually from GitHub's Actions tab once it is on the default branch.

## Checks

| Required check name | Commands |
| --- | --- |
| Backend checks | .NET/tool restore, Release build, migration snapshot check, and all backend tests including PostgreSQL hold-creation races |
| Frontend checks | `npm ci`, `npm run lint`, `npm test`, and `npm run build` |
| Container builds | Validate Compose configuration, build both production images through Compose, and verify clean startup plus database persistence across container recreation |

The backend uses the SDK specified in `global.json`. The frontend uses Node.js 22
and the committed npm lockfile. The jobs run independently on Linux runners and
need no repository secrets. The backend job provisions a disposable PostgreSQL 18
service using synthetic test credentials.

The backend test project contains 33 foundation cases for configuration,
dependency injection, error/validation responses, CORS, and Swagger, plus 11 real
PostgreSQL schema cases, 23 class API cases, 17 seat-summary query cases, 30 hold-creation cases, 16 hold-retrieval cases, 6 expiration cases, 28 confirmation cases, 26 cancellation cases, 19 student-application cases, 7 workflow/failure cases, 2 capacity-burst cases, 2 real-process restart cases, and 24 health-probe configuration cases, 4 administrator-access cases, 6 administrator-register cases, and 2 separate-process allocation cases (256 total). The shared PostgreSQL fixture gives
each test class its own uniquely named database using `SCHEMA_TEST_CONNECTION_STRING`.
The local EF tool also checks that
the model matches its committed migration snapshot. The suite checks concurrent
hold creation, concurrent confirmation/cancellation, expiration contention, and persistence across real API-process restarts. Restart tests launch the built API on an ephemeral loopback port using the installed .NET runtime, then terminate only their own processes. Broader load testing remains outstanding. The container job runs `scripts/Test-ComposePersistence.ps1` with PowerShell: it builds from a clean source export, creates synthetic data, performs ordinary `down`/`up`, and compares complete database rows and API responses. Cleanup removes only its disposable project and volume after the test. [Container verification](CONTAINERS.md#clean-startup-and-volume-persistence) records the successful local run; this workflow change has not been run on GitHub here.
The frontend runs 120 API/configuration, student-session, class-catalog, application-detail, and history/continuity tests with Node's built-in test runner and TypeScript type checking. Tests cover response contracts, validation, polling, creation/application flows, server-synchronized countdowns, confirmation/cancellation, expiry, saved identity, history restoration, request races, storage failures, and a loopback HTTP server; no external service or new test dependency is required. Automated browser workflow coverage remains to be added; [manual UI verification](UI-VERIFICATION.md) is recorded separately.

## Enable the workflow

1. In GitHub Desktop, review `.github/workflows/ci.yml`, this guide, and the README change.
2. Commit with summary `ci: add backend and frontend checks`.
3. Click **Push origin** (or **Publish repository** if the repository has not been published).
4. Open the repository on GitHub, select **Actions**, and open the **CI** run for this commit.
5. Confirm that **Backend checks**, **Frontend checks**, and **Container builds** pass. If a job fails,
   open its failed step, fix the reported error locally, and commit and push the fix.

## Require checks before merging

After the first successful run, configure branch protection in GitHub:

1. Open **Settings > Branches > Add classic branch protection rule**.
2. Set the branch name pattern to `master`, the current primary branch. If you
   rename it, use the new name instead.
3. Enable **Require a pull request before merging**. For solo development, leave
   required approving reviews disabled so another reviewer is not needed.
4. Enable **Require status checks to pass before merging** and select
   **Backend checks**, **Frontend checks**, and **Container builds**.
5. Enable **Require branches to be up to date before merging**.
6. Enable **Do not allow bypassing the above settings** if you want the rule to
   apply to administrators too, then save the rule.

Branch protection availability depends on repository visibility and GitHub plan.
See GitHub's [branch protection guide](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-protected-branches/managing-a-branch-protection-rule).

For each change, create a branch in GitHub Desktop,
commit and publish it, and open a pull request into `master`. Wait for all three
checks to pass before merging. CI detects failures; branch protection enforces
the merge requirement.

## Add deployment later

Add continuous deployment after a hosting target and deployment configuration are ready. CI is configured to validate Compose, build images, and run automated startup/persistence checks. Images are built for validation and are not pushed to a registry.

The current workflow runs validation only. Its first hosted run is verified in
GitHub after pushing; local checks do not execute GitHub Actions itself.
