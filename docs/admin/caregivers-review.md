# Admin caregiver review

Admin review routes live under `/api/v1/admin/caregivers/...` and require the **`CaregiverReviewAdmin`** policy: authenticated, `access_type` = `Normal`, `account_type` = `SuperAdmin` or `SupportAdmin`. `ContentAdmin` receives `403` on these operational review routes (ContentAdmin stays limited to CMS plus caregiver lookups).

Certificate file download lives here too — it is the **only** way to read a certificate scan (private storage, never served under `/files`).

## List caregivers (paged)

```http
GET /api/v1/admin/caregivers?page=1&pageSize=10&status=2&type=1
Authorization: Bearer {{accessToken}}
```

Query parameters (all optional):

- `page` (default 1), `pageSize` (default 10, max 100)
- `status` — `1` Onboarding, `2` PendingReview, `3` NeedsCorrection, `4` Active, `5` Suspended, `6` Rejected
- `type` — `1` Medical, `2` Companion

Response (reviewer name/phone are joined read-only from the Identity users table):

```json
{
  "page": 1,
  "pageSize": 10,
  "totalCount": 1,
  "items": [
    {
      "caregiverId": "guid",
      "userId": "guid",
      "type": 2,
      "status": 2,
      "availability": 2,
      "arabicFullName": "…",
      "englishFullName": "…",
      "phoneNumber": "+20…",
      "updatedOnUtc": "2026-08-31T10:00:00Z"
    }
  ]
}
```

A fresh database returns `200` with `totalCount: 0` and an empty `items`. Invalid paging returns `400 Api.Validation.Failed`.

## Caregiver detail

```http
GET /api/v1/admin/caregivers/{caregiverId}
```

Returns `CaregiverAdminDetailResponse` — the full caregiver profile plus a **booking-cancellation summary** used for suspension decisions:

```json
{
  "profile": { "…": "full CaregiverProfileResponse — profile, selections, pricing, schedule, certificates with verification status and reviewReason, status, statusReason" },
  "cancellations": {
    "cancellationCount": 3,
    "recent": [
      {
        "bookingId": "0198e3f0-3333-7777-8888-000000000003",
        "bookingDate": "2026-06-08",
        "startTime": "10:00",
        "endTime": "12:00",
        "shiftType": 1,
        "cancelledOnUtc": "2026-06-06T09:15:00Z",
        "reason": "ظرف عائلي طارئ"
      }
    ]
  }
}
```

- `profile` — same shape as before this envelope was introduced (nothing removed).
- `cancellations` — durable caregiver-side **Cancel** facts for bookings cancelled **after confirmation** (`CancelledByCaregiver`): `cancellationCount` is the lifetime total and `recent` holds the **5 most recent**, newest first, with the time window, shift type, cancellation timestamp and stored reason (`null` when none was given). Caregiver **Reject** facts and family cancellations are not counted. Refunded incidents remain counted because the summary is fact-based, not derived from the booking's current refund/status state.
- `cancellations` is `null` when the cancellation lookup fails — that failure never fails the detail request.
- A caregiver with no cancellations returns `cancellationCount: 0` and an empty `recent`.
- `404 Caregivers.Onboarding.CaregiverNotFound` — no such caregiver.

## Application review actions

All return `204 No Content` on success. Reason-bearing actions take `{ "reason": "…" }` (required, ≤ 500 chars).

| Action | Route | Valid from | Effect |
|---|---|---|---|
| Approve | `POST /{caregiverId}/approve` | PendingReview | → `Active` (Activation readiness enforced: for Medical, mandatory certificates Verified + unexpired) |
| Reject application | `POST /{caregiverId}/reject` | PendingReview | → `Rejected` (terminal), reason stored |
| Request correction | `POST /{caregiverId}/request-correction` | PendingReview | → `NeedsCorrection`, reason shown to caregiver; he fixes and resubmits |
| Suspend | `POST /{caregiverId}/suspend` | Active | → `Suspended`, reason stored |
| Reactivate | `POST /{caregiverId}/reactivate` | Suspended | → `Active` (activation readiness re-checked) |

Errors:

- `404 Caregivers.Onboarding.CaregiverNotFound` — caregiver does not exist.
- `409 Caregivers.Onboarding.InvalidState` — action not allowed from the current status (e.g. approving an Onboarding caregiver, suspending a PendingReview one), or activation/readiness failed (e.g. mandatory certificates not Verified).
- `400 Api.Validation.Failed` — missing/overlong reason.

Example:

```http
POST /api/v1/admin/caregivers/{caregiverId}/request-correction
Content-Type: application/json

{ "reason": "Please add your service areas and re-upload a clear practice license." }
```

## Certificate verification

Certificate-specific actions, keyed by both ids:

| Action | Route | Valid from | Effect |
|---|---|---|---|
| Verify | `POST /{caregiverId}/certificates/{certificateId}/verify` | Pending | Certificate → Verified |
| Reject certificate | `POST /{caregiverId}/certificates/{certificateId}/reject` | Pending | Certificate → Rejected; mandatory rejection forces the caregiver `Unavailable`; reason required |
| Revoke certificate | `POST /{caregiverId}/certificates/{certificateId}/revoke` | Verified | Certificate → Revoked; revoking a mandatory certificate forces `Unavailable` and suspends an Active caregiver; reason required |

Reject/revoke body: `{ "reason": "…" }`. Same 404/409 error codes as above (`CertificateNotFound` 404 when the certificate id is unknown to the caregiver).

## Download a certificate file

```http
GET /api/v1/admin/caregivers/{caregiverId}/certificates/{certificateId}/file
```

Streams the stored scan from private storage (PDF/JPEG/PNG/WebP) as a file download, `CaregiverReviewAdmin` required (SuperAdmin or SupportAdmin; ContentAdmin receives `403`). The response `Content-Type` matches the stored file; the download filename is `certificate-{certificateId}.<ext>`.

- `404 Caregivers.Onboarding.NotFound` when the caregiver id is unknown; `Caregivers.Onboarding.CertificateNotFound` when the caregiver exists but the certificate id is unknown.
- `404 Storage.File.NotFound` — the stored file is missing.

Certificate metadata (type, expiry, status, review reason) is in the detail response; never construct or share file URLs outside this endpoint. Caregiver self-service onboarding is documented under `docs/app/caregivers/`.

## Review notes (append-only)

Administrative notes about a caregiver, for the reviewing team's own record keeping. Both routes use the same **`CaregiverReviewAdmin`** policy as the rest of this group (Normal SuperAdmin or SupportAdmin; `ContentAdmin` is `403`), and neither route takes a reason, a status or an assignment.

```http
GET  /api/v1/admin/caregivers/{caregiverId}/notes?page=1&pageSize=20
POST /api/v1/admin/caregivers/{caregiverId}/notes
```

### List notes

`GET` returns one caregiver's notes, **newest first** by creation instant and then by note id descending as a stable tiebreak, with `totalCount` covering the caregiver's whole note set:

```json
{
  "page": 1,
  "pageSize": 20,
  "totalCount": 1,
  "items": [
    {
      "noteId": "guid",
      "caregiverId": "guid",
      "text": "Called about the late certificate upload; will re-submit a clear license.",
      "authorUserId": "guid",
      "createdOnUtc": "2026-10-21T10:00:00Z"
    }
  ]
}
```

- Paging follows the house read convention and is **clamped, never rejected**: `page` is raised to 1, `pageSize` is capped at 100 with a floor of 1, and `pageSize` 0 or absent means the default **20**. The normalized values are echoed back.
- A caregiver that exists but has never been annotated returns `200` with `items: []` and `totalCount: 0`; reading past the last page is an empty page too.
- The projection carries only `noteId`, `caregiverId`, `text`, `authorUserId` and `createdOnUtc`. No caregiver identity document, phone number, email address, medical fact or caregiver name is ever returned by this route.

### Add a note

```http
POST /api/v1/admin/caregivers/{caregiverId}/notes
Content-Type: application/json

{ "text": "Reviewer note…" }
```

Returns **`201 Created`** with the created note body (`noteId`, `caregiverId`, `text`, `authorUserId`, `createdOnUtc`) so the caller can read back exactly what was stored.

- `text` is required, trimmed, and **1–2000 characters**; the trimmed text is stored verbatim (no masking, no truncation, no markdown). Exactly 2000 characters is accepted, 2001 is not.
- **Attribution is server-owned.** The author is the authenticated actor's user id and the instant is the server's UTC clock (the same clock the other Caregivers writes use). A client-supplied `authorUserId`, `createdOnUtc` or `noteId` in the body is ignored.
- **No side effects.** Appending a note changes no caregiver status, certificate, rating, payout or review decision, and emits no notification, email or domain event. It reads nothing beyond the caregiver's existence.

### Append-only guarantee

There is no `PUT`, `PATCH` or `DELETE` route for a note: a stored note's text can never change and a note can never be removed through the API. There is deliberately **no `409` and no `5xx` path reachable from request input** — nothing here is concurrent, mutable or stateful. Note reads and writes are recorded for the caregiver only; they are not caregiver-facing and are not shown on any Family, Care Home or app surface.

### Errors

- `404 Caregivers.ReviewNote.CaregiverNotFound` — no such caregiver. Checked **before** the text, so a bad text never masks a missing caregiver.
- `400 Caregivers.ReviewNote.InvalidText` — `text` missing, null, empty or whitespace-only, or longer than 2000 characters after trimming.
- `401` — missing, expired or non-`Normal` token; `403` — authenticated without SuperAdmin/SupportAdmin.

Both codes are mapped locally by the caregiver review controller rather than through the shared problem-details mapper, so a missing caregiver is a real `404`. Responses keep the usual `code` and `traceId` extensions for client keying. A Postman collection with authored examples is at `docs/postman/admins/Sanad.Admin.CaregiverNotes.postman_collection.json`.
