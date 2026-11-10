# Integration coverage

The integration suite runs the actual ASP.NET Core pipeline and EF Core provider against PostgreSQL 18. Database fixtures create uniquely named databases, apply migrations, and drop only their own databases. Foundation tests run without a database; database tests are explicitly skipped if `SCHEMA_TEST_CONNECTION_STRING` is missing. CI supplies the connection and PostgreSQL service.

## Endpoint coverage

| Operation | Verified responses | Primary test files |
| --- | --- | --- |
| POST `/classes` | 201, 400, 409 | `ClassApiTests`, `AdmissionWorkflowTests` |
| GET `/classes` | 200, including empty array | `ClassApiTests`, `AdmissionWorkflowTests` |
| GET `/classes/{classId}` | 200, 400, 404 | `ClassApiTests`, `AdmissionWorkflowTests` |
| POST `/classes/{classId}/holds` | 201, 400, 404, 409 | `HoldCreationTests`, `AdmissionWorkflowTests` |
| GET `/holds/{holdId}` | 200, 400, 404 | `HoldRetrievalTests`, `AdmissionWorkflowTests` |
| POST `/holds/{holdId}/confirm` | 200, 400, 403, 404, 409 | `HoldConfirmationTests`, `AdmissionWorkflowTests` |
| DELETE `/holds/{holdId}` | 204 with empty body, 400, 403, 404, 409 | `HoldCancellationTests`, `AdmissionWorkflowTests` |
| GET `/me/holds` | 200, including empty array, 400 | `StudentApplicationTests`, `AdmissionWorkflowTests` |

## Cross-cutting checks

- Synthetic non-empty IDs, leading zeros, case-sensitive matching, trimmed 64-character IDs, and rejection of missing/blank/overlong/multiple header values.
- Duplicate active/confirmed applications per class, independent classes, retained terminal history, and reapplication after cancellation/expiry.
- Exact expiry boundaries, fresh time after row-lock waits, committed EXPIRED state before overdue confirmation/cancellation returns 409, and ownership checks before mutation.
- Stable confirmation/cancellation retries and capacity derived from persisted statuses.
- A complete HTTP workflow with the real worker enabled, covering all eight operations for three identifier formats.
- A one-second hold that the worker expires without subsequent HTTP requests, followed by retrieval, rejected terminal-state mutations, and reapplication.
- PostgreSQL triggers deliberately rejecting allocation inserts and confirmation/cancellation updates. Responses must be sanitized 500 Problem Details; failed transactions must leave prior state intact. Removing the trigger permits a successful retry. Triggers exist only in the fixture-owned database and are removed during cleanup.

Existing race tests cover the last seat, duplicate applications, queued confirmations/cancellations, skipped locks, and competing lifecycle mutations. They use in-process hosts with separate database connections. Broader load and separate-process deployment tests remain outstanding.

For commands and database-account setup, see [database verification](DATABASE.md#real-postgresql-schema-checks). [Test project notes](../Backend/SchoolAdmission.Tests/README.md) describe individual test groups; [verification results](PROGRESS.md) record the latest local run.
