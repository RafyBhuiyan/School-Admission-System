# Repository audit

Local checks completed on 11 November 2026. The repository owner will handle committing, pushing and GitHub visibility. Public access is still pending verification.

| Check | Result and evidence |
| --- | --- |
| Required files | Root Compose file, env example, ignore rules, solution, frontend, README, assumptions and AI notes are present |
| API contract | All 256 backend tests passed with zero skips, including path/method/header, response fields, validation, ownership and lifecycle checks |
| Frontend | 133 tests passed; production build passed; lint reported zero warnings/errors |
| Languages | Fixed English/Bangla dictionary, one header button, saved preference, localized dates/counts, unchanged identifiers; unit and real-component rendering checks passed |
| Migration snapshot | EF reports no model changes since the committed migration |
| PostgreSQL behavior | Full suite includes capacity, duplicates, lock-time expiry, rollback, restart, worker and separate-process contention cases |
| Clean startup and persistence | All 30 assertions passed in an isolated clean-source export; [generated report](evidence/repository-audit-compose.json) |
| Private configuration | `.env` and generated/test output are ignored; no tracked private env file |
| Source/history scan | 435 reachable Git blobs scanned at base revision `0829477`; no current private env credential, private-key, GitHub-token or AWS-access-key pattern matches |
| Identifiers | Numeric identifier matches were confined to synthetic fixtures and test seeding; reviewed source uses identifiers in headers, not URLs or logs |
| Assignment PDF | No PDF paths found in tracked files or reachable Git history |
| Commit history | 30 incremental commits at the reviewed revision, with separate backend, frontend, tests, containers and documentation changes |
| Public access | Anonymous repository page returned 404; the API returned 403. Visibility cannot be confirmed from those responses. Owner will publish and verify it |

The Compose check excludes `.git`, private `.env`, installed dependencies and build output, and builds from the exported source using pinned images and synthetic credentials. Cached image layers were available. This establishes clean-source startup locally, not a clone of the final published revision. The runner selects separate host ports and cleans up only its disposable project and volume.

The preserved backend TRX is local and ignored: `Backend/SchoolAdmission.Tests/TestResults/repository-audit.trx`. It records 256 passed, zero failed and zero skipped. Existing [two-process evidence](evidence/multi-instance-concurrency.txt) documents exact concurrency results.

The eight required API operations retain their routes and JSON names; [API documentation](API.md) lists the payloads. Admin class creation now requires the private shared key, following the revised user requirement. The extra administrator session/register operations do not replace the student contract.

The source scan checks known secret values and common key patterns; it is not a guarantee against every possible secret format. Existing screenshots document synthetic demo workflows. Current language behavior is covered by isolated rendering tests; direct visual inspection, mobile viewport checks, full browser-process restart and hosted CI remain unverified. Automatic approval review rejected inspecting an unrelated browser tab because it could expose private content.

Before submission, the owner needs to commit/push these changes, confirm the repository opens without signing in, and inspect the hosted CI results. Nothing was committed, pushed or submitted during this audit.
