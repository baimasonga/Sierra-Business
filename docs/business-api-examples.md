# Rokela business API examples

The authenticated specification is available at `/openapi/v1.json`. The application uses the existing account login/session cookie. POST actions also require the antiforgery cookie and token sent in `RequestVerificationToken`. See `scripts/verify-web.ps1` for an executable login, token acquisition and transaction example against an isolated database. Do not embed credentials in integrations.

Operation IDs are UUIDs identifying one intent. Preserve the same ID when retrying an uncertain result; use a new ID for a new operation. Refresh the document register before deciding whether an uncertain post needs retrying. Services enforce fresh permissions, branch access, period locks, validation and idempotency.

Examples below use generated IDs and existing catalogue product IDs. Do not send the placeholder IDs to a production company.

## Read-only reports

```http
GET /api/v1/statements/compare?statement=Profit%20and%20loss&from=2026-10-01&to=2026-10-31&comparisonFrom=2026-09-01&comparisonTo=2026-09-30
GET /api/v1/reports/stock-ageing?asAt=2026-10-09&slowDays=90
GET /api/v1/statements/ratios?from=2026-10-01&to=2026-10-31
GET /api/v1/finance/advances
```

Use the exact statement names exposed by the application/specification. Dates are ISO `yyyy-MM-dd`; amounts and quantities are JSON numbers.

## Post a sale

```json
{
  "operationId": "cbd5161e-7720-4b07-bd07-629a23917f47",
  "kind": 1,
  "party": "Walk-in",
  "businessDate": "2026-10-09",
  "lines": [{ "productId": "eb970c26-da30-42b2-b462-6d1e6a4c2c11", "quantity": 1, "unitPrice": 55, "conversion": 1 }],
  "payment": 0,
  "location": "MAIN"
}
```

Send this body to `POST /api/v1/documents`. The current wire enums are numeric: Sale is 1 and Cash is 0. A credit sale uses a null payment and a due date. Tracked products require the appropriate serial/batch/expiry data. The returned document ID links to the document register. A reversal uses its own operation ID, the original document ID and a nonempty reason; permissions and approvals still apply.

## Live invalidation

Connect an authenticated SignalR client to `/notifications/workspace`. `WorkspaceReady` confirms branch-group membership. `WorkspaceChanged` means fetch the relevant current report/register; it contains no document data and is not a durable event stream. Reconnect using normal SignalR negotiation and refetch after reconnect. Expired or invalid sessions are closed/rejected.

Validation and permission failures use the API's ProblemDetails responses. Never infer success from a notification; the command response and persisted document register are authoritative.
