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

Returns the full `CaregiverProfileResponse` (same shape as the caregiver's own GET — profile, selections, pricing, schedule, certificates with verification status and `reviewReason`, status, `statusReason`).

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
