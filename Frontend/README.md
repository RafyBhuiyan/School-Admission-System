# Frontend

React 19, TypeScript and Vite. This folder contains the student portal and the admin panel. Start with the [project README](../README.md) for Docker setup.

## Run locally

Use Node.js 22.12+ and run from this folder:

```powershell
npm ci
npm run dev
```

Set the browser-reachable API address in `public/config.json`:

```json
{
  "API_BASE_URL": "http://localhost:8080"
}
```

Configure backend CORS for the frontend origin. The address must be absolute HTTP(S), without credentials, a query or a fragment; an API path prefix is allowed. Missing or invalid configuration displays a startup error.

| Running with | Where the API address comes from |
| --- | --- |
| Local Vite | `public/config.json` |
| Production container | `API_BASE_URL`, written into `config.json` at startup |
| Existing static build | Replace `dist/config.json` and reload |

The browser fetches configuration without caching. Exporting `API_BASE_URL` alone does not change local Vite configuration. Keep this public file free of secrets. Internal listener settings and probes are covered in the [Docker guide](../docs/CONTAINERS.md).

## Portal behavior

| Area | Behavior |
| --- | --- |
| Student identity | Exactly 17 ASCII digits, preserving leading zeros; only the identifier is saved in localStorage |
| Application history | Restored from the API on return; includes held, confirmed, expired and cancelled rows |
| Classes | Name/shift search, availability filter, seat counts and Apply |
| Admin | Code prompt, class creation, paginated register with class/status filters |
| Application details | Server-based countdown, simulated confirmation and cancellation |

**Change student** and **Forget this device** clear the selection/history and cancel pending loads. Storage events synchronize open tabs. If storage is blocked, temporary access remains available with a notice. The student identifier is the demo identity mechanism, not authentication.

Admin credentials stay in memory until the panel closes or the page reloads. `X-Admin-Key` is sent only to protected operations. The backend enforces access; the frontend visibility check alone does not authorize a request.

## Refresh and request rules

- Classes and the open admin register refresh every four seconds. Active details/history refresh every 2.5 seconds. History stops polling when no HELD rows remain.
- Polling pauses while the page is hidden. Background reads retain existing content and controls; errors offer recovery without clearing the last successful snapshot.
- The countdown uses database `serverTime` and `expiresAt`, anchored to `performance.now()`, with the request round trip deducted. At estimated zero, actions stop and the API is queried for the final status.
- Mutations are not automatically retried. An uncertain confirmation/cancellation outcome triggers a read. Student/selection changes abort requests and ignore late responses.

## Code map

| Location | Responsibility |
| --- | --- |
| `src/api/` | Configured API client, response validation, request errors and contracts |
| `src/apiContext.ts` | Shared initialized client through `useApi()` |
| `src/studentSession.ts` | Saved identity and history continuity |
| `src/classCatalog.ts` | Catalog reads, writes and refresh state |
| `src/holdDetails.ts` | Detail lifecycle, countdown and mutations |
| `src/AdminPanel.tsx` | Admin sign-in, class form and filtered register |
| `src/App.tsx`, `src/App.css` | Page composition and styling |

The API accepts synthetic student IDs up to 64 characters; the student form enforces the assignment's 17-digit format. Identifiers travel in `X-Student-Id`, never URLs. The client validates successful JSON before updating state, uses a 15-second timeout, and exposes HTTP/network/timeout/cancellation/response errors through `ApiError`.

## Checks

```powershell
npm test
npm run lint
npm run build
```

The latest local run passed **120 tests**, lint and the production build. Tests use Node's built-in runner and TypeScript checking. They cover API contracts, configuration, saved identity, polling, races, application lifecycle and error recovery.

[Browser verification](../docs/UI-VERIFICATION.md) records earlier real-API workflows and fault injection. The current admin-panel/filter changes have automated coverage but have not been visually rechecked. Mobile viewport and full browser-process restart checks remain outstanding.
