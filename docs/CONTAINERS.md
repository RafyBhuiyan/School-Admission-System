# Container images

## Compose startup

Docker with a recent Compose plugin is sufficient to run the complete stack. Copy `.env.example` to `.env` if it does not already exist and set `POSTGRES_PASSWORD`. From the repository root:

```powershell
docker compose config --quiet
docker compose up --build --wait --wait-timeout 120
docker compose ps --all
```

The portal defaults to `http://localhost:5173`, and the API is published on localhost port 8080. PostgreSQL has no published host port. `FRONTEND_PORT` changes the portal's host port; CORS derives the corresponding localhost origin unless `FRONTEND_ORIGIN` is supplied. `API_HTTP_PORT` and `FRONTEND_HTTP_PORT` control internal listener/target ports, both defaulting to 8080. Health probes follow these listener settings. `API_BASE_URL` must remain reachable from the browser. A remote browser requires a reachable API address, an explicit allowed origin, and deliberate port-binding changes to the Compose file.

| Service | Startup requirement | Readiness |
| --- | --- | --- |
| `postgres` | None | TCP `pg_isready` on container localhost |
| `migrator` | PostgreSQL healthy | One successful `/app/migrate` run; exit 0 is expected |
| `backend` | PostgreSQL healthy and migrator completed successfully | `dotnet SchoolAdmission.Api.dll --healthcheck` |
| `frontend` | Backend healthy | Nginx `/healthz`, inherited from the image |

Only `backend` builds the API image. The migrator reuses that exact image with its entry point changed, without attempting a registry pull. `up --build` prepares the image before starting services, including when it is absent initially. PostgreSQL uses a pinned PostgreSQL 18 image and a project-scoped named volume mounted at `/var/lib/postgresql`, the [PostgreSQL 18 image's data-volume location](https://docs.docker.com/guides/postgresql/immediate-setup-and-data-persistence/).

Compose constructs `ConnectionStrings__DefaultConnection` with the internal `postgres` hostname, database, and username. It supplies the password through Npgsql's supported [PGPASSWORD setting](https://www.npgsql.org/doc/connection-string-parameters), so semicolons in a password are not interpreted as connection-string fields. Quote dollar-containing values in `.env` with single quotes. Local-only connection-string and listening-URL entries in `.env.example` are not passed through to these services. Shared hold/expiration settings are passed explicitly.

The API health mode starts no second server. It derives its base address from the configured listener and probes the relative database-backed `classes` route. `HealthCheck__BaseUrl` can override the internal address for custom listeners. The browser-facing `API_BASE_URL` is independent. The probe disables proxy/redirect handling and returns 0 only for a successful response; transport failures, timeouts, invalid configuration, and unsuccessful status codes return 1. Its two-second request timeout is below Compose's three-second process timeout. A listening API with an unavailable database is therefore not ready. The [URL audit](URL-CONFIGURATION.md) documents listener precedence and remaining defaults.

Startup uses [Compose health and successful-completion dependencies](https://docs.docker.com/compose/how-tos/startup-order/). These gate startup, not ongoing operation: a later database outage does not stop the frontend. The API reports unhealthy and can recover after the database returns. Restart policies keep the long-running services running; they do not replace database backups or automatically restart a merely unhealthy container.

Use `docker compose logs migrator backend` to diagnose startup failure. Changing `POSTGRES_*` does not reinitialize an existing database volume; retain its existing credentials or deliberately update the database account. `docker compose down` removes containers/network while preserving data. Adding `--volumes` deliberately deletes the project's database volume and is not part of ordinary shutdown or the persistence check.

## Build individual images

Build from the repository root with Docker in Linux-container mode:

```powershell
docker build --file Backend/SchoolAdmission.Api/Dockerfile --tag school-admission-api:local .
docker build --tag school-admission-frontend:local Frontend
```

The backend context is the repository root; the frontend context is `Frontend`. Ignore files exclude local output, dependencies, and environment files. Base-image digests are pinned in the Dockerfiles. Backend restore uses the committed NuGet lockfile with `--locked-mode`; frontend installation uses `npm ci`. Update image digests and lockfiles deliberately, then rebuild and verify. [NuGet locked restore](https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files#locking-dependencies) documents the dependency behavior.

| Image | Runtime | Container port | User |
| --- | --- | --- | --- |
| `school-admission-api:local` | ASP.NET Core 10; no SDK | 8080, all container interfaces | 1654 (`app`) |
| `school-admission-frontend:local` | Nginx serving the React production bundle; no Node.js | 8080 | `nginx` |

## Run against an existing database

Use a PostgreSQL database reachable from the API container. On Docker Desktop, `host.docker.internal` addresses the host; for another container on the same Docker network, use its service/container name and pass `--network` to both migration and API commands. Database `localhost` inside the API container means that container itself.

The following PowerShell example uses an existing host database. Replace its credentials and port before running:

```powershell
$containerConnection = "Host=host.docker.internal;Port=5432;Database=school_admission;Username=school_admission;Password=replace-with-your-local-password"
docker run --rm --env "ConnectionStrings__DefaultConnection=$containerConnection" --entrypoint /app/migrate school-admission-api:local
docker run --rm --name school-admission-api --publish 8080:8080 --env "ConnectionStrings__DefaultConnection=$containerConnection" --env Cors__AllowedOrigins__0=http://localhost:5173 --env HOLD_DURATION_SECONDS=600 school-admission-api:local
```

The API command stays in the foreground. Start the frontend in another terminal:

```powershell
docker run --rm --name school-admission-frontend --publish 5173:8080 --env API_BASE_URL=http://localhost:8080 school-admission-frontend:local
```

Open `http://localhost:5173`; the API is at `http://localhost:8080`. These example ports match the default CORS origin. Change `Cors__AllowedOrigins__0` when changing the browser's frontend origin. Backend startup does not apply migrations: run the bundle successfully first. Repeating it applies only pending migrations. The [EF migration bundle](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying#bundles) runs with the runtime already in the image, without installing an SDK or EF tool on the host.

The API image defaults `ASPNETCORE_HTTP_PORTS` to 8080; its probe derives the same setting. Avoid overriding it with a localhost-only `ASPNETCORE_URLS` binding, since that would prevent host/container-network access. Settings are supplied explicitly here; the root `.env` is not loaded automatically by `docker run`.

## Frontend configuration

The startup script writes `/usr/share/nginx/html/config.json` from `API_BASE_URL` when an override is supplied. Otherwise it retains the shared default from `Frontend/public/config.json`; the image no longer duplicates that URL. Recreating the container with a different value changes configuration without rebuilding JavaScript. Supply an absolute browser-reachable HTTP(S) URL, optionally with a path prefix. Docker-internal names such as `backend` generally cannot be resolved by the host browser.

The script JSON-escapes the value and rejects missing schemes, empty authorities, credentials, whitespace/control characters, queries, and fragments. The browser also validates the URL. Never put secrets in this public file. Nginx serves config with `Cache-Control: no-store`, index HTML with `no-cache`, and hashed assets with immutable caching. Unknown application routes fall back to the index; missing assets return 404.

`GET /healthz` checks the static server. Its health script uses `HEALTHCHECK_HOST` (default loopback) and `FRONTEND_HTTP_PORT`, the same port rendered into the Nginx template before startup. This does not check backend/database readiness. Compose separately gates frontend startup on the database-backed API probe described above.

## Verification

The [URL configuration audit](URL-CONFIGURATION.md) subsequently verified both probes on non-default internal ports, shared defaults, and explicit override/failure handling. All 57 targeted backend tests passed, including 24 new address-resolution cases. The existing full backend result below remains historical.

Compose startup was checked on 11 November 2026 in a unique project with a synthetic environment file; the existing root `.env` was untouched. Both the example settings and missing-password rejection were checked. The stack reached PostgreSQL/API/frontend healthy states after the migration service exited 0. An initially absent API image tag also built and started successfully, with identical image IDs for the API and migrator. A password containing semicolon, dollar, and quote characters survived the real container environment and authenticated correctly.

Stopping the test database made the explicit API health probe return 1 and Docker mark the API unhealthy. Restoring PostgreSQL recovered readiness automatically. The browser created a class, applied, confirmed, and displayed history/count updates through the stack; the [Compose preview](evidence/compose-portal.png) shows the result. All 33 backend foundation tests passed after the health-mode change. The unchanged frontend's earlier 115-test result was not rerun. [Startup verification output](evidence/compose-startup.txt) records the results. The isolated test project's containers, network, and volume were removed afterward. Full class/application persistence across a complete `down`/`up` cycle remains unverified.

On 11 November 2026, both images built and ran on Docker Desktop Linux/amd64. A disposable PostgreSQL 18 container shared an isolated network with the API. The bundle initialized the schema, and a second run was a no-op. The API answered through its published port; CORS accepted the container-served frontend origin. A real browser created a class, applied, confirmed one synthetic student's admission, and cancelled another's hold. The final counts were total 2, held 0, confirmed 1, available 1. The [container portal preview](evidence/container-portal.png) preserves the result.

All 115 frontend tests and lint passed inside the pinned Linux Node build stage. Production asset delivery, SPA fallback, missing-asset 404, health, cache headers, four runtime URL/escaping cases, and six rejected settings passed. Runtime metadata confirmed both non-root users and no SDK in the API image. [Actual verification output](evidence/container-images.txt) records the results and image IDs. CI now builds both images; a hosted CI run was not performed here.

The minimal ASP.NET image lacks the optional Kerberos library. Npgsql logged a GSSAPI probe warning but connected successfully with the demo's password authentication; Kerberos authentication is not configured or verified. Npgsql's [GSS encryption notes](https://www.npgsql.org/doc/release-notes/10.0.html) explain its preferred-mode fallback. The local Windows image-build check also reported unavailable NuGet vulnerability metadata; the container restore and publish completed successfully. The prior full backend suite passed 220 tests. Other CPU architectures, mobile viewport, and a full browser-process restart remain unverified. Image smoke-test containers/network were removed after verification; built local images were retained.
