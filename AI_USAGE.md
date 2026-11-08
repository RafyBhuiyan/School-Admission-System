# AI usage

I used OpenAI Codex for scaffolding, EF mappings and migrations, class/hold-creation endpoints, backend tests, and documentation drafts. I supplied the revised plan and worked through it one step at a time.

During the initial plan review, I corrected three suggestions:

- **Browser persistence:** The AI-assisted draft suggested avoiding `localStorage` for privacy. I overruled that after checking assignment section 1.5: ongoing applications must remain visible after closing and reopening the browser. I kept persistence in the plan, with a Forget This Device action and an explicit shared-device privacy trade-off.
- **Expiry after lock waits:** I changed the expiry design from `NOW()` to `clock_timestamp()`. PostgreSQL fixes `NOW()` at transaction start, so a request waiting on a `FOR UPDATE` lock could otherwise confirm a hold using stale time. The service must read fresh database time after acquiring the lock.
- **Student-ID validation:** The initial suggestion put a strict 17-digit regex on the backend too. I kept that check in React but changed the API rule to trimmed, non-empty text up to 64 characters, stored in `VARCHAR(64)`. This avoids rejecting evaluator test IDs such as `student-1`; leading zeros and case are preserved.

There were concrete implementation corrections too. Adding EF's design package exposed a relational-package version conflict; the packages were aligned at 10.0.8. A generated schema test expected foreign-key SQLSTATE `23503` for restricted deletion, but PostgreSQL 18 returned `23001`; the expectation was corrected.

I also questioned the handwritten seat-summary SQL. It was replaced with EF Core LINQ, keeping only the PostgreSQL clock expression. The first refactor failed six API tests because EF could not translate filtering/ordering after the record constructor; moving those operations before the response projection fixed it.

At step 7, Codex ran the Release build and PostgreSQL 18 suite: 108 passed, none failed or skipped. The concurrency tests verify requests are blocked on a real class-row lock before releasing them. Two test hosts competing for one seat produced one 201 and one 409; eight same-student requests created one hold. Another test expires the old hold while creation waits, then checks cleanup and the new duration. These are in-process test hosts, not a separate-process deployment test. Retrieval, confirmation/cancellation, the worker, and UI remain planned. Detailed results are in [PROGRESS.md](docs/PROGRESS.md).
