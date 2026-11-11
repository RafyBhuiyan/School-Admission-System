# AI usage

I planned the project and used OpenAI Codex for scaffolding, EF mappings, backend/frontend implementation, tests and documentation drafts. That assistance covered substantial code. My role was to set the requirements, question design choices and request corrections. The source and recorded test results are the basis for reviewing the implementation.

Three decisions came from checking the initial suggestions against the assignment:

- **Browser persistence:** The draft avoided localStorage for privacy. I overruled that after checking section 1.5, which requires ongoing applications to remain visible after closing and reopening the browser. I kept the saved student ID, a shared-device notice and Forget This Device; application state is fetched from the API.
- **Time after a lock wait:** I replaced `NOW()` with `clock_timestamp()`. PostgreSQL fixes `NOW()` at transaction start, so a request that waits on `FOR UPDATE` could otherwise check expiry against stale time. The implementation reads time after acquiring the lock.
- **Student-ID validation:** The draft put a strict 17-digit regex on the API. I kept that in the student form but changed the backend rule to non-empty text up to 64 characters, stored as `VARCHAR(64)`. This accepts synthetic evaluator IDs such as `student-1` and preserves leading zeros.

I also questioned the handwritten seat-summary SQL. It was replaced with EF Core LINQ while retaining the database clock expression. That refactor initially failed six API tests because EF could not translate filtering/ordering after the record constructor; moving those operations before projection fixed it.

Later reviews led to practical changes: I challenged fixed probe URLs so they reuse listener configuration, requested quiet refreshes after noticing the page blink, and changed class creation from the original open demo endpoint to an API-enforced admin check. The admin code stays out of public frontend configuration.

The assisted verification also found defects: malformed list JSON caused a blank screen until runtime response validation was added, and polling only open details left history stale. Those fixes have regression coverage. The full and focused runs have different scopes; [verification notes](docs/PROGRESS.md) keep their results separate. Browser/mobile and hosted-CI gaps are recorded rather than treated as passes.
