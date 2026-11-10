# Frontend

React 19 and TypeScript with Vite. The portal identifies a student, remembers the selection, displays application history, creates admission classes, applies for seats, and confirms or cancels holds through the shared API client.

## Run and check

Use Node.js 22.12+:

```powershell
npm ci
npm run dev
```

Checks: `npm test`, `npm run lint`, and `npm run build`. Tests use Node's built-in runner with type stripping and TypeScript type checking; no additional test dependency is needed.

## Runtime API address

Edit `public/config.json` for development:

```json
{
  "API_BASE_URL": "http://localhost:8080"
}
```

The browser fetches this file with caching disabled before rendering the app. The URL must be an absolute HTTP(S) address reachable from the browser, with no credentials, query, or fragment. An API path prefix is allowed. Configure backend CORS for the frontend origin. Docker-internal hostnames are usually not reachable from the host browser.

Vite copies the file into `dist/config.json`. For an already-built deployment, replace that file and reload the browser; JavaScript does not need to be rebuilt. Exporting `API_BASE_URL` in a shell does not change local Vite configuration. The production container generates this file from its `API_BASE_URL` environment variable at startup, then Nginx serves it without caching. The file is public and must contain no secrets. See [container setup](../docs/CONTAINERS.md) for the build context, port mapping, and health check.

Missing or invalid configuration displays a startup error rather than silently selecting a different backend.

## Shared requests

`src/api/client.ts` provides typed methods for all eight backend operations. Components can call `useApi()` from `src/apiContext.ts`; `main.tsx` supplies the initialized client through `ApiProvider`.

Student-specific methods take the identifier explicitly and send it only in `X-Student-Id`. Identifiers are not logged or placed in URLs. The API client accepts synthetic values up to 64 characters; the identification form requires exactly 17 ASCII digits after trimming and preserves leading zeros. Class/hold path IDs must be UUIDs.

`ApiError` has a `kind`, optional HTTP `status`, validation `errors`, and `traceId`. Kinds distinguish HTTP, network, timeout, caller cancellation, unreadable responses, and local validation. HTTP 400/403/404/409 retain useful Problem Details titles; server failures and non-JSON errors use safe messages. Requests omit cookies, disable caching/redirects, and time out after 15 seconds. Pass `{ signal }` to cancel a request. Mutations are never automatically retried; refresh status after an uncertain outcome before deciding whether to retry.

Successful JSON is validated against the endpoint's response contract before reaching components. Wrong collection shapes, missing fields, invalid counts/statuses, or unreadable timestamps become safe response errors. Cancellation requires an empty 204 response. This prevents malformed server data from replacing UI state and causing a blank screen.

## Student continuity

The validated identifier is stored under `school-admission.student-id` in localStorage. On startup the session restores only a valid stored value and automatically calls `/me/holds`. The screen displays only its final four digits. Each history row opens the application details; the list can also be refreshed manually.

History includes all statuses and classes returned by the API, with original expiry timestamps retained for terminal applications. Only the identifier is saved locally; application data is fetched again on refresh or reopening. While any row is HELD, the list refreshes every 2.5 seconds after the previous read completes, even with details closed. Background reads keep the rows and controls steady. Hidden pages pause polling and fetch fresh history on return. Failures retain the last rows with a visible error and retry while a hold remains; terminal-only and empty lists stop polling.

Change student clears the old selection/history and saved value, then presents an empty form. Selecting another student persists the new value and loads their applications. Forget this device clears storage, the selection, history, and request errors. Both actions abort pending loads; generation checks ignore late responses even if the transport ignores cancellation. Storage events synchronize other open tabs. Effect cleanup also cancels loads, including React Strict Mode's development restart.

If browser storage is blocked, temporary access still works with a visible notice. A failed clear advises the user to clear the site's data before leaving a shared device. A request failure retains the valid student selection and offers a retry. The screen explains the shared-device persistence trade-off; this identifier is not authentication.

## Classes and applying

Class cards show total, held, confirmed, and available seats. The catalog refreshes every four seconds after the previous read completes, pauses polling when the page is hidden, and cleans up requests/timers on unmount. Failures retain the last snapshot, show an error, and allow retry. Class creation requires a trimmed 1–200-character name and a positive integer capacity up to 2147483647. Server validation and duplicate-name conflicts stay visible without clearing the form.

Apply requires a selected student and available capacity. Existing active/confirmed applications disable reapplication; the backend remains authoritative. Duplicate clicks are blocked while a write is pending. Successful creation refreshes classes; successful Apply refreshes both classes and the selected student's history. A 409 also triggers fresh reads. Changing students aborts an in-flight Apply and suppresses its late response. Mutations are never retried automatically.

The demo administration form is unauthenticated, matching the backend. Do not treat it as production administrator authorization.

## Application details

Applying opens details automatically; View application opens an existing entry. The countdown uses the difference between database `serverTime` and `expiresAt`, anchored to `performance.now()`. The entire request round trip is deducted conservatively. Changing the computer's wall clock cannot extend payment time.

While the selected application is HELD, details poll every 2.5 seconds after each completed read. Hidden pages pause timers and refresh on return. At estimated zero, actions are disabled and the backend is queried; only its response can report EXPIRED. Confirmation simulates payment without charging money. Confirmation/cancellation disable competing actions, reconcile details, and refresh history and class counts. Lost responses trigger reads, never an automatic mutation retry. Terminal statuses, failed reads, and ownership failures cannot enable invalid actions. Selection changes and unmounting abort requests and discard late results.

The 115 tests include 39 API/configuration, 19 student-session, 21 class-catalog, 23 detail/countdown, and 13 history/continuity cases. Build and lint also pass. Browser checks against the real .NET API and disposable PostgreSQL 18 cover class creation, applying, confirmation/cancellation, expiry, counts, refresh/tab-reopen restoration, switching/forgetting, and error recovery. A test-only proxy measured polling and injected network, server, and malformed-data faults; the latter exposed a response-validation gap that was fixed and rechecked. See [UI verification and evidence](../docs/UI-VERIFICATION.md) for results and limits. Services were cleaned up and runtime configuration restored. Full browser-process restart and mobile viewport checks remain unverified.
