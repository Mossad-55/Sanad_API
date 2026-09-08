# Certificates (caregiver self-service)

National ID (front/back) is **not** a caregiver certificate. Upload it with `PUT /api/v1/auth/identity-document` after a Normal login. See `docs/auth/identity-document.md`. This page is professional certificates only (practice license, graduation, etc.).

Medical caregivers attach professional certificates (practice license, graduation, etc.) during onboarding. Companion caregivers do not use this surface — a Companion caller receives `409 Caregivers.Certificates.NotRequiredForType`.

Every route requires policy `CaregiverAccess` (Normal JWT + Medical or Companion account). Restricted tokens receive `403`.

Files are stored in **private** storage (not served at `GET /files/{key}`). The caregiver never receives a file URL. Admin reviewers download through `GET /api/v1/admin/caregivers/{id}/certificates/{certId}/file` (see `docs/admin/caregivers-review.md`).

Allowed content types: `application/pdf`, `image/jpeg` (`image/jpg` is accepted and stored as jpeg), `image/png`, `image/webp`. Maximum **5 MB** per file.

## Status values

| Value | Name |
|---|---|
| `1` | Pending |
| `2` | Verified |
| `3` | Rejected |
| `4` | Revoked |

A caregiver cannot mutate a `Verified` certificate. `Rejected` / `Revoked` files can be replaced (status returns to `Pending`, `reviewReason` cleared).

## POST `/api/v1/caregiver/certificates`

Add one additional certificate. Multipart fields:

| Field | Required | Notes |
|---|---|---|
| `file` | yes | The scan |
| `title` | yes | Display name |
| `issuer` | no | Issuing body |
| `issuedOn` | no | ISO date (`yyyy-MM-dd`) |
| `expiresOn` | no | ISO date; must be ≥ `issuedOn` when both set |

Success `201` with the new certificate JSON (no file URL). Duplicate title `409 Caregivers.Certificates.TitleAlreadyExists`. Missing/empty file `400 Storage.File.Empty`. Too large `400 Storage.File.TooLarge`. Wrong type `400 Storage.File.UnsupportedType`.

## PUT `/api/v1/caregiver/certificates/{certificateId}/file`

Replace the scan of an existing certificate. Multipart field `file` (required). Success `200` with the updated certificate JSON.

- `Verified` → `409 Caregivers.Certificates.CannotMutateVerified`
- Unknown id → `404 Caregivers.Certificates.NotFound`

The previous private file is deleted after a successful replace.

## DELETE `/api/v1/caregiver/certificates/{certificateId}`

Remove an **additional** certificate. Success `204`.

- Practice-license / graduation (required types) → `409 Caregivers.Certificates.CannotRemoveRequired`
- `Verified` → `409 Caregivers.Certificates.CannotMutateVerified`
- Unknown id → `404 Caregivers.Certificates.NotFound`

The private file is deleted after a successful remove.

## Suggested client flow

```text
GET  /profile                              (see which required slots are empty)
POST /certificates                         (add practice license / graduation / extras)
PUT  /certificates/{id}/file               (replace a Rejected/Revoked scan)
DELETE /certificates/{id}                  (drop an extra)
POST /submit                               (when required certificates exist)
```
