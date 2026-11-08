# Implementation progress

Baseline: user-supplied revised full implementation plan, section 16. Current completed implementation scope: steps 1–5; step 6 has not started.

## Step 1 — Repository and solution structure

- [ ] Verify public GitHub repository visibility. The user configured `origin` as `https://github.com/RafyBhuiyan/School-Admission-System.git`; `git ls-remote origin` succeeded with no commits. The separate public-visibility API check was declined, so visibility remains unverified.
- [x] Create the backend Web API and actual test project, linked in the root solution.
- [x] Retain the React 18+ frontend using Vite (React 19 + TypeScript).
- [x] Add `.gitignore` and `.env.example`.
- [x] Create initial root documentation.
- [x] Make the first meaningful commit. The user subsequently created `4d78915` (`Initialize school admission system project structure`) and `d5ea59d` (`add backend and frontend checks`), verified locally at the start of step 2. Public visibility/push status was not rechecked.

## Step 1 verification

Verified on 9 November 2026 (Asia/Dhaka):

| Check | Result |
| --- | --- |
| `dotnet restore SchoolAdmission.slnx` | Passed |
| `dotnet build SchoolAdmission.slnx --configuration Release --no-restore` | Passed; zero warnings and errors |
| `dotnet test SchoolAdmission.slnx --configuration Release --no-build --no-restore` | Discovery completed; no tests available, as expected for this empty scaffold |
| `npm ci` | Passed; lockfile installation completed |
| `npm run build` | Passed; TypeScript and Vite production build |
| `npm run lint` | Passed |
| API startup and `GET http://localhost:8080/openapi/v1.json` | HTTP 200; valid OpenAPI 3.1.1 document, zero admission endpoints |
| Git ignore checks | Dependencies, build output, local `.env`, and user-specific IDE files are ignored |

No application or concurrency tests exist yet. The existing API Dockerfile is an unverified starter; Docker packaging remains scheduled for steps 22–24.

## Step 2 — Application foundations

- [x] Configure PostgreSQL through `ConnectionStrings__DefaultConnection`, with startup validation and no committed credentials.
- [x] Add EF Core and the Npgsql PostgreSQL provider.
- [x] Register scoped `AppDbContext` and validated configuration through dependency injection.
- [x] Configure global exception handling and Problem Details with trace IDs and sanitized error details.
- [x] Configure automatic API-controller/DTO validation and sanitized JSON-input errors.
- [x] Configure explicit frontend CORS origins, required methods, and `X-Student-Id`.
- [x] Enable Swagger UI and the existing OpenAPI document in development.
- [x] Update environment examples, setup instructions, assumptions, AI disclosure, and CI documentation.

## Step 2 verification

Verified on 9 November 2026 (Asia/Dhaka):

| Check | Result |
| --- | --- |
| `dotnet restore SchoolAdmission.slnx` | Passed |
| `dotnet build SchoolAdmission.slnx --configuration Release --no-restore` | Passed; zero warnings and errors |
| `dotnet test SchoolAdmission.slnx --configuration Release --no-build --no-restore` | Passed: 21 cases, zero failures, zero skipped |
| Real-process startup using environment configuration | Passed on temporary local port 18080; test process stopped afterward |
| Development `/openapi/v1.json` and `/swagger/index.html` | Both HTTP 200 |
| Preflight using environment-configured `https://parents.example` | HTTP 204 with the matching allowed origin |
| Unknown route in the real process | HTTP 404 with `application/problem+json` |
| Whitespace/diff check | Passed |

The in-process foundation tests cover scoped PostgreSQL provider registration without opening a connection; invalid configuration; allowed and unlisted CORS origins; automatic DTO/JSON validation; sanitized 400/500 responses; 404 Problem Details; and development-only Swagger/OpenAPI. An initial sandboxed run was blocked by Windows Event Log permissions on error paths; the authorized run outside the sandbox passed all 21 cases.

No live PostgreSQL connection, schema, migrations, admission operations, or concurrency behavior has been tested at this step. The frontend was not changed. Step 2 changes are left for review/commit; no Git commit or push was attempted in this iteration.

## Step 3 — Database model

- [x] Implement `AdmissionClass` and `AdmissionHold` with UUID keys and lifecycle timestamps.
- [x] Configure the required relationship, positive-capacity constraint, unique class name, status check, and text/UTC mappings.
- [x] Configure both lookup indexes and partial uniqueness for `HELD`/`CONFIRMED` applications.
- [x] Generate `20261108192613_InitialAdmissionSchema`, its designer metadata, and model snapshot.
- [x] Pin the local EF CLI and align EF runtime/design/relational versions at 10.0.8.
- [x] Verify the migrated schema against the ERD on PostgreSQL 18, including rollback/reapply.
- [x] Add a PostgreSQL CI service, schema-test execution, and migration-snapshot checks.
- [x] Document the ERD, mappings, migration commands, and real schema test setup in `docs/DATABASE.md`.

## Step 3 verification

Verified on 9 November 2026 (Asia/Dhaka):

| Check | Result |
| --- | --- |
| Dependency and local EF CLI restore | Passed |
| Release solution build | Passed; zero warnings and errors |
| Full suite with real PostgreSQL 18 | 32 passed, zero failed, zero skipped (21 foundation + 11 schema cases) |
| Actual migrated ERD | All 13 domain columns match expected types, lengths, and nullability |
| Constraints/index behavior | Verified positive seats, unique class names, FK/restricted deletion, allowed statuses, active/confirmed uniqueness, history/reapplication, and 64-character student IDs |
| UTC mappings and leading zeros | Verified through database round trips |
| Overdue partial-index behavior | An overdue `HELD` blocks a duplicate until persisted as `EXPIRED`, as required for step 7 cleanup |
| Initial migration rollback/reapply | Passed in the fixture-owned database |
| Model snapshot comparison | No pending model changes |
| Idempotent SQL generation | Passed; generated review script in ignored `obj` output |

The schema tests created and removed uniquely named databases in a temporary local Docker PostgreSQL container. The container and its test volume were removed after verification. Results were saved to the ignored `Backend/SchoolAdmission.Tests/TestResults/step3-schema.trx`. The configured CI workflow has not been executed on GitHub during this iteration.

No admission endpoints, lifecycle services, or concurrency-safe allocation have been implemented at step 3. The frontend is unchanged. Step 2 commit `b735ee2` was present when work began; step 3 changes are left for review/commit, with no commit or push attempted.

## Step 4 — Initial assumptions

- [x] Record UUID formats, class/shift representation, and backend/UI student-ID validation differences.
- [x] Record per-class duplicate rules, cross-class applications, terminal states, history retention, and reapplication.
- [x] Record simulated payment and idempotent confirmation/cancellation responses.
- [x] Record database-clock expiry, the exact expiry boundary, overdue commit-before-409 behavior, automatic expiration, and capacity/ordering assumptions.
- [x] Record 403 ownership responses, public hold retrieval, and demo identity/admin security limitations.
- [x] Record localStorage continuity, automatic `/me/holds` retrieval, Forget/Change Student actions, and the privacy trade-off.
- [x] Record countdown/polling authority and existing schema, architecture, configuration, migration, and test-isolation decisions.
- [x] Add a reason for each decision and distinguish the implemented foundation/schema from upcoming workflows.

## Step 4 verification

Reviewed on 9 November 2026 (Asia/Dhaka) against the revised plan's sections 4–9, 13–14, and section 16 step 4, plus the current entity mappings, configuration, and application setup. All requested assumption topics are documented in `ASSUMPTIONS.md`. The whitespace/diff check passed. This documentation-only step required no new builds or test execution; the 32 passing cases above remain the step 3 verification result.

Step 3 commit `4743375` was present when work began. Step 4 documentation changes are left for review/commit; no commit or push was attempted.

## Step 5 — Class management APIs

- [x] Implement `POST /classes` with a request DTO, trimmed-name validation, positive JSON integer capacity, and a 201 response/Location header.
- [x] Return 409 for the named class uniqueness constraint, including simultaneous duplicate creates.
- [x] Implement `GET /classes` and `GET /classes/{classId}` through a shared `ClassService` query and response DTO.
- [x] Return the exact required seat-summary fields; an empty list is `[]`, unknown UUIDs return 404, and malformed UUIDs return 400.
- [x] Keep controllers thin, register the scoped service, and use `AppDbContext` directly.
- [x] Verify endpoint/JSON contracts, validation/errors, duplicate-name races, and mixed-status counts on PostgreSQL.
- [x] Document requests/responses and setup in `docs/API.md`; keep the short assumptions/AI notes current.

## Step 5 verification

Verified on 9 November 2026 (Asia/Dhaka):

| Check | Result |
| --- | --- |
| Release solution build | Passed; zero warnings and errors |
| Full backend suite on PostgreSQL 18 | 55 passed, zero failed, zero skipped: 21 foundation + 11 schema + 23 class API cases |
| Creation response and Location | 201 with exactly `id`, `name`, `totalSeats`; Location retrieves the new class |
| List/detail contract | 200 with `id`, `name`, `totalSeats`, `heldSeats`, `confirmedSeats`, `availableSeats`; empty list is `[]` |
| Invalid input | 400 for missing/blank/overlong names; missing, nonpositive, fractional, overlarge, or quoted capacities; wrong JSON types, malformed JSON, and malformed UUIDs |
| Name normalization and boundaries | Trimming precedes validation; a trimmed 200-character name and `int.MaxValue` capacity are accepted |
| Duplicate names | 409 for a trimmed duplicate; differently cased names remain distinct |
| Eight simultaneous same-name creates | Exactly one 201, seven 409s, and one persisted class |
| Mixed-status counts | A future `HELD` and a historical `CONFIRMED` occupy two of five seats; overdue `HELD`, `EXPIRED`, and `CANCELLED` do not occupy seats; list/details both report availability 3 |
| OpenAPI | Documents POST/GET `/classes` and GET `/classes/{classId}` |
| Migration snapshot | No pending model changes; no new migration needed |
| Whitespace/diff check | Passed |

The tests used fixture-owned databases in a temporary PostgreSQL container. Results were saved to the ignored `Backend/SchoolAdmission.Tests/TestResults/step5-classes.trx`. The existing CI PostgreSQL setup runs the added tests without new secrets; no hosted CI run was performed here.

Step 4 commit `7b0b953` was present at the start. On resuming step 5, the class APIs were already committed as `671d962`. After the ORM review, the handwritten seat-summary CTE/lateral query was replaced with EF Core LINQ. Only the scalar `clock_timestamp()` expression remains handwritten, composed into the same database statement. Filtering and ordering precede the response record projection so EF can translate them. Test logs confirm database-side counts, filtering, ordering, and availability calculation.

The Release build and all 55 PostgreSQL tests passed again after the refactor. The temporary test container and its volume were removed. The refactor and these documentation updates are left for review/commit; no commit or push was attempted. The frontend and hold lifecycle endpoints remain unchanged. The duplicate-class-name race does not verify capacity-safe seat allocation; that implementation is scheduled for step 7 and its concurrency suite for steps 13–15.

## Next planned step

Step 6: verify seat-summary query edge cases, database-time expiry, confirmed counts, nonnegative availability, and consistent list/detail calculation.
