# Assumptions

These are my decisions for the revised plan. The schema is in place; the admission endpoints and browser workflows are still to be implemented.

- I treat each grade/shift as a separate class. I chose UUIDs rather than the sample `c_123`/`h_789` prefixes. Class names are case-sensitive, limited to 200 characters, and trimmed before validation; duplicates return 409.
- I kept the 17-digit check in React. The API will accept a trimmed, non-empty `X-Student-Id` up to 64 characters, including synthetic IDs such as `student-1`. I store it as `VARCHAR(64)` to preserve leading zeros and case.
- I apply the duplicate rule per class: one active hold or confirmed admission per student. Different classes are allowed. Expired/cancelled applications stay in history, with their expiry timestamps; reapplying creates a new row. Confirmed admissions cannot be cancelled.
- I treat the payment button as successful payment. Repeating confirmation returns the same confirmed result (200); repeating an owned cancellation returns 204. Neither retry creates another admission.
- I chose 403 when a valid student ID tries to confirm/cancel another student's hold, and 404 for an unknown hold. Ownership is checked before changing state.
- I chose 409 for cancellation of an expired or confirmed application. If a stored `HELD` row is already overdue, confirm/cancel must save `EXPIRED` and commit before returning 409. It must not become `CANCELLED` or have its expiry update rolled back.
- I use PostgreSQL `clock_timestamp()` after acquiring locks for expiry checks and expiry calculation. Equality with `expires_at` means expired. The default hold is 600 seconds, configurable through `HOLD_DURATION_SECONDS`; a worker will persist expiry without a student request.
- I interpret first-come, first-served as database allocation-lock order, not browser-click time. Counts come from active holds plus confirmed rows. Overdue holds stop consuming capacity immediately, and stale `HELD` rows must be expired before the partial unique index allows reapplication.
- I will remember the validated student ID in `localStorage`, restore it on startup, and fetch `/me/holds`. This meets browser-reopen continuity but leaves a sensitive ID on shared devices. Forget This Device clears it; Change Student replaces it and reloads applications. IDs must stay out of logs and URLs.
- I treat the countdown as a display aid: use server time and polling, disable payment at estimated expiry, and fetch the backend status. The browser clock cannot authorize payment.
- I leave `POST /classes` unauthenticated for this demo and preserve header-free `GET /holds/{holdId}`. The header and UUIDs do not authenticate anyone; knowing an ID can expose status or allow impersonation. Real deployment would need authentication.
- I chose services using scoped `AppDbContext` directly, without repository wrappers. Schema/configuration details and verification results are in [DATABASE.md](docs/DATABASE.md) and [PROGRESS.md](docs/PROGRESS.md).
