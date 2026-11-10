# Frontend

React 19 and TypeScript with Vite. The portal identifies a student, remembers the selection, and displays their application history through the shared API client. Class administration, applying, and payment/cancellation controls remain unimplemented.

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

Vite copies the file into `dist/config.json`. For an already-built deployment, replace that file and reload the browser; JavaScript does not need to be rebuilt. Serve it without long-lived caching. Exporting `API_BASE_URL` in a shell does not rewrite the JSON file or change an existing bundle. Container-startup generation is not implemented yet. The file is public and must contain no secrets.

Missing or invalid configuration displays a startup error rather than silently selecting a different backend.

## Shared requests

`src/api/client.ts` provides typed methods for all eight backend operations. Components can call `useApi()` from `src/apiContext.ts`; `main.tsx` supplies the initialized client through `ApiProvider`.

Student-specific methods take the identifier explicitly and send it only in `X-Student-Id`. Identifiers are not logged or placed in URLs. The API client accepts synthetic values up to 64 characters; the identification form requires exactly 17 ASCII digits after trimming and preserves leading zeros. Class/hold path IDs must be UUIDs.

`ApiError` has a `kind`, optional HTTP `status`, validation `errors`, and `traceId`. Kinds distinguish HTTP, network, timeout, caller cancellation, unreadable responses, and local validation. HTTP 400/403/404/409 retain useful Problem Details titles; server failures and non-JSON errors use safe messages. Requests omit cookies, disable caching/redirects, and time out after 15 seconds. Pass `{ signal }` to cancel a request. Mutations are never automatically retried; refresh status after an uncertain outcome before deciding whether to retry.

## Student continuity

The validated identifier is stored under `school-admission.student-id` in localStorage. On startup the session restores only a valid stored value and automatically calls `/me/holds`. The screen displays only its final four digits. Applications are read-only here and can be refreshed manually.

Change student clears the old selection/history and saved value, then presents an empty form. Selecting another student persists the new value and loads their applications. Forget this device clears storage, the selection, history, and request errors. Both actions abort pending loads; generation checks ignore late responses even if the transport ignores cancellation. Storage events synchronize other open tabs. Effect cleanup also cancels loads, including React Strict Mode's development restart.

If browser storage is blocked, temporary access still works with a visible notice. A failed clear advises the user to clear the site's data before leaving a shared device. A request failure retains the valid student selection and offers a retry. The screen explains the shared-device persistence trade-off; this identifier is not authentication.

The 49 tests include 30 API/configuration checks and 19 session/validation checks covering restoration, switching, forgetting, races, storage failures, and retries. Browser checks used a local synthetic API fixture to verify invalid input, automatic loading after reload/tab reopen, changing students, and forgetting across two tabs. The [preview](../docs/evidence/student-portal.png) contains only synthetic data and a masked identifier. Live .NET/browser integration and a full browser-process restart were not exercised in this frontend verification.
