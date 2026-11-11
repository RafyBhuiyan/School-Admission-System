# Repository audit

Local checks completed on 11 November 2026. The owner subsequently confirmed that a full browser restart restores the student correctly and reported pushing the reviewed changes. The owner is handling GitHub publication; public access has not been independently reverified.

| Check | Result and evidence |
| --- | --- |
| Required files | Root Compose file, env example, ignore rules, solution, frontend, README, assumptions and AI notes are present |
| Controller/service separation | Administrator queries, validation, expiry calculation and pagination moved to `AdminApplicationService`; 43 targeted backend tests passed with zero skips, including exact response-field and timestamp checks |
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

The source scan checks known secret values and common key patterns; it is not a guarantee against every possible secret format. Existing screenshots document synthetic demo workflows. Current language behavior is covered by isolated rendering tests; direct visual inspection, mobile viewport checks and hosted CI remain unverified by the agent. Full browser-process restart was subsequently confirmed by the owner. Automatic approval review rejected inspecting an unrelated browser tab because it could expose private content.

The owner reported pushing the audited revision. The later administrator-service refactor is a new local change to review and commit. The agent has not committed, pushed or submitted anything. A push alone does not establish a fresh-checkout run; the preserved Compose evidence remains a clean-source export.
