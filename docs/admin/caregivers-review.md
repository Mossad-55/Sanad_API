# Admin caregiver review

Admin review routes live under `/api/v1/admin/caregivers/...` and require the **`CaregiverReviewAdmin`** policy: authenticated, `access_type` = `Normal`, `account_type` = `SuperAdmin` or `SupportAdmin`. `ContentAdmin` receives `403` on these operational review routes (ContentAdmin stays limited to CMS). The one exception on this route prefix is the name+ID lookup below, which has its own dedicated policy (`SuperAdmin`/`FinanceAdmin`).

Certificate file download lives here too — this is the **only admin** way to read a certificate scan. Scans live in private storage and are never served under `/files`; the only other read path is the owning caregiver's own scoped self-download route (see `docs/app/caregivers/certificates.md`). A removed history version's retained bytes are downloadable from no route.

## List caregivers (paged)

```http
GET /api/v1/admin/caregivers?page=1&pageSize=10&status=2&type=1
Authorization: Bearer {{accessToken}}
```

Query parameters (all optional):

- `page` (default 1), `pageSize` (default 10, max 100)
- `status` — `1` Onboarding, `2` PendingReview, `3` NeedsCorrection, `4` Active, `5` Suspended, `6` Rejected
- `type` — `1` Medical, `2` Companion

Response (reviewer name/phone are joined read-only from the Identity users table; `identityPresent` reports only whether that join found a row):

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
      "updatedOnUtc": "2026-08-31T10:00:00Z",
      "identityPresent": true
    }
  ]
}
```

A fresh database returns `200` with `totalCount: 0` and an empty `items`. Invalid paging returns `400 Api.Validation.Failed`.

## The `identityPresent` flag (narrow semantics)

Every list item carries **`identityPresent`**. It means exactly one thing: an `identity.users` row **was joined on the configured query target during this read** (the same target the name/phone enrichment comes from). It is a join result, nothing more.

What it deliberately does **not** mean:

- **Not a cause discriminator.** `true` covers every joined row alike — a normal member, an anonymized placeholder row, or an identity behind a deactivated caregiver. The flag alone never tells you *why* enrichment is missing.
- **Not authoritative identity absence.** `false` says only that no row was joined on the queried target. A wrong `user_id`, a stale/secondary Identity copy, or a split-target deployment can all produce `false` while another target still holds the identity. Do not treat `false` as proof the identity was deleted anywhere.
- **No deletion or provenance claims.** The flag carries no information about whether, when or by whom an identity was ever deleted; anonymization is not deletion (the row stays with placeholder names — see below).

The row classes a reviewer sees on the list:

| Shape | Names / phone | `identityPresent` |
|---|---|---|
| Normal joined row | real names, real phone | `true` |
| Missing identity (orphaned `user_id`) | `null` / `null` / `null` | `false` |
| Anonymized identity | `محذوف` / `Deleted` / synthetic `+200…` phone | `true` |
| Deactivated caregiver (status `7`) | intact names | `true` |

Anonymization (privacy placeholder) writes the placeholder name pair (`محذوف` / `Deleted`), clears the email and replaces the phone with a synthetic `+200` + 9-digit number; the identity row **remains**, so the list keeps rendering it — plainly distinguishable from the `null` missing-identity shape by the values themselves, not by the flag. Deactivation is the caregiver row's own status (`status: 7`, filterable with `?status=7`); the identity enrichment stays intact behind it.

## The Identity admin-detail boundary (`/api/v1/admin/users/{id}`)

The caregiver list is paired with a separate Identity detail route. Its boundaries:

- **Authorization first.** `GET /api/v1/admin/users/{id}` requires the **`SuperAdminOnly`** policy (Normal SuperAdmin). A SupportAdmin who may read the caregiver list receives **`403`** on this Identity detail — the denial is policy-shaped and says nothing about the data.
- **Target-local 404.** For a SuperAdmin, a referenced user that is absent on the queried target returns `404` with code **`Identity.User.NotFound`**. Read it the same narrow way as `identityPresent: false`: no row found *where you looked*, not authoritative cross-target absence.
- **Anonymized identity detail.** For an anonymized user the same route returns `200` with the placeholder names, a `null` email, the synthetic `+200…` phone and status `Blocked` — an existing row, never a 404.

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

- `profile` — same shape as before this envelope was introduced (nothing removed). Certificate entries now also carry the additive `expiryStatus`, `hasPendingReplacement` and `versions` members (each version with its classification, authorized `fileLink` and `canRemove`), plus a `removedCertificates` array of tombstoned qualifications retained for audit. These are additive: the existing `certificates` array, its fields and their meanings are unchanged (live qualifications only). `canRemove` is the authoritative answer to whether a version may be removed (see the version-removal action below). See `docs/app/caregivers/certificates.md`.
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

A qualification keeps an append-only list of uploads (its **versions**). These
are the effective operators over that list; the version-target routes let a
reviewer act on one explicitly named upload rather than always the newest
pending one.

| Action | Route | Valid from | Effect |
|---|---|---|---|
| Verify | `POST /{caregiverId}/certificates/{certificateId}/verify` | Pending | Newest Pending version → Verified (Current) |
| Verify a version | `POST /{caregiverId}/certificates/{certificateId}/versions/{versionId}/verify` | Pending | That Pending version → Verified (Current); any previous Current becomes history |
| Reject certificate | `POST /{caregiverId}/certificates/{certificateId}/reject` | Pending | Newest Pending version → Rejected (history); reason required |
| Reject a version | `POST /{caregiverId}/certificates/{certificateId}/versions/{versionId}/reject` | Pending | That Pending version → Rejected; reason required |
| Revoke certificate | `POST /{caregiverId}/certificates/{certificateId}/revoke` | Verified (Current) | Current version → Revoked (terminal); reason required |
| Remove a history version | `DELETE /{caregiverId}/certificates/{certificateId}/versions/{versionId}` | Superseded history | Version → logically removed (hidden; file retained); idempotent `204` |

- Reject powers the mandatory-safety consequence: a mandatory certificate whose
  rejection leaves **no valid approved current version** forces the caregiver
  `Unavailable`. When a valid approved current remains, a rejected replacement
  leaves the caregiver's status and availability **unchanged** and the rejected
  upload stays as history.
- Revoke of a mandatory certificate forces `Unavailable` and suspends an Active
  caregiver.
- Reject/revoke body: `{ "reason": "…" }` (required, ≤ 500 chars).
- The server recomputes the version classification on the freshly loaded
  aggregate; a concurrent change makes a verify/reject/remove lose the race with
  `409 Caregivers.Onboarding.CertificateConflict` instead of acting on a stale
  classification.

Errors (all certificate review actions): `404 Caregivers.Onboarding.NotFound` for
an unknown caregiver, `404 Caregivers.Onboarding.CertificateNotFound` for an
unknown certificate id, `404 Caregivers.Onboarding.CertificateVersionNotFound`
for an unknown version id, `409 Caregivers.Onboarding.InvalidCertificateOperation`
for an illegal transition or a version-target review of a version that is not
pending, and `409 Caregivers.Onboarding.CertificateVersionNotRemovable` when a
removal names a **protected** version. Only a genuinely superseded upload can be
removed; the protected cases are the current effective version (even when
expired), the live pending candidate, and the retained version that still
**backs the effective snapshot** — the newest upload after an initial rejection,
when nothing is approved. Removing that snapshot-backing version would leave the
qualification's displayed status and its no-version file download pointing at a
removed file, so it is refused; supersede it with a replacement first. The
per-version `canRemove` flag in the detail response is authoritative for what is
removable. Removing a version this admin already removed is an idempotent `204`.

## Download a certificate file

```http
GET /api/v1/admin/caregivers/{caregiverId}/certificates/{certificateId}/file
GET /api/v1/admin/caregivers/{caregiverId}/certificates/{certificateId}/versions/{versionId}/file
```

Streams a stored scan from private storage (PDF/JPEG/PNG/WebP) as a file
download, `CaregiverReviewAdmin` required (SuperAdmin or SupportAdmin;
ContentAdmin receives `403`). The first route serves the **effective snapshot**
version — the approved current version, otherwise the live pending candidate,
otherwise the newest retained upload after an initial rejection — a key that is
always non-removed; the second serves the explicitly named version. The response
`Content-Type` matches the stored file; the download filename is
`certificate-{certificateId}.<ext>`.

- `404 Caregivers.Onboarding.NotFound` when the caregiver id is unknown;
  `Caregivers.Onboarding.CertificateNotFound` when the caregiver exists but the
  certificate id is unknown; `Caregivers.Onboarding.CertificateVersionNotFound`
  when the version id is unknown or has been logically removed (its retained
  bytes are no longer downloadable).
- `404 Storage.File.NotFound` — the stored file is missing.

Certificate metadata (type, expiry, status, review reason) and the per-version
Current / Pending / History list — each with its own authorized `fileLink` — are
in the detail response; never construct or share file URLs outside these
endpoints. Caregiver self-service onboarding is documented under
`docs/app/caregivers/`.

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

## Caregiver name+ID lookup

Resolve a caregiver name to the caregiver id the review routes expect, without
reading the review list (which also publishes `phoneNumber`) and without any
contact permission being widened.

```http
GET /api/v1/admin/caregivers/lookup?search=Ahmed&page=1&pageSize=20
Authorization: Bearer {{accessToken}}
```

Access is the dedicated **`CaregiverLookupAdmin`** policy — lookup-only and
deliberately **not** the shared review policy this guide describes
(`SuperAdmin`/`FinanceAdmin`, `Normal` access only). `SupportAdmin`'s prior
lookup access is **removed**: the review and ratings surfaces keep the shared
`CaregiverReviewAdmin` policy unchanged, so this change affects the lookup
route only. `ContentAdmin` and every app audience receive `403`; an anonymous
or restricted-verification caller is `401`.

| Query | Default | Meaning |
|---|---|---|
| `page` | `1` | Page number; clamped to at least `1` |
| `pageSize` | `20` | Default `20` when `0` or absent; capped at `100` and floored at `1` |
| `search` | `–` | Case-insensitive substring of the Arabic or English full name. Blank and unmatched searches both return a page; no maximum length is imposed |

```json
{
  "items": [
    {
      "id": "guid",
      "arabicFullName": "أحمد محمود",
      "englishFullName": "Ahmed Mahmoud"
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 1,
  "totalPages": 1
}
```

- `id` is the caregiver aggregate id (`caregivers.caregivers.id`) — the id every
  route in this guide takes — and **not** the Identity user id.
- Each item carries only `id`, `arabicFullName` and `englishFullName`. Phone and
  email are not searchable and not returned: phone lookup stays on the
  `SuperAdmin`-only admin user list, and this route adds no new contact
  permission. Bank, payout and medical columns are never selected.
- Caregivers whose Identity user row is missing (legacy rows) are excluded from
  the lookup because a name lookup cannot name them. The rows are **not**
  deleted and the existing list's `identityPresent` flag still reports them.
- Ordering is deterministic and server-side (Arabic name, English name, then
  caregiver id); filtering, counting and paging stay in SQL. Invalid paging is
  clamped and an unmatched search is an empty page, so the route has no `400`
  case (a page number far past the last page is an empty page too, never an
  error).
- `search` is matched **literally**: `%`, `_` and `\` in the caller's text match
  those characters, they are never treated as wildcards.
- Runnable requests (authored examples, not execution evidence):
  `docs/postman/admins/Sanad.Admin.CaregiverNotes.postman_collection.json`
  (`Caregiver name+ID lookup`).

