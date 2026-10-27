# Document lifecycle phase — 2026-10-27 (care-home documents + same-concept caregiver/medical certificates)

Phase state: `in_progress` (implementation actually started 2026-10-27 after
parent acceptance of the preparation handback). Release state: `authorized` —
Owner explicitly authorized implementation, review, test, and immediate
commit/push/deploy right away after review, well-documented; **post-release
validation against the release is explicitly authorized and will arrive in a
separate Owner message** (Task C records this; do not act on it before that
message). Parent Mastermind: review only. This brief is the single active
brief; the Mastermind handoff links it. **This brief is the APPROVED PUBLIC
active brief: it is included in the release** (docs + Postman ship together
with the code). Only `docs/internal/Mastermind_Handoff.md` is private and
unstaged.

Sessions (actual): Task A worker `ses_e81f1013dffednn3WvSUUq` (River GLM 5.3
Flash `#high`, care-home implementation + sole brief writer); Task B worker
`ses_e81f09539ffejW8OgfjhJBJQKU` (independent; read-only discovery finished,
implmentation dispatched — do not poll or duplicate its source). Pinned
revision at preparation: `85503ec0`
(`85503ec04da8bf541fe6690ff00cba878643dba2`), matching `origin/main`; previous
slice live release `b302a3fc` (rehearsal, deployed 2026-10-27). Git state at
preparation: only the private `Mastermind_Handoff.md` modification plus the
known untracked build/test output debris and `.opencode-audit-tmp/` — preserve
all of it.

## Saved approval scope (exact Owner request/recommendation)

- Phase approved 2026-10-27: **care-home document separation into
  current-approved vs pending-replacement/history with an expired indicator,
  AND the same concept for medical/caregiver certificates; posts are OUT of
  scope.**
- Owner explicitly authorizes the workers' implementation, review, test,
  commit, push, deploy right away after review, well-documented. Parent
  Mastermind does review only.
- Owner scope addition (2026-10-27, verbatim intent): *"Also please add an
  option that they can delete a certificate from the history"* — applies to
  **BOTH** care-home documents (Task A) and caregiver/medical certificates
  (Task B, relayed by parent). Minimal **history-only logical removal**;
  audit retention; **no irreversible purge**; old files preserved.
- Expiry clarification (OWNER OVERRIDE of the preparation wording): the
  expiry **date** is independent of verification status. `IsExpired =
  supplied ExpiryDate < CairoToday` (except a true stored
  `VerifiedNonExpiring`, which is never expired). It can be `true` for
  pending/rejected/history documents — a pending certificate with a past
  supplied expiry date must NOT read as reassuring. `Status` remains the
  separate, unchanged verification/approval field, which is what prevents a
  pending document from presenting as approved. An expired CURRENT document
  stays `current` with `IsExpired=true`; NO fallback selection of an older
  valid history document. A missing date is honest "no expiry recorded" and
  is never represented as admin-verified non-expiring unless the stored flag
  says so.
- Parent-resolved choices (2026-10-27):
  1. **Additive per-document flags + dedicated filtered list endpoints; no
     duplicate embedded groups.** Existing `Documents` arrays preserved as-is
     (owner: all insertion-ordered; admin: all newest-first) with new additive
     fields per document.
  2. New list GET default = **ALL with labels**; optional `view`
     = `current|pending|history` and `type` filters; unsupported values
     return validation **400**, never a silent empty result; optional
     `isExpired` bool filter. **No fourth mutually-exclusive lifecycle value
     is invented** — expired stays an independent overlapping flag.
  3. Caregivers migration IS needed (replace-overwrite currently hard-deletes
     the old scan): approved authoring = additive version child table +
     logical removal — **Caregivers module only**, authored by Task B,
     reviewed by the parent before any apply.
- Unchanged: auth model, private storage, audit (no hard delete of documents
  or binaries), no new approval policy, no scheduler, no new booking policy.
  Admin LIST stays unchanged; admin DETAIL and owner self-profile get the
  additive version/expiry/pending views; old arrays kept.
- Constraints: no persistent writes/runtime/file uploads/env secrets/provider
  contact/commit/push/deploy NOW (integration later per Task C); no
  dotnet build/test until A and B are frozen — authored checks listed for the
  parent; docs validations allowed. No new framework/ledger/posts/refunds/
  HMAC rotation/infrastructure changes. Task B scope is Caregivers
  source/controllers/tests/docs+Postman + migration/snapshot ONLY — no shared
  API policies/DI/admin-overview edits; needs reported via parent.

## Actual flows inspected (preparation evidence, revision `85503ec0`)

- Upload (owner): `POST /api/v1/care-homes/facilities/mine/documents`
  (`CareHomeFacilitiesController`, policy `CareHomeOwnerAccess`) →
  `UploadCareHomeDocumentCommandHandler` → `facility.UploadDocument(...)`.
  Upload is refused while `PendingReview`/`Suspended`; otherwise it **appends**
  a new `CareHomeDocument` (status `PendingReview`) tied to the current
  **editable (unfrozen) revision**. New storage file via
  `IFileStorage.SavePrivateAsync`; on any domain/EF failure the just-saved
  file is deleted. PDF/JPG/PNG ≤ 10 MiB; storage key never leaves the server.
- Clone-on-edit: `SaveDraft`/`PrepareProfileMediaRevision` clone every
  document of `ApprovedRevisionId` via `CareHomeDocument.Clone`, copying
  status (incl. `Verified`), `ExpiryDate`, `VerifiedNonExpiring`, **and
  `CreatedOnUtc`** (tie source) with a fresh v7 id and the new
  `ProfileRevisionId`.
- Submit: freezes the editable revision, sets `SubmittedRevisionId`, requires
  one document of each of the four required types in the submitted revision.
- Document review (admin): verify/reject only touch documents of
  `SubmittedRevisionId` while the facility is `PendingReview`. `Status` enum
  is `PendingReview=1, Verified=2, Rejected=3, Revoked=4` — **`Revoked` has
  no authoring path anywhere** (grep-proven at `85503ec0`); reserved value,
  never surfaced as produced data.
- Facility review: approval requires every required type usable
  (`IsUsableOn(currentDate)`); reactivation re-checks the approved revision.
- Reads: owner `GET /api/v1/care-homes/facilities/mine` maps **all** documents
  (aggregate insertion order); admin `GET /api/v1/admin/care-homes/{id}` maps
  **all** documents `OrderByDescending(CreatedOnUtc)` (no Id tiebreak today —
  the additive classifier is deterministic: `CreatedOnUtc` then `Id`; the
  existing array ordering is left byte-compatible).
- File read: admin document file endpoint fails closed unless facility status
  ∈ {PendingReview, Approved, Suspended}. **No owner document-file endpoint
  existed** — Task A adds the scoped owner route (own facility only).
- Cairo clock: `Africa/Cairo` via `CareHomePublicEligibility.CairoDate`, on an
  injected `IDateTimeProvider` (server clock — never a browser-provided
  date). Every application read/mutation of the new flags uses the injected
  clock; no path derives the comparison date from client input.
- Status mapping: `ResultProblemDetailsMapper` is the coded status authority
  (mapped codes below were added for the new errors).

## Approved contract — Task A (care-home documents), as implemented

### Classification (application-level, no domain status change, no client trust)

Shared deterministic mapper in `CareHomes.Application.Facilities`
(`CareHomeDocumentLifecycle`), recomputed on every read **and at mutation
time** (the removal gate never trusts a client-supplied view flag):

- Active documents = all facility documents with `RemovedOnUtc == null`
  (logically removed rows vanish from every view, list, and file read).
- `LifecycleView` — one of exactly `"current" | "pending" | "history"`
  (string, lowercase, documented; NOT a new enum, NOT a status replacement):
  - **current**: per required `Type`, the effective approved slot. Within
    `ApprovedRevisionId`: newest document of the type by `CreatedOnUtc` DESC
    then `Id` DESC (the established domain comparator; clone `CreatedOnUtc`
    ties resolved by id). If that newest slot document is `Rejected`/`Revoked`
    (no production authoring path reaches it — the review gates refuse a
    negative latest and `Revoked` is reserved — but legacy/stored data states
    are explicitly supported) there is **no current for that type: an older
    Verified document is never resurrected**.
    **Review round 1 hardening (parent, 2026-10-27):** this effective
    negative verdict is computed over the slot's **full** row set including
    logically removed rows, in BOTH the classifier and the domain
    approval/reactivation latest-per-type gates. Removing the negative
    document from history therefore retains the "no current" verdict and can
    never unblock an eligibility gate — no hidden security-bypass surface.
    Otherwise current = the newest **Verified** document of the type in the
    approved revision — unchanged even when expired (`IsExpired=true`
    documented), and a newer **pending** candidate never displaces it.
  - **pending (candidate)**: the target revision = `SubmittedRevisionId`
    while `PendingReview`, else the unfrozen editable revision (may be
    null). Per type, the newest target-revision document by the same
    comparator is the candidate **regardless of status** — a `Verified`
    candidate inside an unapproved revision is still `pending`
    (candidate-of-revision, not only `PendingReview` status), and the latest
    **Rejected** candidate stays visible here with `NeedsAction=true`
    (awaiting correction, never lost in history). Superseded same-type
    target-revision documents are `history`.
  - **history**: everything else (superseded uploads in any revision,
    older revisions, rejected docs of past submissions) with authored
    status/expiry verbatim.
- `IsExpired` = `!VerifiedNonExpiring && ExpiryDate.HasValue &&
  ExpiryDate < CairoDate(injectedClock.UtcNow)`. Independent of `Status`
  (pending/rejected/history can be expired — no false reassurance); missing
  date → `false` (unknown ≠ non-expiring unless the stored flag says so);
  never a fourth lifecycle value; no stored status is added.
- `NeedsAction` = `true` only for a `pending`-view candidate whose stored
  `Status` is `Rejected`; additive and documented.
- `ReviewTargetRevisionId` added additively at the end of BOTH
  `CareHomeOwnerProfile` and `CareHomeAdminApplicationDetail` = the target
  revision above. The client uses it to never act on a historical revision;
  domain scoping (`SubmittedRevisionId`-only verify/reject) is unchanged and
  remains the enforcement.
- Facility eligibility/review domain semantics are untouched; the
  representation never declares an approval gate passed when the actual
  evaluation fails.

### Read endpoints

- Existing detail readbacks keep their arrays and ordering byte-compatible
  (minus logically removed rows) — each element gains
  `lifecycleView`, `isExpired`, `needsAction`; the profile/detail payloads
  gain `reviewTargetRevisionId`. No embedded duplicate groups.
- **New** `GET /api/v1/care-homes/facilities/mine/documents`
  (owner, `CareHomeOwnerAccess`) and
  `GET /api/v1/admin/care-homes/{careHomeId}/documents`
  (admin, `CareHomesOperationalAdmin`): default `view=all` (every active
  document, labeled), optional `view=current|pending|history`,
  optional `type` (actual `CareHomeDocumentType`, accepted as number or
  enum name), optional `isExpired=true|false`. Unknown `view` → coded
  `400 CareHomes.Document.InvalidView`; undefined `type` value → coded
  `400 CareHomes.Document.InvalidType`. Response
  `{ view, type, isExpired, totalCount, items[] }`, items newest-first
  (`CreatedOnUtc` then `Id`). Aggregate-load + in-memory classify (no new
  SQL translation surface).
- **New** `GET /api/v1/care-homes/facilities/mine/documents/{documentId}/file`
  (owner; own facility only — any facility status, current through history;
  removed or missing → 404). Admin document file behavior unchanged and not
  broadened. No storage keys or bytes in any DTO.

### History-only logical removal (Owner scope addition, Task A side)

- **New** `DELETE /api/v1/care-homes/facilities/mine/documents/{documentId}?expectedVersion=N`
  (owner, own facility) and
  `DELETE /api/v1/admin/care-homes/{careHomeId}/documents/{documentId}?expectedVersion=N`
  (admin, existing policy). `expectedVersion` mirrors the house DELETE
  pattern (`RemoveCareHomeProfileMediaCommand`); the facility aggregate
  `Version` token stays authoritative.
- Semantics: the server recomputes the classification at mutation time.
  **Only `history`-classified documents are removable.** The current
  effective approved document is protected **even when expired**; active
  pending candidates (including the latest rejected candidate awaiting
  correction) are protected. Protection → coded
  **`409 CareHomes.Document.NotRemovable`**; unknown document or another
  facility's id → coded `404` (owner sees only its own facility — no
  cross-facility enumeration); stale version → existing
  `CareHomes.Document.Conflict`/`CareHomes.Admin.Conflict` 409; admin 401/403
  unchanged.
- **Idempotent**: a repeated authenticated DELETE of an already-removed owned
  document returns the same `204` (no second audit write, no version error).
- Persistence: logical only — new additive nullable columns
  `removed_on_utc`, `removed_by_user_id` on the care_homes `documents`
  table (NONDESTRUCTIVE migration trio hand-authored, parent-reviewed
  **before apply**; no persistent write until the Task C release plan and
  backups). **No physical deletion ever**: `IFileStorage.DeleteAsync` is
  never called by removal; bytes stay shared with any live clone/reference.
  Audit row retained; no removed-user metadata is exposed in responses
  (bare `204`).
- Reads: removed rows disappear from the existing `Documents` arrays, both
  new lists, the new owner file route, and are excluded from classify input.
  Domain fences keep them inert: `Clone` refuses removed sources (clones
  never copy removed docs — approved-active clones preserved),
  `Submit`/approval/reactivation gates and document verify/reject lookups
  ignore removed rows; direct verify/reject of a removed row fails closed
  (existing `InvalidOperation` 409) — no reanimation. No fallback
  resurrection is introduced anywhere; public discovery eligibility is
  untouched.

### Task A test plan (authored; gate runs after A+B freeze)

`tests/Sanad.UnitTests/CareHomes/CareHomeDocumentLifecycleTests.cs` (aggregate
+ in-memory handler doubles, matching existing patterns):

1. Clone tie: equal `CreatedOnUtc` from clone — deterministic comparator and
   stable admin ordering by Id.
2. Approved + replacement pending: current stays visible; candidates stay
   `pending` (incl. Verified-in-unapproved-revision, Rejected with
   `needsAction`); superseded target same-type → history.
3. Replacement approved: new doc becomes current, old approved becomes
   history (exact ids).
4. Expired axis: `ExpiryDate == cairoToday` → false; day after → true;
   non-expiring → never; **pending with past supplied date → true** (no
   false reassurance); missing date → false; expired current keeps
   `current` with no history fallback; rejected-in-approved-slot → no
   current (no resurrection).
5. Removal: owner removes a history row → 204, idempotent repeat 204, row
   hidden from arrays/lists/file, storage delete never invoked, shared
   clone reference still served through its live id; current-even-expired →
   `409 NotRemovable`; active pending candidate (incl. latest rejected) →
   409; wrong version → conflict; other owner's document id → 404 facility
   not found.
6. Backcompat: owner array order/contents unchanged; admin array
   newest-first unchanged; default list == labeled full set; invalid
   `view`/`type` → coded 400s.
7. Review/eligibility non-regression: submit/approve/reactivate with
   removed history rows behaves exactly as before (removed rows ignored).
8. (Review round 1) **Negative newest approved-slot**: forced legacy state —
   newest approved-revision license Rejected over an older Verified one —
   before: no current for the type; the negative row is history and
   removable; after its removal: current must STILL be none, the older
   Verified stays history, and the suspended→reactivated gate must FAIL
   (domain latest-per-type includes the removed negative row — no rescue,
   no hidden bypass). The state is simulated by forcing the stored status
   because no API authoring path can produce it.
9. (Review round 1) **Expired current is not removable**: documented-current
   expired health document → `409 NotRemovable`, row untouched, still
   current on re-read.
10. (Review round 1) **Route-level checks**
    (`tests/Sanad.UnitTests/API/CareHomeDocumentRouteTests.cs`): the four
    new routes' unauthenticated 401 branches, token-actor propagation into
    list/file/remove commands, coded problem mappings via the real
    `ResultProblemDetailsMapper` (404 DocumentNotFound, 400 InvalidView,
    409 NotRemovable, 404 ApplicationNotFound), success envelopes (200 list,
    204 remove, streamed file), and reflection asserts that
    `CareHomeOwnerAccess` / `CareHomesOperationalAdmin` policies guard the
    two controller classes (policies themselves run in middleware).

## Task B: medical/caregiver certificates — approved contract (implemented by `ses_e81f09539ffejW8OgfjhJBJQKU`)

Discovery result (reported to the parent): current PUT replacement
**overwrites** the stable certificate row and **hard-deletes** the old scan
binary. Approved implementation (B owns it end to end; parent relays nothing
else to A):

- **Stable qualification ids**: certificate id stays stable across
  replacements; up to 5 `AdditionalType` certificates supported.
- **One pending replacement per certificate**; the valid approved certificate
  remains the effective current one while a replacement is pending
  (availability not suspended). **Expired or revoked ⇒ NOT qualified** for
  booking eligibility.
- **Targeted version review**: review pins the exact scan version it
  approves (concurrency guard so admin can never approve the wrong scan).
- **Additional-type removal = logical/history-retaining** (same principles
  as Task A removal); mandatory-certificate removal rules preserved.
- **Nullable expiry**: no-date accepted and labeled honestly ("no expiry
  recorded") — never presented as admin-verified non-expiring; the same
  `IsExpired = date < CairoToday` ownership-override semantics apply.
- Reads: own/admin private history **files are not public**; admin LIST
  unchanged; admin DETAIL + caregiver self-profile get additive
  version/expiry/pending views; old certificate arrays kept.
- **Migration (approved)**: additive version/child table so old scans are
  retained — NONDESTRUCTIVE, Caregivers module only, hand-authored + lazy
  legacy snapshot behavior; nothing applied until the Task C release plan
  with backups; reviewed before apply.
- B scope boundaries: Caregivers source/controllers/tests/docs+Postman +
  migration/snapshot ONLY. A does not edit B source and vice versa; B does
  not edit this brief (A is the single brief writer and records B's
  handback statuses when the parent relays them).
- Integrated Task C: after A and B are frozen, one serialized release
  executes the pre-authorized commit → push → deploy with established gates
  (strict Release 0/0, focused tests, docs+Postman for both consumers),
  migration apply only after review, with backup posture. **Post-release
  validation against the release is explicitly authorized by the Owner and
  defined in a separate Owner message** — not yet received; do not act on it
  until it lands.

## Unresolved material choices

None material outstanding. (Preparation's three choices were resolved by the
parent as recorded above; the expiry semantics were settled by the Owner's
override; Task C's post-release validation scope arrives in the Owner's
separate message.)

## Current result

- Task A implementation **authored complete** by `ses_e81f1013dffednn3WvSUUq`
  (this session) — no build/test run (source freeze until A+B), no persistent
  writes, no commit:
  - Domain: `CareHomeDocument.cs` (`RemovedOnUtc`/`RemovedByUserId`/`IsRemoved`
    + `Remove`, clone guard), `CareHomeFacility.cs` (removed-row filters in
    approved-clone paths, `Submit`, verify/reject lookups, approval/reactivation
    gates; `RemoveOwnDocumentHistory`/`RemoveDocumentHistory`/private
    `RemoveDocument` with version token).
  - Application: `CareHomeDocuments.cs` (additive DTO fields,
    `CareHomeDocumentViews`, `CareHomeDocumentErrors` (`NotRemovable`/
    `InvalidView`/`InvalidType`), internal `CareHomeDocumentLifecycle`
    classifier, `IDateTimeProvider`-clocked upload, owner/admin list queries +
    shared mapper, owner file query, owner/admin removal handlers on TRACKED
    aggregates with idempotent short-circuit before the version check);
    `CareHomeProfileCommands.cs` (additive `ReviewTargetRevisionId`,
    classified owner projection, clock injection);
    `AdminCareHomeApplications.cs` (additive fields, classified newest-first
    projection minus removed rows, clock injection).
  - Controllers: owner `GET mine/documents`, `GET mine/documents/{id}/file`,
    `DELETE mine/documents/{id}?expectedVersion=`; admin `GET {id}/documents`,
    `DELETE {id}/documents/{id}?expectedVersion=`; `ResultProblemDetailsMapper`
    entries (`DocumentNotFound` 404, `NotRemovable` 409,
    `InvalidView`/`InvalidType` 400).
  - Migration trio (hand-authored, NONDESTRUCTIVE, review-before-apply):
    additive nullable `removed_by_user_id`/`removed_on_utc` on care_homes
    `documents` — `20261027120000_AddCareHomeDocumentHistoryRemoval.cs` +
    `.Designer.cs` (newest existing designer's prologue + new snapshot body,
    parity-checked; occurrence counts 1/1/1/1 property, 2 partial-class,
    1943 lines) + `CareHomesDbContextModelSnapshot.cs`; EF configuration maps
    the columns. Nothing applied.
  - Tests: `tests/Sanad.UnitTests/CareHomes/CareHomeDocumentLifecycleTests.cs`
    — 11 focused authored tests (current-survives-pending + insertion-order
    backcompat; Cairo expiry boundary `==today` false / next-day true /
    expired-stays-current; pending-with-past-date `isExpired` honesty;
    rejected-candidate `needsAction`; multiple-verified newest-current;
    logical-removal matrix — idempotent replay, protected current/pending 409
    `NotRemovable`, stale-version `Conflict`, cross-owner 404, storage delete
    never called, removed rows hidden from arrays/list/file while the live
    current still serves through its own id; clone-skip of removed rows +
    review-gate non-regression (suspended→reactivated still Approved) +
    fail-closed re-review of a removed row; list validation codes owner+admin;
    admin list count parity;
    **review-round 1: negative-newest-approved-slot no-resurrection**
    (forced legacy Rejected latest over an older Verified — no current before,
    still none after the negative row's history removal, older Verified stays
    history, suspended→reactivated FAILS because the domain latest-per-type
    gate includes the removed negative row; **expired current is refused**
    `409 NotRemovable`, row untouched, still current).
    Plus `tests/Sanad.UnitTests/API/CareHomeDocumentRouteTests.cs` — the four
    new routes: unauthenticated 401 branches, token-actor propagation into
    list/file/remove requests, coded problem mappings through the real
    mapper (404 `DocumentNotFound`, 400 `InvalidView`, 409 `NotRemovable`,
    404 `ApplicationNotFound`), success envelopes (200 list, 204 remove,
    streamed file), and reflection asserts that
    `CareHomeOwnerAccess`/`CareHomesOperationalAdmin` guard the controller
    classes. Existing handler-ctor call sites patched across
    6 test files (74 + 1 leftover fixed) via shared
    `tests/Sanad.UnitTests/CareHomes/CareHomeTestClock.cs` + per-file
    `CareHomeTestClockHost`; count-verified, no whole-record equality
    assertions. **All test/code assertions NOT RUN** (freeze).
  - Docs + Postman (both consumers): `docs/care-homes/owner-onboarding.md`
    (new "Document lifecycle views, file read, and history removal" section +
    upload-section additive fields + Postman note) and
    `docs/admin/care-homes.md` (detail additive fields + new "Document views
    list and history removal" section); Postman owner collection gains
    list/current-view/file-read/remove-history requests, admin collection gains
    list + remove-history requests — both JSON re-validated as parseable.
- Task B implementation **in progress** by `ses_e81f09539ffejW8OgfjhJBJQKU`
  under the approved contract above; status handbacks recorded here via the
  parent when relayed.

### Review round 1 (parent review of the handback, 2026-10-27) — fixed

- **Critical classifier edge fixed**: newest Rejected/Revoked approved-slot
  document labeled `history` was removable, and removal could resurrect an
  older Verified document as current and unblock the reactivation gate. Fix
  (chosen: retain the negative-audit decision for effective-state calc —
  no protected-history special case, so the API surface is unchanged):
  `Classify` now receives the facility's FULL document set; the effective
  newest approved-slot state (no-current-on-negative) is decided over all
  rows including removed ones, in the classifier AND in the domain
  approval/reactivation latest-per-type gates (`CareHomeFacility.Review`)
  which now group over the full revision row set while the type-presence
  clause stays active-only. View/pending assignment still covers active rows
  only — removed rows remain invisible in every serialized view. Chosen in
  exactly the parent's Option-A shape: audit semantics decide effective
  state independent of list visibility; no `CanRemove` API flag needed and
  no deletion-dependent security gate.
- Verified (authored reads, NOT executed build): `CareHomes.Document.Conflict`
  maps to 409 in `ResultProblemDetailsMapper` (line ~85) and
  `CareHomes.Admin.Conflict` pre-exists at 409; the facility `Version`
  property is a real concurrency token
  (`builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken()`)
  so the handler's `DbUpdateConcurrencyException` path is backed by the
  registered token, and `expectedVersion` is checked against it.
- Shared-file discipline: `ResultProblemDetailsMapper.cs` edits remain only
  the four Task A keys (DocumentNotFound/NotRemovable/InvalidView/InvalidType);
  no Caregivers keys touched; B-merged code left intact.
- Scope: this round touched CareHomes module files + the two consumer docs +
  the two consumer Postman collections ONLY — no Caregivers code.
- Still frozen: no build/test, no staging/push/deploy/persistent migrate;
  EF designer/snapshot validity is pending the phase gate (hand-authored
  trio is count-verified, not EF-generated).

## Task B handback (round 2 final, 2026-10-27) — accepted, sources frozen

Task B implementer `ses_e81f09539ffejW8OgfjhJBJQKU` handed back with both
source trees frozen. Parent-reviewed fixes recorded:

- **P1 (transaction)**: demote-save/promote-save runs through
  `ICaregiversDbContext.ExecuteInTransactionAsync` with an infrastructure
  relational guard; a failure in the second save rolls back the first
  (commit once, rollback on exception).
- **P2 (effective snapshot)**: `EffectiveSnapshotVersion` = Current → else
  Pending → else newest *retained* version (a removed version can never be
  the effective fallback); `CanRemoveHistory` excludes the snapshot's
  effective version; the fallback re-read targets the correct version and the
  DTO carries `CanRemove`.
- **P3 (legacy states)**: explicit legacy 409; missing id → 404; known id not
  pending → 409.
- B migrations unchanged (`20261027120000_AddCaregiverCertificateVersions` +
  `.Designer.cs`, snapshot updated). Interface change touched 4 files;
  provisioner/test fakes adapted. B tests authored-not-run, including
  `CaregiverCertificateVersionsPostgresTests` (real Postgres; rollback
  primitive + demote/promote second-save rollback + revision-race regressed).
- Review round 1 negative-audit status semantics for B match Task A's shape:
  a negative-audit latest remains the effective decision after removal (no
  resurrection) — reviewer-accepted.

**Phase transition (2026-10-27, parent)**: freeze lifted for the integrated
gate. This session (`ses_e81f1013dffednn3WvSUUq`) now owns the serialized
gate + release + post-release verification, to completion:
strict 0-warning build → focused + full unit suites → architecture +
provisioner → EF validity on disposable nonce Postgres → B real-PG facts →
docs/Postman/secret checks → single release commit + push → Staging backup →
publish exact pushed SHA → apply the two approved migrations → live
verification → Owner-authorized post-release tests → final send-ready
response doc + docs-only receipt commit.

## Integrated gate results (2026-10-27, this session) — PASSED

All runs foreground/serial; build first, tests after the final compiled fix.

- **Strict build** `Sanad.slnx -warnaserror`: **0 warnings / 0 errors** (full
  tree, after gate fixes below).
- **Focused batches (all green)**: A lifecycle+route+real-HTTP policy
  17/17; pre-existing CareHomes (clock-patched) 59/59; B certificates/
  discovery/payout 124/124; API/auth/metadata/mapper 169/169.
- **Architecture tests** 1/1; **PortalProvisioner tests** 75/75 (compiled
  interface change compatibility verified).
- **EF validity** (harness outside the repo, no prod connections):
  `PendingModelChanges` = False for BOTH contexts (hand-authored snapshots
  match compiled models); CareHomes 20 / Caregivers 12 migrations declared,
  new heads `20261027120000_AddCareHomeDocumentHistoryRemoval` /
  `20261027120000_AddCaregiverCertificateVersions`; generated old→new SQL
  contains ZERO DROP/ALTER COLUMN/TRUNCATE/UPDATE statements (CareHomes: two
  nullable ADDs; Caregivers: three columns + new versions table + composite
  PK + partial unique index `ux_caregiver_certificate_versions_current`).
- **Nonce apply flows** (disposable local Postgres 18, exact prefix names,
  old-head → seed legacy row → new-head):
  CAREHOMES PASS (facility/revision/document preserved — 17 document
  columns intact, removal columns NULL — no fake history);
  CAREGIVERS PASS (12 certificate columns preserved, `is_removed=False`,
  `removed_on_utc NULL`, `revision=0`, `LEGACY_VERSION_ROWS=0`).
- **B real-Postgres** `CaregiverCertificateVersionsPostgresTests`:
  **4/4 PASSED, 0 skipped** (rollback primitive, second-save demotion
  rollback, revision race, partial-unique + cascade); self-created
  `SanadCertVersionVerify_<guid>` cleaned up (0 remain).
- **A real-Postgres readback** `CareHomeReadbackPostgresTests`:
  **7/7 PASSED** after correcting the stale committed baseline — all 20
  CareHomes migrations ARE recorded in the shared history table (the old
  comment claiming the empty-Up `20261003222617` is not recorded was false);
  head pin updated to the new head.
- **FULL unit suite (final compiled state)**: **5887 passed / 0 failed /
  27 skipped / 5914 total**. Skips are the standing env-conditional
  real-PG pattern (13 CareHomeFinance, 3 Families SubscriptionTax, 4
  Identity VerificationContinuity + 7 CareHomeReadback which RAN green in
  the dedicated real run above; none of the phase's tests skip).

### Gate fixes (bounded, same-phase, no skip/hide/move)

- `Classify` signature widened to `IReadOnlyCollection<CareHomeDocument>`
  (facility.Documents type) — compile fix only.
- Shared `CareHomeTestClockHost` stub added (UtcNow semantics) for
  clock-injected handlers at untouched call sites; one missing using.
- Lifecycle test: the open revision legitimately holds BOTH the license
  clone (SaveDraft clones every approved document — product-by-design) and
  the new upload; replacement picked by newest-first comparator.
- B `ReplaceCertificateFile_ShouldRetainPreviousFileAsHistory`: corrected to
  the approved contract — the approved certificate REMAINS the effective
  current one while the replacement pends (Verified, not Pending).
- xUnit analyzer findings: `Assert.Single(collection, predicate)`,
  `Assert.DoesNotContain`, `string?`, `IReadOnlyList.Count`.
- **Full-suite findings (all in previously-never-run classes), fixed as
  contract corrections**:
  - `CaregiverDomainEventTests`: replacing a VALID mandatory certificate no
    longer raises review-required / flips Active→PendingReview (contract:
    approved version stays effective, availability not suspended) — test
    rewritten to assert the contracted semantics (no event, status stays
    Active, availability unchanged, pending candidate recorded, effective
    status Verified).
  - `CaregiverAvailabilityComplianceTests`: split — revoked mandatory
    certificate still blocks availability (guarded); pending/rejected
    replacements do not unseat the approved current, so BecomeAvailable
    succeeds (contract).
  - `CareHomeDocumentRoutePolicyTests` (phase-authored): corrected against
    the real registered policy — `CareHomesOperationalAdmin` admits
    SuperAdmin + SupportAdmin (they reach the handler → 404 for unknown
    facility ids); every other account type gets 403. Owner routes deny all
    non-owner types with 403 (verified unchanged).
