# Care homes owner onboarding API

These owner endpoints are part of the Care homes facility API. They require a normal-access JWT whose active account is `CareHomeOwner`; each request is scoped to the user ID in the token. A Family or administrator token cannot read or mutate an owner draft. Admin operations are documented separately under `docs/admin` when those routes are implemented.

Base path: `/api/v1/care-homes/facilities`.

## Create a facility draft

`POST /api/v1/care-homes/facilities`

No request body. Creates the caller's single facility in `Draft` status and returns `201 Created` with its ID, version, status, revision references, and current draft (initially `null`). A second facility for the same owner returns `409 CareHomes.Facility.AlreadyExists`.

## Read my facility

`GET /api/v1/care-homes/facilities/mine`

Returns `200 OK` with the caller's facility, current editable draft, document metadata, revision identifiers, version, status, and review history. Returns `404 CareHomes.Facility.NotFound` when the account has not created a facility. No caller-supplied facility ID is accepted, preventing cross-owner reads.

## Save my profile draft

`PUT /api/v1/care-homes/facilities/mine/profile`

Request body:

```json
{
  "expectedVersion": 1,
  "draft": {
    "arabicName": "دار الأمل",
    "englishName": "Hope House",
    "arabicDescription": "رعاية وإقامة آمنة",
    "englishDescription": "Safe care and accommodation",
    "contactName": "Contact name",
    "contactPhone": "+201000000000",
    "contactEmail": "contact@example.test",
    "governorate": "Cairo",
    "city": "Nasr City",
    "area": "District 1",
    "address": "Street address",
    "arabicAdmissionConditions": "الشروط",
    "englishAdmissionConditions": "Conditions",
    "amenities": [{ "arabic": "إقامة", "english": "Accommodation" }],
    "medicalServices": []
  }
}
```

Drafts may be incomplete; submission is the validation boundary for required bilingual public profile, contact, and address fields. Names are limited to 200 characters, descriptions/admission conditions to 4,000, address to 1,000, and contact fields to their corresponding 32/200/256 character limits. Each amenities/services array accepts at most 100 bilingual entries, each language value up to 200 characters. Successful saves return `200 OK` and increment `version`.

Errors: `400 CareHomes.Facility.InvalidProfile` for field limits, `404 CareHomes.Facility.NotFound` when there is no facility, `409 CareHomes.Facility.Conflict` for stale versions or a non-editable lifecycle state, and `401/403` for authentication/account-policy failures.

## Upload or replace an onboarding document

`POST /api/v1/care-homes/facilities/mine/documents` (`multipart/form-data`)

Fields: `expectedVersion` (current facility version), `type` (`1` operating license, `2` registration, `3` health certificate, `4` civil-defense certificate), optional `expiryDate` (`YYYY-MM-DD`), and `file`. The upload is accepted only for the caller's editable draft/correction. Content is checked server-side by file signature and MIME type; accepted files are PDF/JPG/PNG up to 10 MiB. A replacement creates a new private document row; prior revision/document evidence is retained. The response is `200 OK` with the new document's ID, type, revision, nullable expiry, `PendingReview` state, and timestamps; the facility version increments.

The owner read endpoint returns document metadata only. It never returns the private storage key or file content. Private file retrieval and document verification/rejection are Admin operations and will be documented under `docs/admin` when implemented.

Errors: `400 CareHomes.Document.InvalidContent` or `Storage.File.*` for unsupported/mismatched/empty/oversized files, `404 CareHomes.Document.FacilityNotFound`, `409 CareHomes.Document.Conflict` for stale version or a non-editable state, and `401/403` for authentication/account-policy failures.

## Manage facility cover and gallery media

`POST /api/v1/care-homes/facilities/mine/media` (`multipart/form-data`)

Fields: `expectedVersion`, `kind` (`1` cover, `2` gallery), and `file`. Images must be actual JPG or PNG content and no larger than 5 MB. A revision can contain one cover and up to ten gallery images. Uploads are stored privately and increment the facility version. `DELETE /api/v1/care-homes/facilities/mine/media/{mediaId}?expectedVersion={version}` removes an image from the editable draft/correction revision and also increments the version. Stale versions and non-editable revisions are rejected; drafts do not become public when changed.

`GET /api/v1/care-homes/facilities/mine/media/{mediaId}/file` reads the owner's image. Admin review uses `GET /api/v1/admin/care-homes/{careHomeId}/media/{mediaId}/file`. Public discovery emits cover/gallery URLs only for the approved revision, served by `GET /api/v1/care-homes/discovery/{careHomeId}/media/{mediaId}`. Draft and pending media remain private. The generic `/files` static route does not serve private storage keys.

## Submit the application

`POST /api/v1/care-homes/facilities/mine/submit`

Request body:

```json
{ "expectedVersion": 6 }
```

Submission is the validation boundary. The current draft must contain the required bilingual profile/contact/address fields and one uploaded document of each required type (operating license, registration, health certificate, civil-defense certificate). The expected version must match the current editable facility version. On success the current revision is frozen, the facility enters `PendingReview`, and the response is `200 OK` with the owner profile, new `version`, `status`, submitted revision ID, document metadata, and review history. A stale version or non-editable state returns `409 CareHomes.Facility.Conflict`; incomplete bilingual/profile/document requirements return `409 CareHomes.Facility.InvalidSubmission`; unauthenticated and non-owner accounts receive `401/403`.

## Current lifecycle boundary

This API slice exposes owner draft editing, private document and image uploads, and submission. Admin review and private-file inspection are documented separately under `docs/admin/care-homes.md`. Submission and each review decision produce idempotent in-app/email status notifications: the owner receives status changes; active SuperAdmin/SupportAdmin accounts receive submission and correction events. No SMS is sent. A document replacement remains `PendingReview` until Admin verification; it does not restore approval or booking eligibility by itself. License-expiry alerts are produced by the Admin-side daily UTC-midnight monitor described in the Admin guide. Public discovery/detail and checkout eligibility are documented in `docs/care-homes/discovery.md`; checkout rechecks current license eligibility, while existing stays remain unchanged. Private documents and draft images cannot be served through the generic public/static file route.

Postman owner requests in `docs/postman/care-homes/Sanad.CareHomes.Owner.postman_collection.json` include image upload, owner readback, and removal; set `careHomeProfileImagePath` to an eligible local JPG/PNG before running those examples. The Bruno owner-onboarding collection was not expanded or run for this media change.
