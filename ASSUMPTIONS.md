# Assumptions

These are the choices I made where the assignment left room for interpretation.

- I model each grade/shift as a separate class, with UUIDs for classes and holds. Class names are trimmed, case-sensitive and unique; duplicates return 409.
- I keep the 17-digit check in the student form. The API accepts one trimmed, non-empty `X-Student-Id` up to 64 characters, including `student-1`. `VARCHAR(64)` preserves leading zeros and case. Identity comes from the header, not the request body.
- I apply the duplicate rule per class: one active hold or confirmed admission per student. Different classes are allowed. Expired/cancelled rows stay in history; reapplying creates a new row. History is newest first, then UUID for ties.
- I treat payment as a simulation. Repeating confirmation returns the same 200 result; repeating an owned cancellation returns 204. Confirmed admissions cannot be cancelled.
- I chose 403 when a valid student ID tries to confirm/cancel someone else's hold, 404 for an unknown hold, and 409 for an expired or confirmed cancellation. Ownership is checked first. An overdue HELD row must be committed as EXPIRED before returning 409.
- I use `clock_timestamp()` after lock acquisition. Equality with the deadline means expired. The default hold lasts 600 seconds. The worker processes up to 100 overdue rows each second; both limits are configurable, and locked rows are skipped.
- I interpret first-come, first-served as database allocation-lock order. Active holds plus confirmed rows occupy seats. Allocation cleanup and insertion commit together; failed allocation rolls cleanup back. Read summaries still exclude overdue holds immediately.
- I save only the student ID in localStorage and fetch application history again on return. This meets browser continuity but leaves an identifier on shared devices, so the portal masks it and offers Forget/Change actions. The countdown is a display aid; database time decides whether confirmation is allowed.
- I require a private shared admin key for class creation and the application register, following the revised requirement. It stays in browser memory until close/reload. Student headers and public hold-by-UUID reads remain the demo identity model; they do not verify a person's identity.
- I use scoped EF Core services directly, without repository wrappers. Compose runs one migration job before the API. Frontend runtime configuration separates browser URLs from internal listener addresses. Requests time out after 15 seconds; writes are never automatically retried because a lost response may follow a committed transaction.
