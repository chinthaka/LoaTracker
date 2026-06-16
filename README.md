# LoaTracker

LoaTracker is an API for submitting and tracking Letter of Authority (LoA) requests. Advisers submit a request via the API; the service records an audit entry, queues the request for processing, and exposes a status endpoint keyed by tracking ID.

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
  "AdviserName": "Chinthaka Kumarasiri",
  "MemeberEmail": "chinthaka@icloud.com",
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
| `src/` | Application projects (API, Functions, AppHost, etc.) |
| `tests/` | Unit and integration tests |
| `LoaTracker.sln` | Solution file |