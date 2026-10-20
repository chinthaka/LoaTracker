# LoaTracker

LoaTracker is a system for submitting and tracking Letter of Authority (LoA) requests. Advisers submit a request via the API; the service records an audit entry, queues the request for processing, and exposes a status endpoint keyed by tracking ID. A React page can upload the letter and show that status.

## Run on Windows

Install these and confirm each command prints a version:

| Tool | Used for | Check |
|---|---|---|
| .NET 10 SDK | API, functions, and the Aspire AppHost | `dotnet --version` |
| Node.js 22, with npm | React page | `node --version` |
| Docker Desktop, Linux containers | Azurite storage emulator | `docker info` |
| Azure Functions Core Tools 4 | Confirmation-email worker | `func --version` |

Docker Desktop must be running before the AppHost starts. Azurite uses ports `10000`, `10001`, and `10002`.

The confirmation email is sent by the functions worker. To send it, put a SendGrid key in `src/LoaTracker.Functions/local.settings.json`:

```json
"SendGridApiKey": "your-sendgrid-key"
```

Leave the value empty to run the page and the API without sending mail. The worker records a failure when it tries to send.

From the repository root, the folder that contains `LoaTracker.sln`:

```powershell
dotnet run --project src/LoaTracker.AppHost
```

In Visual Studio, set `LoaTracker.AppHost` as the startup project and start it.

The console prints the Aspire dashboard address, normally `https://localhost:17064`, and a login link. The browser opens the dashboard. When the `web` resource is running, open its endpoint. That page uploads a PDF, PNG, or JPEG and shows the tracking status. Aspire installs the React dependencies on the first start, so that start takes longer.

Stop the app with Ctrl+C in the same terminal.

Run the API tests with:

```powershell
dotnet test
```

## API Endpoints

All endpoints are grouped under `/loa`.

### Submit LoA Request

Submits a new LoA request. On success the request is persisted to the `LoaAudit` table, enqueued on `loa-request`, and a tracking ID is returned.

| | |
|---|---|
| **Method** | `POST` |
| **Path** | `/loa` |
| **Content-Type** | `application/json` |

#### Request body

| Field | Type | Required | Description |
|---|---|---|---|
| `AdviserName` | `string` | Yes | Name of the adviser submitting the request. |
| `MemeberEmail` | `string` |  | Email address of the member the LoA relates to. |
| `SchemeName` | `string` | No | Pension or scheme name. Defaults to `"(unspecified)"` in audit records when omitted. |

```json
{
  "AdviserName": "Jane Smith",
  "MemeberEmail": "member@example.com",
  "SchemeName": "TestRun"
}
```

#### Responses

**202 Accepted** — request accepted and queued.

```json
{
  "trackingId": "0192a3f4b5c6789d0e1f2a3b4c5d6e7f",
  "status": "Received"
}
```

| Field | Type | Description |
|---|---|---|
| `trackingId` | `string` | Version 7 GUID (32-character hex, no dashes). Use this value with `GET /loa/{trackingId}`. |
| `status` | `string` | Initial status; always `"Received"` on successful submission. |

The `Location` header points to `/loa/{trackingId}`.

**400 Bad Request** — validation failed (`AdviserName` or `MemeberEmail` missing/blank).

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "request": [
      "MemberEmail and AdviserName are required"
    ]
  }
}
```

**400 Bad Request** — unexpected server/storage error while processing the request.

```
Please try again later
```

---

### Get LoA Status

Returns the current status and full audit history for a previously submitted request.

| | |
|---|---|
| **Method** | `GET` |
| **Path** | `/loa/{trackingId}` |

#### Path parameters

| Parameter | Type | Required | Description |
|---|---|---|---|
| `trackingId` | `string` | Yes | Tracking ID returned from `POST /loa`. |

#### Responses

**200 OK** — tracking ID found.

```json
{
  "trackingId": "0192a3f4b5c6789d0e1f2a3b4c5d6e7f",
  "currentStatus": "Approved",
  "history": [
    {
      "partitionKey": "2025-01-10",
      "rowKey": "0192a3f4b5c6789d0e1f2a3b4c5d6e7f",
      "timestamp": "2025-01-10T14:30:00.0000000+00:00",
      "status": "Received",
      "adviserName": "Alice",
      "mememberEmail": "a@example.com",
      "schemeName": "SchemeX"
    },
    {
      "partitionKey": "2025-01-11",
      "rowKey": "0192a3f4b5c6789d0e1f2a3b4c5d6e7f",
      "timestamp": "2025-01-11T09:15:00.0000000+00:00",
      "status": "Approved",
      "adviserName": "Alice",
      "mememberEmail": "a@example.com",
      "schemeName": "SchemeX"
    }
  ]
}
```

| Field | Type | Description |
|---|---|---|
| `trackingId` | `string` | The requested tracking ID. |
| `currentStatus` | `string` | `Status` from the most recent audit event (last item in `history` after ordering by `timestamp`). |
| `history` | `LoaAuditEvent[]` | All audit events for this tracking ID, ordered oldest to newest. |

**`LoaAuditEvent` object** (each item in `history`):

| Field | Type | Description |
|---|---|---|
| `partitionKey` | `string` | Table partition key (UTC date, `yyyy-MM-dd`). |
| `rowKey` | `string` | Tracking ID. |
| `timestamp` | `string` (ISO 8601) | When the audit event was recorded. |
| `status` | `string` | Status at this point in the lifecycle (e.g. `"Received"`, `"UnderReview"`, `"Approved"`, `"Processed"`). |
| `adviserName` | `string` | Adviser name from the original request. |
| `mememberEmail` | `string` | Member email from the original request. |
| `schemeName` | `string` | Scheme name from the original request. |

**404 Not Found** — no audit events exist for the given `trackingId`. No response body.

---

### Update LoA Audit Status

Appends a new audit event for an existing LoA request. Unlike `POST /loa` (collection create) and `GET /loa/{trackingId}` (read), this endpoint uses **PATCH** to partially update the tracked resource by recording a new status in the audit trail.

| | |
|---|---|
| **Method** | `PATCH` |
| **Path** | `/loa/{trackingId}` |
| **Content-Type** | `application/json` |

#### Path parameters

| Parameter | Type | Required | Description |
|---|---|---|---|
| `trackingId` | `string` | Yes | Tracking ID returned from `POST /loa`. |

#### Request body

| Field | Type | Required | Description |
|---|---|---|---|
| `status` | `string` | Yes | New status to record (e.g. `"UnderReview"`, `"Approved"`, `"Rejected"`). |

```json
{
  "status": "UnderReview"
}
```

#### Responses

**200 OK** — status updated and new audit event appended.

```json
{
  "trackingId": "0192a3f4b5c6789d0e1f2a3b4c5d6e7f",
  "status": "UnderReview",
  "previousStatus": "Received",
  "updatedAt": "2025-06-17T10:30:00.0000000+00:00"
}
```

| Field | Type | Description |
|---|---|---|
| `trackingId` | `string` | The updated tracking ID. |
| `status` | `string` | The newly recorded status. |
| `previousStatus` | `string` | Status from the most recent audit event before this update. |
| `updatedAt` | `string` (ISO 8601) | UTC timestamp when the update was recorded. |

**400 Bad Request** — `status` is missing or blank.

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "status": [
      "Status is required"
    ]
  }
}
```

**404 Not Found** — no audit events exist for the given `trackingId`. No response body.

---

## Example workflow

```http
POST /loa
Content-Type: application/json

{
  "AdviserName": "Jane Smith",
  "MemeberEmail": "member@example.com",
  "SchemeName": "Acme Pension Scheme"
}
```

```http
GET /loa/0192a3f4b5c6789d0e1f2a3b4c5d6e7f
```

```http
PATCH /loa/0192a3f4b5c6789d0e1f2a3b4c5d6e7f
Content-Type: application/json

{
  "status": "UnderReview"
}
```

## Project layout

| Path | Description |
|---|---|
| `src/LoaTracker.AppHost` | Aspire app that starts the API, the React page, functions, and storage |
| `src/LoaTracker.Web` | React page for uploading a letter and reading its status |
| `src/` | Application projects (API, Functions, AppHost, etc.) |
| `tests/` | Unit and integration tests |
| `LoaTracker.sln` | Solution file |

## Next steps

Extend the service from a request log into a tracked Letter of Authority workflow. Do the audit-trail work before a portal, extra email types, or provider integrations. Those later features depend on every event being stored and returned in order.

### Make the audit trail trustworthy

The audit table is the record of the case.

- Give every event for one tracking id a unique partition key and row key. A status change on the same UTC day as submit currently reuses the submit key, so Table Storage rejects the write.
- Store the function's `Processed` event on the same tracking id. It is written under a different row key today, so `GET /loa/{trackingId}` never returns it.
- Keep the log append-only.
- Replace the free-text status with a fixed set and the moves that are allowed: `Received`, `AwaitingConsent`, `SentToProvider`, `AwaitingProvider`, `InformationReceived`, `Completed`, `Rejected`, `Cancelled`.
- Record who made each change.

### Add the workflow after intake

`ProcessLoaRequest` currently tells the member that a request arrived. The steps after that are consent and the chase.

- Store the member's consent as its own event: when they agreed, by what means, and a hash or blob reference for the signed authority.
- Put the signed document and any provider reply in Blob Storage. Keep only the reference on the audit row.
- After consent, enqueue work to send the authority to the scheme and to chase it.
- Raise a `ChaseDue` event from a timer when a case stays in `AwaitingProvider` past the agreed number of days. Each chase is another audit event.

### Let an adviser list their cases

`GET /loa/{trackingId}` serves a member who holds a link. An adviser needs their open cases, filtered by status and scheme.

- Keep the table as the event log.
- Add a query store keyed by adviser, updated on the same write path as each new event. The current table is partitioned by date and looked up by row key, so it cannot serve that list.
- Require Entra ID for submit, list, and status changes.
- Leave the member link as a limited read of their own case.

### After the trail and the status rules

- Send a notification for each transition, using templates and a configurable from-address.
- Add an API version that corrects `MemeberEmail` and `MememberEmail` while the current clients keep working.
- Report SLA figures from the audit log: time to consent, time with the provider, and number of chases.
