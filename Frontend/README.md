# Frontend

React 19 and TypeScript with Vite. The shared API client is initialized at startup; the UI is still a starter without admission workflows.

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

Student-specific methods take the identifier explicitly and send it only in `X-Student-Id`. No identifier is retained globally, logged, or placed in a URL. The client trims IDs and accepts synthetic values up to 64 characters; the future identification form will enforce the 17-digit UI requirement. Class/hold path IDs must be UUIDs.

`ApiError` has a `kind`, optional HTTP `status`, validation `errors`, and `traceId`. Kinds distinguish HTTP, network, timeout, caller cancellation, unreadable responses, and local validation. HTTP 400/403/404/409 retain useful Problem Details titles; server failures and non-JSON errors use safe messages. Requests omit cookies, disable caching/redirects, and time out after 15 seconds. Pass `{ signal }` to cancel a request. Mutations are never automatically retried; refresh status after an uncertain outcome before deciding whether to retry.

The 30 tests cover request contracts, error handling, cancellation, configuration validation, and real HTTP requests after runtime URL changes. Browser interactions and live .NET/backend integration are not covered by these client tests.
