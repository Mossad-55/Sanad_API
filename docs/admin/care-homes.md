# Admin Care Homes operations

The operational review and private-file routes in this guide are restricted to normal `SuperAdmin` and `SupportAdmin` accounts by `CareHomesOperationalAdmin`. `ContentAdmin` is denied (`403`) and has no access to operational review, private files, or inventory inspection. Review routes are under `/api/v1/admin/care-homes`; inventory inspection routes use `/api/v1/care-homes/inventory/admin` and are described below.

## List applications

`GET /api/v1/admin/care-homes?page=1&pageSize=20&status=2`

`page` is at least 1, `pageSize` is 1–100, and optional `status` must be a defined `CareHomeStatus` value. The `200 OK` response contains `page`, `pageSize`, `totalCount`, and `items`; each item contains `id`, `ownerUserId`, `status`, `version`, `submittedRevisionId`, bilingual names, and `updatedOnUtc`. Invalid paging/status returns `400 CareHomes.Admin.InvalidQuery`.

## Application detail

`GET /api/v1/admin/care-homes/{careHomeId}`

Returns `200 OK` with the owner ID, lifecycle status/version, submitted and approved revision IDs, ordered frozen/editable `revisions`, document metadata, and chronological `reviewHistory`. Unknown IDs return `404 CareHomes.Admin.ApplicationNotFound`. Document metadata contains IDs, type, revision, expiry, review state/reason, and timestamps; it does not expose the private storage key.

## Read a private document

`GET /api/v1/admin/care-homes/{careHomeId}/documents/{documentId}/file`

Returns the private file bytes with its stored content type for applications in `PendingReview`, `Approved`, or `Suspended`. Draft and correction applications return `400 CareHomes.Admin.PrivateDocumentUnavailable`; an unknown application returns `404 CareHomes.Admin.ApplicationNotFound`, and an unknown document returns `400 CareHomes.Admin.DocumentNotFound`. The route is never available to owner, Family, or ContentAdmin tokens.

## Verify or reject a document

`POST /api/v1/admin/care-homes/{careHomeId}/documents/{documentId}/verify`

```json
{ "expectedVersion": 7, "expiryDate": null, "confirmNonExpiring": true }
```

Verification requires an expiry date or explicit `confirmNonExpiring: true`; a genuinely non-expiring document is therefore an explicit Admin decision. The response is the updated application detail (`200 OK`).

`POST /api/v1/admin/care-homes/{careHomeId}/documents/{documentId}/reject`

```json
{ "expectedVersion": 7, "reason": "Unreadable registration" }
```

Rejection requires a reason and returns the updated detail. Both document actions return `409 CareHomes.Admin.Conflict` when `expectedVersion` is stale, `400 CareHomes.Admin.InvalidOperation` for an invalid state, and `400 CareHomes.Admin.DocumentNotFound` for a missing document. Authentication/authorization failures are `401/403`.

## Review the application

`POST /api/v1/admin/care-homes/{careHomeId}/review`

```json
{ "expectedVersion": 8, "action": 1, "reason": null }
```

`action` is the numeric `CareHomeReviewAction` enum: `1` Approved, `2` Rejected, `3` CorrectionsRequested, `4` Suspended, and `5` Reactivated. Use a reason for rejection, correction, or suspension. The response is the updated detail (`200 OK`) and includes the appended reasoned history. Stale versions return `409 CareHomes.Admin.Conflict`; invalid lifecycle transitions return `400 CareHomes.Admin.InvalidOperation`; missing applications return `404 CareHomes.Admin.ApplicationNotFound`.

## Lifecycle and privacy

Owners save versioned bilingual drafts, upload private documents, and submit a frozen revision. Admins inspect the submitted snapshot, review each document, then request correction, approve, reject, suspend, or reactivate according to the domain lifecycle. Replacement uploads remain pending review. Verified documents may have a nullable expiry only when the Admin explicitly confirms they are non-expiring. A hosted daily scan at UTC midnight checks the latest operating license on each approved facility. An expired license means a verified, dated license whose expiry date is before the scan date; Admin-verified non-expiring documents and unverified or rejected documents do not alert. Each active SuperAdmin and SupportAdmin with an email receives one in-app notification and one durable email-outbox message per UTC date, facility, and recipient. The in-app event is idempotent; SMTP Message-ID is a stable SHA-256 digest of the email idempotency key. Delivery is at least once, so a provider duplicate remains possible if SMTP accepts a message before the worker records it as sent. Discovery and Family checkout both require the latest required documents on the approved revision to be usable on the current Cairo date. Existing stays are unchanged after expiry. Notifications are in-app/email; SMS remains limited to verification OTP.

The runnable Admin requests are in [`Sanad.Admin.postman_collection.json`](../postman/admins/Sanad.Admin.postman_collection.json) and [`tests/Bruno/collections/Sanad/admin-care-homes`](../../tests/Bruno/collections/Sanad/admin-care-homes). The expiry monitor has no HTTP route, so no Postman or Bruno request was added for HC-TASK-014. The additive outbox migration is already up to date on the authorized disposable database; no migration was applied during the latest verification.

## Inspect Care Homes inventory and availability

`GET /api/v1/care-homes/inventory/admin/{facilityId}` returns the facility's
room types, rooms, beds, maintenance blocks, and current inventory version.
`POST /api/v1/care-homes/inventory/admin/{facilityId}/availability` accepts
`{ "startDate": "2027-01-01", "endDate": "2027-01-20" }` and returns derived
availability by room type. These read routes require the
`CareHomesOperationalAdmin` policy: normal `SuperAdmin` and `SupportAdmin` are
allowed, while `ContentAdmin`, owners, Family accounts, and anonymous callers
are denied. Availability uses Egypt-local half-open maintenance dates and
excludes archived assets, maintenance, and supplied active occupancy.

The current occupancy provider is intentionally empty until HC-TASK-032/034
supply Care Homes holds and stays. Booking creation is not part of HC-TASK-020.
The additive inventory migration has not been generated and awaits fresh
owner authorization for the exact disposable target; no database execution is
claimed here.
