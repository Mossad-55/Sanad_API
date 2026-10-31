# Care homes owner onboarding API

These owner endpoints are part of the Care homes facility API. They require a normal-access JWT whose active account is `CareHomeOwner`; each request is scoped to the user ID in the token. A Family or administrator token cannot read or mutate an owner draft. Admin operations are documented separately under `docs/admin` when those routes are implemented.

Base path: `/api/v1/care-homes/facilities`.

## Create a facility draft

`POST /api/v1/care-homes/facilities`

No request body. Creates the caller's single facility in `Draft` status and returns `201 Created` with its ID, version, status, revision references, and current draft (initially `null`). A second facility for the same owner returns `409 CareHomes.Facility.AlreadyExists`.

## Read my facility

`GET /api/v1/care-homes/facilities/mine`

Returns `200 OK` with the caller's facility, current editable draft, document metadata, revision identifiers, version, status, and review history. Returns `404 CareHomes.Facility.NotFound` when the account has not created a facility. No caller-supplied facility ID is accepted, preventing cross-owner reads.

Alongside `draft`, the response carries two nullable revision-bound readbacks. `submittedProfile` (`revisionId` plus profile projection) is populated only while a submitted revision is pending review; `approvedProfile` follows `approvedRevisionId` and remains available while a newer editable draft exists. `openRevisionId` identifies the current editable revision, when one exists; otherwise it is `null`. A `GET` never creates, saves, mutates, or backfills a draft or revision assets. Owner private contact fields stay on this owner route and are never returned through public discovery.

When an approved facility is edited, the new editable revision inherits the approved documents and media through new revision records that reference the same stored files. Document verification attribution, expiry, and verification/rejection status are preserved; expired or rejected documents remain unusable until replaced and normally reviewed. Removing or replacing an inherited media record removes only that revision reference and does not delete the shared file while another revision still references it.

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
    "medicalServices": [],
    "cancellationTerms": {
      "tiers": [
        { "refundPercentage": 100, "deadlineHoursBeforeStart": 24 },
        { "refundPercentage": 50, "deadlineHoursBeforeStart": 12 }
      ]
    }
  }
}
```

Drafts may be incomplete; submission is the validation boundary for required bilingual public profile, contact, and address fields. Names are limited to 200 characters, descriptions/admission conditions to 4,000, address to 1,000, and contact fields to their corresponding 32/200/256 character limits. Each amenities/services array accepts at most 100 bilingual entries, each language value up to 200 characters. Successful saves return `200 OK` and increment `version`.

`cancellationTerms` is the facility's structured cancellation schedule under clause 6 of the approved refund policy (`2026-10-22`): it accepts one to six tiers, each with `refundPercentage` (whole 0–100 percent of the paid total) and `deadlineHoursBeforeStart` (whole 0–8760 hours before the scheduled stay start). Tiers are normalized longest-deadline-first, duplicate deadlines are rejected, and unreferenced by money while the Owner-approved refund policy ships dormant. Once activated, a booking made with the facility's approved revision snapshots exactly these terms. Omitting `cancellationTerms` or sending `null` clears previously published terms from the draft (replacement semantics); the clause-2 default schedule then governs bookings once the policy is activated.

Errors: `400 CareHomes.Facility.InvalidProfile` for field limits and invalid cancellation terms (out-of-range percentage/deadline, duplicate deadlines, more than six tiers, a missing tiers list, or null tier entries), `404 CareHomes.Facility.NotFound` when there is no facility, `409 CareHomes.Facility.Conflict` for stale versions or a non-editable lifecycle state, and `401/403` for authentication/account-policy failures.

## Upload or replace an onboarding document

`POST /api/v1/care-homes/facilities/mine/documents` (`multipart/form-data`)

Fields: `expectedVersion` (current facility version), `type` (`1` operating license, `2` registration, `3` health certificate, `4` civil-defense certificate), optional `expiryDate` (`YYYY-MM-DD`), and `file`. The upload is accepted only for the caller's editable draft/correction. Content is checked server-side by file signature and MIME type; accepted files are PDF/JPG/PNG up to 10 MiB. A replacement creates a new private document row; prior revision/document evidence is retained. The response is `200 OK` with the new document's ID, type, revision, nullable expiry, `PendingReview` state, timestamps, and — additive since 2026-10-27 — the `lifecycleView` (always `pending` for a fresh upload), `isExpired`, and `needsAction` fields described below; the facility version increments.

The owner read endpoint returns document metadata only. It never returns the private storage key or file content. Private file retrieval and document verification/rejection are Admin operations documented in [the Admin Care Homes guide](../admin/care-homes.md).

Errors: `400 CareHomes.Document.InvalidContent` or `Storage.File.*` for unsupported/mismatched/empty/oversized files, `404 CareHomes.Document.FacilityNotFound`, `409 CareHomes.Document.Conflict` for stale version or a non-editable state, and `401/403` for authentication/account-policy failures.

## Document lifecycle views, file read, and history removal

Since 2026-10-27 every document a caller sees carries three additive, computed
fields next to the unchanged `status` field:

- `lifecycleView` — exactly one of `current`, `pending`, or `history`. It is
  never stored and never a fourth status value.
  - `current`: per document type, the newest `Verified` document on the
    facility's approved revision (`approvedRevisionId`). It stays `current`
    even when its expiry has passed, and a newer pending replacement never
    displaces it. If the newest document on the approved revision for a type
    is rejected or revoked (a legacy/stored-data state the classification
    explicitly supports; today's flow cannot author it), that type has **no
    current document**: older verified documents are never resurrected, and
    deleting the negative document from history does not restore one — the
    effective state is retained from the audit rows, so removing the negative
    row cannot unblock any eligibility gate.
  - `pending`: the newest document of the type on the actionable target
    revision — the submitted revision while under review, else the current
    editable revision — regardless of its stored status. This includes a
    latest rejected candidate awaiting correction, so it is never lost in
    history.
  - `history`: everything else (superseded uploads, older revisions, rejected
    documents of past submissions).
- `isExpired` — `true` when the supplied `expiryDate` is before the current
  Cairo server date and the document is not Admin-confirmed non-expiring. It
  is independent of `status`: a pending candidate with a past date reads
  `isExpired: true` instead of reassuring, and a missing date reads `false`
  ("no expiry recorded", never treated as non-expiring). The comparison date
  always comes from the server clock; clients cannot influence it.
- `needsAction` — `true` only for a `pending` candidate whose stored status is
  `Rejected`.

The `mine` readback and each document carry these fields; its additive
`reviewTargetRevisionId` is the revision whose documents are the actionable
candidate set. Existing array order (insertion order) is unchanged.

`GET /api/v1/care-homes/facilities/mine/documents` returns the same items as a
labeled list: `{ view, type, isExpired, totalCount, items[] }`, newest-first.
The default `view` is `all`; optional `view=current|pending|history`, `type`
(1–4) and `isExpired=true|false` filters are accepted. Unknown `view` values
return `400 CareHomes.Document.InvalidView` and undefined `type` values return
`400 CareHomes.Document.InvalidType` — never a silent empty result.

`GET /api/v1/care-homes/facilities/mine/documents/{documentId}/file` returns
one of the facility's own private document files, current through history, at
any facility status. Logically removed or unknown documents return `404
CareHomes.Document.DocumentNotFound`; another owner's ID is indistinguishable
(`404 CareHomes.Document.FacilityNotFound`). The storage key is never exposed.

`DELETE /api/v1/care-homes/facilities/mine/documents/{documentId}?expectedVersion={version}`
removes a document from the facility's history. Only `history`-classified
documents are removable — the server recomputes the classification at mutation
time and never trusts a client-supplied view flag. The current effective
document is protected even when expired, and active pending candidates
(including the latest rejected candidate) are protected; both return `409
CareHomes.Document.NotRemovable`. A stale version returns `409
CareHomes.Document.Conflict`. Success is `204 No Content`. Repeating the same
DELETE returns `204` again — it is idempotent and writes nothing. Removal is
logical: the audit row and the stored file bytes are retained, the document
disappears from every read (the `mine` array, the list, and the file route),
a removed document is never carried into a new revision, and a removed
document cannot be reviewed again. Files still referenced by live revisions
keep working unchanged.

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

Submission is the validation boundary. The current draft must contain the required bilingual profile/contact/address fields and one uploaded document of each required type (operating license, registration, health certificate, civil-defense certificate). At least two facility photos including the cover must be uploaded for the open revision first — otherwise submission fails with `400 CareHomes.Facility.ProfileMediaRequired`. The expected version must match the current editable facility version. On success the current revision is frozen, the facility enters `PendingReview`, and the response is `200 OK` with the owner profile, new `version`, `status`, submitted revision ID, document metadata, and review history. A stale version or non-editable state returns `409 CareHomes.Facility.Conflict`; incomplete bilingual/profile/document requirements return `400 CareHomes.Facility.InvalidSubmission`; unauthenticated and non-owner accounts receive `401/403`.

## Current lifecycle boundary

This API slice exposes owner draft editing, private document and image uploads, and submission. Admin review and private-file inspection are documented separately under `docs/admin/care-homes.md`. Submission and each review decision produce idempotent in-app/email status notifications: the owner receives status changes; active SuperAdmin/SupportAdmin accounts receive submission and correction events. No SMS is sent. Status notices are written to the inbox regardless of the owner's notification preferences; the owner's own push behaviour is governed by two self-service switches — `systemNotifications` for review/status events (`CareHomes`/`OnboardingStatus`) and `bookingUpdates` for care-home booking events — managed at `GET/PUT /api/v1/account/notification-preferences`. Email is not affected by those preferences. A document replacement remains `PendingReview` until Admin verification; it does not restore approval or booking eligibility by itself. License-expiry alerts are produced by the Admin-side daily UTC-midnight monitor described in the Admin guide. Public discovery/detail and checkout eligibility are documented in `docs/care-homes/discovery.md`; checkout rechecks current license eligibility, while existing stays remain unchanged. Private documents and draft images cannot be served through the generic public/static file route.

Postman owner requests in `docs/postman/care-homes/Ahlena.CareHomes.Owner.postman_collection.json` include image upload, owner readback, document lifecycle list/readback, owner document file read, and history removal; set `careHomeProfileImagePath` to an eligible local JPG/PNG and `careHomeDocumentId` from a list response before running those examples. The Bruno owner-onboarding collection was not expanded or run for the lifecycle work.
