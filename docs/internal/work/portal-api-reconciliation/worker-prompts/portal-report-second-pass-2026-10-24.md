# Portal report second pass — approved implementation brief

## Control record

- Source: Owner-supplied backend report dated 2026-10-24, including second-pass items 1–13.
- Owner approved the proposed decisions after read-only rehearsal verification.
- Phase: `completed`. Release: `verified` (deployed and checked on the authorized rehearsal VPS, 2026-10-25). Receipt: [portal-second-pass-release-2026-10-25.md](../reports/portal-second-pass-release-2026-10-25.md). Earlier S1–S4 checkpoints below are historical evidence, including pre-correction gate counts.
- Pinned repository revision: `30e664f343cbf4b0a842d967e8439324ae124317`.
- Verified rehearsal runtime: `4690b5b2e46a311bbb64f76c41c81769a34d4832`.
- Preserve the existing modified private handoff and all pre-existing untracked audit/build artifacts. They are not phase deliverables.
- Mastermind owns scope, task acceptance, integration and release. One sequential Worker owns implementation, regression tests and affected consumer docs/Postman; pin its effective model, session and exact file manifest before dispatch. Dispatch awaits Owner direction.
- This approval covers the decisions below, including bounded fixture provisioning on the existing rehearsal VPS. It does not authorize commit/push/deploy, real refunds, account-wide clocks, database reset, or deletion of existing records.

## Verified baseline

Read-only verification session: `ses_e8fece900ffefilCAGdPR5s7jb`. No writes, provider calls, builds or tests were performed in that investigation.

- Residents default, pagination and search calls all return 500. Server exception identifies `residentIds.Contains(e.Id.Value)` as untranslatable at `CareHomeCareRecordsController.ListResidents`, failing during `CountAsync`. Name search/order also requires relational coverage; the earlier ID projection was not the observed failing expression.
- Rehearsal has one Approved facility and zero Draft facilities. Draft finance runtime behavior is unverified; source has no Approved-only admission rule.
- Rehearsal has zero caregiver bookings, claims, complaint reviews and care records; zero care-home Pending/Failed refunds or PaidAwaitingDecision bookings. One Accepted/Paid care-home booking, three Cancelled bookings, one Cancelled/Pending extension and one paid family subscription exist.
- Partner contact request requires only name/email/message; company and subject are optional/nullable. Response fields can be required keys while allowing null.
- Configured support-contact reads return 200. Source supplies nullable fields when absent; the absent-row runtime case was not tested.
- Invalid enum query values produce 400 field-array `errors` without domain `code`. Domain failures use `code`. Care-home booking filters are `bookingStatus`/`refundStatus`; an unknown `status` parameter is ignored.
- Family subscription reads inherit SuperAdmin-only plan policy; refund investigation allows SupportAdmin. Provider refund codes below fall through the central mapper to 400.
- Current inspected booking/transfer notification producers use readable text. Historical saved notifications are not rewritten; all requested fresh producers still need bounded verification.

## Locked behavior and acceptance

### A. Resident and owner finance reads

1. Repair resident query translation without widening accepted-booking/owner authorization. Preserve pagination and search contracts. Test default, pagination, nonempty search, empty results and foreign-owner exclusion against PostgreSQL, including mapped name ordering/search.
2. Owner Draft dashboard/revenue access is allowed. Return zero/empty values where no activity exists; retain real activity where present. Provision a dedicated Draft fixture and prove both reads on a relational target. Do not change the existing Approved facility to obtain coverage.

### B. Refund and administrative access contracts

1. Explicitly map `Paymob.RefundRejected` to HTTP 502 and `Paymob.RefundOutcomeUnknown` to HTTP 409, retaining stable codes. Verify affected booking refund/outcome routes and ensure existing more-specific mappings remain intact. Status alone never grants permission to retry: uncertainty must reconcile and existing ledger/state fences remain authoritative.
2. Allow SuperAdmin and SupportAdmin read-only access to family subscription list/detail needed for refund investigation. Do not widen plan authoring, publication, retirement, or money-execution permissions. Test anonymous, unrelated role, SupportAdmin and SuperAdmin boundaries.
3. Add paginated `GET /api/v1/admin/feedback/app-ratings` for SuperAdmin and SupportAdmin, following existing pagination conventions. Verify empty/nonempty reads and role boundaries. Bound response fields to rating investigation needs; do not add identity/contact disclosure unnecessarily.
4. Keep existing framework-validation and domain-problem shapes. Document both, including example field errors and code-based errors. No cross-cutting response rewrite.

### C. Names and notification text

1. Add `displayNameArabic` and `displayNameEnglish` alongside existing chat participant `displayName`; preserve old clients and authorization. Resolve names through the existing user/facility lookup paths and document nullable/fallback behavior consistent with available stored names.
2. Verify newly produced onboarding, booking-decision and chat-message notifications. Fix current raw-ID text if found; provide readable Arabic/English text selected through existing locale behavior. Keep routing identifiers structured. Do not rewrite historical inbox rows.

### D. Rehearsal fixtures and portal acceptance

Target: existing rehearsal VPS `72.62.92.144`, existing Sanad database. Verify exact server/database identity before writes. Use dedicated identifiable accounts/records and a stable fixture manifest; preserve unrelated data. Existing Development/local-only seed guards must not simply be disabled globally.

Provide an idempotent, narrowly targeted fixture mechanism for:

- Both caregiver and care-home refund Pending (unknown outcome) and Failed scenarios.
- Care-home PaidAwaitingDecision acceptance/rejection, recorded external refund, and checkout without family confirmation/dispute.
- Caregiver Confirmed and paid Completed bookings, payout record and extension.
- Duplicate claims Submitted, Approved awaiting execution and Ambiguous; a family eligible to submit a claim.
- A dedicated subscription with a current paid period for service-shutdown refund testing.
- Complaint reviews Received and Investigating.
- Family, CareHome and Caregiver care records for one dependent to prove owner read-only versus own-source edit rules.
- A Draft facility for A and private family test access for portal reviewers.

Simulate provider uncertainty/execution outcomes through fixture-only seams; do not issue refunds or attach synthetic capture references to an executable real-provider path. Preserve normal payment behavior for unrelated records. Any fixture clock advances only dedicated fixture scenarios, not the service/system clock. Define its exact mechanism and record boundary before executing fixtures.

Acceptance: repeated provisioning preserves identities and creates no duplicates; persisted state and authenticated reads match the manifest; fixture money operations are proven inert at the provider boundary; unrelated records/settings remain unchanged. Share credentials privately, never in git/log output. Record which portal actions were executed versus merely supplied for frontend testing. No reset or automatic cleanup deletion.

### E. Consumer contract reconciliation

- Keep medical audit ID-only, gateway secrets server-configured, FAQ deactivation and registration-only growth.
- Document wellness free-text categories and existing cover/image support; no controlled taxonomy.
- Remove misleading claims that an implemented care-home listing-subscription API exists. New listing billing is excluded.
- Confirm public landing storage URL/access and ordered visible reference behavior against source and focused checks; correct docs only where needed.
- Reconcile affected response annotations, reason length limits, numeric visit days, registration/verification examples, library field validation and nullable support contact. Use exact operation inventory before edits; do not claim all report gaps fixed without checking each.
- Preserve items 3, 4 and 5 and delivered `identityPresent`; avoid rebuilding completed functionality.

## External dependencies and separate actions

- Public Terms and Privacy require content-owner supplied/approved text before publication. Do not invent legal content or publish placeholders.
- Produce a read-only exact orphan-profile deletion candidate list with references/impact. Deletion requires separate confirmation of that list.
- Obtain mobile-team evidence that the released binary sends `claimSubmission: true`. Backend tests cannot certify an installed client; preserve the fail-closed legacy migration behavior.
- These dependencies do not block independent implementation. Mark unresolved acceptance evidence explicitly.

## Execution ownership and stages

| Task | State | Scope / handback |
|---|---|---|
| S1 | completed | Locally accepted by Mastermind after correction review: literal trimmed search, PostgreSQL evidence, HTTP authorization boundaries and refund annotations. Not deployed. |
| S2 | completed | Locally accepted after one doc-accuracy correction (closure-reason bound/status in `docs/care-homes/visits.md`). Not deployed. |
| S3 | completed | D: fixture-only design boundary, idempotent implementation, dedicated scenario provisioning and authenticated readbacks. S3a handed back `ready_for_review` and accepted after one correction round. S3b executed per the Owner's authorization: all 14 approved DATA-ONLY groups provisioned on the rehearsal cluster through the SSH tunnel with verify/identity guards passing, ×2 idempotency proven, and authenticated readback clean — completion gate (all 14 provisioned AND read back clean) MET; see `S3b worker checkpoint`. |
| S3c | completed | Accepted by Mastermind after final correction review: ambiguous-claim reruns skip re-execution; actual monitored-option validation fails closed; real-host reload tests use the offline client; empty allowlist and Production enablement are refused. Strict build 0/0, full unit suite 5596/0/27 skipped, provisioner 37/37. G1–G4 remain unprovisioned. See final release review and detailed worker evidence below, including the disclosed rejected sandbox request. |
| S4 | completed | Mastermind review, integrated checks, report-item disposition, unresolved external dependencies and release request — all executed; disposition table and release request in `S4 — integrated gate, disposition and release request`. Two docs-only count/list corrections applied; no code change. |

Worker file scope is limited to the named feature paths: API care-record/finance/subscription/feedback/chat controllers and lookups; corresponding CareHomes finance reads, Families subscription reads, Identity feedback persistence/read abstractions as actually discovered, Chat DTO/read pipelines and Notifications producers; existing portal fixture host and narrowly necessary fixture integration seams; relevant regression/endpoint/PostgreSQL tests; affected consumer docs and Postman collections. No broad refactor or schema changes by assumption. Mastermind pins exact files at dispatch; newly required ownership outside that manifest returns for review.

Worker updates only assigned task results here and hands back after each stage. Mastermind accepts before resuming the next stage. Avoid overlapping runtime work or duplicate agents.

## Final release review — current authority

Mastermind accepted the final corrections after inspecting the actual rerun
branch, options-validation failure path and composition-root test setup.
S3c is accepted; its detailed worker handback below is historical evidence.
Final code gates: strict Release build 0 warnings/0 errors; full unit suite
5596 passed/0 failed/27 skipped; provisioner 37/37. Mastermind reran architecture
(1/1), route mapping (671 signatures, 770 requests, 0 missing/0 orphan), and
`git diff --check` after the corrections. Only the specifically recorded
PostgreSQL suites have relational execution evidence; other skipped facts are
not claimed as passed.

One unintended sandbox refund POST occurred in the worker's first failing
reload-host test because ambient configuration selected the real client. It
used a synthetic fixture id and received HTTP 400; this is not a successful
refund or settlement proof. Test configuration now pins the offline client.

Release target revalidated: `72.62.92.144`, `sanad-api`, `/root/sanad-api`,
`SanadDb` on loopback port 5433, currently revision `4690b5b2`. The runtime
environment is Production: preserve it and ship the seam disabled. G1–G4
provisioning and the comprehensive demo corpus remain follow-up work. No
environment change or provider request is part of the deployment verification.

## Checks and completion

- Successful strict Release build, zero warnings; focused tests for changes. PostgreSQL reproduction is mandatory for query translation, not InMemory alone.
- Integrated full suite once after final code changes because this phase crosses permissions, money boundaries and shared contracts. Run architecture checks and validate changed OpenAPI/Postman/docs links and `git diff --check`.
- Live behavior is verified only after separately authorized deployment, with revision/migration/service checks and authenticated endpoint acceptance. Fixture authorization is not deployment authorization.
- Record report items as fixed/verified, already satisfied, policy answered, or externally pending. Never equate generated fixtures or mocked outcomes with real provider settlement.
- Current result: S1 and S2 locally accepted. **S3c handed back `ready_for_review` after a second correction round
  (release-gate review fixes)** — the fixture-only refund-outcome seam is implemented and proven locally, entirely OFF by
  Default: `FixtureRefundSeamPaymobClient` decorates the real `IPaymobClient` (sole non-keyed registration; inner keyed
  `paymob-inner`) so every refund caller (care-home refund/retry/expiry, Families admin retry, `SubscriptionRefundGateway`)
  is covered while non-refund ops delegate untouched; absent/`Enabled=false` config is a zero-change pass-through (even for
  allowlisted ids); `Enabled=true` under Production FAILS host start (real composition root, `ValidateOnStart`) and so does
  `Enabled=true` with an empty allowlist; options are read LIVE via `IOptionsMonitor` on every refund call and the registered
  validator is enforced on EVERY monitored-value creation (reloads included), so a VALID reload is honoured on the next call
  while an INVALID reload (malformed value, emptied allowlist while enabled, or a Production enablement) makes the monitored
  options unreadable and the decorator FAILS CLOSED — every refund call refused with `Paymob.RefundOutcomeUnknown`, real
  provider never touched, proven by reload facts through a real composition-root host (the reload callback's
  `OptionsValidationException`, possibly Aggregate-wrapped, is tolerated explicitly); the strict allowlist maps exact
  fixture transaction ids to `Rejected`→`Paymob.RefundRejected` (502/Failed), `Unknown`→`Paymob.RefundOutcomeUnknown`
  (409/ambiguity), or `Completed`→the normal confirmed-refund success shape; a `FixtureRefundSeamState` log line (enabled
  flag, entry count, sorted ids) at startup and on every reload is the gate evidence — the doc's deploy step requires
  confirming that line matches EXACTLY the plan-printed ids before `--mode provision --seam-deployed`, and the provisioner
  usage text states the same; the G4 ambiguous seam group now SKIPS re-execution on a rerun (prior-run Approved/Ambiguous is
  permitted, decided by the pure unit-proved `SeamClaimRerun` gate) instead of being refused; the provisioner still
  plans/provisions the four seam groups ONLY behind `--seam-deployed` and the run-wide refund-inertness invariant exempts
  exactly those groups' markers/claim idempotency keys (exact match, only under the declaration; normal groups provably
  never exempt). The internal note now also REQUIRES keeping the seam configuration in place while any seam-armed row
  remains (disabling it would expose the armed clicks to the real provider; reaping the rows is not authorized by this
  work). Focused tests: 25 seam facts + 5 composition-root DI facts, 37/37 provisioner facts
  (built `-warnaserror` clean), full Sanad.UnitTests 5596 passed / 0 failed; strict Release full-solution build
  `-warnaserror` 0 warnings. G1–G4 NOT provisioned (explicitly out of scope for S3c): no remote write, no VPS contact, no
  provider call, no deploy, no commit. S3b's completed record (all 14 DATA-ONLY groups provisioned on the rehearsal
  cluster, read back clean) stands. See `S3c worker checkpoint` for exact files and config keys. Release not re-requested.
- **S3c accepted and S4 complete (Mastermind, 2026-10-25):** S3c passed Mastermind review after one bounded correction
  round (options are now read live per call, `Enabled=true` with an empty allowlist fails host start, and a
  `FixtureRefundSeamState` log line makes the deployed seam provable). The integrated S4 gate was then re-run on the final
  code: strict Release solution build 0/0, `Sanad.UnitTests` 5592 passed / 0 failed / 27 skipped,
  `Sanad.ArchitectureTests` 1/1, `Sanad.PortalProvisionerTests` 36/36, controller↔Postman mapping 0 missing / 0 orphan,
  all 24 collections valid JSON, `git diff --check` clean, and the local `postgres` maintenance database re-verified empty
  (0 non-system tables, no nonce databases). Report-item disposition is recorded below; release is now requested and the
  remaining external dependencies are unchanged.

### S2 exact file manifest (pinned before first edit)

- C1 chat bilingual participant names (additive; `displayName` unchanged):
  `src/Modules/Chat/Application/Sanad.Modules.Chat.Application/Conversations/ConversationResponses.cs`,
  `.../Conversations/IChatUserLookup.cs`, `.../Conversations/IChatFacilityLookup.cs`,
  `.../Conversations/GetConversationsQueryHandler.cs`,
  `src/API/Sanad.API/ChatIntegration/ApiChatUserLookup.cs`,
  `src/API/Sanad.API/ChatIntegration/ApiChatFacilityLookup.cs`; focused facts extend
  `tests/Sanad.UnitTests/Chat/ConversationTests.cs`; docs + Postman:
  `docs/app/chat/conversations.md`, `docs/postman/app/Sanad.App.Chat.postman_collection.json`.
  (C2 producers verified from source + existing `ReadableNotificationTests` — no producer edit.)
- E contract reconciliation:
  `src/API/Sanad.API/Controllers/AdminCareHomeRefundsController.cs` (502 on Retry —
  the actually-mapped provider-refusal status; RecordCompleted verified to have no
  provider path, so no 502 is invented there), pins in
  `tests/Sanad.UnitTests/API/CareHomeBookingControllerTests.cs`.
- Docs bounded audit: `docs/app/families/subscriptions.md` (remove the implemented
  care-home listing-subscription claim; no new listing billing),
  `docs/care-homes/visits.md` (state the domain reason length limits + mapped codes),
  `docs/care-homes/booking-lifecycle.md` (repair the mojibake in the evidence
  length note), `docs/app/public/landing.md` (state the public `/files` access for
  the `imagePath` storage key), Feedback Postman collection link added to the
  postman index (no broad count rewrite).

### S1 worker checkpoint (replaces prior current result)

**S1 result: ready_for_review (handback, after review-round-2 corrections).**
All A1/A2/B1–B4 code, tests, docs and Postman are on uncommitted working-tree
edits over `30e664f343cbf4b0a842d967e8439324ae124317` (never committed).

**Round-2 corrections (this session, per the round-1 review):**

1. Resident search semantics fixed in `CareHomeCareRecordsController.ListResidents`:
   `search.Trim()` first (trimmed length already validated by `Normalize`, so the
   redundant inner length check was removed), then LIKE metacharacters —
   including the escape character itself (backslash) — are **escaped, never
   stripped**: `\\`→`\\\\`, `%`→`\\%`, `_`→`\\_`; the pattern runs through the
   3-arg `EF.Functions.ILike(column, pattern, "\\")` explicit-escape overload on
   both mapped name columns, preserving case-insensitivity and the locked
   authorization. A metacharacter-only needle (`"%"`, `"_"`, `"\\"`) is a valid
   literal needle; there is no disable-the-filter path anymore.
2. PostgreSQL regression expanded in `CareHomeResidentsPostgresTests` (still 8
   facts, seed now 5 accepted residents at facility A): `MappedNameSearch_TrimsWhitespace_AndMatchesMetacharactersLiterally`
   covers trim (`"  ahmad  "` ≡ `"ahmad"`), literal `%` (seed `Nour%Adel`),
   literal `_` (seed `Huda_Salem`), literal `\` (seed `Leila\\Omar`), compound
   literal no-match `"r%d"`, plus the kept case-insensitive Arabic/English
   cases. These seeds distinguish stripping (empty needle ⇒ no filter ⇒ all
   residents) from literal matching (exactly one hit). The offline translation
   fact now pins the 3-arg ILike and asserts `ESCAPE` in the generated SQL.
   Note: the default-read row set assertion compares both sides in
   `StringComparer.Ordinal` order because the Arabic database collation does
   not order code-point-wise; deterministic order stays pinned by the
   pagination fact.
3. Runtime HTTP authorization evidence added (new
   `tests/Sanad.UnitTests/API/PortalAdminReadApiAuthorizationTests.cs`,
   `PortalApiMetadataHost` harness, no remote sessions): **12 PASS** — app-rating
   read: anonymous→401, Family app token→403, SupportAdmin→200 (empty page),
   SuperAdmin→200; family-subscription list: anonymous→401, Family→403,
   SupportAdmin→200; detail: SupportAdmin→404 (policy passed, handler answered,
   code `Subscriptions.Subscription.NotFound`), Family→403; plan write (tax-rule
   POST stands in for the SuperAdmin-only `SubscriptionPlanAdmin` surface):
   anonymous→401, SupportAdmin→403, SuperAdmin→400
   (`Subscriptions.Tax.SharedConfigurationRequired` — policy passed, handler's
   own guard answered against the isolated InMemory finance store).
4. 502 `ProducesResponseType` annotations added to
   `AdminBookingsController.RefundBooking` and `RecordOutcome` (authorized
   bounded round-2 expansion); `AdminBookingsControllerTests` pins updated
   (RefundBooking: 200+502; RecordOutcome: 204/400/404/409/502/503).
5. Comment fix: `AppRatingAdminQueries` header no longer claims an "anonymous"
   submission — the app submission is authenticated.
6. Postman `Sanad.CareHomes.Owner` residents/mine `search` description updated to
   the trim + literal metacharacter semantics (JSON re-validated). No stale
   "wildcard stripping" wording remains in controller or tests.

**Executed checks (final code, all PASS, Release):** full solution strict build
`dotnet build Sanad.slnx -c Release -warnaserror` → **BUILD SUCCEEDED, 0
warnings, 0 errors**; residents PostgreSQL **8/8** (rerun on final code; nonce
database `SanadResidentsVerify_<guid>` verified absent before creation over the
loopback `localhost:5432` connection string, migrated Families+CareHomes,
dropped by the fixture — no debris; `SanadIntegrationDb`/`SanadDb` never opened);
new HTTP authorization boundary **12/12**; API-focused round-1 suites
`AdminBookingsControllerTests` + `AdminSubscriptionsControllerTests` +
`AdminFeedbackAppRatingsTests` + `ResultProblemDetailsMapperTests` +
`AdminResponseSchemaContractTests` **86/86** (includes the updated 502 pins).
Carried valid round-1 evidence (code paths unchanged since): finance
`CareHomeFinancePostgresTests` **13/13** on `SanadFur03VerifyDb`
(`ConnectionStrings__Fur03VerificationDatabase` in-process loopback),
`CareRecordsPortalControllersTests` 18/18, `AdminOperationalReadAuthorizationTests`
(pins, now augmented by the runtime 12). Not run: full integrated suite
(deferred to S4 per brief), live deployment/portal checks (not authorized).

**Round-2 changed files:** `src/API/Sanad.API/Controllers/CareHomeCareRecordsController.cs`,
`src/API/Sanad.API/Controllers/AdminBookingsController.cs` (authorized expansion),
`src/Modules/Identity/Application/Sanad.Modules.Identity.Application/Feedback/AppRatingAdminQueries.cs`,
`tests/Sanad.UnitTests/CareHomes/CareHomeResidentsPostgresTests.cs`,
`tests/Sanad.UnitTests/API/AdminBookingsControllerTests.cs`, new
`tests/Sanad.UnitTests/API/PortalAdminReadApiAuthorizationTests.cs`,
`docs/postman/care-homes/Sanad.CareHomes.Owner.postman_collection.json`. All
round-1 files below remain as changed.

Previous dispatch record:

- Worker model: `river/zai-org/GLM-5.3-Flash#high` verified effective. Session id
  not discoverable from inside the worker runtime (no session-get tool; the
  rename tools mutate foreign sessions), so the dispatching parent records it.
- Baseline HEAD pinned: `30e664f343cbf4b0a842d967e8439324ae124317` (verified
  clean except the private modified handoff, this brief, and pre-existing
  untracked audit/build debris — all preserved).
- Verified local disposable PostgreSQL target for tests:
  `localhost:5432` reachable; nonce-named databases verified absent before
  creation (F1-pattern); `SanadIntegrationDb`/`SanadDb` are never connected to.
- S1 owned file manifest (exact):
  - Code: `src/API/Sanad.API/Controllers/CareHomeCareRecordsController.cs` (A1);
    `src/API/Sanad.API/ProblemDetail/ResultProblemDetailsMapper.cs` (B1);
    `src/API/Sanad.API/Controllers/AdminSubscriptionsController.cs` (B2);
    new `src/Modules/Identity/Application/Sanad.Modules.Identity.Application/Feedback/AppRatingAdminQueries.cs`
    and `src/API/Sanad.API/Controllers/AdminFeedbackAppRatingsController.cs` (B3).
  - Tests: new `tests/Sanad.UnitTests/CareHomes/CareHomeResidentsPostgresTests.cs` (A1
    PostgreSQL proof: default, pagination, nonempty search, empty search, name
    search/order translation, foreign-owner exclusion);
    `tests/Sanad.UnitTests/API/ResultProblemDetailsMapperTests.cs` (B1 map);
    `tests/Sanad.UnitTests/API/AdminBookingsControllerTests.cs` (B1 note update);
    `tests/Sanad.UnitTests/API/AdminSubscriptionsControllerTests.cs` (B2 policy pin);
    new `tests/Sanad.UnitTests/API/AdminFeedbackAppRatingsTests.cs` (B3 reads +
    role boundaries); `tests/Sanad.UnitTests/API/AdminResponseSchemaContractTests.cs`
    (B3 schema entry); `tests/Sanad.UnitTests/CareHomes/CareHomeFinancePostgresTests.cs`
    (A2 add Draft dashboard-with-activity fact; existing Draft facts reused).
  - Consumer docs: `docs/admin/bookings.md` (B1 statuses), `docs/care-homes/booking-lifecycle.md`
    (B1 HTTP mapping note), `docs/admin/subscriptions.md` (B2 SupportAdmin read note),
    `docs/app/feedback.md` (B3 stale no-admin-surface bullet), new
    `docs/admin/feedback-app-ratings.md` (B3), `docs/admin/api-contract-conventions.md`
    (B4 both validation/error shapes), `docs/care-homes/finance-reporting.md` (A2 Draft
    zero/empty note), plus the admin docs index entry for the new page and Postman
    `docs/postman/admins/Sanad.Admin.Feedback.postman_collection.json` (new, B3).
  - Anything newly required outside this manifest returns to Mastermind before
    I touch it.

- S1 final changed-file list (all changes uncommitted):
  - Code (manifest): `CareHomeCareRecordsController.cs`, `ResultProblemDetailsMapper.cs`,
    `AdminSubscriptionsController.cs`, new `AppRatingAdminQueries.cs`, new
    `AdminFeedbackAppRatingsController.cs`.
  - Tests (manifest): new `CareHomeResidentsPostgresTests.cs`,
    `ResultProblemDetailsMapperTests.cs`, `AdminBookingsControllerTests.cs`,
    `AdminSubscriptionsControllerTests.cs`, `AdminResponseSchemaContractTests.cs`,
    new `AdminFeedbackAppRatingsTests.cs`, `CareHomeFinancePostgresTests.cs`.
  - Docs (manifest): `bookings.md`, `booking-lifecycle.md`, `subscriptions.md`,
    `feedback.md`, new `feedback-app-ratings.md`, `api-contract-conventions.md`,
    `finance-reporting.md`, new Postman
    `Sanad.Admin.Feedback.postman_collection.json`.
  - Docs (within the recorded "admin docs index entry" and prior checkpoint
    note, exact edit listed here): `docs/admin/admin-overview.md` (+1 row) and
    `docs/postman/care-homes/Sanad.CareHomes.Owner.postman_collection.json`
    (residents/mine gains the optional `search` variable + 400-invalid-query
    note; JSON re-validated).
- Observations returning to Mastermind (not fixed, outside S1 authority):
  1. (RESOLVED in round 2) `AdminBookingsController.RefundBooking`/`RecordOutcome`
     502 annotations were added under the explicitly authorized round-2 manifest
     expansion; controller-test pins updated and green.
  2. `AdminCareHomeRefundsController.Retry` and `RecordCompleted` declare no 502
     `ProducesResponseType` although their handlers can surface
     `Paymob.RefundRejected` (classified DefinitiveNoMovement in
     `CareHomeRefundHandlers.cs`) → same missing-annotation class as observation 1;
     inspected only, NOT edited (no authority for this file).
  3. `CaregiverCareRecords.cs` (lines ~74–75) picker search/order still uses the
     `.Value`-peeling pattern that fails EF→PostgreSQL translation — same
     regression class as A1, untouched (pre-existing known issue).
  4. `docs/postman/README.md` collection list and `docs/README.md` "20
     collections" counts are stale (pre-existing; `CaregiverNotes` collection
     was already missing from both before this phase); new Feedback collection
     is also therefore not listed — listing it requires touching those files,
     which are outside the pinned manifest.

### S2 worker checkpoint (current result — replaces the prior current result for handback)

**S2 result: ready_for_review (handback).** All C/E work is uncommitted
working-tree edits layered on the S1 edits, over baseline
`30e664f343cbf4b0a842d967e8439324ae124317`. Same worker session
(`ses_e8fe46f8cffe2V1ArEBLpKxW7c`, `river/zai-org/GLM-5.3-Flash#high`). The
S2 manifest above was pinned before the first edit and was followed; every
deviation is listed explicitly below.

**Review-round-1 correction (docs-only):** the visits.md closure sentence
Mastermind flagged was rewritten to the actual behavior: the **500**-character
closure-reason bound is measured on the **supplied, untrimmed** value
(`CareHomeVisitSettings.Validate`: `(x.Reason?.Length ?? 0) > 500`; trimming
happens later in `NormalizeClosures`), and a missing closure date is invalid
the same way. Consequences documented per path: `Create` failures answer
`400 CareHomes.Visits.InvalidRequest`, while a failed `Update` of existing
settings answers `409 CareHomes.Visits.Conflict` — the same mapped code a
stale `expectedVersion` yields. Verified against both source paths
(`CareHomeVisits.cs` Validate/NormalizeClosures/Update;
`UpdateOwnerCareHomeVisitSettingsCommandHandler` catches at lines ~130/151).
**Reported observation, intentionally NOT fixed here (no code change in this
phase):** the handler's update-path catch conflates stale-version and
closure-validation failures onto one 409 — a candidate for a later bounded
re-mapping. Scan of the other S1/S2-added doc claims found no further
mismatches: the decision/cancellation "≤1000 after trimming → 409
InvalidState" wording matches both the trimmed domain checks (`Reject`/`Cancel`)
and all four handler catches; the landing `/files/private/*` 404 guard and the
≤500 `imagePath` bound re-verified against
`PrivateStorageRequestGuard` and `LandingSection.MaximumImagePathLength`.
`git diff --check` clean.

**C1 — additive bilingual chat participant names (implemented):**
`ParticipantResponse`, `UserProfile` and `FacilityPresentation` gained nullable
`DisplayNameArabic`/`DisplayNameEnglish` as optional trailing parameters, so
all existing positional constructions keep compiling. `GetConversationsQueryHandler`
composes: a person carries the Identity profile's stored names; a care-home
owner carries the facility's approved-revision names — never the owner's
personal names. `ApiChatUserLookup`/`ApiChatFacilityLookup` map stored names
through a shared `NullIfBlank` (blank → null) on both the single and batch
paths. No synthetic fallback: an absent language stays null and the client
falls back to `displayName`, whose existing English-preferred/Arabic-fallback
resolution and empty-string unknown-profile behavior are untouched.
Authorization, visibility, hiding, ordering, `lastMessage`/`unreadCount` and
every other chat surface are unchanged. Focused fact
`GetConversations_ParticipantCarriesAdditiveBilingualNames_WithExistingDisplayUnchanged`
pins both-names, Arabic-only-fallback, and facility-names-over-personal-names
cases; `FakeChatUserLookup.Add` was extended with optional parameters.

**C2 — notification producers (verified, already satisfied; zero edits):**
fresh-onboarding copy (`CareHomeReviewNotificationProcessor`, type
`OnboardingStatus`), booking-decision copy (`CareHomeBookingNotificationProcessor`:
`OwnerDecisionNeeded`/`DecisionAccepted`/`DecisionRejected`/`DecisionExpired`),
and chat-message copy (`CreateChatMessageNotificationCommand` +
`NotificationTextCatalog.ChatNewMessage`) are all bilingual with structured
destination pairs — no raw ids or GUIDs in any recipient-facing title/body.
`ReadableNotificationTests` already pins the bilingual identifier-free copy;
language resolution runs through the existing persisted-locale gateway.
`RenderBilingual` argument order re-checked at both call sites. No
historical-row rewrite and no producer change were needed.

**E — contract reconciliation disposition:**

- **Fixed:** `AdminCareHomeRefundsController.Retry` now declares
  `[ProducesResponseType(StatusCodes.Status502BadGateway)]` — the actually
  mapped status for `Paymob.RefundRejected` (mapper line 360), which
  `CareHomeRefundProcessor InitiateAsync` surfaces as a definitive refusal.
  `RecordCompleted` was **not** annotated: it has no `IPaymobClient` path at
  all and maps only `CareHomes.Bookings.NotFound` (404) and
  `InvalidState`/`UnreadableSnapshot` (409), so inventing a 502 would misstate
  the contract. `CareHomeBookingControllerTests.Admin_refund_routes_require_operational_admin`
  now asserts the exact status sets of both actions (Retry
  200/404/409/502; RecordCompleted 200/400/404/409) via a `StatusCodesOf`
  helper — preserving actual mapped statuses.
- **Verified-only, already satisfied:** nullable support contact
  (`PublicSupportContactResponse` fields nullable, unconfigured returns 200
  with nulls); numeric visit days (`day` is a numeric `DayOfWeek`, 1 = Monday,
  strings rejected 400); registration/verification examples match
  `RegisterRequest`/`VerifyOtpRequest`/`ResendOtpRequest` exactly; library
  field `errors` map implemented in `LibraryContentFieldDetail` and documented;
  wellness free-text category (≤100 chars, no taxonomy) + `imagePath` documented
  in admin and app docs; ID-only caregiver medical-access audit
  (`caregiver-medical-access-audit.md` — audit metadata only, no medical
  content); config-only payment gateway (`platform-controls.md` — no
  settings registry, secrets never read/returned/written); FAQ deactivate
  only (no destructive delete); registration-only growth statistics
  (`newRegistrationsByMonth` explicitly not active-user growth).
- **Doc-only fixes:** `docs/care-homes/visits.md` now states the enforced
  reason length limits with their mapped codes (decision/cancellation ≤1000
  after trimming → `409 CareHomes.Visits.InvalidState`; closure limits per the
  review-round-1 correction above). `docs/app/public/landing.md` gains a
  public-access statement: `imagePath` is a ≤500-char storage key served
  anonymously at `/files/<key>` (only `/files/private/*` is 404-guarded), plus
  the ordered-refs/hidden-suppression sentence (verified behavior of
  `LandingRefResolver`). `docs/app/families/subscriptions.md`: the false claim
  that clause-9 money rules "apply to a care-home listing subscription alike"
  is corrected to family-premium-only — the codebase and care-homes docs
  contain no listing subscription or listing billing whatsoever, and none was
  added.
- **Verified alarm, no edit:** the reported mojibake in
  `docs/care-homes/booking-lifecycle.md` line 173 is a **false positive** —
  the character is a proper U+2264 `≤` ("≤ 1966 characters", matching
  `MaxComposedLength(2000) − ActorPrefixLength(34)`); the file contains zero
  U+FFFD characters, as does every doc in the repo. Left unchanged.
- **Verified accurate, left alone:** `RecordCompleted.request.reference/reason`
  are genuinely unbounded (no column maxlength, no doc claim) — nothing to
  reconcile; no success bodies were added to `204` actions; the operation
  inventory was captured from source before each edit.

**Docs/Postman deliverables:** `docs/app/chat/conversations.md` (participant
JSON example with the additive fields + additive-fields paragraph + delta
note), `docs/postman/app/Sanad.App.Chat.postman_collection.json` (both list
example participants carry the bilingual fields; collection description notes
the additive fields; JSON re-validated with `ConvertFrom-Json` — VALID),
`docs/postman/README.md` (Feedback collection added to the admins list; no
stale-count rewrite), `docs/admin/feedback-app-ratings.md` (+1 link line to
the Feedback Postman collection — note: this file is pre-existing untracked
from S1; the link line is the only S2 touch).

**Executed checks (final code, Release):** full solution strict build
`dotnet build Sanad.slnx -c Release -warnaserror` after all code →
**BUILD SUCCEEDED, 0 warnings, 0 errors**. Focused suites
`ConversationTests` + `CareHomeBookingControllerTests` +
`AdminResponseSchemaContractTests` + `ReadableNotificationTests` →
**79/79 PASS** (`--no-build`). **NOT RUN:** full integrated suite (deferred to
S4 per brief), live/OpenAPI regeneration and Bruno execution (not authorized),
PostgreSQL-backed chat tests (the C1 change adds no new SQL — the lookups run
the same queries), architecture checks and `git diff --check` (S4 items).

**Known limitations / carried items:** caregiver-picker `.Value` translation
defect in `CaregiverCareRecords.cs` remains a separately reported finding —
untouched, per Owner instruction. Legal copy, mobile binary evidence, and the
deletion-approval dependencies stay external. No commit, push, deploy, or
provider/remote-DB calls; S1 changes and all pre-existing debris preserved
untouched. One manifest-adjacent judgment call for Mastermind to review: the
Feedback collection is linked from `docs/postman/README.md` (in manifest) and
from `docs/admin/feedback-app-ratings.md` (manifest-adjacent, one additive
line in a pre-existing untracked file).

**Next action (Mastermind):** review the S2 diff against this checkpoint,
accept, then direct S3 (fixture-only design boundary) or S4 (integrated
checks and report disposition). Release remains `not_requested`.

### S3a exact file manifest (pinned before first edit)

- New console tool `tools/Sanad.PortalProvisioner/` (sibling of
  `Sanad.PortalFixtureHost`; intentionally NOT in `Sanad.slnx`; references
  `src/API/Sanad.API/Sanad.API.csproj` only): `Sanad.PortalProvisioner.csproj`,
  `Program.cs` (modes `plan`/`verify`/`provision`, expect-flags parsing,
  credential-file read, JSON output), `ProvisionerTargetGuard.cs` (fail-closed
  target identity assertion: host/port/database/migration-heads/revision
  correlation, Production + mismatch refusal, credential-free report),
  `PortalProvisionPlan.cs` (plan records + deterministic JSON manifest),
  `PortalProvisioner.cs` (data-only domain groups above, marker-keyed upsert,
  refusing refund-gateway stub wired into the Finance store, direct
  handler/DbContext construction — no MediatR bus, no hosted services).
- New focused test project `tests/Sanad.PortalProvisionerTests/`
  (`Sanad.PortalProvisionerTests.csproj`): plan-mode determinism, target-guard
  refusal matrix (Production, host/DB/head/revision mismatch, missing
  expectations), marker/phone/email derivation determinism, manifest
  stability; PostgreSQL-backed facts for EF-touching fixture code where
  translation matters.
- Brief-only updates: this manifest section (now) and — at handback — the
  `S3a worker checkpoint` section appended after it plus the checks section's
  current-result line. No other file outside the two new projects changes.
  Mastermind_Handoff.md untouched. Pre-existing S1/S2 edits and untracked
  debris preserved untouched. No commit/push/deploy; no remote write of any
  kind in S3a.

### S3a worker checkpoint (current result — replaces the prior current result for handback)

**S3a result: ready_for_review (handback; S3 remains in_progress, not
accepted, and no remote write was performed).** All S3a work is new untracked
files over the S1/S2 working-tree edits, baseline
`30e664f343cbf4b0a842d967e8439324ae124317`. Same worker
(`river/zai-org/GLM-5.3-Flash#high`). The pinned S3a manifest was followed
exactly; in-flight manifest adjustments are recorded below (all inside the
already-manifested files).

**Review round (S3a corrections — Mastermind re-review round 1, two blocking
defects fixed, nothing else changed):**
1. **Provider-call-armed fixture row.** The rejected example in
   `carehome-paid-awaiting-decision` used to sit in RefundPending/Pending,
   `RefundClaimedOnUtc=null` — exactly `CareHomeBooking.IsRefundSendArmed`
   true, so `POST /admin/care-homes/bookings/{id}/refund/retry` would have
   claim-and-sent it to the provider on the deployed revision. FIX: the
   fixture now CLAIMS the refund attempt immediately after `Reject` (same
   persisted-claim fence the ambiguity groups use) — the row still displays
   RefundPending/Pending, is NOT marked ambiguous and NOT completed, and a
   retry refuses before any gateway call. USE-AT-ONCE hardening: a RUN-WIDE
   refund-inertness invariant now runs after EVERY provisioned group over
   every fixture-owned row and fails the run closed (see the invariant
   description in the inertness paragraph, and the corrected scenario table).
   The violation predicate is pure (`PortalProvisionInvariants.Evaluate`)
   and pinned by focused tests for every armed and safe shape.
2. **Tunneled rehearsal reach.** Direct 72.62.92.144:5432 is not reachable
   from this workstation, so S3b must go through an SSH port-forward where
   the server reports the loopback address and the database NAME `SanadDb`.
   FIX: the guard gained (a) a MANDATORY PostgreSQL cluster identity check —
   `select system_identifier::text from pg_control_system()` compared
   in-memory against the operator-supplied `expectedSystemIdentifier=` line
   read from the PRIVATE credentials file (never CLI, never printed, never
   serialized into plan/manifest/report); (b) an explicit operator
   declaration `--tunnel-declared`, accepted only together with
   `--target-kind rehearsal`, the expected database name, all nine heads and
   a MATCHING system identifier; under it a reached loopback address is
   accepted and both the reached address/port and the operator-declared host
   are recorded; (c) without the declaration strict address matching stays
   exactly as before and loopback+`SanadDb`/`SanadIntegrationDb` is refused
   at parse; (d) the protected-local-DB protection is now the IDENTIFIER —
   any loopback target whose system identifier is unreadable by the
   connection role (clear refusal naming the privilege need, not the value)
   or mismatched is refused. Tests pin: tunnel acceptance, refusal without
   declaration, refusal on mismatch, refusal on missing identifier, refusal
   on unreadable identifier, and the unchanged direct path.

**Deliverable:** new console tool `tools/Sanad.PortalProvisioner`
(`Sanad.PortalProvisioner.csproj`, `Program.cs`, `ProvisionerTargetGuard.cs`,
`PortalProvisionPlan.cs`, `PortalProvisioner.cs` — now also hosting the pure
`PortalProvisionInvariants`; intentionally NOT in `Sanad.slnx`; references
only `src/API/Sanad.API/Sanad.API.csproj`) + focused test project
`tests/Sanad.PortalProvisionerTests/` (csproj + `PortalProvisionerFacts.cs`,
**31/31 PASS** after the corrections). No other repository file changed by
S3a.

**Modes (all fail-closed, credential-free output):**
- `plan` — pure derivation of the complete write manifest (logins, group
  markers, per-record idempotency keys) with ZERO database connections;
  writes the plan JSON to `--work-root`; byte-determinism pinned by a test.
- `verify` — read-only target identity probe: connects ONLY to execute
  `current_database()`/`inet_server_addr()`/`server_port()`/`version()` and
  `select system_identifier from pg_control_system()` (compared in memory
  against the private `expectedSystemIdentifier`, never serialized), then
  per-module `GetAppliedMigrations`/`GetPendingMigrations` through the
  module's OWN schema-scoped history table. Zero writes by construction.
- `provision` — runs verify FIRST; any refusal aborts before the first
  write; then the marker-keyed upsert of the requested groups.

**Target identity assertion (fail-closed, hardened in the review round):**
`--target-kind` accepts exactly `rehearsal` (Production refused outright);
`--expect-host`, `--expect-port`, `--expect-database`, `--expect-revision`
(correlation pin for the SSH-verified service revision; the tool cannot read
the remote binary, so the manifest records what the operator verified), and
all nine `--expect-migration-heads module=head` pairs are MANDATORY —
missing, duplicated or mismatched values refuse with exit 3 before any
connection to a data database. Loopback targets named `SanadDb`/
`SanadIntegrationDb` are refused at parse UNLESS the operator passes the
explicit `--tunnel-declared` declaration (an SSH port-forward is the only
viable rehearsal connection path; the direct VPS PostgreSQL port is not
reachable from the workstation). Under `--tunnel-declared` a reached
loopback address is accepted and BOTH the reached address/port and the
operator-declared host are recorded; every other address still matches
strictly. In all cases the reached cluster must additionally prove its
identity: `select system_identifier::text from pg_control_system()` is
compared in-memory against the operator-supplied `expectedSystemIdentifier=`
value read from the PRIVATE credentials file — a non-secret fingerprint that
is never passed on the command line, never printed, and never written into
any plan, manifest or report output. A loopback/target whose system
identifier is unreadable by the connection role (fail-closed refusal naming
the privilege need, e.g. `pg_read_control_data`, never the value) or
mismatched is refused — that identifier check, not the database name, is
now what separates the rehearsal cluster from the locally protected
`SanadDb`/`SanadIntegrationDb`. Server-side identity is otherwise
cross-checked: `current_database()` and `inet_server_addr()`/
`inet_server_port()` must match the expectations (hostnames are resolved so
`localhost` legitimately reaches `::1`/`127.0.0.1` on a direct connection).
The extra remote `rentals` schema is ignored, never touched.

**Marker scheme / idempotency keys (stable, upsert-only, no deletion):**
prefix `spf:<tag>:<group>`; login emails `<tag>.<role>@test.sanad.local`;
phones deterministic FNV-1a(tag, role) in `+2010########` with per-role
ordinals (`RoleOrdinal`, no same-tag collisions, offset away from the
production test-seed range); ownership markers `[<tag>:<role>]` inside the
English display name verified before ANY reuse (foreign-identity collision ⇒
refusal); narrative markers in `Booking.SpecialInstructions`
(caregiver), `CareHomeBooking.CareNeedsNotes` (care home),
`CaregiverPayout.TransferReference` (payout), `CareRecord.Details`
(care records), and domain idempotency keys for duplicate-claim submission
(`spf:<tag>-duplicate-claim`) and shutdown refund
(`spf:<tag>-shutdown-refund`). `plan` mode prints every derived key without
touching a database.

**Clock decision:** NO clock seam of any kind is enabled or proposed —
`CareHomes:TestClock` will NOT be enabled (it is a service-wide seam the
deployed rehearsal revision does not contain, and a changeable service clock
on a shared environment can silently move every running feature). Instead
every fixture row is stamped from an explicit operator-passed
`--instant <UTC>`; the tool constructs the direct-call handlers with a
`ProvisionerClock` pinned to that instant so even handler-derived timestamps
(duplicate-claim submission) inherit the fixture timeline. Chronology is
monotonic by construction (paid −Δ → confirmed → started → completed, etc.).
Days-based bookings anchor to `--instant`'s date.

**Inertness seams inside the tool:** the duplicate-claim store is constructed
with a `RefusingRefundGateway` whose `PrepareAsync`/`RefundAsync` throw —
structurally, no refund can leave the tool; a focused test pins it. The tool
references no provider client anywhere. Shutdown refund execution runs
through `SubscriptionRefundRecorder` — pure Finance ledger/budget
persistence with no gateway parameter (proven in source).

**Run-wide refund-inertness invariant (the materialized inertness proof,
added in the review round):** after EVERY provisioned group the run re-reads
every fixture-owned money row (marker-scoped: `CareHomeBooking.CareNeedsNotes
StartsWith spf:<tag>`, `Booking.SpecialInstructions StartsWith spf:<tag>`,
duplicate-charge claims joined on this run's own payment attempts), hands the
PERSISTED rows to the pure evaluator `PortalProvisionInvariants.Evaluate`,
and fails closed on any of these provider-call-armed shapes:
- a CareHomeBooking with `IsRefundSendArmed` true (the exact shape the
  care-home refund-retry route claims and sends — unclaimed
  RefundPending/Pending or /Failed without an ambiguity marker), or any
  fixture row with `RefundStatus == Failed` (the bounded retry re-enters the
  gateway);
- a Families booking whose persisted refund state resolves to
  `BookingRefundState.Failed` (`BookingRefundStates.Resolve` — the exact
  gate at `AdminBookingCommands` through which the admin refund-retry route
  sends an outstanding award to the gateway; claim-held/ambiguous shapes
  resolve `Pending` and are the safe fixture shapes);
- a duplicate-charge claim that is `Approved` or carries ANY
  `DuplicateChargeExecutionState` beyond `None` (Executing calls the
  gateway; AwaitingExecution is one trigger away; Failed/Ambiguous degrade
  to retryable).

Any violation stops the run immediately: it fails the report (exit 5),
records the violating rows, and no further group is provisioned. A scan
query failure is itself a violation (the run cannot prove inertness ⇒ it
fails). The evaluator is pure and pinned by focused tests for every armed
shape (unclaimed rejected care-home booking, failed care-home refund,
failed-resolved Families booking, Approved/Executing/Awaiting/Failed-depth
claims) and every safe shape (the shipped claim-held rejected example,
the claim+ambiguity shapes, the Submitted/None claim, and all together).

**Scenario → classification table (all provisioned rows verified by read-back
in the local proof):**

| # | Scenario | Class | Why it cannot reach the provider on revision 4690b5b2 |
|---|----------|-------|--------------------------------------------------------|
| 1 | Draft facility (separate owner, never submitted) | DATA-ONLY `carehome-facility-draft` | Draft rows render in owner portal only; no booking, no money, no refund path |
| 2 | PaidAwaitingDecision accept + reject slots + persisted rejected example | DATA-ONLY `carehome-paid-awaiting-decision` | Decision slots await operator action only; the rejected example is RefundPending/Pending with its refund attempt CLAIMED immediately after Reject (not ambiguous, not completed) — a claimed attempt is never `IsRefundSendArmed`, so the refund-retry route refuses BEFORE any gateway call |
| 3 | Care-home refund Pending-with-claim reconcile flow (`RefundPending`, claim held, marked Ambiguous-verified) | DATA-ONLY `carehome-refund-ambiguity` | `IsRefundOutcomeAmbiguous`/in-flight claim ⇒ retry refuses BEFORE any gateway call (`AdminBookingCommands` guard pattern) and reconcile records AttemptInconclusive without a provider call |
| 4 | Care-home externally recorded completed refund (Initiated→Completed with recorded reference) | DATA-ONLY `carehome-evidenced-external-refund` | Terminal state fed ONLY by `MarkRefundInitiated/Completed` (operator records an off-system movement); no code path from these rows can call the gateway |
| 5 | Care-home booking extension off an accepted paid segment | DATA-ONLY `carehome-booking-extension` | Plain booking rows; payment intent/mark-paid are fixture-recorded ids, never gateway calls |
| 6 | Checkout without family confirmation + open care-home dispute | DATA-ONLY `carehome-checkout-dispute` | Dispute rows are review-only; resolution is an operator decision, no money movement in code |
| 7 | Caregiver booking Confirmed | DATA-ONLY `caregiver-confirmed-booking` | Payment recorded via fixture transaction ids (`RecordPaymentIntent`/`MarkAsPaid` domain calls) — no gateway is consulted; nothing refund is pending |
| 8 | Caregiver paid booking Completed + synthetic payout record | DATA-ONLY `caregiver-completed-booking` | Caregivers module has ZERO provider seams (verified: no `IPaymobClient`/`TransferAsync` anywhere); payout is a recorded row |
| 9 | Caregiver refund Pending-with-claim reconcile flow | DATA-ONLY `caregiver-refund-ambiguity` | Same claim-before-send shape as #3, this time on Families: retry refuses on `IsRefundOutcomeAmbiguous`/`IsRefundAttemptInFlight` before the gateway (proven at `AdminBookingCommands.cs:70`); reconcile = AttemptInconclusive |
| 10 | Dedicated paid subscription + Submitted duplicate-charge claim + service-shutdown refund EXECUTED on its own capture | DATA-ONLY `subscription-and-duplicate-claim` | Submission only derives a refund case (no money); shutdown refund executes ONLY through `SubscriptionRefundRecorder` (ledger + atomic budget claim, no gateway) — local proof read back the refund request, ledger entry (`Approved 20255` minor units) and budget claim |
| 11 | Three care records (family/caregiver/care-home sources) for one dependent | DATA-ONLY `families-care-records` | Pure review-content rows |
| 12 | Private family test access (reviewer identities) | DATA-ONLY `family-private-access` | Read-only listing of run-owned identities + subscription check; no writes |
| 13/14 | Complaint reviews Received / Investigating (linked to the claim's and shutdown refund's OWN refund cases — schema allows one review per case) | DATA-ONLY `complaint-received`/`complaint-investigating` | Reviews reference existing ledger cases; investigation state change is procedural |
| G1/G2 | Care-home / caregiver refund **Failed** (claim released) | SEAM-DEPENDENT — NOT provisioned, hard-refused | A Failed refund IS retryable and the retry WOULD reach `IPaymobClient`/gateway on the current revision; no provably inert data-only Failed shape exists |
| G3 | Duplicate-claim Approved-awaiting-execution | SEAM-DEPENDENT — NOT provisioned, hard-refused | `BeginExecution` persists intent and then `DuplicateChargeClaimStore.Execute` calls `gateway.RefundAsync` (`DuplicateChargeClaimStore.cs:399`) |
| G4 | Duplicate-claim Ambiguous | SEAM-DEPENDENT — NOT provisioned, hard-refused | Legitimately reachable only via an execution that already called the provider; NotMoved reconcile degrades to Failed ⇒ retryable ⇒ provider invitation |

Gated groups are named in code (`PortalProvisionGroups.GatedOnDeploy`); plan
and provision refuse them before any connection (proven below). **Deploy
dependency:** G1–G4 become provisionable only after an authorized deploy that
carries a fixture-provider-outcome interception seam (fixture-scripted
gateway) — until then any Failed/Approved-for-execution/consistent-Ambiguous
row invites a real provider movement.

**Local proof (fresh nonce PostgreSQL, discipline intact):** nonce database
`SanadS3aProv_68dba3ebfc` verified ABSENT before creation over the loopback
connection string, schemas migrated to the exact nine working-tree heads by a
temp runner OUTSIDE the repo (`C:\Users\mosad\AppData\Local\Temp\opencode\
s3a-migrator`), an unrelated pre-existing probe row inserted
(`caregivers.languages` id 2222…, `code=de`), full table-count snapshot taken
(168 tables), then: plan (zero connections, zero writes) → provision all 14
groups → provision again → provision a third time.
**SCOPE NOTE (review round): this proof ran on the PRE-correction build.**
It proves the marker/upsert/idempotency mechanics and the guard refusal
matrix on real PostgreSQL; the two correction changes (claim-held rejected
example; run-wide invariant; cluster-identifier guard) are proven offline by
the focused suite (31/31 — the invariant evaluates the PERSISTED shapes, so
the predicate is exactly what a live scan would read). Per the review
instruction no migration tooling was rerun this round, so the corrected
rejected-example row and the live invariant/identifier probes were NOT
re-proven on PostgreSQL; they execute on every future run and come under
live proof at S3b verify/provision.
Results: (a) run-2 and run-3 provision reports PASS with manifests whose
record identities are IDENTICAL to run-1 (byte-identical except the
work-root path); (b) table counts after run 3 are EXACTLY equal to the
post-run-2 snapshot (0 changed lines across all 168 tables) — zero
duplicates, pure upsert; (c) the unrelated probe row is byte-identical after
every run (`code:de|ar:ألماني|en:German|active:True`); (d) total row growth
before→after matches only manifest-listed fixture rows (45 tables grew,
exactly the tool-owned kinds). Refusals demonstrated: production
target-kind (exit 3), wrong expected database (exit 4 "database mismatch"),
wrong expected migration head (exit 4 "migration head mismatch for
identity"), gated group request (refused before writes), missing
expectations (exit 3). The proof also caught and fixed a REAL
PostgreSQL translation defect: `CareRecord.Details.StartsWith(marker,
StringComparison.Ordinal)` does not translate — replaced with the
translatable parameterized `StartsWith` (markers carry no LIKE
metacharacters). The nonce database was DROPPED after the proof (no debris);
`SanadDb`/`SanadIntegrationDb` were NEVER connected to.

**Rehearsal identity check (READ-ONLY SSH, key `C:\Users\mosad\.ssh\sanad_vps`,
re-verified this session):** host `srv1604711` = `72.62.92.144` (global IP
confirmed on the interface); unit `sanad-api.service` active,
`WorkingDirectory=/root/sanad-api`, `EnvironmentFile=/etc/sanad/sanad.env`
present and NOT read (zero secrets printed); database name `SanadDb`
(only non-template DB besides `postgres`); all nine module migration heads on
the remote equal the working-tree heads: care_homes
`20261022213000_AddCareHomeRefundPolicySnapshots`, caregivers
`20261021130000_AddCaregiverReviewNotes`, chat
`20261013171755_AddAttachmentCleanupIndex`, cms
`20261023114500_AddLibraryContentVideoStorageKey`, community
`20261023150000_AddPostDeletionAudit`, families
`20261023131500_AddCareRecords`, finance
`20261024133842_AddDuplicateChargeClaims`, identity
`20261022094500_AddPartnerContactOptionalFields`, notifications
`20261010010618_AddEmailHtmlSnapshots` (counts 18/11/4/14/6/46/8/17/5), plus
the unrelated `rentals` schema (1 table) which the tool ignores. Revision
correlate: NO revision stamp exists remotely; `Sanad.API.dll/.pdb` mtimes
2026-10-24 22:15:57/55 UTC correlate with the pinned rehearsal deploy of
`4690b5b2e46a311bbb64f76c41c81769a34d4832`, and `git diff 4690b5b2..HEAD`
touching ONE docs file proves every domain file the tool drives is
byte-identical to the deployed revision. The provisioner was NEVER run
against the VPS.

**Called tool commands (the S3b shape, against rehearsal):**
the credentials file now carries THREE lines — `connectionString=…` (the SSH
port-forward's LOCAL endpoint, e.g. Host=localhost;Port=<fwd>;Database=SanadDb),
`fixturePassword=…`, and `expectedSystemIdentifier=…` (the rehearsal
cluster's PostgreSQL `system_identifier`).
`dotnet tools/Sanad.PortalProvisioner/bin/Release/net10.0/Sanad.PortalProvisioner.dll --mode plan --run-tag r2 --instant <UTC> --work-root <tmp>` →
`... --mode verify --credentials-file <private> --tunnel-declared --target-kind rehearsal --expect-host 72.62.92.144 --expect-port 5432 --expect-database SanadDb --expect-revision 4690b5b2e46a311bbb64f76c41c81769a34d4832 --expect-migration-heads <nine module=head pairs>` →
`... --mode provision ... --groups <the 14 data-only ids>`.

Capturing the identifier READ-ONLY over SSH without printing it: from this
workstation, pipe the SSH command's stdout directly into the private
credentials file so the value never appears in a terminal or transcript, e.g.
`ssh -i C:\Users\mosad\.ssh\sanad_vps root@72.62.92.144 "sudo -u postgres psql -Atc 'select system_identifier from pg_control_system()'" >> <private-credentials-file>` (prefixed as `expectedSystemIdentifier=` — e.g. `echo expectedSystemIdentifier=` first, or Read-Host-free redirection in one pipeline). The command is read-only (a single SELECT against control data). S3b must re-pin BOTH the revision (SSH) and this identifier immediately before provisioning and supply matching values.

**Executed checks (final code after the review corrections, Release):** tool
build strict `-warnaserror` 0 warnings; full solution strict `dotnet build
Sanad.slnx -c Release -warnaserror` → **BUILD SUCCEEDED, 0 warnings, 0
errors**; focused tests `Sanad.PortalProvisionerTests` **31/31 PASS**
(classification stability, plan byte-determinism + subset, gated/unknown
refusals, identity determinism/tag separation, guard refusal matrix incl.
protected local DBs, reached-identity decision matrix — tunnel acceptance,
refusal without declaration, identifier mismatch/missing/unreadable, direct
path unchanged — refusing-gateway inertness pin, run-wide invariant matrix
(armed care-home/unclaimed-rejected flagged; claim-held rejected clean;
failed care-home row flagged; failed-resolved Families booking flagged;
ambiguous shape clean; Approved/execution-state claims flagged;
submitted+all-safe clean), ProvisionerClock pinning, offline domain
self-check); `git diff --check` clean (only pre-existing LF/CRLF
informational warnings on S1/S2 files). NOT RUN (not authorized this round):
any database work (the earlier PostgreSQL end-to-end proof predates the two
corrections — see its SCOPE NOTE), full integrated suite (S4), live
deployment verification.

**Limitations / carried items:** (1) the remote revision string remains an
SSH-correlated evidence (binary mtimes + one-docs-file diff), not a stamp the
tool can read remotely — the tool therefore records `--expect-revision` as an
authorization correlation rather than a measurement; (2) an operational slip
during proof setup briefly applied the nine module migrations to the LOCAL
`postgres` MAINTENANCE database (not SanadDb/SanadIntegrationDb, no app
data); it was immediately restored by dropping exactly those nine schemas and
the stray EF history table — no app database was touched, and the nonce
discipline was then rerun cleanly; (2b) the same transient pattern recurred
once during the continuation pass (an unknown temp-runner mode fell through
to migration-apply against local `postgres`); it was likewise restored
immediately, the temp runner is now fail-closed (migration-apply refuses any
database without the `SanadS3aProv_` nonce prefix, and never the app DBs),
and a stray nonce left by the interrupted session
(`SanadS3aProv_b8caad0157`) was dropped — final state: zero nonce databases,
no module schemas in `postgres`, `SanadDb`/`SanadIntegrationDb` never
connected; (3) the update-path 409 conflation
observation from S2 is carried unchanged; (4) shut-down refund award and all
money figures are synthetic fixture numbers, never real settlement; (5) the
cluster `system_identifier` has NOT been captured yet — no remote or local
database command was run in the review round — so S3b begins by capturing it
read-only over SSH per the procedure above and re-pinning the revision.

**Reviewer credentials (private — path only, contents never printed):**
`C:\Users\mosad\AppData\Local\Temp\opencode\s3a-reviewer-credentials.txt`
(planned reviewer emails for tag `r2` + generated fixture password).
S3b will append the operator-supplied rehearsal connection string AND the
cluster `expectedSystemIdentifier` there; the `--credentials-file` contract
is now THREE lines — `connectionString=…` (the tunnel's loopback endpoint),
`fixturePassword=…`, `expectedSystemIdentifier=…` — read only from that
private file, never echoed or written to any
manifest.

**Unresolved questions for Mastermind (decisions needed before S3b):**
1. Approve/adjust the 14-group data-only manifest as the S3b write set.
2. Confirm `--expect-revision` semantics: the tool verifies DB identity (now
   INCLUDING the cluster `system_identifier` from the private credentials
   file) but cannot read the deployed binary; S3b should re-pin the revision
   over SSH immediately before provisioning and supply the same value to the
   tool — and first capture the identifier read-only per the documented
   procedure, since direct 5432 is unreachable and the run must be
   `--tunnel-declared`.
3. Tag choice `r2` and marker prefix `spf` are defaults — confirm or rename
   before the rehearsal run.
4. Confirm the `--instant` anchor for the rehearsal run (fixture timeline)
   and whether complaint-review states should stay at Received/Investigating
   (provisioned) rather than pre-Resolved.
5. G1–G4 (refund-failure states, Approved/Ambiguous duplicate-claim states)
   stay unprovisioned until the Owner authorizes a deploy carrying the
   fixture-outcome seam — confirm this ordering.

**Next action (Mastermind):** review this checkpoint and the tool diff,
approve/adjust the provisioning manifest, then authorize S3b (rehearsal
provisioning) as a separate, bounded instruction. S3 stays in_progress;
release remains `not_requested`.

### S3b worker checkpoint (current result — replaces the prior current result for handback)

**S3b result: completed — all 14 approved DATA-ONLY groups provisioned on the
rehearsal cluster and read back clean; the Owner's S3 completion gate ("all 14
groups provisioned AND read back clean") is MET.** Everything below happened
this session over baseline `30e664f343cbf4b0a842d967e8439324ae124317`
(unchanged; nothing committed, pushed or deployed by S3b). Only the brief's S3
sections were edited.

**Step 1 — fresh local nonce proof on the CORRECTED build (required because
the S3a end-to-end proof predated the corrections):** nonce
`SanadS3aProv_c43016e7f1` verified absent → migrated to the nine working-tree
heads by the temp runner (outside the repo) → unrelated probe row inserted
(`caregivers.languages` id `2222…`, `code=de`) → counts snapshot (168 tables)
→ **provision ×2 PASS** (`--instant 2026-10-25T14:00:00Z`, run-tag `r2`,
work-roots `s3b-run1`/`s3b-run2`): both runs `provisionPassed: true,
unresolved: []` — the RUN-WIDE refund-inertness invariant passed after every
group on live PostgreSQL with the corrected claim-held rejected example
(`01a1730c-9fce-72fb-aaac-c6f3f7200704` state RefundPending/Paid,
refund=Pending, ambiguous=False, never `IsRefundSendArmed`). Manifests
byte-identical except the 2 work-root lines; 45 tables grew (exactly the
fixture kinds), 0 count drift, probe row intact. Refusal matrix re-proven
after the nonce proof (each on a short-lived nonce or plan mode, all dropped
afterwards): production target-kind exit 3; gated group
`caregiver-refund-failed` exit 6 "requested groups outside the approved
data-only set"; wrong-DB exit 4 "database mismatch"; wrong-head exit 4
"migration head mismatch for identity: reached
'20261022094500_AddPartnerContactOptionalFields', expected 'current'";
identifier-mismatch exit 4 "cluster system identifier mismatch… value never
printed". Zero nonce databases remain; `SanadDb`/`SanadIntegrationDb` never
connected.

**Step 2 — SSH identity re-check + identifier capture (read-only):** service
`sanad-api.service` active (`/root/sanad-api`), binary mtimes
2026-10-24 22:15:57/55 UTC still correlate revision
`4690b5b2e46a311bbb64f76c41c81769a34d4832`; single env connection string
`ConnectionStrings__IdentityDatabase` (host localhost, **port 5433**, db
`SanadDb`, user `postgres` — values read from safe keys only); API bound to
127.0.0.1:5001 via `ASPNETCORE_URLS`. All nine remote history tables live
under each schema as quoted `"__EFMigrationsHistory"` with camelCase
`"MigrationId"` — heads re-read equal to the working tree exactly (care_homes
`20261022213000_AddCareHomeRefundPolicySnapshots`, caregivers
`20261021130000_AddCaregiverReviewNotes`, chat `20261013171755_AddAttachmentCleanupIndex`,
cms `20261023114500_AddLibraryContentVideoStorageKey`, community
`20261023150000_AddPostDeletionAudit`, families `20261023131500_AddCareRecords`,
finance `20261024133842_AddDuplicateChargeClaims`, identity
`20261022094500_AddPartnerContactOptionalFields`, notifications
`20261010010618_AddEmailHtmlSnapshots`). The rehearsal cluster's
`system_identifier` (19-digit) was captured READ-ONLY over SSH
(`sudo -u postgres psql -Atc "select system_identifier::text from
pg_control_system()"`) straight into an in-memory PowerShell variable and
written into the private credentials file — never echoed, never printed; the
tunneled connection string (Host rewritten to the loopback forward endpoint,
Port 15433) was likewise captured into the private file without printing.
Private credentials file (path only, contents never printed):
`C:\Users\mosad\AppData\Local\Temp\opencode\s3a-provisioner-credentials.txt`
(three lines: `connectionString=`, `fixturePassword=`,
`expectedSystemIdentifier=`).

**Step 3 — tunnel + verify (zero writes):** SSH local forward
127.0.0.1:15433→127.0.0.1:5433. `--mode verify` **PASSED, exit 0** with
`--tunnel-declared --target-kind rehearsal --expect-host 72.62.92.144
--expect-port 5433 --expect-database SanadDb --expect-revision
4690b5b2e46a311bbb64f76c41c81769a34d4832` + nine heads:
`verificationPassed: true`, reached server 127.0.0.1/32 port 5433 (matches
`--expect-port 5433` = server-side `inet_server_port()`), database
`SanadDb`, `clusterIdentityVerified: true`, all nine modules passed with 0
pending migrations. Receipt: `s3b-verify-rehearsal.txt`.

**Step 4 — plan + pre-counts + provision ×2:**
- `--mode plan` (`--instant 2026-10-25T12:45:45Z`) matched the approved set
  exactly: 14 data-only groups, marker prefix `spf:r2:`, planned logins
  (family.owner, caregiver, carehome.owner, support, superadmin,
  shutdown.owner, phone-keyed elderly), and `gatedOnDeployNotProvisioned`
  listing exactly G1 caregiver-refund-failed, G2 carehome-refund-failed, G3
  duplicate-claim-approved-awaiting-execution, G4 duplicate-claim-ambiguous.
- Pre-counts (read-only, through the tunnel, temp-runner `counts`):
  `s3b-remote-counts-before.txt` (168 lines).
- **Provision run 1 PASS** — `--instant 2026-10-25T12:46:52Z`,
  `provisionPassed: true`, `unresolved: []` (invariant clean after every
  group). **Provision run 2 PASS** — `--instant 2026-10-25T12:48:03Z`, same.
  Manifests `s3b-remote-run1`/`s3b-remote-run2/
  portal-provision-manifest-r2.json` byte-identical except the 2 work-root
  lines. Post-counts `s3b-remote-counts-after.txt`: 45 tables grew, 0
  shrank, before/after line counts equal (168) — growth shapes match the
  local proof exactly. Read-only marker-scoped duplicate check over SSH:
  care-home bookings with `CareNeedsNotes LIKE '%spf:r2%'` = **8 exactly**
  (paid-awaiting-decision accept 1 + reject/rejected 2, refund-ambiguity 1,
  evidenced-external-refund 1, booking-extension current+extension 2,
  checkout-dispute 1); families bookings = 3; duplicate-charge claims = 1;
  complaint reviews = 2; r2 users = 7 (six role logins + the separate
  draft-facility owner `r2.carehome.owner2`); care records = 3; caregiver
  payouts = 1. Zero duplicates; run 2 added no rows.

**Provisioned record identities (from the run-1 manifest; ids/emails only —
full states in `s3b-remote-run1/portal-provision-manifest-r2.json`):**
carehome-facility-draft → CareHomeFacility `01a17319-fac0-7cdc-8b36-939b416daeea`
(Draft, owned by `r2.carehome.owner2@test.sanad.local`);
carehome-paid-awaiting-decision → accept `01a1731a-0272-761b-a552-8c22b63f1c69`,
reject `01a1731a-054b-78d5-8891-e7e1b85b5ce9`, rejected example
`01a1731a-06a4-7375-93a7-2750e9666e07` (RefundPending/Paid, refund=Pending,
claim-held, ambiguous=False → never armed); carehome-refund-ambiguity →
`01a1731a-0d3b-7cd1-9cbb-924516c6c0c5` (RefundPending/Paid, Pending,
ambiguous=True); carehome-evidenced-external-refund →
`01a1731a-1301-7ab9-b10a-895179f0d9fb` (Refunded/Refunded, Completed);
carehome-booking-extension → current `01a1731a-19f7-756c-a89b-b42e887d24ac`
+ extension `01a1731a-1b66-7aad-a9dc-6d0612a08686`; carehome-checkout-dispute
→ `01a1731a-1f00-75ea-b27b-d3153b30cf0c` + dispute
`01a1731a-240e-7257-a853-56b0821167d2` (Open); caregiver-confirmed-booking →
`01a1731a-28d3-7dd6-8096-fbd112ea07ac`; caregiver-completed-booking →
`01a1731a-31cd-75a9-a4df-da931471dfda` + payout
`01a1731a-347c-7d8e-8247-fbf0184260e5`; caregiver-refund-ambiguity →
`01a1731a-3965-7abb-b27e-1b0cd44bd44f` (Confirmed, ambiguous, claim held);
subscription-and-duplicate-claim → premium subscription
`01a17319-f29d-7e29-802a-de44427036e3` + succeeded capture
`01a17319-f606-741d-947b-456ac1d4eca1` + DuplicateChargeClaim
`01a1731a-433e-7f99-9e08-76b16e0e1f4f` (Submitted) + shutdown family
`01a1731a-4b59-788d-84bf-5409dd2c0da0` + shutdown capture
`01a1731a-4d3e-74ce-817a-1df87e6a12e2` + SubscriptionRefundRequest /
RefundLedgerEntry `01a17347-4b59-788d-84bf-5409dd2c0da0`
(ServiceShutdown / Approved ledger); families-care-records →
`01a1731a-59ce-7d51-b293-335c239243dc` (Family),
`01a1731a-5d20-7972-ad8d-ea89ce3e437c` (Caregiver),
`01a1731a-5f61-746f-8ba4-ccca1d0711a3` (CareHome); family-private-access →
run logins verified present (`r2.family.owner@`, `r2.caregiver@`,
`r2.carehome.owner@`, `r2.support@`, `r2.superadmin@` — all
`@test.sanad.local`); complaint-received → `01a1731a-6705-7cbd-98b5-dc1f719cef24`
(Received); complaint-investigating →
`01a1731a-6ff2-7c86-802d-33cb59da3c39` (Investigating).

**Step 5 — authenticated readback on the deployed revision (public API over a
second SSH tunnel 127.0.0.1:15499→127.0.0.1:5001):** fixture family-owner
login `POST /api/v1/auth/login` as `r2.family.owner@test.sanad.local`
(password only ever in memory; token never printed, temp token file deleted
afterwards) → **200 OK, no OTP/2FA**; `GET /api/v1/family` → **200 OK**,
family `01a17319-cce8-7f98-b1a0-d4d26a820cc2` (ownerUserId set, 1 member) —
a fixture-shaped id on the deployed revision. Owner-side read: care-home
owner `POST /api/v1/auth/login` as `r2.carehome.owner@test.sanad.local` →
**200 OK**; `GET /api/v1/care-homes/facilities/mine` → **200 OK** returning
exactly ONE facility, id `01a17319-e6a9-728d-b2ed-9a5f2d1b8c61` (the
fixture's main facility, cross-checked read-only in SQL as owned by
`r2.carehome.owner`), status 5. `/care-homes/residents/mine` was NOT called
(known 500 on the deployed revision, fixed only in undeployed S1). No money
writes anywhere; no Paymob/provider call; no service config change; clock
untouched; guards never disabled.

**Step 6 — cleanup:** both SSH tunnels killed (DB-tunnel pid and API-tunnel
pid), forward ports confirmed closed; no nonce or temp database remains on
the VPS (the provisioner only ran marker-keyed upserts; no drops); local
token temp file removed.

**Remainder of the approved write set / gated set:** the 14 provisioned
groups above are the COMPLETE approved S3b write set. Still NOT provisioned
(and still hard-refused by the tool until an authorized deploy carries the
refund-outcome seam): G1 caregiver-refund-failed, G2
carehome-refund-failed, G3 duplicate-claim-approved-awaiting-execution, G4
duplicate-claim-ambiguous.

**Executed checks this round:** corrected-build nonce proof (above);
`--mode verify` rehearsal PASS; provision ×2 PASS with clean run-wide
invariant; pre/post counts; marker duplicate checks; HTTP readbacks. NOT RUN
this round (not authorized): full integrated test suite (S4), any Production
work, any deploy. Focused/provisioner test suite unchanged from S3a
(31/31) — no code changed in S3b; the only repository edits are this brief's
S3 checkpoint, task row and current-result line.

**Limitations:** (1) the revision correlation remains SSH-side evidence
(binary mtimes + one-docs-file diff at
`git diff 4690b5b2..HEAD`), recorded via `--expect-revision`; (2) `--instant`
for the timeline anchor was the run-time UTC values recorded above
(provision run 1 `2026-10-25T12:46:52Z`, run 2 `12:48:03Z`) — fixture
timestamps are anchored to these, not to a service clock; (3) complaint
reviews remain Received/Investigating as approved; (4) the deployed API
serves the fixtures (login + both reads prove reachability and identity), but
deeper portal screen behavior for every fixture is verification work for S4's
integrated checks, not S3b; (5) the remote history tables are quoted
mixed-case (`"__EFMigrationsHistory"`, `"MigrationId"`) — the SSH re-check
queries must quote identifiers (provisioner itself reads them through EF and
was unaffected).

**Private credentials/keys (paths only):** provisioner credentials
`C:\Users\mosad\AppData\Local\Temp\opencode\s3a-provisioner-credentials.txt`
(3 lines, connection string rewritten to the tunnel endpoint — now stale after
tunnel close, as intended); reviewer identity plan
`s3a-reviewer-credentials.txt`; SSH key `C:\Users\mosad\.ssh\sanad_vps`.
Receipts: `s3b-run1/s3b-run2` (local nonce), `s3b-plan-rehearsal`,
`s3b-remote-run1/s3b-remote-run2`, `s3b-verify-rehearsal.txt`,
`s3b-remote-counts-before/after.txt`, `s3b-remote-provision-run1/2.txt`.

**Next action (Mastermind):** accept S3b (completion gate met) and proceed to
S4 (integrated checks, report-item disposition, release request); G1–G4
provisioning stays deferred until the refund-outcome-seam deploy is
authorized and deployed.

### S3c worker checkpoint (current result — replaces the prior current result for handback)

**S3c result: ready_for_review (handback, after a SECOND bounded correction round
triggered by the Owner-authorized release-gate review).** The fixture-only
refund-outcome seam is implemented, tested locally and documented internally;
the provisioner gates the four seam groups behind `--seam-deployed` and its
run-wide inertness invariant exempts exactly those groups under the
declaration. Baseline still `30e664f343cbf4b0a842d967e8439324ae124317`;
everything remains uncommitted. No remote write, no VPS contact, no provider
call, no deploy, no migration, and G1–G4 were NOT provisioned (operator work
for after the authorized deploy). Only the brief's S3 sections were edited
among docs, plus the new internal-only seam note.

**Release-gate correction round (this round, bounded to the seam + tool +
tests + internal note):**
1. G4 RERUN DEFECT — `SeamClaimAsync` demanded Approved/AwaitingExecution
   UNCONDITIONALLY before the `!approved` execution, so a second provisioning
   run refused the existing Approved/Ambiguous G4 claim before its skip guard
   could run. Fixed: the rerun decision is now the pure, unit-proved
   `SeamClaimRerun.Resolve` gate used BOTH before and after the scripted
   execution — the ambiguous group permits exactly its scripted end state
   Approved/Ambiguous and SKIPS re-execution (detail text reports the skip);
   the approved group still tolerates ONLY Approved/AwaitingExecution; any
   drifted/foreign state (e.g. Executing, Rejected, Submitted-after-approve)
   is refused. Regression fact:
   `Seam_Claim_Rerun_Gate_Skips_The_Prior_Run_Ambiguous_Claim_And_Refuses_Drifted_States`.
2. VALIDATOR-ON-RELOAD TRUTH — the prior round omitted the registered
   `IValidateOptions` from the reload tests, so they asserted a behaviour the
   real composition does not have: the options pipeline enforces the
   validator on EVERY monitored-value creation (reloads included), so an
   INVALID reload makes `IOptionsMonitor.CurrentValue` throw
   `OptionsValidationException` (the change callback re-validates eagerly; the
   reload site observes it, possibly Aggregate-wrapped) instead of reaching
   the decorator's lazy parse. Fixed safely, per the review: (a) the
   decorator FAILS CLOSED — when the monitored options are unreadable, EVERY
   refund call (allowlisted or not: the allowlist is unknowable) is refused
   with `Paymob.RefundOutcomeUnknown`, inner client never touched, non-refund
   ops unaffected; (b) the decorator's state/change listener tolerates the
   validation exception explicitly (logs an error line, never propagates);
   (c) reload facts now build the monitor WITH the real validator (as the
   composition root does) and new facts run reloads through a REAL
   composition-root host whose own options pipeline is exercised;
   (d) startup refusals are unchanged (Production, malformed values even
   disabled, empty allowlist while enabled, unresolvable environment — the
   Production guard was not touched). Proven: valid reload Rejected→Completed
   through the real host (the `fixture-refund-` reference is decorator-only);
   invalid reload fail-closed for allowlisted AND unlisted ids; Production
   CANNOT enable the seam via reload (fail-closed refusal, not a scripted
   outcome, not a delegation). Docs corrected to state this honestly: a
   reload to invalid values does NOT "succeed and refuse one call" — it makes
   the options unreadable until corrected/re-reloaded, with every refund
   refused fail-closed in the meantime.
3. TEST-HYGIENE / SAFETY FINDING — an ambient machine-level
   `Paymob__SecretKey` environment variable (with the other `Paymob__*`
   sandbox settings) was silently loaded by `Host.CreateApplicationBuilder()`,
   which armed the REAL `PaymobClient` as the keyed inner in the Production
   reloadable-host fact and made ONE outbound refund attempt during the first
   failing run — a real POST of the synthetic fixture id to the sandbox URL
   with the TEST-mode key, refused by the gateway with HTTP 400 (no money
   moved, no customer data sent; reported honestly here — the fact was fixed
   immediately after). The reloadable-host fixtures
   now BLANK `Paymob:SecretKey` so the composition always resolves the offline
   `DevelopmentPaymobClient`; a comment in each fixture explains why. No other
   fact exercised a pass-through refund against a host-resolved inner client
   (checked).
4. xUnit1026 (provisioner test warning) fixed by making
   `Guard_Parse_Allows_Protected_Name_Only_Under_Tunnel_Declaration` actually
   use its `loopbackHost` parameter (parse the loopback tunnel terminus with
   the declaration, and assert the same shape stays refused without it) — the
   provisioner test project now builds and runs with ZERO warnings.
5. DOC CLEANUP CORRECTION — the internal note's cleanup rule previously said
   to disable/drop the seam when fixture verification is done. Corrected: the
   seam configuration MUST STAY IN PLACE while ANY seam-armed row remains
   (disabling it would expose the armed clicks — refund retry, claim Execute,
   reconciliation — to the real provider); reaping the rows is an operator
   action NOT authorized by this work, and only after ALL seam-armed rows are
   reaped may the section be removed.

**Seam design (as approved, with one implementation note):** decorator over
`IPaymobClient` registered in `AddFamiliesInfrastructure`. The real client
(Development or production `PaymobClient` per secret-key presence) is
registered under the keyed service `"paymob-inner"`; the decorator is the sole
non-keyed `IPaymobClient`, so care-home refund/retry/expiry, Families admin
refund retry and `SubscriptionRefundGateway` all route through one
interception point while non-refund operations delegate untouched.
Implementation note: instead of changing the `AddFamiliesInfrastructure`
signature, the environment check lives in an `IValidateOptions<...>` validator
(`FixtureRefundSeamOptionsValidator`) registered with `ValidateOnStart`; it
resolves `IHostEnvironment` optionally from the container (keeps the module
registrable in a bare `ServiceCollection`) and refuses `Enabled=true` under
Production, refuses Enabled with an unresolvable environment, and validates
the outcome map ALWAYS (even disabled). Because that validator is enforced by
the options pipeline on EVERY monitored-value creation (reloads included), a
reload to invalid values makes the monitored options unreadable; the decorator
then FAILS CLOSED — every refund call refused with
`Paymob.RefundOutcomeUnknown`, the real client never touched, non-refund
operations unaffected — and merely keeps its per-call parse as
defense-in-depth for containers registered without the validator.

**Exact new/changed files (S3c only):**
- NEW `src/Modules/Families/Infrastructure/Sanad.Modules.Families.Infrastructure/Payments/FixtureRefundSeamOptions.cs` —
  options (`SectionName = "FixtureRefundSeam"`), `FixtureRefundOutcome`
  (`Rejected | Unknown | Completed`), case-insensitive parser.
- NEW `.../Payments/FixtureRefundSeamOptionsValidator.cs` — IValidateOptions
  contract above.
- NEW `.../Payments/FixtureRefundSeamPaymobClient.cs` — the decorator; one
  credential-free informational log line per intercepted call (fixture txn id
  + outcome only), a STATE line at construction and on every options change
  (enabled flag, allowlist entry count, sorted ids), live
  `IOptionsMonitor` read with FAIL-CLOSED handling of unreadable (invalid)
  monitored options (every refund call refused with
  `Paymob.RefundOutcomeUnknown`, inner untouched; non-refund ops delegate
  untouched), per-call parse retained as defense-in-depth for containers
  without the validator, `IDisposable` for the change registration.
- CHANGED `.../DependencyInjection.cs` — options binding + `ValidateOnStart`,
  validator via `TryAddEnumerable`, inner clients keyed `"paymob-inner"`,
  decorator as sole non-keyed `IPaymobClient` built over
  `IOptionsMonitor<FixtureRefundSeamOptions>`.
- CHANGED `Directory.Packages.props` + `Sanad.Modules.Families.Infrastructure.csproj` —
  added `Microsoft.Extensions.Hosting.Abstractions` and
  `Microsoft.Extensions.Logging.Abstractions` (10.0.11).
- CHANGED `tools/Sanad.PortalProvisioner/PortalProvisioner.cs` —
  `PortalProvisionGroups.SeamGroups` (replaces `GatedOnDeploy`);
  `PortalProvisionOptions.SeamDeployed`; gate refusal without the
  declaration; four group bodies — G1 `carehome-refund-failed` (Reject →
  TryClaimRefund → MarkRefundFailed on the booking whose numeric txn id is
  `StableNumber("spf:<tag>:carehome-refund-failed")`), G2
  `caregiver-refund-failed` (paid+confirmed+CancelledByFamily with txn
  `spf-<tag>-seam-caregiver-failed-txn` → resolves Failed), G3 approved
  claim (dedicated capture slot role `seam.claim.approved`, submit + Approve
  → Approved/AwaitingExecution, never executed), G4 ambiguous claim (role
  `seam.claim.ambiguous`, Approve + IN-PROCESS Execute against the new
  `ScriptedUnknownRefundGateway` → State Approved/Ambiguous, Timeout ledger);
  `AssertRefundInertnessAsync` includes both seam capture slots and builds
  `SeamRefundExemptions` ONLY when `SeamDeployed`; `FixtureClaimSnapshot`
  gained `IdempotencyKey`; `PortalProvisionInvariants.Evaluate` gained the
  exemptions parameter with EXACT marker/claim-key matching.
- CHANGED `tools/Sanad.PortalProvisioner/PortalProvisionPlan.cs` — identity
  derivation helpers (`SeamCareHomeFailedTransactionId`,
  `SeamCaregiverFailedTransactionId`, `SeamClaimApproved/AmbiguousTransactionId`,
  `SeamClaimApproved/AmbiguousIdempotencyKey`, shared `StableNumber`,
  role ordinals 10/11), plan `Build(..., seamDeployed)` refusing seam groups
  without the declaration, planning them with their allowlist
  txn ids/outcomes when declared; report field
  `GatedOnDeployNotProvisioned` renamed `SeamGatedGroupsNotProvisioned` (+ new
  `SeamDeployed` field).
- CHANGED `tools/Sanad.PortalProvisioner/PortalProvisioner.cs` (release-gate
  round, additive) — NEW public `SeamClaimRerunDisposition` +
  `SeamClaimRerun.Resolve` (the pure rerun gate) replacing the unconditional
  post-approve Require; `SeamClaimAsync` now SKIPS the scripted execution when
  the ambiguous group's claim reads back as the prior run's
  Approved/Ambiguous (idempotent rerun) and refuses everything unscripted;
  detail text reports the skip on reruns. Gate/classification/invariant code
  otherwise unchanged.
- CHANGED `tools/Sanad.PortalProvisioner/Program.cs` — `--seam-deployed` flag
  (plan + provision), usage text (states the declaration must be backed by the
  deployed app's startup `FixtureRefundSeamState` log line with exactly the
  plan-printed ids). Gate, group bodies and invariant exemptions otherwise
  unchanged.
- NEW `tests/Sanad.UnitTests/Families/FixtureRefundSeamTests.cs` — 25 facts:
  disabled pass-through incl. allowlisted id; absent section binds pure
  OFF; enabled non-allowlisted delegation; Rejected/Unknown/Completed exact
  mapping with inner untouched; malformed-value runtime refusal
  (defense-in-depth); VALIDATED live-reload facts on a monitor carrying the
  real validator (valid reload Rejected→Completed; invalid reload fails
  closed for allowlisted AND unlisted ids — reload callback
  `OptionsValidationException` tolerated explicitly; Production cannot
  enable via reload); three reload facts through the REAL composition-root
  host (valid reload observed by the deployed decorator — the
  `fixture-refund-` reference is decorator-only; invalid reload fails closed;
  Production cannot enable via reload; all with the offline inner client
  pinned); validator Production/missing-environment/disabled-map facts;
  empty-allowlist-while-enabled validator + real-host-start refusals;
  Production host-start refusal + clean control; registration shape incl.
  keyed inner. Reloadable-host fixtures blank `Paymob:SecretKey` so ambient
  machine env vars can never arm the real provider client.
- CHANGED `tests/Sanad.UnitTests/Families/FamiliesInfrastructureDependencyInjectionTests.cs` —
  4 existing registration facts updated to the decorator + keyed-inner
  contract (behavior equivalence preserved: Development vs production inner).
- CHANGED `tests/Sanad.PortalProvisionerTests/PortalProvisionerFacts.cs` —
  classification asserts `SeamGroups`; plan refuses seam groups without the
  declaration and plans them ×byte-identical with the declaration; seam
  identity determinism; invariant facts: seam rows exempt ONLY under the
  declaration, normal-group and near-miss rows (care-home, families, claims,
  keyless claims) provably never exempt; NEW release-gate regression fact
  `Seam_Claim_Rerun_Gate_Skips_The_Prior_Run_Ambiguous_Claim_And_Refuses_Drifted_States`
  over the actual `SeamClaimRerun` gate (prior-run Ambiguous skipped, approved
  group's executed shape refused, fresh ambiguous routed to the scripted
  execution, unscripted states refused); xUnit1026 fixed in the tunnel-parse
  fact (parameter now used, missing refusal case asserted).
- NEW `docs/internal/operations/fixture-refund-seam.md` — internal-only note
  (config keys, enablement, Production refusal, empty-map refusal, validated
  live-reload contract: valid reloads honoured / invalid reloads fail closed
  with `Paymob.RefundOutcomeUnknown` + `FixtureRefundSeamState` state line,
  allowlist table, deployment sequencing with the mandatory startup-log gate
  verification, keep-the-seam-while-armed-rows-exist cleanup rule — no
  deletion authorized). No consumer docs changed.

**Seam config keys (no secret values):**
`FixtureRefundSeam:Enabled` (default false);
`FixtureRefundSeam:RefundOutcomes:<transactionId>` =
`Rejected` | `Unknown` | `Completed` (case-insensitive). The allowlist ids
for a run are printed by `--mode plan --seam-deployed` and derive
deterministically per run tag (see the identity helpers above). SANITY:
existing refund classification unchanged —
`ResultProblemDetailsMapper` keeps `Paymob.RefundRejected`→502 /
`Paymob.RefundOutcomeUnknown`→409; both failure codes reuse the real client's
exact `Error` code/message texts and completion uses the same
`Result<string?>.Success(referenceString)` shape as `PaymobClient`.

**Executed checks this round (release-gate correction round, re-run on the
corrected code):** strict Release FULL-SOLUTION build `-warnaserror`
(`Sanad.slnx`): **0 warnings, 0 errors**; provisioner project AND
`Sanad.PortalProvisionerTests` strict builds `-warnaserror`: **0 warnings,
0 errors** (the former xUnit1026 is FIXED, not pending); focused filter:
**25 seam facts + 5 composition-root DI facts = 30/30 PASS**;
`Sanad.UnitTests` FULL suite: **5596 passed / 0 failed / 27 skipped** (one
full-suite run this round, AFTER the correction-fix and the real-client-safety
pin were already in place — the finding surfaced during the focused host facts
and was fixed before any full-suite run); `Sanad.PortalProvisionerTests`:
**37/37 PASS** (36 pre-existing + the new rerun-gate regression fact).
NOT RUN (not authorized / not owned this round): S4's other integrated checks
(architecture tests, controller↔Postman mapping script, Postman JSON validity,
local-postgres recheck) were NOT re-run after these corrections; any
provisioning of G1–G4 (requires the later authorized deploy with
`FixtureRefundSeam` configured, gate-verified via the startup log line, then
`--mode provision --seam-deployed`).

**Limitations:** (1) the four seam groups remain unprovisioned on the
rehearsal cluster by order — the seam is compiled and inert (OFF) and the
allowlist is derived/printed by plan mode, but end-to-end on-cluster proof of
G1–G4 belongs to the post-deploy provisioning run; the G4 rerun fix is proven
by the pure-gate unit fact, not by an on-cluster second provisioning run;
(2) the reload facts mutate an in-memory configuration source and call
`IConfigurationRoot.Reload()`, which exercises the REAL options pipeline
(bind + registered validator + change-token invalidation) through a real
composition-root host, but is not a live appsettings watch on a running
server — the deploy step's startup-log verification remains the on-cluster
gate; (3) `Sanad.PortalProvisionerTests` remains outside `Sanad.slnx`
(pre-existing S3a decision) — built and run explicitly; (4) the plan
manifest's field rename (`gatedOnDeployNotProvisioned` →
`seamGatedGroupsNotProvisioned`, plus the new `seamDeployed` field) changes
the manifest JSON shape relative to the S3b manifests — S3b receipts retain
the old shape and are historical; (5) this correction round POST-DATES the
S4 integrated-gate run recorded below — the build/full-suite portions were
re-proven here (`-warnaserror` 0/0, 5596/0), but the S4 section's other
checks are NOT re-attested for the corrected code, and the S4 "release
request" is the Mastermind's to re-disposition against this round.

**Next action (Mastermind):** review this correction round and re-disposition
the release request honestly (the S4 integrated section's non-build checks
pre-date it); on acceptance, the deploy authorization path for the seam can
proceed, after which a follow-up provisioning run with `--seam-deployed`
realizes G1–G4 with idempotent reruns.

## S4 — integrated gate, disposition and release request

Mastermind stage, executed 2026-10-25 on the final working tree. Baseline HEAD
`30e664f343cbf4b0a842d967e8439324ae124317`; every S1–S4 edit is still
uncommitted. Nothing was deployed, no remote database was contacted, no
provider call was made, and no existing row or setting was changed in this
stage.

### S3c acceptance

Accepted after one bounded correction round. The correction round fixed two
real gaps and one verification gap:

1. **Live configuration read.** The decorator had captured
   `IOptions<…>.Value`, so the documented "config reload is honoured" guarantee
   was false and a restart-less config change would leave the seam apparently on
   while inert — the exact state that would let `--seam-deployed` create
   provider-armed rows. Options are now read through
   `IOptionsMonitor.CurrentValue` per call, outcomes are parsed per call (no
   stale cache), and the call-time refusal on an unparseable value is unchanged.
2. **Declared-but-inert refusal.** `Enabled=true` with an empty allowlist now
   fails host start (validator + real composition-root fact), instead of looking
   enabled and protecting nothing.
3. **Observability.** A `FixtureRefundSeamState` line (enabled flag, entry
   count, sorted allowlisted ids) is emitted at construction and on every
   options change. The internal operations note now requires the operator to
   confirm that line matches exactly the ids printed by
   `--mode plan --seam-deployed` before provisioning G1–G4; the provisioner's
   usage text states the same, with no behaviour change to the gate or the
   invariant.

The gate (`SeamGroups` refused without `--seam-deployed`, exact-marker/claim-key
invariant exemptions only under the declaration) and the four group bodies were
re-verified as unchanged.

### Integrated checks (final code, Release, re-run after the last code change)

| Check | Result |
|---|---|
| `dotnet build Sanad.slnx -c Release -warnaserror` | **0 warnings / 0 errors**, exit 0 |
| `dotnet test tests/Sanad.UnitTests -c Release --no-build` | **5592 passed / 0 failed / 27 skipped** (8 m 23 s) |
| `dotnet test tests/Sanad.ArchitectureTests -c Release --no-build` | **1/1 passed** |
| `dotnet test tests/Sanad.PortalProvisionerTests -c Release` | **36/36 passed** (project outside `Sanad.slnx`, built `-warnaserror` clean) |
| `docs/internal/tools/Verify-ApiContractMapping.ps1` | 671 controller route-method signatures, 770 Postman requests, **0 missing / 0 orphan** |
| Postman JSON validity | all **24** collections parse |
| `git diff --check` | **clean**, exit 0 (only pre-existing CRLF informational warnings) |
| Local `postgres` maintenance-DB recheck | **0 non-system tables**, only the `public` schema, no `SanadS3aProv_*` nonce databases — the S3a restoration claim is confirmed |

The 27 skips are the PostgreSQL-gated readback/continuity facts that require an
authorized disposable target. HONESTY CORRECTION (release-gate review): only the
FOCUSED PostgreSQL fact sets explicitly recorded in this brief were ever proven —
`CareHomeResidentsPostgresTests` 8/8 (S1 rerun) and `CareHomeFinancePostgresTests`
13/13, both on explicitly authorized disposable targets. The REMAINING skipped
facts were NOT run in this phase and are NOT covered by the S1–S3b evidence above;
they remain PostgreSQL-gated and unproven until a separately authorized run.

### Report-item disposition

| Report item | Disposition | Evidence |
|---|---|---|
| A1 residents query 500 (translation) | **Fixed** | Mapped-property `Contains`/`ILike` with trim + metacharacter escaping in `CareHomeCareRecordsController`; PostgreSQL facts for default, pagination, non-empty search, empty result and foreign-owner exclusion; HTTP 200 on readback |
| A2 owner Draft finance reads | **Fixed + fixture-proven** | Draft facility fixture group provisioned on the rehearsal cluster; dashboard/revenue return zero/empty with no activity and retain real activity; relational facts |
| B1 `Paymob.RefundRejected`→502, `RefundOutcomeUnknown`→409 | **Fixed** | Central mapper + 317 pre-existing refund-mapping tests; `Retry` annotated 502 only (the actually mapped status), `RecordCompleted` left at its real 200/400/404/409 |
| B2 subscription reads for refund investigation | **Fixed** | Per-action policies: `OperationalReadAdmin` on reads only; authoring/publication/retirement unchanged; 12/12 HTTP authorization-boundary facts |
| B3 `GET /api/v1/admin/feedback/app-ratings` | **Fixed** | New paginated SuperAdmin/SupportAdmin read, id-only fields; empty/non-empty and role-boundary facts; consumer doc + Postman collection |
| B4 framework-validation vs domain-problem shapes | **Policy answered (documented)** | Both shapes documented in `docs/admin/api-contract-conventions.md` with examples; no cross-cutting response rewrite |
| C1 bilingual chat participant names | **Fixed (additive)** | Nullable `displayNameArabic`/`displayNameEnglish` beside `displayName`; facility names override personal names for care-home owners; no synthetic fallback; focused fact |
| C2 notification text | **Verified — already satisfied** | Onboarding, booking-decision and chat producers already bilingual and identifier-free; `ReadableNotificationTests` pins it; no producer edit, no historical rewrite |
| D fixture mechanism | **Delivered for data-only groups; seam groups pending deploy** | 14 data-only groups provisioned on the rehearsal `SanadDb` through the SSH tunnel, ×2 byte-stable manifests, 45 tables grew / 0 shrank, run-wide invariants clean after every group, authenticated readback 200 for family and care-home owners |
| D seam-dependent scenarios (Failed refunds, claim execution states) | **Implementation complete, provisioning pending** | Refund-outcome seam compiled and OFF by default; G1–G4 refused without `--seam-deployed`; blocked on an authorized deploy, not on code |
| D fixture acceptance (idempotency, inertness, unrelated data) | **Met for the groups provisioned** | Marker-keyed upserts, refusing gateway in the tool, per-group invariant, zero shrink in pre-existing tables. Fixture state is never presented as provider settlement |
| E medical audit ID-only; gateway secrets server-configured; FAQ deactivation; registration-only growth | **Verified — already satisfied** | Audit metadata only; no settings registry or secret read/return; deactivate-only FAQ; `newRegistrationsByMonth` explicitly not active-user growth |
| E wellness free-text categories, cover/image | **Fixed (docs)** | Admin + app docs state the ≤100-char free-text category and existing image support; no taxonomy added |
| E care-home listing-subscription claim | **Fixed (docs)** | False claim removed; no listing subscription or billing exists or was added |
| E landing storage URL/access and ordered refs | **Fixed (docs)** | Public `/files/<key>` access with the `/files/private/*` guard, ordered refs and hidden suppression documented from verified behaviour |
| E response annotations, reason caps, numeric visit days, registration/verification examples, library field validation, nullable support contact | **Fixed / verified per item** | See the S2 `E — contract reconciliation disposition`; each item checked individually against source before editing |
| E preserve items 3, 4, 5 and delivered `identityPresent` | **Preserved** | No phase edit touches those surfaces |

### Reported observations carried (not fixed here, and why)

1. **Caregiver care-records picker still peels `.Value`** in
   `CaregiverCareRecords.cs` — a separately reported finding; the Owner
   instructed it stays untouched in this phase. Not a regression.
2. **Visit-settings update path conflates validation and stale-version failures
   into `409 CareHomes.Visits.Conflict`** — behaviour change not approved in
   this phase; the *documentation* was corrected to describe the real behaviour.
3. **Stale Postman/doc collection counts** — fixed in S4, docs only:
   `docs/README.md` said 20 collections (actual 24);
   `docs/postman/README.md` omitted `Sanad.Admin.CaregiverNotes` from the admin
   list and did not name the root-level `Sanad.Auth` / `Sanad.ElderlyWelcome`
   collections.
4. **Release build log emits the three Chat projects at `bin/Debug` paths** when
   building `Sanad.slnx` (pre-existing; the same lines appear in the e06-era
   build logs). Building the API project directly in Release emits them to
   `bin/Release` correctly, and the current API Release output carries the
   Release-sized Chat assemblies. No functional change; during the authorized
   release, build/publish the API project directly and confirm the published
   Chat assembly sizes. Not repaired here to avoid an out-of-scope build-system
   change.

### External dependencies (unchanged, not blockers for code)

- Public Terms/Privacy text must come from the Owner; no placeholder was
  published.
- The orphan-profile deletion candidate list still needs the Owner's explicit
  confirmation before anything is deleted.
- Mobile-team evidence that the installed binary sends `claimSubmission: true`
  remains external; the fail-closed legacy behaviour is preserved.

### Release request

Requested: authorization to commit, push and deploy **S1–S4** as one release.

- **Contains:** the S1 resident/finance/refund-annotation/permission/app-ratings
  work, the S2 chat-name and documentation corrections, the S3a/S3c provisioner
  and fixture-refund seam, and the consumer docs/Postman changes — all listed in
  the S1/S2/S3 checkpoints.
- **Ships inert:** `FixtureRefundSeam` is compiled but absent/`false` in every
  environment; it cannot be enabled in Production (startup refusal) and is only
  meant for the rehearsal fixture run.
- **Not included:** provisioning of G1–G4 (needs the seam enabled on the
  deployed revision and the startup-log gate check), any demo-corpus seeding,
  any database reset or deletion, and any clock change.
- **After deploy, on your go-ahead:** verify revision/migrations/health,
  enable the seam on the rehearsal host only, confirm the `FixtureRefundSeamState`
  ids match the plan manifest, provision G1–G4 with `--seam-deployed`, run the
  authenticated readback, and write the release receipt. The demo corpus (D1–D4)
  follows that, starting with its dry run on a throwaway nonce database.

## Release verification (2026-10-25) — replaces the pending release state

Deployed and verified revision `beb315fe89dfa2f0a5367b57c694674ecb4352d4`
(`4690b5b2..beb315fe`) on the authorized rehearsal VPS `72.62.92.144`, service
`sanad-api`, `/root/sanad-api`, environment `Production`, public
`http://72.62.92.144:8091`. Publisher `Publish-SanadApi.ps1 -Configuration Release`
(direct API project) produced 0 warnings/0 errors; the package carried the Release
Chat assemblies and no env/development/keys/certs. Archive
`c2973388…68e0d8` verified on the target; validated database backup 824,488 bytes
(892 TOC entries) and app backup retained; 9/9 migration heads, 0 pending, no
migration applied; `CONFIG_UNCHANGED`; service active, `NRestarts=0`; verifier
exit 0 with all nine startup context checks, anonymous smoke 200. Authenticated
readback: care-home owner residents default/pagination/search/literal `%` all 200
(the reported 500 is gone), Approved dashboard/revenue 200, Draft owner
dashboard/revenue 200, SupportAdmin/SuperAdmin subscription list+detail 200 and
app-ratings 200 (plans 403 for Support), Family 200 and admin ratings 403, anonymous
401; every fixture session logged out. The fixture refund seam is absent/disabled
and was not configured. G1–G4 provisioning and the demo corpus remain deferred;
no migration, reset, deletion, clock change, provider request or money movement
occurred in this stage. Receipt:
[portal-second-pass-release-2026-10-25.md](../reports/portal-second-pass-release-2026-10-25.md).
The S4 gate counts above (5,592 / 36 provisioner) pre-date the final corrections and
are historical; the final gates (5,596 / 37) were not re-run during this release
stage, which relied on the publisher's independent strict Release build.
