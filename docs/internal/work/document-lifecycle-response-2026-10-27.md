# Document lifecycle release response — 2026-10-27
# Care-home documents (Task A) + medical/caregiver certificates (Task B)

Phase: `document-lifecycle-2026-10-27` — integrated gate **PASSED**, release
executed under the Owner-authorized serialized plan (Task C). This document
is the send-ready English matrix of both shipped contracts, the migration
and legacy-data facts, the limitations that are **not** promised, and the
post-release verification record.

- Baseline commit: `85503ec0` (matches the pre-phase remote pin).
- Release commit: see "Release receipt" below (exact SHA filled at push).
- Deployed target: Staging (srv1604711 / 72.62.92.144, unit `Sanad-api`,
  DB loopback:5433 `SanadDb`, app Staging #7 identity seam) — unchanged
  configuration posture: no seam/CORS/proxy/HMAC/provider/config changes,
  no new accounts.

---

## 1. Task A — care-home document lifecycle (shipped contract)

### 1.1 Classification (representation only — no domain status change)

A shared deterministic classifier (`CareHomeDocumentLifecycle`) is recomputed
on every read **and at mutation time** (the removal gate never trusts a
client-supplied view flag):

- Logically removed rows vanish from every view, list, file read, and
  classification input.
- `lifecycleView` ∈ `"current" | "pending" | "history"` (additive field on
  every document item):
  - **current** — per document `Type`, the effective approved slot: the
    newest document of the type in `ApprovedRevisionId` by
    `CreatedOnUtc` DESC then `Id` DESC. If that newest slot document is
    `Rejected`/`Revoked` (legacy/stored state only — no authoring path
    produces it), there is **no current for that type**: an older Verified
    document is **never resurrected**. This verdict is computed over the
    slot's FULL row set including removed rows (in the classifier AND the
    domain approval/reactivation gates), so removing the negative row can
    never unblock an eligibility gate. Otherwise current = the newest
    Verified document of the type — **unchanged even when expired**, and a
    newer pending candidate never displaces it.
  - **pending** — per type, the newest target-revision document
    (`SubmittedRevisionId` while `PendingReview`, else the unfrozen
    editable revision), regardless of stored status: a Verified candidate
    inside an unapproved revision is still `pending`, and the latest
    Rejected candidate stays visible here with `needsAction=true`.
    Superseded same-type target-revision documents are `history`.
  - **history** — everything else, with authored status/expiry verbatim.
- `isExpired` = `!verifiedNonExpiring && expiryDate.HasValue &&
  expiryDate < CairoToday(injected clock)` — independent of status (a
  pending/rejected/history document can be expired; missing date → `false`;
  never a fourth lifecycle value; no stored status added).
- `needsAction` = `true` only for a pending-view candidate whose stored
  status is `Rejected`.
- `reviewTargetRevisionId` added additively at the tail of BOTH the owner
  profile response and the admin application detail. Clients use it to act
  only on the actionable revision; server-side scoping to
  `SubmittedRevisionId` remains the enforcement.

### 1.2 Read endpoints (new, additive; existing payloads unchanged plus these fields)

- `GET /api/v1/care-homes/facilities/mine/documents`
  (owner, `CareHomeOwnerAccess`) and
  `GET /api/v1/admin/care-homes/{careHomeId}/documents`
  (admin, `CareHomesOperationalAdmin` → SuperAdmin + SupportAdmin).
  - Query: `view` default `all` (every active document, labeled), optional
    `view=current|pending|history`; optional `type` (number or enum name);
    optional `isExpired=true|false`.
  - Errors: unknown `view` → `400 CareHomes.Document.InvalidView`;
    undefined `type` → `400 CareHomes.Document.InvalidType`.
  - Response: `{ view, type, isExpired, totalCount, items[] }`, items
    newest-first (`createdOnUtc` then `id`). Items carry the existing
    document fields plus `lifecycleView`, `isExpired`, `needsAction`.
- `GET /api/v1/care-homes/facilities/mine/documents/{documentId}/file`
  (owner; own facility only; any facility status; current through history;
  removed or missing → 404). Admin document file behavior unchanged and not
  broadened. No storage keys or bytes in any DTO.
- Backcompat: the existing owner `Documents` array and admin detail arrays
  keep order and contents (minus removed rows) byte-compatible; each element
  gains only the additive lifecycle fields. No embedded duplicate groups.

### 1.3 History-only logical removal (new)

- `DELETE /api/v1/care-homes/facilities/mine/documents/{documentId}?expectedVersion=N`
  (owner, own facility) and
  `DELETE /api/v1/admin/care-homes/{careHomeId}/documents/{documentId}?expectedVersion=N`
  (admin, existing operational policy).
- Semantics: classification is recomputed server-side at mutation time.
  **Only `history`-classified documents are removable.** The current
  effective approved document is protected **even when expired**; active
  pending candidates (including the latest rejected candidate awaiting
  correction) are protected.
  - Protected → `409 CareHomes.Document.NotRemovable`.
  - Unknown document / another facility's id → `404` (owner sees only its
    own facility — no cross-facility enumeration).
  - Stale `expectedVersion` → existing conflict `409`
    (`CareHomes.Document.Conflict` / `CareHomes.Admin.Conflict`).
  - Admin 401/403 unchanged (real registered policy).
- **Idempotent**: a repeated authenticated DELETE of an already-removed
  owned document returns the same `204` (no second audit write, no version
  error).
- Persistence: logical only — additive nullable columns `removed_on_utc`,
  `removed_by_user_id` on `care_homes.documents` (NONDESTRUCTIVE migration
  `20261027120000_AddCareHomeDocumentHistoryRemoval`). `IFileStorage.DeleteAsync`
  is **never** called by removal; bytes stay shared with any live
  clone/reference. Audit columns retained; no removed-user metadata is
  exposed in responses (bare `204`).
- Domain fences: clones never copy removed sources; `Submit`/approval/
  reactivation gates and document verify/reject lookups ignore removed rows;
  direct verify/reject of a removed row fails closed (409). No
  resurrection path exists; public discovery eligibility untouched.

---

## 2. Task B — medical/caregiver certificate lifecycle (shipped contract)

### 2.1 Versioned qualifications, stable ids

- A stable qualification id is kept across file replacements; up to 5
  `AdditionalType` certificates per caregiver are supported. The PUT
  replacement no longer overwrites the row nor hard-deletes the old scan.
- **One pending replacement per certificate.** The valid approved
  certificate **remains the effective current version while a replacement
  is pending** — the caregiver's availability is not suspended and no
  review-required event flips an Active caregiver back into review.
  The new upload rides along as the pending candidate for targeted review.
  (Expired or revoked ⇒ NOT qualified for booking eligibility — unchanged.)
- Effective-snapshot rule: `Current → Pending → newest retained version`;
  a removed version can never be the effective fallback.

### 2.2 Additive version item (owner self + admin detail readbacks)

Old certificate arrays and fields keep their exact meaning (live
qualifications only). Each certificate additionally carries its per-upload
history as `versions[]` of:

```
CertificateVersionItemResponse {
  id, expiryDate (DateOnly?), verificationStatus,
  expiryStatus, classification ("Current"|"Pending"|"History"),
  reviewReason (string?), createdOnUtc, supersededOnUtc (DateTime?),
  fileLink (authorized download path; carries no storage key),
  canRemove (bool)
}
```

- Removed history versions are hidden from the list; `canRemove` reports
  whether the specific version may be removed (the certificate's effective
  snapshot version is never removable).
- *Nullable expiry*: a version with no date is labeled honestly ("no expiry
  recorded" semantics via `expiryStatus`) — never presented as
  admin-verified non-expiring; the same `IsExpired = date < CairoToday`
  ownership-override semantics apply.

### 2.3 Version-targeted review (admin)

- Admin verify/reject/revoke pin the **exact scan version** they decide on
  (concurrency guard: an unexpected version state → coded conflict, admin
  can never approve the wrong scan).
- Coded errors (namespace `Caregivers.Onboarding.`):
  `CertificateNotFound` (404), `CertificateVersionNotFound` (404 — scoped
  so existence is never leaked across caregivers), `InvalidCertificateOperation`
  (409), `CertificateVersionNotRemovable` (409 — only a retained history
  version can be removed; the effective version is protected even when
  expired by the Cairo calendar), `CertificateConflict` (409 — server
  classification changed since read; reload and retry).
- Legacy states covered: no-pending replacement on an explicit version
  operation → 404 for a missing id, 409 for a known id that is not pending.
- Availability consequence: replacing a VALID mandatory certificate never
  suspends availability; revocation (or expiry) of the effective mandatory
  version still makes the caregiver NOT qualified and suspends exactly as
  before. A rejected pending replacement does not unseat the approved
  current.

### 2.4 Removal (logical, history-retaining)

- Additional-type removal and per-version history removal are **logical**,
  same principles as Task A: tombstone columns
  (`is_removed`, `removed_on_utc`, `removed_by_user_id` on versions), audit
  retained, no physical file deletion, bytes readable through retained
  references. Mandatory-certificate removal rules are preserved
  (mandatory certificates are replaced, not removed).
- New routes: caregiver self
  `DELETE /api/v1/caregiver/certificates/{certificateId}/versions/{versionId}`
  (history version) and version file
  `GET .../certificates/{certificateId}/versions/{versionId}/file`;
  admin mirror readbacks under
  `/api/v1/admin/caregivers/{caregiverId}/certificates/...` (admin LIST
  surface unchanged; admin DETAIL gains the additive version views).

---

## 3. Migrations and legacy-data facts (both tasks)

- Exactly **two** approved NONDESTRUCTIVE migrations, both timestamped
  `20261027120000`:
  - CareHomes `AddCareHomeDocumentHistoryRemoval` (1 of 20 CareHomes
    migrations): two nullable columns on `care_homes.documents`.
  - Caregivers `AddCaregiverCertificateVersions` (1 of 12 Caregivers
    migrations): three columns on `caregivers.caregiver_certificates`
    (`is_removed NOT NULL DEFAULT FALSE`, `removed_on_utc` nullable,
    `revision NOT NULL DEFAULT 0`) plus the new history child table
    `caregivers.caregiver_certificate_versions` (composite PK
    `(caregiver_id, certificate_id, id)` + partial unique index
    `ux_caregiver_certificate_versions_current WHERE is_current AND NOT
    is_removed`).
  - The other 7 module contexts ship ZERO migrations this release (9 heads
    total across the app).
- EF validity: `PendingModelChanges` = False for both contexts; generated
  old→new SQL contains **zero** DROP / ALTER COLUMN / TRUNCATE / UPDATE
  statements; every statement is inside a single transaction per script.
- Legacy apply proof (disposable nonce databases, old-head → seed →
  new-head): existing rows are preserved column-for-column; CareHomes
  legacy documents keep all 17 columns with removal columns NULL;
  Caregivers legacy certificates keep 12 columns with `is_removed=False`,
  `removed_on_utc NULL`, `revision=0`, and **zero fabricated version rows**
  (`LEGACY_VERSION_ROWS=0` — no fake history is materialized for legacy
  data; legacy uploads surface as their single legacy effective state until
  a replacement creates the first real version row).

## 4. Honest limitations (NOT promised)

- **Removal is logical, not physical**: no permanent-delete semantics for
  removed history. Storage bytes of retained history remain on disk and
  stay served through any live shared reference (e.g., approved clones).
  "Deleted forever" is explicitly NOT a product promise anywhere.
- **Real historical deleted files are irrecoverable**: scans actually
  deleted by the OLD pre-release replacement flow (hard-delete on PUT) are
  gone; no migration or feature recreates them. Only files existing at
  release time are managed by the lifecycle.
- **Legacy rows have no fabricated versions**: pre-release certificates and
  documents expose their legacy effective state only (single version-like
  row for Caregivers is NOT created); the version/history axis starts
  accumulating at the first post-release upload.
- The care-home lifecycle view is representation only: it never declares an
  approval or eligibility gate passed when the domain evaluation fails.
- HMAC signature surface, standard storage provider seam, CORS/proxy/
  identity seams, and payout/refund flows are untouched (separate open
  scope); no new accounts, no provider changes.
- Admin documentation-live surfaces: admin LIST payloads unchanged; admin
  DETAIL and caregiver self-profiles gain the additive views. No new
  admin POST surfaces were added beyond the approved targeted-version
  review decisions and history removal.

## 5. Gate evidence (serialized, foreground)

- Strict build `Sanad.slnx -warnaserror`: **0 warnings / 0 errors**.
- Full unit suite (final compiled state): **5887 passed / 0 failed /
  27 skipped / 5914 total** — every skip is the standing env-conditional
  real-Postgres pattern (13 CareHomeFinance, 3 Families SubscriptionTax,
  4 Identity VerificationContinuity, 7 CareHomeReadback which ran green in
  a dedicated real run); none of the phase's own tests skip.
- Focused batches: A lifecycle/route/policy 17/17; CareHomes existing
  59/59; B certificates/discovery/payout 124/124; API/auth/metadata/mapper
  169/169; architecture 1/1; provisioner 75/75.
- Real Postgres (local nonce/standing verification DBs, no prod connection):
  B `CaregiverCertificateVersionsPostgresTests` **4/4 PASSED** (transaction
  rollback primitive, second-save demotion rollback, revision race, partial
  unique index + cascade), self-created DB cleaned (0 remain);
  A `CareHomeReadbackPostgresTests` **7/7 PASSED** (baseline corrected:
  all 20 CareHomes migrations recorded — the previous 18/19 comment was a
  stale false claim).
- Secret scan of all added lines (tracked diffs + new files): **clean** —
  no DSN, token, credential, key, or URL material anywhere; the only hits
  are schema property names EF generates and prose words.

## 6. Release receipt (filled at execution)

- Pushed commit SHA: `PENDING_PUSH`
- Remote `origin/main` HEAD after push: `PENDING_PUSH`
- Pre-deploy live SHA: `b302a3fc`; pre-phase remote: `85503ec0`
- Staging backup: `PENDING_BACKUP_RECEIPT`
- Applied migrations on Staging: `PENDING_APPLY_RECORD`
- Live verification + Owner-authorized post-release tests:
  `PENDING_POST_RELEASE` (if not executed, recorded NOT RUN with reasons in
  the docs-only receipt commit)
