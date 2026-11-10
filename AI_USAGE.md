# AI usage

I planned the project and used OpenAI Codex to help with scaffolding, EF mappings and migrations, backend/frontend API code, tests, and documentation drafts. I made the design choices and reviewed the suggestions against the assignment.

During the initial review, I corrected three suggestions:

- **Browser persistence:** The AI-assisted draft suggested avoiding `localStorage` for privacy. I overruled that after checking assignment section 1.5: ongoing applications must remain visible after closing and reopening the browser. I kept persistence in the design, with a Forget This Device action and an explicit shared-device privacy trade-off.
- **Expiry after lock waits:** I changed the expiry design from `NOW()` to `clock_timestamp()`. PostgreSQL fixes `NOW()` at transaction start, so a request waiting on a `FOR UPDATE` lock could otherwise use stale time. The service reads fresh database time after acquiring the lock.
- **Student-ID validation:** The initial suggestion put a strict 17-digit regex on the backend too. I kept that requirement for the planned React input but changed the API rule to trimmed, non-empty text up to 64 characters, stored in `VARCHAR(64)`. This accepts evaluator IDs such as `student-1` and preserves leading zeros and case.

There were implementation corrections too. Adding EF's design package exposed a relational-package version conflict; the packages were aligned at 10.0.8. A generated schema test expected SQLSTATE `23503` for restricted deletion, but PostgreSQL returned `23001`; the expectation was corrected.

I also questioned the handwritten seat-summary SQL. It was replaced with EF Core LINQ, keeping only the database clock expression. The first refactor failed six API tests because EF could not translate filtering/ordering after the record constructor; moving those operations before the response projection fixed it.

For the countdown, I kept the backend authoritative: local zero disables actions and triggers a read, rather than declaring the hold expired. I used server time with monotonic elapsed time and deducted request transit time. Tests also check that a lost confirmation response cannot cause an automatic second write.

The history review caught another gap: polling the open detail panel left the list stale when details were closed. I added independent list refresh while any hold remains, with quiet background reads and cancellation on student changes.

During UI checks, I sent a JSON object where the class list should be an array and got a blank screen. The client was casting JSON to TypeScript types without checking it. I added runtime response validation and nine regression cases, then repeated the fault and recovery checks. I also corrected a stale success message that still said a seat was held after confirmation or cancellation.

For containers, I kept the API URL out of Vite's build variables: startup generates `config.json`, and the same image was checked with different values. Both pinned images built; the migration bundle and browser confirmation/cancellation worked against disposable PostgreSQL. All 115 frontend tests and lint passed inside the Linux build stage. The earlier backend suite passed 220 tests and was not rerun in full. All data was synthetic; test services were cleaned up. Compose persistence and full browser-process restart remain unverified. See [container verification](docs/CONTAINERS.md).
