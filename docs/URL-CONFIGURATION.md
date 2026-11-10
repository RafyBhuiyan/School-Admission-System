# URL configuration audit

Runtime application requests use configured base addresses. The browser's `API_BASE_URL` identifies the API from the user's machine; an internal health probe must reach the process in its own container. A public hostname or reverse proxy could target another instance, so it is not reused for local readiness.

| Location | Change |
| --- | --- |
| Backend `Program.cs` | The probe sets `HttpClient.BaseAddress` from API listener configuration and requests relative `classes`; its fixed absolute URL was removed. |
| Backend `Common/ApiHealthProbe.cs` | Resolves configured Kestrel endpoints, URLs, and HTTP/HTTPS ports. Wildcard bindings become loopback destinations. `HealthCheck__BaseUrl` can explicitly override the internal base address, including a path prefix. |
| Frontend Docker health check | Uses `HEALTHCHECK_HOST` and the same `FRONTEND_HTTP_PORT` rendered into the Nginx server template. It has no fixed destination URL or duplicated listener port in the request. |
| Frontend image API default | Removed the duplicate `API_BASE_URL` image default. When no environment override is supplied, startup retains the value already in `public/config.json`; an explicit override is JSON-escaped into that file. |
| Development launch profile | Removed the embedded database connection string, which could override an exported environment setting. Local database configuration now follows the documented environment-variable setup. |

The API resolver uses `HealthCheck:BaseUrl` first, then the configured listeners. Explicit Kestrel endpoints normally take precedence over hosting URLs/ports; `preferHostingUrls` is respected. HTTP is preferred when both schemes are configured. There is no fallback port in the resolver: absent/invalid settings fail the probe. Dynamically assigned or programmatically configured listeners need an explicit internal probe address. See [Kestrel endpoint configuration](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-10.0).

## Intentional addresses remaining

- `.env.example` and `docker-compose.yml` contain configurable local defaults, browser URL/origin fallbacks, and loopback host-port bindings. `API_HTTP_PORT` and `FRONTEND_HTTP_PORT` each drive their service's listener and published target port. The API host port remains 8080.
- `Frontend/public/config.json` contains the editable development/standalone browser API default; the shared API client already reads this configuration for all eight operations.
- `Backend/SchoolAdmission.Api/appsettings.json` contains an overridable development CORS origin. `Properties/launchSettings.json` contains development listener profiles, which also supply the API probe's listener configuration when that profile is used.
- The API Dockerfile's `design_time` connection is a model-generation placeholder used to build the migration bundle, not a runtime database destination.
- Test fixtures, documentation examples, and earlier evidence retain their explicit addresses. JSON schema and SVG namespace URLs are metadata identifiers, not application request destinations. Relative API paths such as `classes` remain part of the endpoint contract.

## Verification

On 11 November 2026, 24 resolver regression cases and 33 foundation cases passed (57 total). Coverage includes URL/port precedence, wildcard/IPv6 bindings, uppercase schemes, public/internal separation, explicit base-path overrides, and invalid/missing settings.

An isolated Compose stack was tested with internal API port 19080 and Nginx port 19081, while the public API stayed on host port 8080. Both services became healthy; direct probes succeeded. An unreachable explicit API override returned 1. Published API/CORS, frontend config, index, SPA fallback, and missing-asset behavior passed. The first template startup exposed a non-root directory-permission issue; ownership was corrected and the stack rerun successfully. Standalone checks confirmed the shared baked API default, runtime URL overrides/escaping, and invalid-port rejection. See [actual results](evidence/url-configuration.txt).

The previous full backend run covered 220 cases. The project now contains 244 cases after the new resolver tests; that full suite was not rerun for this change. Frontend application code was unchanged, and its prior 115-case result was not rerun. The existing root `.env` was not modified. Only the isolated test project's containers/network/volume were removed afterward.
