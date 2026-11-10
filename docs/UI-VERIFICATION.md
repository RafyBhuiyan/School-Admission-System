# UI verification

Checked locally on 11 November 2026 using the in-app browser, React/Vite, the real ASP.NET API, and a fresh PostgreSQL 18 database. Holds lasted 30 seconds. All class and application records were created through the UI; only database creation and migrations used terminal commands.

| Check | Observed result |
| --- | --- |
| Browser workflows | Created classes, applied, opened details, confirmed simulated payment, cancelled, and viewed retained history without Postman or direct data changes. |
| Validation and empty states | Short student IDs and empty class fields showed inline errors. Empty class/history lists showed explanatory text. A duplicate class name showed the backend conflict message. |
| Counts without reload | Applying increased held occupancy; confirming moved it to confirmed; cancelling and expiring released capacity. Full and duplicate applications were disabled. |
| Countdown and expiry | The timer decreased to zero without reload. Payment and cancellation were disabled while status verification was pending. The server response then changed details to EXPIRED and removed those actions. |
| Polling | In the recorded steady active-hold window, detail gaps were 2.506–2.518 seconds, history gaps 2.512–2.532 seconds, and class gaps 4.013–4.021 seconds after the preceding response completed. See [actual timing samples](evidence/ui-polling.txt). |
| Saved student | Refresh and closing/reopening the portal tab restored the leading-zero student and loaded history without identifier re-entry. The proxy recorded the automatic `/me/holds` requests. |
| Forget this device | The form returned with no student/history selected. Refresh and tab reopening kept it forgotten; unit tests also check removal of the storage key. |
| Network and server failures | Injected disconnections and HTTP 503 responses produced readable messages and retry controls in details, history, and classes. Last-loaded rows remained visible. Raw synthetic internal error text was not displayed. Restoring connectivity and retrying recovered the page. |
| Malformed successful response | An unexpected JSON object initially caused a blank screen in the class list. After adding response-contract validation, the same fault displayed an unreadable-response message with retries. Recovery, creation, confirmation, cancellation, and history were rechecked against the real API. |
| Invalid startup configuration | An empty runtime config displayed “Unable to start” with recovery guidance. Restoring valid configuration and refreshing recovered the portal. |

Two fixes resulted from these checks: JSON response shapes are now validated before reaching components, and the application-created message no longer claims a seat is still held after its status changes.

Faults came from a temporary loopback proxy, not production endpoints. For the disabled-at-zero observation, it delayed the real EXPIRED detail response by ten seconds; the backend clock, worker, and stored application state were unchanged. [Pending expiry](evidence/ui-expiry-checking.png), [server failure](evidence/ui-service-error.png), and [malformed response after the fix](evidence/ui-malformed-response.png) preserve the observed screens. All data was synthetic.

All 115 frontend tests, lint, and the production build passed. The nine additional API cases cover valid/invalid response contracts, nested field failures, and empty-response rules without retrying mutations. The unchanged backend's preceding full suite passed 220 tests; it was not rerun here.

This was manual browser verification, not a committed end-to-end browser suite. HTTP 403/404, timeouts, storage failures, and request races have automated client/controller coverage rather than browser fault-injection coverage in this run. Tab reopen was checked; a full browser-process restart, mobile viewport, and container deployment remain unverified. Test services and the disposable database were removed, default runtime configuration was restored, and the synthetic student was forgotten.
