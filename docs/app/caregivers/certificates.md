# Certificates (Medical caregivers)

Certificate uploads are **Medical caregiver only**. Companion accounts receive `409 Caregivers.Onboarding.InvalidCertificateOperation` for all certificate routes.

Routes are `multipart/form-data`. Files are stored in a **private** storage area that is never served by the public `/files` static endpoint; the scan can only be retrieved by an admin through the admin download endpoint (see `docs/admin/caregivers-review.md`). The caregiver's own profile response shows certificate **type, status and expiry only — never a file link**.

Allowed content types: `application/pdf`, `image/jpeg`, `image/png`, `image/webp`. Maximum size **5 MB**.

## Certificate types

| Value | Type | Rules |
|---|---|---|
| `1` | Practice License | Mandatory. Exactly one; cannot be removed (replace the file). Expiry optional. |
| `2` | Graduation Certificate | Mandatory. Exactly one; cannot be removed. |
| `3` | Additional Certificate | Optional. At most **5**. Can be removed. |

New uploads start at verification status **Pending** (`1`). An admin verifies or rejects them.

## Add a certificate

```http
POST /api/v1/caregiver/certificates
Authorization: Bearer {{caregiverToken}}
Content-Type: multipart/form-data

  type: 1            (form field, number)
  expiryDate: 2028-12-31   (form field, optional)
  file: <binary>
```

- `200` — full profile response; the new certificate is Pending.
- `400 Storage.File.Empty` / `Storage.File.TooLarge` / `Storage.File.UnsupportedType` — missing, >5 MB, or disallowed content type.
- `409 Caregivers.Onboarding.InvalidCertificateOperation` — duplicate Practice License / Graduation Certificate, already 5 additional, expiry date in the past, or a Companion account.

Compensation: if the command fails after upload, the orphaned file is deleted automatically.

## Replace a certificate file

Use this (not delete+add) for mandatory certificates. Replacing resets the certificate to **Pending** and clears the previous review reason; for a mandatory certificate it also forces `Unavailable` and, while Active, returns the caregiver to `PendingReview` for re-approval. The previous file is deleted after a successful save.

```http
PUT /api/v1/caregiver/certificates/{certificateId}/file
Content-Type: multipart/form-data

  expiryDate: 2029-01-01   (optional)
  file: <binary>
```

- `404 Caregivers.Onboarding.CertificateNotFound` — the certificate does not belong to this caregiver.
- Storage errors and InvalidState as with add.

## Remove an additional certificate

Only additional certificates (`type: 3`) can be removed; attempting to remove a mandatory certificate returns `409 Caregivers.Onboarding.InvalidCertificateOperation`. The stored file is deleted.

```http
DELETE /api/v1/caregiver/certificates/{certificateId}
```

## Certificate state and activation

```text
Pending ──verify──▶ Verified
Pending ──reject──▶ Rejected  (mandatory rejection forces Unavailable)
Verified ──revoke──▶ Revoked  (mandatory revoke suspends an Active caregiver)
Rejected/Revoked/Verified ──replace file──▶ Pending (re-review)
```

Approve / BecomeAvailable require mandatory certificates to be **Verified** and unexpired. Submit requires them to be present, unexpired, and Pending-or-Verified. Admins manage verification — see `docs/admin/caregivers-review.md`.
