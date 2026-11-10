# AI usage

I planned the project and used OpenAI Codex to help with scaffolding, EF mappings and migrations, backend/frontend API code, tests, and documentation drafts. I made the design choices and reviewed the suggestions against the assignment.

During the initial review, I corrected three suggestions:

- **Browser persistence:** The AI-assisted draft suggested avoiding `localStorage` for privacy. I overruled that after checking assignment section 1.5: ongoing applications must remain visible after closing and reopening the browser. I kept persistence in the design, with a Forget This Device action and an explicit shared-device privacy trade-off.
- **Expiry after lock waits:** I changed the expiry design from `NOW()` to `clock_timestamp()`. PostgreSQL fixes `NOW()` at transaction start, so a request waiting on a `FOR UPDATE` lock could otherwise use stale time. The service reads fresh database time after acquiring the lock.
- **Student-ID validation:** The initial suggestion put a strict 17-digit regex on the backend too. I kept that requirement for the planned React input but changed the API rule to trimmed, non-empty text up to 64 characters, stored in `VARCHAR(64)`. This accepts evaluator IDs such as `student-1` and preserves leading zeros and case.

There were implementation corrections too. Adding EF's design package exposed a relational-package version conflict; the packages were aligned at 10.0.8. A generated schema test expected SQLSTATE `23503` for restricted deletion, but PostgreSQL returned `23001`; the expectation was corrected.

I also questioned the handwritten seat-summary SQL. It was replaced with EF Core LINQ, keeping only the database clock expression. The first refactor failed six API tests because EF could not translate filtering/ordering after the record constructor; moving those operations before the response projection fixed it.

The latest frontend checks passed 49 tests plus build/lint. Session tests check that switching/forgetting cancels requests and ignores late responses, preventing old applications from appearing for a new student. Browser checks with a synthetic API fixture verified validation, restoration after reload/tab reopen, and forgetting across tabs. They did not use the live .NET API. The previous backend run passed 220 tests; it was unchanged and not rerun. Container deployment remains unverified. See [verification notes](docs/PROGRESS.md).
