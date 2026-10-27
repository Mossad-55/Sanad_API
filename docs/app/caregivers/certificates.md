# Certificates (Medical caregivers)

National ID (front/back) is **not** a caregiver certificate. Upload it with `PUT /api/v1/auth/identity-document` after a Normal login. See `docs/auth/identity-document.md`. This page is professional certificates only (practice license, graduation, additional).

Certificate **uploads** (add / replace) are **Medical caregiver only**. A Companion account gets `409 Caregivers.Onboarding.InvalidCertificateOperation` when it tries to add a certificate. The remaining certificate routes resolve no certificate for a Companion and answer `404 Caregivers.Onboarding.CertificateNotFound`.

Upload routes are `multipart/form-data`; download routes stream the stored bytes. Files are stored in a **private** storage area that is never served by the public `/files` static endpoint. A scan can be read by its owner through the caregiver self-download routes below, and by an admin through the admin download routes (see `docs/admin/caregivers-review.md`). The caregiver's profile response shows certificate **type, status, expiry and the authorized per-version file link only — never a raw storage key**.

Allowed content types: `application/pdf`, `image/jpeg`, `image/png`, `image/webp`. Maximum size **5 MB**.

## Certificate types

| Value | Type | Rules |
|---|---|---|
| `1` | Practice License | Mandatory. Exactly one; cannot be removed (replace the file). Expiry optional. |
| `2` | Graduation Certificate | Mandatory. Exactly one; cannot be removed. |
| `3` | Additional Certificate | Optional. At most **5**. Can be removed. |

Several qualifications of the same type may coexist for **Additional** only (up to the cap of 5); each stays its own stable `certificateId` across a file replacement and review.

New uploads start at verification status **Pending** (`1`). An admin verifies or rejects them.

## Certificate versions (append-only history)

A qualification keeps an **append-only** list of uploads, its *versions*. Nothing is physically deleted: a replacement, a review decision or a logical removal only changes how a version is classified.

| Classification value | Meaning |
|---|---|
| `1` Current | The approved, effective version backing the certificate's fields. It may have become **expired** on the Cairo calendar while remaining the honest effective label. |
| `2` Pending | The single upload awaiting a review decision (an initial upload or a pending replacement). |
| `3` History | A superseded or rejected version retained for audit. |

At most **one** version is Current and at most **one** is Pending at any time. A removed (logically deleted) history version is not listed at all.

Every certificate also carries a derived **`expiryStatus`** — `0` none (no expiry date recorded; never treated as a confirmed non-expiring approval), `1` valid, `2` expired — computed against the current **Africa/Cairo** calendar date on each read. An expired Verified version is no longer usable for availability or public discovery.

## Profile response additions

The caregiver profile response (`GET /api/v1/caregiver/profile`, and the response of most certificate writes) keeps every existing field and array meaning. These members are **additive**:

```jsonc
{
  "certificates": [                     // live (non-removed) qualifications only
    {
      "id": "guid",
      "type": 1,
      "expiryDate": "2028-12-31",
      "verificationStatus": 2,          // effective status (unchanged meaning)
      "reviewReason": null,
      "expiryStatus": 1,                // none | valid | expired (Cairo date)
      "hasPendingReplacement": false,
      "versions": [
        {
          "id": "guid",
          "expiryDate": "2028-12-31",
          "verificationStatus": 2,
          "expiryStatus": 1,
          "classification": 1,          // current | pending | history
          "reviewReason": null,
          "createdOnUtc": "2026-08-31T10:00:00Z",
          "supersededOnUtc": null,
          "fileLink": "/api/v1/caregiver/certificates/{id}/versions/{versionId}/file",
          "canRemove": false            // true only for genuine superseded history
        }
      ]
    }
  ],
  "removedCertificates": []             // tombstoned qualifications, retained for audit
}
```

`fileLink` is a **route path** (no storage key) scoped to the authenticated owner; it is the authorized way to fetch that specific version's scan.

`canRemove` is the authoritative answer to "may I `DELETE` this version?": `true` only for a genuinely superseded history upload. A history version that still **backs the effective snapshot** — the newest retained upload after an initial rejection, when nothing is approved — reports `false` and is refused with `409`, so the profile never presents a protected version as removable and then rejects it with a surprise error.

## Add a certificate

```http
POST /api/v1/caregiver/certificates
Authorization: Bearer {{caregiverToken}}
Content-Type: multipart/form-data

  type: 1            (form field, number)
  expiryDate: 2028-12-31   (form field, optional)
  file: <binary>
```

- `200` — full profile response; the new certificate is Pending with exactly one Pending version.
- `401` — unauthenticated; `403` — caller does not satisfy `CaregiverAccess`; `404 Caregivers.Onboarding.NotFound` — caregiver profile is absent.
- `400 Storage.File.Empty` / `Storage.File.TooLarge` / `Storage.File.UnsupportedType` — missing, >5 MB, or disallowed content type.
- `400 Api.Validation.Failed` — an invalid type or an expiry date in the past.
- `409 Caregivers.Onboarding.InvalidCertificateOperation` — duplicate Practice License / Graduation Certificate, already 5 additional, or a Companion account.

Compensation: if the command fails after upload, the orphaned file is deleted automatically.

## Replace a certificate file

Use this (not delete+add) for mandatory certificates. The replacement is **appended as a new Pending version**; the previously approved file is **retained as history** (its bytes stay readable through its version file route). The effective certificate keeps describing the current approved version until the replacement is itself reviewed.

```http
PUT /api/v1/caregiver/certificates/{certificateId}/file
Content-Type: multipart/form-data

  expiryDate: 2029-01-01   (optional)
  file: <binary>
```

- If the qualification has a valid (approved, unexpired) current version, the caregiver's status and availability are **unchanged** while the replacement is pending.
- For a mandatory certificate with **no** valid current version (never approved, or the approved version has expired/been revoked), the mandatory-safety consequence applies exactly as before: `Unavailable`, and an Active caregiver returns to `PendingReview` for re-approval.
- `404 Caregivers.Onboarding.CertificateNotFound` — the certificate does not belong to this caregiver.
- `400 Api.Validation.Failed` — invalid/empty certificate ID or invalid expiry date (a past expiry date is refused).
- `401` — unauthenticated; `403` — caller does not satisfy `CaregiverAccess`.
- `409 Caregivers.Onboarding.CertificateConflict` — the qualification changed since it was read; reload and retry.

## Download a certificate scan (self-service)

```http
GET /api/v1/caregiver/certificates/{certificateId}/file
GET /api/v1/caregiver/certificates/{certificateId}/versions/{versionId}/file
```

Streams the stored scan from private storage. The first route serves the **effective snapshot** version — the approved current version, otherwise the live pending candidate, otherwise the newest retained upload (after an initial rejection) — a key that is always non-removed. The second serves the explicitly named version. Both are scoped to the authenticated owner and carry no storage key; the download filename is `certificate-{certificateId}.<ext>`.

- `404 Caregivers.Onboarding.NotFound` — no caregiver profile for the caller.
- `404 Caregivers.Onboarding.CertificateNotFound` — the certificate is unknown, belongs to another caregiver, or has been tombstoned.
- `404 Caregivers.Onboarding.CertificateVersionNotFound` — the version is unknown or has been logically removed (its bytes stay retained but are no longer downloadable).
- `404 Storage.File.NotFound` — the stored file is missing.

## Remove an additional certificate

Only additional certificates (`type: 3`) can be removed; attempting to remove a mandatory certificate returns `409 Caregivers.Onboarding.InvalidCertificateOperation`.

```http
DELETE /api/v1/caregiver/certificates/{certificateId}
```

Removal is a **logical tombstone**, never a physical delete: the row and every version file stay retained for authorized history. The qualification leaves `certificates[]`, appears in `removedCertificates[]`, is excluded from public discovery, and no longer occupies an Additional slot (a new one can be added).

Success returns `200` with the updated profile. Unauthenticated requests return `401`, callers outside `CaregiverAccess` receive `403`, and an unknown/non-owned ID returns `404 Caregivers.Onboarding.CertificateNotFound`.

## Remove a retained history version

Logically remove **one superseded history version** of the caregiver's own qualification. The version and its stored file are retained for audit but hidden from the normal read surfaces, and it can never be verified or reanimated. The profile flags exactly which versions this is allowed on with `canRemove`.

```http
DELETE /api/v1/caregiver/certificates/{certificateId}/versions/{versionId}
```

- `204 No Content` — the version is now removed. Calling it again for a version this caregiver already removed is an **idempotent `204`**.
- `409 Caregivers.Onboarding.CertificateVersionNotRemovable` — the version is protected and only a genuinely superseded upload may be removed. The protected cases are the **current effective** version (even when expired), the **live pending** candidate, and the retained version that still **backs the effective snapshot** (the newest upload after an initial rejection, when nothing is approved). Removing a snapshot-backing version would leave the displayed status and the no-version download pointing at a removed file, so it is refused — supersede it with a replacement first.
- `404 Caregivers.Onboarding.CertificateNotFound` — the certificate is unknown or belongs to another caregiver.
- `404 Caregivers.Onboarding.CertificateVersionNotFound` — the version is unknown on this qualification.
- `409 Caregivers.Onboarding.CertificateConflict` — a concurrent change altered the classification; reload and retry.
- `401` — unauthenticated; `403` — caller does not satisfy `CaregiverAccess`.

The server recomputes the version classification and removability on the freshly loaded qualification, so a version that becomes current, pending or snapshot-backing between the read and the write is refused with a stable error instead of being accidentally removed. Nothing is physically deleted, and a removal never changes a qualification's effective status, its eligibility or its own file download.

## Certificate state and activation

```text
add              ──▶ Pending (version: Pending)
Pending          ──admin verify──▶ Verified (version: Current)
Pending          ──admin reject──▶ Rejected (version: History; protected from removal while it backs the
                                   effective snapshot; mandatory with no valid current ⇒ Unavailable)
Verified/Revoked ──replace file──▶ effective current preserved + a new Pending version
                                   (mandatory with no valid current ⇒ Unavailable / PendingReview)
Current          ──admin revoke──▶ Revoked (terminal)
Superseded history ──remove version──▶ Removed (hidden; file retained; never reanimatable)
Additional       ──remove cert─────▶ Removed (tombstone; leaves the cap and discovery)
```

Approve / BecomeAvailable require mandatory certificates to be **Verified and unexpired**. Submit requires them to be present, unexpired, and Pending-or-Verified. Public discovery shows only live qualifications backed by a Verified, unexpired version — an expired or revoked one is hidden. Admins manage verification — see `docs/admin/caregivers-review.md`.
