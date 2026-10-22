# Caregiver profile: bootstrap, read, professional profile, address

All routes require the `CaregiverAccess` policy (Normal JWT, `MedicalCaregiver` or `CompanionCaregiver` account). See `overview.md`.

## Bootstrap the profile

The caregiver aggregate is created once, explicitly. The type comes from the JWT account claim — there is no body.

```http
POST /api/v1/caregiver/profile
```

Responses:

- `201` — created. Body is the `CaregiverProfileResponse` (status `Onboarding`).
- `409 Caregivers.Onboarding.AlreadyExists` — a profile already exists for this user. One profile per user.
- `403` — account is not a caregiver account, or token is Restricted.

Example:

```http
POST /api/v1/caregiver/profile
Authorization: Bearer {{caregiverToken}}

(no body)
```

## Read own profile

```http
GET /api/v1/caregiver/profile
```

- `200` — full current state (see response shape below).
- `404 Caregivers.Onboarding.NotFound` — not bootstrapped yet. The app shows the "Start onboarding" screen.

Partially completed onboarding returns **200 with partial content**: filled sections are present, the rest are `null` or empty arrays, `status` is `Onboarding`. Nothing is ever lost or reset across logins/devices.

## Update medical professional profile

Medical caregiver accounts only.

```http
PUT /api/v1/caregiver/profile/medical
Content-Type: application/json
Authorization: Bearer {{caregiverToken}}

{
  "professionalTitleId": "guid",
  "yearsOfExperience": 7,
  "specializationId": "guid",
  "academicDegreeId": "guid",
  "currentWorkplace": "Cairo Hospital",
  "biography": "Optional, up to 2000 chars"
}
```

Rules:

- `professionalTitleId`, `specializationId`, `academicDegreeId` must exist and be active; the specialization must be a **Medical** specialization.
- `yearsOfExperience` 0–80; `currentWorkplace` ≤ 200 chars; `biography` ≤ 2000 chars. Blank/null optional fields are stored as null.
- While **Active**, editing the profile returns the caregiver to `PendingReview` + `Unavailable` and raises a re-review (admin must re-approve).

Errors:

- `404 Caregivers.Onboarding.NotFound` — no profile.
- `409 Caregivers.Onboarding.WrongCaregiverType` — caller is a Companion account.
- `404 Caregivers.Lookups.NotFound` — one of the referenced lookups does not exist.
- `409 Caregivers.Onboarding.InactiveLookup` — referenced lookup is inactive (or specialization is for the other type).

## Update companion professional profile

Companion caregiver accounts only.

```http
PUT /api/v1/caregiver/profile/companion
Content-Type: application/json

{
  "yearsOfExperience": 5,
  "specializationId": "guid",
  "biography": "Optional, up to 2000 chars"
}
```

Rules mirror the medical update; specialization must be a **Companion** specialization. While Active, editing also returns the caregiver to `PendingReview` + `Unavailable` (aligned with medical since C5a).

## Update detailed address

Optional free-text address (up to 500 characters), shared by both types.

```http
PUT /api/v1/caregiver/profile/address
Content-Type: application/json

{ "detailedAddress": "12 Tahrir Street, apartment 4"
}
```

Blank/whitespace clears the address (stored as `null`); whitespace is trimmed. `200` returns the full caregiver profile. Values longer than 500 characters return `400 Api.Validation.Failed`; unauthenticated requests return `401`, non-caregiver accounts receive `403`, and a missing profile returns `404 Caregivers.Onboarding.NotFound`.

## Response shape — `CaregiverProfileResponse`

Returned by every profile/section save and by the GET. Certificate entries expose name/type/status only — **no file paths or URLs** (the scan is reachable solely through the admin-only download endpoint; see `docs/admin/caregivers-review.md`). See [Avatar and certificate files](#avatar-and-certificate-files) for how the underlying files are served.

```json
{
  "id": "guid",
  "userId": "guid",
  "type": 1,
  "status": 1,
  "availability": 2,
  "detailedAddress": "12 Tahrir Street, apartment 4",
  "statusReason": null,
  "medicalProfile": {
    "professionalTitleId": "guid",
    "yearsOfExperience": 7,
    "specializationId": "guid",
    "academicDegreeId": "guid",
    "currentWorkplace": "Cairo Hospital",
    "biography": "..."
  },
  "companionProfile": null,
  "certificates": [
    {
      "id": "guid",
      "type": 1,
      "expiryDate": null,
      "verificationStatus": 2,
      "reviewReason": null
    }
  ],
  "serviceIds": ["guid"],
  "languageIds": ["guid"],
  "areaIds": ["guid"],
  "medicalPricing": {
    "homeVisitPrice": 150.00,
    "eightHourShiftPrice": 500.00,
    "twelveHourShiftPrice": 700.50,
    "twentyFourHourShiftPrice": 1200.00
  },
  "companionPricing": null,
  "medicalSchedule": {
    "shifts": [
      { "dayOfWeek": 0, "shiftType": 1 }
    ],
    "homeVisitWindows": [
      { "dayOfWeek": 1, "startTime": "09:00", "endTime": "12:00" }
    ]
  },
  "companionSchedule": null
}
```

Companion responses carry `companionProfile` / `companionPricing` / `companionSchedule` instead of the medical fields.

Certificate `verificationStatus`: `1` Pending, `2` Verified, `3` Rejected, `4` Revoked. A rejected/revoked certificate carries `reviewReason`.

## Avatar and certificate files

Verified against the source (2026-10-21): **neither file is publicly served**,
so no caregiver document or response may promise a public file URL.

- **Certificate files are private.** `POST /api/v1/caregiver/certificates` and
  `PUT /api/v1/caregiver/certificates/{certificateId}/file` store the upload
  with the private file storage under the `caregiver-certificates` folder, so
  the stored key is `private/caregiver-certificates/...`. A private key is
  written to the sibling `<storage-root>-private` directory, which the static
  file host never serves, and every request for `/files/private/...` is
  answered with `404`. The only download route is the admin-only
  `GET /api/v1/admin/caregivers/{caregiverId}/certificates/{certificateId}/file`.
  The caregiver's own profile and the public caregiver profile expose the
  certificate **type, expiry date and verification status only** — never a file
  path or URL.
- **Avatars are private too.** The avatar belongs to the Identity account:
  `PUT /api/v1/auth/avatar` stores it with the private file storage
  (`private/avatars/...`) and the only avatar read route,
  `GET /api/v1/auth/avatar`, serves the **caller's own** avatar. The
  `avatarUrl` field on the caregiver discovery reads (`GET /api/v1/caregivers`
  search cards and `GET /api/v1/caregivers/{caregiverId}` public profile) is
  that stored reference, or `null` when the caregiver has no avatar. It is a
  storage key, **not a fetchable URL**: it is not served under `/files`, so the
  app must not load it as an image URL and must render its own placeholder
  instead.
