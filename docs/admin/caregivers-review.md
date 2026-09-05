# Admin caregiver review

Admin review routes live under `/api/v1/admin/caregivers/...` and require the **`CaregiversAdmin`** policy: authenticated, `access_type` = `Normal`, `account_type` = `SuperAdmin` or `ContentAdmin`.

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
- `cancellations` — every booking this caregiver cancelled **after confirmation** (`CancelledByCaregiver`): `cancellationCount` is the lifetime total; `recent` holds the **5 most recent**, newest first, with the time window, shift type, cancellation timestamp and the stored reason (`null` when none was given). Families' cancellations are **not** counted.
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

Streams the stored scan from private storage (PDF/JPEG/PNG/WebP) as a file download, `CaregiversAdmin` required. The response `Content-Type` matches the stored file; the download filename is `certificate-{certificateId}.<ext>`.

- `404 Caregivers.Onboarding.CaregiverNotFound` / `Caregivers.Onboarding.CertificateNotFound`
- `404 Storage.File.NotFound` — the stored file is missing.

Certificate metadata (type, expiry, status, review reason) is in the detail response; never construct or share file URLs outside this endpoint. Caregiver self-service onboarding is documented under `docs/app/caregivers/`.
