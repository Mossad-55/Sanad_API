# Media, care records, payments and Rentals removal — execution brief

## Control record

### Current execution status — locally accepted; external/release evidence remains blocked

Owner approved the implementation scope and selected all worker models. Serialized
P1 Rentals removal is now **locally accepted**; P1 handback session:
`ses_e9607ee9effebMTAgkryH3uWV2` (`river/deepseek-ai/DeepSeek-V4.1-Flash#high`).
Rentals runtime/routes/projects are removed; deployed DB schema/data are retained.

Mastermind review corrections and verification:
- Fixed integrated compile/test issues, including care-record nullable EF mappings,
  query/controller dispatch, migration metadata, test adapters, and W2 privacy
  projection alignment.
- Updated Paymob key-mode handling for the newer Egypt prefixes: the development
  fixture now blocks `egy_sk_live_` as well as legacy `sk_live`, and fixture-log
  redaction covers new-format secret, public and client-secret keys. The local
  fixture verifier also now passes under Windows PowerShell 5.1 and PowerShell 7.
- `dotnet build Sanad.slnx --no-restore -c Release -m:1`: PASS, 0 warnings/errors.
- Final full suite on the exact final build: architecture 1/1 PASS; unit tests
  5,324 passed, 26 skipped, 0 failed (5,350 total), 3m13s. The run overrode
  `ConnectionStrings__IdentityIntegrationDatabase` with a random nonce base
  database: the Identity fixture ran only against that nonce, Families F1,
  W4-F3 and the slice-schema suite each created a verified-absent random
  database and removed it, and Chat used its dedicated
  `SanadChatCleanupTestDb`. The nonce base was dropped after the run; no shared,
  fixed-name or production database was deleted. The 26 skips are 4 FUR-02
  verification-continuity cases, 12 FUR-03 CareHomes finance cases, 7 FUR-04
  CareHomes readback cases that require their explicit fixed-name targets, and
  3 legacy/unrelated concurrency cases. Skips are not claimed as passes.
- Focused care-record HTTP-host tests: 18/18 PASS.
- W4-F3 nonce-database PostgreSQL proof: 2/2 PASS against a random database on
  validated localhost:5432; the test applied only its Families/Finance migrations
  to that disposable target and its fixture removed that exact target. No shared
  or production database was touched.
- Slice schema nonce-database proof: 3/3 PASS. All three new migrations apply;
  care-record → elderly deletion is RESTRICT, confirmed by constraint
  `FK_care_records_elderlies_elderly_id` and SQLSTATE `23001`; Community
  `Version`/`DeletedBy`/`DeletedOnUtc` round-trip after a real delete; CMS
  `video_storage_key` round-trips with its 500-character database cap. The test
  created and removed only its random `SanadSliceVerify_*` database.
- Families F1 nonce-database suite: 8/8 PASS (it also applies
  `20261023131500_AddCareRecords`); Chat attachment-cleanup PostgreSQL suite:
  3/3 PASS on its dedicated `SanadChatCleanupTestDb`; Identity relational suite:
  2/2 PASS on a nonce base database that was dropped afterwards.
- Real-disk storage proof: 34/34 focused storage/media tests PASS. A private MP4
  persists and reopens through a second `LocalDiskFileStorage` instance; the
  host answers a byte range with 206 from a real file; an unsatisfiable range
  returns 416; and a simulated host restart (new host, new storage instance,
  same root) serves the full file. The asset is still a synthetic 12-byte
  `ftyp` box, not genuine video or a consumer screen.
- Focused P1/cap suites: 31 passed, 0 failed (Rentals removal, legacy webhook,
  complaint cap).
- Final route/Postman mapping: `Verify-ApiContractMapping.ps1` PASS, 662 controller
  signatures / 758 Postman requests, 0 missing / 0 orphan. All 25 Postman JSON
  collections parse; `git diff --check` PASS (only Git line-ending notices).
- Real Paymob evidence is **PARTIAL; end-to-end payments remain BLOCKED**. The
  Owner supplied a test-mode secret; its value is not recorded. The new-format
  public key was verified as test-mode. A direct Paymob test intention using the
  configured Card integration returned HTTP success with the expected intent
  fields; the Wallet integration returned HTTP 404. These were provider-intent
  checks only, with synthetic billing data and a 1 EGP test amount; no checkout
  was completed and no charge was made. Paymob unit tests passed 57/57 with the
  test environment loaded. The supplied test secret was exposed in chat and must
  be rotated before further provider use. Owner directed us to stop wallet
  sandbox investigation and move on; wallet end-to-end verification is deferred,
  not passed, and no further wallet provider requests should be made for this
  slice. The observed failure remains unresolved: after the Owner supplied the active wallet ID and transaction
  unresolved: after the Owner supplied the active wallet ID and transaction
  callback URL, a direct Paymob wallet-intent retry returned HTTP 404. Its
  sanitized body identifies Paymob's `Integration ID/Name does not exist` error;
  this is the Paymob endpoint response, not a Sanad API response. Paymob's current
  [Create Intention documentation](https://developers.paymob.com/paymob-docs/developers/intention-apis/create-intention)
  says the ID must match the secret key's Test/Live mode, belong to the same
  account and be an online, well-configured integration. The client sends the ID
  as an integer, as documented. The dashboard's “active” status alone does not
  verify those conditions. The transaction callback is not contacted during
  intention creation and cannot cause this 404. A Release solution test run after
  the safety fixes, with database/payment credentials scrubbed, passed Architecture
  1/1 and Unit 5,309/5,353 (44 skipped, 0 failed); the skips are DB-gated.
  `dotnet build Sanad.slnx --no-restore -c Release -m:1` passed with 0 warnings/
  errors; offline fixture verification passed 306/306 under both Windows
  PowerShell 5.1 and PowerShell 7. Re-ran route/Postman parity: 662 controller
  signatures / 758 requests, 0 missing / 0 orphan. Fixed the stale focused mapping
  test's script path and expectations; it passes under both PowerShell 5.1 and 7.
  Continued provider-free payment verification: Paymob intent selection/error
  surface, HMAC, booking payment-intent/webhook and subscription callback focused
  tests passed 111/111 with provider credentials scrubbed and no database access.
  Owner's updated release sequence is: verify the Community post response's saved
  author-avatar download URL, run FUR-02/03/04 relational proofs, then complete
  the Paymob Test-mode Card flow. The avatar response contract is covered
  directly (added a JSON-body regression proving `avatarUrl` contains
  `/api/v1/community/authors/{id}/photo`, not the private storage key; Community
  feed and author-photo focused tests passed 42/42; no consumer app required).
  Existing real-disk storage proof remains 34/34; it uses synthetic MP4 bytes and
  proves storage/range/restart, not genuine-content playback. Wallet checkout is
  deferred per Owner direction. No new Paymob request was made in either later
  continuation. The earlier verified nonce-DB full run remains 5,324 passed /
  26 skipped / 0 failed; the 26 skips included the 23 FUR cases executed below.
  The exposed Paymob and PostgreSQL credentials were rotated by the Owner and a
  later PostgreSQL safety preflight that had accidentally emitted the ambient
  local connection string was superseded by that rotation; this continuation ran
  on the rotated values only.

  FUR-02/03/04 relational proofs plus the local-safe Card callback proof were
  then completed on the exact guarded local targets (worktree HEAD `9433c1d` +
  preserved uncommitted delta; worker `river/zai-org/GLM-5.3-Flash`; every test
  child process ran with exactly one `ConnectionStrings__*` variable injected and
  all other `ConnectionStrings__*`/`Paymob__*` variables scrubbed and restored):
  - `dotnet build Sanad.slnx --no-restore -c Release -m:1`: PASS, 0 warnings /
    0 errors, exit 0.
  - FUR-02: the required target `sanad_fur02_verify` was absent; it was created
    with its exact quoted name (absence verified before creation). 4/4
    VerificationContinuityPostgresTests PASS (Release). Post-run `identity.users`
    = 0; no care_homes schema — expected for that suite.
  - FUR-03: `SanadFur03VerifyDb` (care_homes-schema history convention) was
    upgraded additively first — 3 pending migrations applied to head
    `20261022213000_AddCareHomeRefundPolicySnapshots`, 18/18 applied — by a
    temporary local tool since removed. 12/12 CareHomeFinancePostgresTests PASS.
    Post-run fixture rows: 0.
  - FUR-04: the committed test constants were stale versus HEAD (29 = care-homes
    15 + identity 14; the refund-policy wave grew the baseline to 35 = 18 + 17).
    `CareHomeReadbackPostgresTests.cs` now pins the current committed baseline
    (18/17 counts, care-homes head `20261022213000...` asserted as last) while
    staying strict. 7/7 CareHomeReadbackPostgresTests PASS; the suite
    self-migrated both contexts (public history now 35 rows). Post-run fixture
    rows: 0.
  - Restored `tests/Verify-ApiContractMapping.Tests.ps1` failure-detection
    coverage: fixture-repository end-to-end cases drive the real checker script
    as a child process (missing route → exit 1 with both diagnostics; clean →
    exit 0; negative inlined item still accepted → exit 0); it passes under
    Windows PowerShell 5.1 and PowerShell 7. Real-checker re-run on the repo:
    662 controller signatures / 758 Postman requests, 0 missing / 0 orphan,
    exit 0.
  - Card-sandbox fallback (local-safe): new
    `tests/Sanad.UnitTests/Families/Bookings/BookingPaymentCallbackPostgresTests.cs`
    (6 cases) proves the exact webhook-driven handler
    (`ConfirmBookingPaymentCommandHandler`) persists card callback outcomes on
    PostgreSQL. It derives a randomly named `SanadCardCbVerify_*` target from
    `ConnectionStrings__IdentityIntegrationDatabase` (loopback:5432 guard),
    proves the name absent, creates it explicitly, tracks it the moment it
    exists (so teardown runs even if migration fails), applies only the
    committed Families migrations with their per-schema history, uses the
    `DevelopmentPaymobClient` double so no provider call was possible, and
    after every test drops that whole database — there is no per-row cleanup;
    the handler's DbContext is disposed per test. 6/6 PASS (Release) re-run
    after the parent-review fixes (per-test context disposal;
    failed-migration-safe tracked creation; corrected class doc) with a
    zero-warning build: success persists Paid + transaction Succeeded; amount
    mismatch refused without state change; duplicate success idempotent
    AlreadyProcessed; failure persists Failed; pending acknowledged without
    state change; unknown order → Bookings.NotFound. Post-run full local
    database inventory lists no `SanadCardCbVerify_%` database.
  - Residue and safety: all three FUR targets and the callback-proof database are
    the only databases written; no destructive reset, and no host outside this
    machine's loopback PostgreSQL was contacted. The earlier "production lags
    HEAD" phrase is corrected: it was a read-only catalog measurement of the
    LOCAL `SanadDb` development database on the loopback server (applied
    migrations: care_homes 16 / last `20261020210910_AddCareHomeRatingModeration`,
    identity 15 / last `20261020191733_AddSupportTicketWorkflow`), which lags the
    HEAD baseline of 18/17 asserted by the FUR-04 suite's fresh-migration
    verification. Whether the actual remote deployed host lags HEAD is unknown;
    no VPS/production contact is authorized or was made. Migrations were
    confined to the three named local FUR targets.
  - PASSED — real Paymob Test-mode Card E2E with real webhook delivery
    (Owner-authorized bounded run, 2026-10-24): the Owner explicitly authorized
    temporarily exposing ONLY the local webhook endpoint via an HTTPS tunnel
    for a sandbox Card checkout (no VPS, no real charge, no live keys, no
    commit/push/deploy, wallet stays deferred). Evidence chain, sanitized
    throughout (values/secret/checkout URL never printed or logged):
    1) Test-mode gate (booleans only): User-scope `Paymob__SecretKey` matches a
       test pattern and not a live pattern; public key test-pattern; Card
       integration numeric; HMAC secret present; base host accept.paymob.com;
       webhook URL https via the temporary tunnel.
    2) Disposable nonce databases `SanadSbxCard_*` on loopback:5432 with
       ownership markers; the real app process later created and migrated its
       own nonce through the real startup migration path. Both nonces dropped
       afterward; final inventory shows no `SanadSbxCard_%` and no
       `SanadCardCbVerify_%` database (`sanad_fur02_verify` from the earlier
       authorized run remains; pre-existing stray `sanadbrunotestdb` untouched).
    3) Fail-closed negative proof: on the app-created nonce the Finance startup
       migrations are Development-gated off, so the E04 platform-controls row
       was absent and intent creation was refused with
       `Platform.ControlStateUnavailable` before any provider call — the gate
       fails closed exactly as specified.
    4) On the fully-migrated nonce (one real Finance migration run including the
       E04 seed), the REAL `PaymobClient` inside the real DI graph accepted one
       Card intention: accepted=true, amount 226.86 EGP, PaymobOrderId = the
       seeded booking id, one Pending transaction persisted by the real intent
       handler.
    5) The REAL hosted checkout (eg.checkout.paymob.com) was completed in a
       real local Chrome driven by a temporary Playwright harness: official
       Paymob sandbox test card (the public sandbox card published in Paymob's
       docs), success text observed on the hosted page. The checkout URL with
       its single-use client secret existed only inside the browser and a
       loopback `/go` redirector (file → response header); it was never printed.
    6) The REAL provider webhook was delivered over the temporary public tunnel
       to a restricted forwarder that forwarded ONLY the exact webhook path
       (GET on it was rejected 405; all other routes 404/405; counts:
       forwarded=1, rejected=1, last status 200). The real
       `PaymobWebhookController` verified the HMAC and the real
       `ConfirmBookingPaymentCommandHandler` persisted: transaction Succeeded
       (provider transaction id masked `55953645…`), SettledOnUtc
       2026-10-24T02:06:58Z, booking PendingPayment → PendingCaregiverApproval;
       a second readback was identical (stable state). Exactly one webhook was
       delivered (no provider retries observed); duplicate/idempotent behavior
       remains covered by the 6/6 callback-proof suite.
    7) Teardown verified: edge/tunnel/app stopped by own PIDs only, ports
       released, tunnel closed, both nonce databases dropped, credential-bearing
       temp files (checkout URL file, state file with tunnel host) deleted,
       env scopes verified restored (Process: the 8 baseline names, connection
       string value equal to the User-scope value, no secret; User: 9 names
       including the rotated secret; Machine: 0), and no harness-owned browser
       processes remained. No charge (Test mode only), no commit/push/deploy,
       persistent `SanadDb` untouched.
       Harness disclosure (what was real vs harness): real app components were
       the `PaymobClient`, the intent handler, the webhook controller + HMAC
       verification, `ConfirmBookingPaymentCommandHandler`, all persistence,
       and Paymob's own hosted checkout; the temporary harness (local-only,
       since removed/deactivated) was a provisioning/invoker tool driving the
       real handler, the restricted loopback forwarder, the `/go` redirector,
       a cloudflared quick tunnel, and the Playwright driver filling only
       Paymob's official sandbox test card. Receipts are sanitized and were
       kept in the worker temp directory only.
    Remaining NOT RUN / release blockers (unchanged in nature): the deployed
    host's schema state is unknown (no VPS contact authorized); production
    webhook URL configuration (VPS/dashboard) is a cutover task; wallet checkout
    stays deferred per Owner; release authorization itself belongs to
    Mastermind/Owner.
  - No deployment, charge, commit or push was performed.

The next phase was **dispatched in four parallel authoring lanes** after the
Owner's explicit continuation signal. No production-host/provider operations,
schema/data deletion, commit/push/deploy were authorized. P1's `rnt_` callback returns
503 pending reconciliation; provider retry semantics/event inventory remain a
production-cutover blocker, not a blocker to parallel code authoring. W4 must
revisit only with sanitized evidence/verified provider policy; do not acknowledge
unapplied payment as settled.

| Worker | State/session | Model | Owns |
|---|---|---|---|
| W1 Care Records | ready_for_review — `ses_e95eb90f3ffeE78LtqLMsO2YGX` | `river/zai-org/GLM-5.3-Flash#high` | Families care-record aggregate, restrictive Elderly FK, API controllers, handler/domain tests, and HTTP-host route/auth tests. |
| W2 Community | ready_for_review — same session `ses_e95eb46bfffemLcSUpGpo3Uz1b` | `river/zai-org/GLM-5.3-Flash#high` | Community own-post edit/delete, moderator deletion, privacy alignment, deleted-post reaction guards, and actual HTTP-host binding/authorization tests. |
| W3 Storage/library | ready_for_review — same session `ses_e95eade3cffeFL6t3vs5kDo0ek` | `river/deepseek-ai/DeepSeek-V4.1-Flash#high` | Storage-backed library media, common upload hardening, plus actual test-host multipart video-upload binding/auth and HTTP 206 range regressions. |
| W4 Finance/payments | ready_for_review — `ses_e95ea8e6effeWTNtoeqagEmM6b` | `opencode/deepseek-v4.1-flash-free#max` | Payment/Paymob, complaint cap and F3 refund correction; no Families snapshot. |

Mastermind owns integration/shared API DI, error mapper, shared Postman collections,
the only build/test/runtime/database slot and acceptance. Workers are author-only:
they must not execute tests/builds, DB, provider, storage or host operations.

P1 exclusive temporary ownership (released at handback):
- Delete Rentals-only tracked sources/projects under `src/Modules/Rentals/`,
  `tests/Sanad.UnitTests/Rentals/`, the four rental API controllers and Finance's
  rental-only `RentalRefundCommands.cs`; preserve deployed schema/history.
- Exact shared amendments delegated solely for removal: `Sanad.slnx`, API/unit
  csproj; API `DependencyInjection.cs`, `ApplicationBuilderExtensions.cs`,
  `Program.cs`, `Deployment/DeploymentInfo.cs`,
  `ProblemDetail/ResultProblemDetailsMapper.cs`,
  `Seeding/TestUserSeedTargetGuard.cs`, `Controllers/PaymobWebhookController.cs`;
  Finance infrastructure `DependencyInjection.cs`; unit API
  `PortalApiMetadataHost.cs`; internal tools `Publish-SanadApi.ps1`,
  `Test-SanadPortalGate.ps1`, `Verify-SanadDeployment.ps1`; current operations
  `publish.md`, `deployment.md`, `deploy-runbook.md`, `vps/staging.md`;
  `docs/postman/README.md`, rental Admin collection and family operations collection.
- Dedicated new removal/webhook regression test files under unit API tests;
  existing webhook tests only if needed for the removed branch. Do not delete
  shared money-core tests or alter historical receipts/contracts.
- Preserve existing cap-related mapper/Finance DI edits byte-for-byte except
  Rental-only lines. Do not fix the cap compile issue in this stage or claim the
  integrated build passes while it remains. Report any additional reference
  outside this manifest before editing it.
- Preserve HMAC validation. Legacy rental webhook callbacks must not be silently
  treated as successful financial settlement or sent to booking handlers. Propose
  bounded handling with explicit reconciliation visibility and tests; production
  cutover is blocked until outstanding event/payment inventory and recovery are
  verified. Never erase schema/data or send provider calls as part of removal.

P1 authored tests; Mastermind executed the focused suite above and accepted this
stage locally. No extra tracker/report files.

- Historical checkpoint before P1; superseded by the current execution status
  above. P1 has now been implemented, reviewed and locally accepted.
- Release: not_requested. Earlier refund-wave release authorization does not
  authorize this slice. Prepared 2026-10-23.
- Single task/status ledger: this brief. Acceptance rules live in the linked
  [testing contract](../contracts/media-care-records-testing-2026-10-23.md).
- Start from the repository's current HEAD and `git status`, recording both at
  actual kickoff. Do not assume the deployed revision equals the worktree.
- Mastermind owns contract mapping, exact file manifests, shared files,
  integration, reviews and the sole runtime slot.
- P0 pinned base: `9433c1df117f85131c3871f850659cc78981d298` plus preserved
  uncommitted planning documents and paused cap/script/Postman delta. Worker
  profile `opencode/deepseek-v4.1-flash-free#high` verified available at dispatch.
  Three disjoint read-only discovery lanes: A media/storage + Rentals inventory;
  B care records/portal permissions; C payment reads/Paymob + finance verification.
  No lane owns edits, builds, database or network/provider operations. Return
  findings to Mastermind; only Mastermind edits the shared ledger in P0.
- P0 lanes A `ses_e96504bebffebK9kuI7rR0fEPY`, B
  `ses_e965016ddffeG6P9I7l1XR5awK`, and C
  `ses_e964fde5fffe627eYjG8HALRvy` returned read-only handbacks; no worker
  changed files. P0 reconciliation is complete.
- Historical P0 dispatch checkpoint; superseded by current status above. The
  Owner later authorized continuation and W1–W4 were dispatched on the pinned scopes.

## Locked Owner requirements — do not ask again

### Latest Owner addition — community post edit/delete (LOCKED)

- An authenticated post author can edit and delete THEIR OWN post only. Another
  user's post cannot be modified/deleted through the app endpoint, including by
  forging an author ID. Author identity is taken from authentication and remains
  immutable. Authorization is enforced in handlers/database scope, not just UI.
- Administrators with the existing Community moderation permission can delete
  any post through the Admin surface. This does not grant unrelated Admin roles
  moderation powers or grant Admin editing/impersonation. Reuse the existing
  `CommunityModeration` policy and document its actual role membership.
- Map app and Admin endpoints, update UI consumer guidance/Postman and test both.
  Proposed shapes: PUT/PATCH and DELETE `/api/v1/community/posts/{postId}`;
  DELETE `/api/v1/admin/community/posts/{postId}`. Verify actual controller route
  prefixes and reuse equivalent existing commands before adding routes. The
  source scan found only DELETE like in the app posts controller; this is not
  a delete-post endpoint.
- Preserve existing anonymity, author attribution and moderation rules. Do not
  let editing silently bypass an existing approval requirement. Inspect and pin
  allowed fields/status transitions, concurrency/retry behavior and delete status
  codes in the implementation manifest; use established lifecycle semantics.
- Deletion must remove the post from public feeds/detail/search/saved reads and
  prevent comments/replies/reactions from continuing against a deleted parent.
  Retain required audit/moderation history; do not infer cascading hard deletion
  of users, all their media, or unrelated posts. Define referenced-media cleanup
  with the media owner so deletion never removes another record's asset.
- Verify same-record ownership on every edit/delete and approved access to any
  replacement image. Do not accept arbitrary external URL/foreign private image.
- Assign to the Community lane below, alongside R2 and community image ownership;
  do not dispatch a separate overlapping Community editor.

### Parallel execution plan — locked scope; Owner dispatch authorization satisfied

Owner supplied this model allowlist; exact IDs and efforts verified in catalog:

- River AI GLM 5.3 Flash/high: `river/zai-org/GLM-5.3-Flash#high`.
- River AI DeepSeek V4.1 Flash/high: `river/deepseek-ai/DeepSeek-V4.1-Flash#high`.
- Personal/OpenCode DeepSeek V4.1 Flash Free/max:
  `opencode/deepseek-v4.1-flash-free#max`.

Override the worker profile default explicitly at dispatch; no silent provider,
model or effort substitution. Owner authorized continued dispatch; the four pinned
sessions and actual model mapping are recorded in the status table above. Four
workers use three model choices. Keep files disjoint and runtime serialized; do not
switch models silently on rate limits.

Recommended maximum: **four implementation workers plus Mastermind integration**.
This supersedes the earlier fully sequential feature ordering where exact file
ownership is disjoint; dependency/migration/runtime gates still apply.

| Lane | Exclusive feature scope | Boundaries |
|---|---|---|
| W1 Care records | Families care-record aggregate, assigned portal controllers/handlers, its tests/guides; sole Families migration author | No subscription/payment files or shared API DI/mapper/Postman edits. |
| W2 Community | Author photos across posts/comments/replies, image ownership validation, NEW own-post edit/delete and Admin deletion, dedicated tests/guides | No shared storage/API policy or mapper edits; supplies bounded amendments. |
| W3 Storage/library | Storage-backed videos, common upload/download correctness, media caps/signatures/ranges, CMS migration, tests/guides | No Community controller/handler edits. Publish storage-interface amendments first; avoid interface churn while other lanes consume it. |
| W4 Finance/payments | Payment history/Paymob, cap draft, F3 correction, refund PostgreSQL tests | Own dedicated subscription/Finance files; no Families care-record files/snapshot edits. Any new Families migration waits for W1 or gets authored by W1's migration owner. |

Rentals removal is complete and shared files are released. Mastermind owns shared
solution/API DI/policies/error mapper/deployment and shared Postman changes. One
migration author per context: W1 owns any Families migration/snapshot, W2 any
Community migration/snapshot, W3 any CMS migration/snapshot; W4 must not modify
Families schema/snapshot. If Finance/payment work needs such a migration, stop and
request a serialized handoff rather than editing another lane's snapshot.

All lanes may author code and tests concurrently under exact manifests, but ONE
coordinated runtime slot executes build/test/API/PostgreSQL work. Focused checks
run at lane handback; run the complete combined gate once after final integration.
Missing required PostgreSQL/consumer/provider evidence remains BLOCKED, never a
silently skipped PASS. A reviewer may inspect a completed frozen diff concurrently
with unrelated authoring, but cannot write shared files or start another runtime.

Pinned path manifests, dependencies, and storage-interface boundaries were recorded
at dispatch against the then-current worktree. They remain the exclusive scopes;
there is no blanket folder-write license. No new product questions are needed for
this scope.

1. Remove the ENTIRE Rentals feature: API, code, Finance integrations, dependency
   registration, projects, tests, consumer documentation/Postman and deployment
   references. This supersedes prior decisions including equipment rentals.
2. Audit ALL media/file inputs and reads, not merely `IFormFile` occurrences:
   images, videos, PDFs, documents, avatars, certificates, reports, chat, CMS,
   nested JSON media fields, URL/key/path inputs and generated files.
3. Actual media creation must upload file bytes into application storage rather
   than require an externally supplied media URL. Generated download/playback
   URLs in responses are valid. References to already-uploaded owned assets are
   not automatically a defect; validate origin, existence and authorization.
   Do not convert unrelated hyperlinks (social/contact links) into uploads.
4. Library videos are included: storage-backed upload and playable reads, not
   replacement of example.com with another external link.
5. Fix broken community author photos and identity hiding. No `/auth/avatar`
   fallback that could show the caller's image for a different author.
6. Care records have THREE sources: Family, CareHome, Caregiver. Both medical
   and companion caregivers can ADD records. Earlier read-only caregiver advice
    is superseded. Elderly users read only their own combined timeline. Final
    mobile report confirms authorized families READ ALL THREE sources; this is
    settled scope. Families edit/delete family-source records only by member role.
7. Family administrators/editors create, update and delete family-origin records
   for authorized dependents. Actor, family/facility, source and elderly binding
   are server-derived. `visitDate` is date-only; audit timestamps are UTC.
8. Map every feature across mobile, Admin, Caregiver, Family, Elderly and Care
   Home dashboards. Absence of permission is an explicit mapping, not an omitted
   portal. Reuse existing authorization/medical-consent mechanisms.
9. Duplicate-charge refund POST already exists. Complete payment reads/invoice
   linkage, not a duplicate refund endpoint.
10. Verify Paymob card and wallet behavior separately. Wallet is method 2;
    card-enrollment availability does not establish wallet availability.
11. Complete paused complaint-cap work with the Owner-approved suggested-default
    approach (current draft EGP 500 = 50,000 piastres), the real PostgreSQL refund
    verification gaps, and an F3 residual decision brief. Do not silently accept
    the financial residual or expand refund policy beyond the approved cap.
12. Every change requires executable behavior tests plus consumer-facing contract
    synchronization. A successful route inventory or test double alone is not
    proof of functional HTTP, media playback or real Paymob success.

## Report evidence — regression inputs, not diagnosis

| ID | Report | Required outcome |
|---|---|---|
| R1 | Library content `01a155d9-59cf-77ef-bcde-fdf9c66c541f`, `videoUrl=https://example.com/videos/quick-healthy-meal` | Trace fixture/admin origin; find other placeholders; support actual uploads and playback. `canAccess=true` is entitlement only; empty sections are normal for video. Existing row needs a real supplied video, not invented content. |
| R2 | GET `/api/v1/community/authors/01a0a5c5-f58f-7c7d-bdd3-e5ae48406be2/photo` returns 404 despite avatarUrl | Trace persisted key, file existence, author mapping, access and anonymity. Available/authorized image returns bytes; unavailable/hidden image yields null reference at response creation. Still handle deletion races on the client. |
| R3 | Family-created records and elderly health-story timeline | Implement source-aware permissions, stable pagination and correct resident/dependent linking; include caregiver creation per subsequent Owner amendment. |
| R4 | POST `/api/v1/family/subscriptions/refunds/duplicate-charge` needs a succeeded paymentAttemptId | Own-payment history or documented invoice linkage; exact amount units, status, currency, paidOnUtc, optional invoiceId. Success is not proof of a duplicate charge. |
| R5 | POST `/api/v1/family/subscriptions/payment-intent`, 502 Paymob.GatewayError, upstream 400, trace `0HNPG5KPDG28A:00000001` | Trace request mapping and sanitized upstream evidence; document actual provider result, not only improved error wording. |
| R6 | Wallet method 2, planVersionId `01a155d8-7994-7154-b71e-55d3814c2f28`, trace `0HNPGJ3AQGFPS:00000001`, 2026-10-23 16:17:47 UTC; advertised available but upstream 400 | Check wallet integration/merchant configuration and payload; reconcile availability semantics; expose safe rejection reason; prove test-provider wallet intention success or retain explicit blocker. Hidden billing means the rejected field is not yet known. |

## Contract mapping to finish in P0

### Final mobile report reconciliation

This report originates from MOBILE, but delivery is CROSS-PORTAL. P3 must deliver
Care Home dashboard entry/read and Admin dashboard support/read endpoints with
their consumer contracts and tests. Reuse equivalent existing routes after source
inspection and provide teams a proposed-to-actual route map. No duplicate APIs.

| Final report requirement | Reconciliation / recorded amendment | Task |
|---|---|---|
| Family/elderly three-source reads | Family visibility across all three is now locked; family cannot edit other sources. Elderly remains own read-only. | P0/P3 |
| Caregiver recipients/list/create | Explicit routes, paged bilingual picker, dependentId on creation/read added below; medical and companion covered. | P0/P3 |
| Care Home and Admin dashboard mapping | Required capabilities and proposed route shapes below; not mobile-only delivery. | P0/P3 |
| Date-only, pagination, stable sorting, permissions/errors | Retained; query and validation recommendations below require P0 contract mapping. | P0/P3/P6 |
| Subscription payment selection | R4 already covers this; exact fields retained below; no duplicate refund POST. | P4 |
| Author photo null/200 | R2 explicitly covers POSTS, COMMENTS AND REPLIES. | P2/P6 |
| Wallet failure | R6 retains exact plan, method, trace and timestamp; safe diagnosis plus real-provider intention success, not just improved wording. | P4/P6 |

Earlier library-video, full media audit, Rentals removal and carry-over tasks stay
in scope even though this final mobile report does not repeat them.

Proposed routes (not claims of current implementation):

```text
GET/POST /api/v1/family/dependents/{dependentId}/care-records
PUT/DELETE /api/v1/family/dependents/{dependentId}/care-records/{recordId}
GET /api/v1/caregiver/care-records/recipients
GET/POST /api/v1/caregiver/care-records
GET /api/v1/elderly/care-records?page=1&pageSize=20&source=Family|CareHome|Caregiver
```

Create/update fields: `hospitalName`, `visitDate` (YYYY-MM-DD), `details`.
Caregiver creation additionally accepts `dependentId` as a selector, NOT authority:
revalidate assignment/permission at write time even if the picker returned it.
Recipient response: paged items of `dependentId`, `arabicFullName`,
`englishFullName`, with the same envelope below. No extra medical data in picker.
Timeline envelope: `items`, `page`, `pageSize`, `totalCount`, `totalPages`.
Item fields: `id`, `dependentId`, `hospitalName`, `visitDate`, `details`, `source`, `sourceName`,
`createdOnUtc`, nullable `updatedOnUtc`. Pin author/type representation and
sourceName localization using repository conventions. Sort visitDate descending
with a deterministic tie-breaker. Do not expose arbitrary elderly-id selection
on the elderly self route. Validate filters and pagination boundaries.

| Portal | Required mapping |
|---|---|
| Family | Authorized members read all three sources; administrator/editor CRUD on family-source records only. Other roles do not gain write authority. |
| Elderly | Own timeline read-only, all three sources; no create/edit/delete. |
| Medical + companion caregiver | Authorized-recipient picker, scoped list and creation; no implied blanket history access or arbitrary elderly access. |
| Care home | Staff-authorized resident entry and facility attribution; map exact dashboard actions and resident-to-identity relationship. |
| Admin | Permission-controlled support/audit mapping; no automatic unrestricted medical-data access or author impersonation. |
| Payment reads | Family owns its subscription transactions; finance-admin investigation follows existing permissions; no automatic caregiver/care-home/elderly access. |

### Bounded unresolved decisions (block dependent work only)

P0 must inspect existing contracts FIRST. If no existing rule settles these,
present one consolidated decision table to Owner, with recommendations:

- Family/elderly three-source visibility is settled. Resolve caregiver/care-home
  cross-source visibility and medical consent, their edit/delete authority,
  post-assignment/post-stay access and deletion audit policy. Recommend own-source
  edits under established roles, no cross-source edits and preserved audit; do
  not invent time windows. Pin Admin medical-read permission explicitly.
- Technical interpretation: all supplied family/caregiver examples include
  `hospitalName`; treat it as required for every source unless a documented
  existing product contract contradicts the report. Do not ask Owner to repeat
  this field requirement. P0 still recommends validating max length 200 and
  `visitDate` boundaries; report any existing-contract conflict rather than
  inventing one.
- Rentals deployed-data cleanup: inventory actual rows and foreign references;
  removal is approved, but irreversible table/data deletion requires exact-target
  approval and validated backup. Recommend feature removal first, bounded schema
  cleanup second. Never erase applied migration history blindly.
- Existing external media compatibility/cutover: inventory consumers and rows;
  propose an explicit transition rather than silently breaking old clients or
  downloading arbitrary external URLs. Real replacement video is content input.
- F3 residual: describe exact subscription-period over-refund path, affected
  scope, alternatives and financial consequence. Do not mislabel it as a settled
  booking-policy decision or accept it on the Owner's behalf.

Technical choices consistent with established patterns are Mastermind decisions,
not a reason to repeat intake. Explicitly record assumptions versus approvals.

## Work packages and ownership

### Dashboard capabilities and query recommendations

Dashboard CAPABILITIES are required scope; route shapes, query defaults and new
policy details below are RECOMMENDATIONS pending P0 mapping, not already approved
permissions or existing API claims. Prefer established repository conventions.

- Care Home: authorized resident picker, resident-scoped list/detail and
  care-home-source creation. Proposed shape under the existing owner/facility
  prefix: `residents/{residentId}/care-records` GET/POST and `.../{recordId}` GET.
  Map resident identity to dependent/elderly without trusting caller attribution.
  Entries must appear in family/elderly views. Update/delete require policy lock.
- Admin: permission-scoped paged list/detail, proposed GET
  `/api/v1/admin/care-records` and `/{recordId}`. Provide authorized dependent,
  author and facility selection where needed. No generic-admin unrestricted
  medical access, impersonation or automatic edit/delete authority.
- Map authorized family/caregiver detail reads if needed; reuse existing detail
  routes. P0 supplies exact methods, routes, consumers, roles and error codes.
- Lists: recommend page=1/pageSize=20, maximum 100 (reuse existing limit if
  different). Authorization precedes filtering, counts, sorting and pagination.
  Empty lists return the same paged envelope.
- Care-record filters: required source; recommended inclusive date-only
  visitDateFrom/visitDateTo; dependentId on authorized multi-recipient lists only.
  Admin may filter permitted facilityId/authorId. Elderly self route never accepts
  alternate identity. Reject reversed ranges/unknown source; filters cannot widen access.
- Search: recommend trimmed `search` (maximum 100 characters) over hospitalName
  and permitted source/author display names; recipient picker searches Arabic and
  English names. Specify case/Arabic behavior, parameterize queries, avoid default
  medical-details full-text search and logging sensitive search text.
- Sorting: recommend allowlisted sortBy=visitDate|createdOnUtc and
  sortDirection=asc|desc. Default visitDate DESC, createdOnUtc DESC, id DESC.
  Stable ties do not imply a frozen snapshot across concurrent insertions.
- Validation: recommend hospitalName <=200, details <=4000 characters, plain text,
  reject whitespace-only required fields. Hospital requiredness for companion
  observations and future dates remain bounded policy questions, not guesses.
- SourceName is display/localization only, never an authorization key. Preserve
  original author attribution; source wire values are Family/CareHome/Caregiver.
- Recommend optimistic concurrency using established version conventions, 409
  for stale edits, and retained audit trail. Lock deletion policy before coding.
- Error contract: pin exact stable codes and precedence in P0. Recommend 401
  anonymous, 403 wrong role, 404 foreign/missing scoped record, 400 invalid fields/
  filters, 409 stale writes; align with existing privacy and API conventions.

Payment reads retain `paymentAttemptId`, `status`, `amount`, `currency`,
`paidOnUtc`, nullable `invoiceId`. Explicitly define amount units: mobile's 199
example must not be confused with internal piastres. Recommend paged own-history
filters status/method/paid-date range and newest paidOnUtc plus stable ID ordering;
define null ordering for pending attempts. Finance support invoice/attempt lookup
is permission-scoped. Never return raw gateway/billing/secrets. Any refund-state
or eligibility hint must reflect actual server rules, not just successful payment.

R2 photo acceptance applies to post, comment AND reply author projections and
their privacy/cache behavior. Audit all builders and their shared download path.

Historical pre-decision task table below (retained as discovery provenance).
Current task state is: P0 complete; P1 Rentals removal locally accepted; W1–W4
feature lanes pending Owner's dispatch confirmation. The current four-lane table
above governs; historical entries below do not grant file ownership.

| Task | Owner / discovery boundary | Deliverable and acceptance dependency |
|---|---|---|
| P0 | Mastermind; API controllers/requests, module handlers, existing guides, tests, storage and payment wiring | Complete endpoint/media inventory, portal permissions matrix, proposed contract diffs, existing-data/migration transition, exact per-worker manifests. Resolve bounded decisions; do not repeat already locked scope. |
| P1 | Rentals worker; `src/Modules/Rentals`, rental controllers, Finance rental integration and their tests | Remove feature with reference search proof. Hand shared DI/solution/deployment changes to integration owner. Schema removal only after its gate. |
| P2 | Media worker; BuildingBlocks storage, CMS library, Community photo/read paths and all discovered media surfaces | Classify every input/output; implement approved storage corrections, upload video, fix R1/R2 and other verified defects. Validate uploads, retrieval, cleanup and compatibility; list unaffected verified surfaces. |
| P3 | Care-record worker; Families/Caregivers/CareHomes integration seams and API portals | Three-source creation/read model, family CRUD, elderly timeline, assigned dashboard operations, permissions, migrations, documentation and tests. Depends on P0 permissions/model agreement. |
| P4 | Payment worker; Families subscriptions/Paymob, Finance reads, platform availability | Payment-history/invoice read linkage, R5/R6 diagnosis and safe errors, wallet/card tests, provider evidence or concrete external blocker. No real charge/refund for a smoke. |
| P5 | Finance worker; paused cap files and refund verification pack | Repair/finish cap draft; test boundary and configuration failure semantics; real PostgreSQL gaps; F3 decision brief. Preserve existing work and distinguish suggestion from deployed policy. |
| P6 | Mastermind + assigned test owner | Shared reusable HTTP/PostgreSQL/file fixtures, full integration gate, consumer verification matrix, final receipt in this brief. Every implementing worker owns focused tests/docs/Postman with their feature. |

### Historical initial parallel-execution notes (superseded by current lane table)

- Owner approved parallel work where safe, NOT immediate implementation. On a
  future start order, independent read-only inventory lanes can run concurrently.
- Implementation overlap requires disjoint exact manifests. P1/P4/P5 all touch
  Finance; P2/P3 share media/Identity risks. Sequence overlapping tasks rather
  than allowing concurrent writes to the same files or DbContext snapshots.
- Mastermind exclusively owns shared `DependencyInjection.cs`, solution/project
  integration, `ResultProblemDetailsMapper.cs`, API metadata hosts, deployment
  scripts, shared Postman collections and this ledger, unless one explicit
  exclusive handoff transfers a file. Workers supply bounded amendments.
- One migration author per DbContext; one build/test/API/database runtime slot.
  Author tests in parallel, execute using that slot. Pin the model/provider under
  standing governance; do not silently substitute a model.
- Default one whole-phase worker; split only approved disjoint packages, recording
  session, revision, files and dependencies here. No duplicate planner reports.
- Worker hands back ready_for_review, with commands/exits, changed files, test
  evidence, unresolved limits and next action. Mastermind independently checks
  the diff and evidence, accepts or sends bounded corrections to the same worker.

## Preserved paused work and evidence corrections

At planning intake `git status` showed modified Finance complaint commands,
Finance DI, API error mapper, Finance Postman and gateway-double script, plus
new `ComplaintAwardOptions.cs` and `ComplaintAwardCapTests.cs`. Preserve all.

- Initial known build failure (`IOptions.CurrentValue`) and malformed-config
  fallback were corrected by Mastermind. Invalid/negative configured values now
  fail fast; explicit zero config disables the cap. Focused cap tests and complete
  solution build passed locally; nothing is deployed.
- Prior session removed the user-scoped development Paymob key; existing process
  trees retained it. Never claim every new child process is clean. Explicitly
  scrub verification processes and assert the payment double before tests.
- Prior session moved host Development settings to its release archive and
  reported 200 controls. No fresh verification in this planning task.
- Equal credential lengths do NOT prove equal keys; presence does NOT establish
  valid merchant integration. A historical clientSecret example is not assumed
  to be an account API key; classify credential type/lifetime before rotation advice.
- Earlier upload audit was preliminary, not exhaustive. Recheck community
  public/private classification, all path/key inputs and video-capable surfaces.
- Empty-database timeout selection did NOT prove seeded timeout reconciliation
  or no-double-refund behavior. Include checklist 4.2 and 4.5, not just the five
  previously advertised relational gaps. Scenario fixtures may contain stale W3
  placeholders and inaccurate rounding/calendar examples; verify expected
  results against approved policy, mathematics and timezone data before reuse.

## P0 handback reconciliation — A/B/C

All three workers used the assigned DeepSeek V4.1 Flash Free/high profile and
performed static read-only inspection at pinned HEAD `9433c1d`; no files,
tests, runtime, DB, host, storage or provider state changed. Full worker reports
remain available in sessions A `ses_e96504bebffebK9kuI7rR0fEPY`, B
`ses_e965016ddffeG6P9I7l1XR5awK`, C `ses_e964fde5fffe627eYjG8HALRvy`.

### A — media/storage and Rentals

- Actual implementation is local disk `IFileStorage`, not cloud. Public default
  max 2 MiB; private default 5 MiB with override; private files sit outside
  `/files`. Public response keys are often raw storage keys, so map app resolution
  before standardizing response URLs.
- Confirmed source defects/risk candidates: video has external URL-only path and
  no stored writer/reader; advertised 5 MiB cover/wellness cap conflicts with
  public store's 2 MiB limit; unchecked MIME/signature and missing medical-report
  request limit; landing JSON accepts unvalidated `imagePath`; community post can
  attach arbitrary/external image URL including another user's private community
  asset; chat MP4/three private reads lack byte-range processing. Source findings
  only, not runtime reproductions. P2 must prioritize and verify every path.
- R2 mismatch explained statically: projections emit avatar under looser rules
  than author-photo read (which also requires public post history, no anonymous
  history, private key, existing file). Published docs contradict the conservative
  hide rule. Need one privacy contract and consistent post/comment/reply URL
  projection; no caller-avatar fallback.
- Rentals has a per-file deletion/edit inventory in lane A's report. Preserve
  shared Finance ledger/credit/subscription code and historical artifacts. Risks:
  upgraded DB retains applied schema while fresh DB omits it; deleting migration
  files is not cleanup. Pending `rnt_` webhook events need defined safe handling.
  No DB data checked; no destructive cleanup authority.

### B — care-record routes and portal contracts

- No care-record route/aggregate exists. Reuse family dependent route convention;
  add caregiver recipient picker/list/create; elderly self timeline; care-home
  owner resident picker/list/create/detail under `residents/mine`; Admin
  operational list/detail. Add family and authorized detail reads. Family reads
  all three sources as Owner-settled; caregiver medical/companion both create;
  family writes own-source only; elderly self is read-only.
- Existing `ElderlyNote`, immutable `VisitReport`/`MedicalReport`, and
  `MedicalAccessGrant` are different aggregates; don't overload them. Caregiver
  target authorization derives from booking assignments (no assigned-dependent
  table); care-home owner is the only existing facility staff model. Medical
  grants are medical-only and cannot be reused to authorize companion writes.
- Recommended item adds `dependentId`, author identity/display attribution and
  **string** source (`Family|CareHome|Caregiver`); raw enums serialize numerically.
  Stable query order, authorized filtering/counts, recipient name search and
  `DateOnly` are specified by the worker. Detailed exact routes, P3 manifest and
  test matrix are in session B.
- B recommends explicit technical defaults for caregiver booking scope,
  own-source visibility, CareHome accepted-stay scope, source-specific mutation,
  Admin SuperAdmin+SupportAdmin read with payload-free audit, 403 foreign
  dependent (existing precedent), string source, and validation/search. These
  are recommendations pending Mastermind review, NOT yet locked in the brief.
  `hospitalName` requiredness is the bounded Owner question (recommend required
  for caregiver/CareHome, optional for family observation); do not ask again on
  already approved family/all-source/caregiver-add requirements. Lane B's D8
  question on hospitalName is resolved from the supplied create payloads: the
  field is required for all three sources unless an existing approved contract
  contradicts that. No Owner question needed for it.
- Dashboard source inventory is limited: worker found no Flutter/Admin UI source
  tree (only design PNGs in `ui/`); API contract mapping is possible, actual screen
  integration cannot be claimed until consumer repos/builds are available.

### C — payments, Paymob, cap and verification

- Invoice routes exist but omit stored `PaymentAttemptId`; no payment-attempt read
  route exists. Duplicate-charge handler checks ownership and `Succeeded`, but
  does not itself verify duplicate evidence. Plan-change captures may have no
  invoice. Existing invoice amount is EGP-major decimal, refund amount piastres;
  API must name/document units. Failed intention occurs before attempt persistence,
  so history cannot show that failure without lifecycle work.
- Wallet uses its own `WalletIntegrationId`; payment availability is control flag
  plus config presence, not provider capability. Wallet path omits
  `subscription_plan_id`; no evidence says that field caused the 400. Non-numeric
  integration config can map to generic error before a provider request. Existing
  safe upstream-reason logic exists; missing items are original sanitized
  responses and successful wallet test-environment evidence. R5/R6 are BLOCKED,
  not fixed or diagnosed. No production/provider call was made.
- Paused cap draft has multiple defects, not just the first build error:
  `.CurrentValue` on `IOptions` (use `.Value`); controller's local error mapping
  defaults new cap code to 400 while mapper/Postman say 409; malformed value
  silently selects default and parsing is culture-dependent. Mapper is
  Mastermind-owned. Preserve draft; do not call it working/deployed.
- F3 residual source-path finding: plan-change credit may remain refundable
  against source capture while also present in plan-change refund budget, enabling
  a duplicated prorated credit refund per plan change. Needs explicit financial
  Owner decision brief; not accepted by workers.
- PostgreSQL checklist 4.1–4.7 are all NOT RUN; prior empty timeout query is not
  seeded reconciliation proof. Scenario IDs are fixtures/checklist only, no test
  implementation/seeder. Reuse existing validated disposable-Postgres harness
  after checking its safety contract. P4/P5 share Finance/DI/mapper/Postman files
  and must be sequenced, not concurrent.

## P0 handback reconciliation — A/B/C

All three workers used the assigned DeepSeek V4.1 Flash Free/high profile and
performed static read-only inspection at pinned HEAD `9433c1d`; no files,
tests, runtime, DB, host, storage or provider state changed. Full handbacks remain
in sessions A `ses_e96504bebffebK9kuI7rR0fEPY`, B
`ses_e965016ddffeG6P9I7l1XR5awK`, C `ses_e964fde5fffe627eYjG8HALRvy`.

### A — media/storage and Rentals

- One local-disk `IFileStorage`; public default cap 2 MiB, private default 5 MiB
  with override; private root cannot be reached by `/files`. Public responses
  often return storage keys, so consumer URL resolution must be mapped first.
- Source risks: 5 MiB library/wellness controller caps conflict with 2 MiB store;
  several uploaders trust declared MIME; landing JSON accepts unvalidated
  `imagePath`; community posts can attach arbitrary/external image URLs including
  another user's private asset; medical report lacks request-size cap/signature
  check; chat MP4 and three private file reads lack byte-range processing. These
  are static findings, NOT runtime reproductions.
- R1: library video is external URL-only; no stored upload/read path exists.
  Premium entitlement gates the URL value, not external bytes. Existing example
  row requires real content supplied by Owner/Content; no URL fetch/invention.
- R2: response projection has looser author-photo rules than the download route
  (published post, no anonymous history, private key, existing file). A URL can
  therefore point to a 404. Existing community docs contradict the conservative
  anonymity rule. Align post/comment/reply projections with one privacy predicate;
  never use caller-avatar fallback.
- Rentals exact delete/edit inventory is in lane A's handback. Preserve shared
  Finance ledger/credit/subscription code and historical receipts. Applied schema
  will remain in upgraded DB if code/migrations are removed; fresh DB differs.
  Check pending `rnt_` webhook events before cutover. No DB inspection/cleanup or
  destructive authority in P0.

### B — care-record routes and portal contracts

- No care-record route or aggregate exists. Family dependent CRUD, caregiver
  recipient/list/create, elderly self timeline, CareHome owner resident picker/
  list/create/detail, and Admin operational list/detail are new. Existing
  `ElderlyNote`, immutable `VisitReport`/`MedicalReport` and `MedicalAccessGrant`
  are different concepts and must not be overloaded.
- Caregiver target authorization must derive from assigned bookings and be
  rechecked on create; no caregiver-dependent table exists. Existing care-home
  authorization has facility-owner identity but no staff model. Medical grants
  are medical-only and cannot authorize companion caregiver record writes.
- Recommended item: `dependentId`, author identity/display attribution and string
  `source=Family|CareHome|Caregiver` (API currently serializes enums numerically).
  Stable paging/filter/sort and dedicated error mappings needed. Full route map,
  exact P3 manifest and portal test matrix are in session B.
- Technical recommendations (not yet locked): caregiver access booking scope;
  source-specific edit/delete; facility owner scope; Admin SuperAdmin/SupportAdmin
  read with payload-free audit; 403 foreign-dependent status consistent with
  repository precedent; concurrency/audit rules. Do not re-ask settled family
  all-source read, elderly read-only, both caregiver types create, or family
  own-source CRUD. `hospitalName` required for all sources from supplied payloads.
- No Flutter/Admin UI source was found (only UI design PNGs); actual screen
  integration cannot be claimed until consumer source/build is available.

Recommended route map (new unless source verification finds an equivalent):

```text
GET/POST /api/v1/family/dependents/{dependentId}/care-records
GET/PUT/DELETE /api/v1/family/dependents/{dependentId}/care-records/{recordId}
GET /api/v1/caregiver/care-records/recipients
GET/POST /api/v1/caregiver/care-records
GET /api/v1/elderly/care-records
GET /api/v1/care-homes/residents/mine
GET/POST /api/v1/care-homes/residents/mine/{residentId}/care-records
GET /api/v1/care-homes/residents/mine/{residentId}/care-records/{recordId}
GET /api/v1/admin/care-records
GET /api/v1/admin/care-records/{recordId}
```

Recommended common envelope `{items,page,pageSize,totalCount,totalPages}`; pageSize
20 default/max 100; source filter; inclusive date-only range; search hospital and
permitted author name/recipient Arabic-English names; deterministic
`visitDate DESC, createdOnUtc DESC, id DESC`. Do not search medical details by
default. Distinguish foreign dependent (403, existing precedent) from unknown
record under an authorized dependent (404). Item contains `dependentId` and
author attribution; wire source is a string, not the API's numeric enum.

### C — payment reads, Paymob and Finance verification

- Family invoices exist but omit stored `PaymentAttemptId`; no payment-attempt
  read route exists. Duplicate-charge POST checks family ownership and success,
  not duplicate evidence. Plan-change attempts may have no invoice. Invoice
  amount is decimal EGP major; refund amount is integer piastres. Failed intention
  happens before attempt persistence, so failed attempts may not appear in history.
- Wallet uses its own `WalletIntegrationId`; advertised availability is control
  flag + configuration presence, not a provider capability probe. Non-numeric
  integration config can produce a generic error before HTTP. Existing sanitized
  provider-reason code exists; trace bodies are missing. R5/R6 remain externally
  BLOCKED; no source-only diagnosis or DevelopmentPaymobDouble success can prove
  actual wallet success.
- Paused cap draft defects: `IOptions<T>.CurrentValue` compile error (use `.Value`),
  controller-local unknown-code fallback gives 400 while mapper/Postman say 409,
  malformed config silently chooses default, culture-dependent parsing. Mapper is
  Mastermind-owned. Draft remains unverified.
- F3 residual: plan-change credit may remain refundable against its source capture
  while also present in the plan-change attempt budget, allowing a duplicated
  prorated credit refund per plan change. Requires explicit financial decision.
- PostgreSQL checklist 4.1–4.7 all NOT RUN; empty sweep had no candidates and no
  seeded reconciliation proof. Scenario IDs have no implementing tests/seeder.
  Reuse the validated disposable PostgreSQL harness; sequence P4/P5 because they
  overlap Finance DI/mapper/Postman. No worker changed files.

## Mastermind consolidated delivery sequence (after decisions)

P0 discovery is complete for planning. Implementation tasks remain NOT STARTED;
this sequence is not release authorization. Do not repeat discovery or reopen
settled scope.

1. Resolve the bounded decisions below; inventory data only through explicitly
   authorized, read-only targets. Obtain real R1 content and safe Paymob evidence.
2. **Rentals removal**: worker removes exact Rentals-only code/routes/tests/docs;
   Mastermind serially edits shared DI, solution/project refs, startup/fixture
   contexts, deployment probe, mapper, webhook, Postman and scripts. Require a
   safe `rnt_` event disposition before release. Applied database cleanup is a
   separate migration/backup/approval gate; never delete migration history blindly.
3. **Care records**: one Families migration author implements aggregate plus
   family/caregiver/elderly/CareHome/Admin routes. Mastermind owns shared auth
   policies, DI, mapper, API metadata and five Postman collections; worker supplies
   exact amendments. No other Families snapshot/migration author in parallel.
4. **Media**: storage-backed library MP4 and gated range playback; community
   post/comment/reply author projection aligned to photo route and anonymity;
   fix validated-reference, size/signature and range gaps found during P0. Migrations
   serialized by DbContext. Mastermind owns shared integrations.
5. **Payment/Paymob**: payment-history query/link using existing attempt/invoice
   schema if sufficient; unit-named EGP/piastre contract; validate integration IDs
   safely; actual wallet test intention evidence or explicit blocker. No real charge.
6. **Cap + relational verification**: resume preserved draft after fixing compile,
   HTTP mapping and invariant fail-fast config; seed true PostgreSQL races and all
   4.1–4.7 scenarios; correct stale fixture expectations before using them.
7. Mastermind integration and one coordinated final build/runtime gate, endpoint
   parity, upgrade/fresh-schema verification, portal smoke matrix and receipt.
   Independent test authoring can parallelize; shared files, migrations, runtime,
   database and builds remain serialized. Every release requires separate order.

### Owner-locked decisions — do not re-ask

Owner answered these on 2026-10-23. They bind future implementation workers.
W1–W4 remain pending the Owner's separate dispatch confirmation.

| ID | LOCKED decision |
|---|---|
| D1 | Medical and companion caregivers can create/read own-source records only for dependents linked by their own booking. Access while Confirmed/InProgress and for a **30-day grace period after completion**. Revalidate on every write; no unrelated or cross-source history. |
| D2 | Caregiver and CareHome authors have **full CRUD on their own source records**, including edit/delete, with version/concurrency checks and audit. No cross-source edits/deletes. Family retains own-source CRUD. Preserve deletion attribution; no silent overwrite. |
| D3 | CareHome authority: **facility owner only**, using existing ownership; no invented staff role. Scope to its facility's accepted resident bookings. |
| D4 | Admin access: **SuperAdmin + SupportAdmin list/detail only**, with payload-free read audit; no create/edit/delete or author impersonation. |
| D5 | Remove Rentals runtime feature but **retain deployed schema/data initially**. No table/data drop in this implementation. Later destructive cleanup needs exact-target inventory, validated backup and separate authorization. Safely handle legacy `rnt_` events through cutover. |
| D6 | Disable externally linked library-video playback until approved bytes are uploaded. Never fetch arbitrary URLs. Retain metadata/migration state as needed; no playback URL until uploaded. R1 still requires genuine supplied content. |
| D7 | Preserve conservative community anonymity: any anonymous-post history suppresses that author's photo. Make post/comment/reply projections consistently return null wherever the photo route denies; never use caller-avatar fallback. |
| D8 | External evidence dependency, not policy: use sanitized original Paymob responses for both traces if retained; otherwise perform a non-charging wallet intention in the approved test environment and record sanitized result. Never request credentials or billing PII. |
| D9 | **Fix the F3 double-refund residual** so plan-change credit can refund only once across source and plan-change budgets, preserving auditable accounting. Do not accept the residual. |
| D10 | Complaint-cap default was already approved: **EGP 500 / 50,000 piastres enabled**. Finish with above-cap 409, invalid config fails startup, zero award valid; configuration override and tests required. |

Remaining operational inputs are not scope questions: genuine R1 video asset;
sanitized Paymob evidence/test-environment access; and, before any future Rentals
schema cleanup, authorized exact-target inventory and backup. Independent code
work can proceed after dispatch signal; affected end-to-end checks remain BLOCKED
until their external inputs exist.

HospitalName is required based on all supplied create payloads unless an approved
existing contract conflicts. Family reads all sources; elderly self is read-only;
caregivers add; family modifies own-source only; mobile scope includes CareHome
and Admin dashboards. Those are locked requirements.

## Report completeness check

- [x] Three-source family/elderly timeline, caregiver picker/create and date-only
  records; source/author server-attributed; sorting/paging/filter/search mapped.
- [x] Cross-portal CareHome/Admin capabilities and permissions explicitly mapped.
- [x] Existing duplicate-refund POST retained; payment-attempt read gap and money
  units recorded.
- [x] Community photo behavior applies to posts/comments/replies; anonymity intact.
- [x] Exact wallet plan/method/trace/time and safe diagnostics/real intention proof.
- [x] Rentals removal plus schema/webhook risks; full media inventory; testing plan.
- [x] D1–D10 decisions recorded. External evidence/content dependencies remain.
  P1 and the four implementation lanes are locally accepted after the final gate;
  the earlier awaiting-confirmation note was a planning checkpoint and is superseded.

## Completion and stop conditions

Use the testing contract. No author-only check counts as executed. Preserve
historical receipts; do not rewrite past results into a claimed new gate.
Accepted implementation is not deployed. When all required local evidence is
reviewed, report acceptance and request release direction. Report external
provider/app evidence as blocked/not run if unavailable, never completed.

Lane handbacks: W1 Care Records (`ses_e95eb90f3ffeE78LtqLMsO2YGX`, including its
restrictive-FK correction and actual HTTP-host route/auth tests) has handed back
`ready_for_review`; W2 Community (`ses_e95eb46bfffemLcSUpGpo3Uz1b`, including its
bounded reaction-guard and HTTP-host corrections) has handed back
`ready_for_review`. W1 contract/file details are in
`contracts/care-records-w1-2026-10-23.md`; W2 in
`contracts/community-post-management-2026-10-23.md`. Their authored tests were
included in the final full suite; the focused care-record host suite also passed.
Mastermind integrated W1 error mappings and W1/W2 Postman routes.

W3 Storage/library (`ses_e95eade3cffeFL6t3vs5kDo0ek`) has handed back
`ready_for_review` after the bounded signature and real-host test corrections.
Session/base:
`ses_e95eade3cffeFL6t3vs5kDo0ek` at HEAD
`9433c1d` (branch `main`, dirty worktree preserved). Storage-backed library
video attach/remove/playback, the CMS `video_storage_key` migration pair +
snapshot, bounded upload-signature/range/landing-path hardening, the
Owner-approved amendment of the two superseded library assertions, four new
test files, the library guide and its contract
(`contracts/media-upload-library-2026-10-23.md`) are complete. Storage-interface
amendments: **none needed** (private `SavePrivateAsync`/`OpenReadAsync` already
suffice; proposed before editing, nothing changed). Migration name
`20261023114500_AddLibraryContentVideoStorageKey` (files only, nothing applied).
Changed files: `src/Modules/Cms/Domain/.../Library/LibraryContent.cs`,
`src/Modules/Cms/Application/.../Library/LibraryContent{Commands,Queries,Responses,FieldDetail}.cs`,
`src/Modules/Cms/Infrastructure/.../Configurations/LibraryContentConfiguration.cs`,
`.../Migrations/CmsDbContextModelSnapshot.cs`,
`.../Migrations/20261023114500_AddLibraryContentVideoStorageKey{,.Designer}.cs`,
`src/API/Sanad.API/Controllers/{AdminLibraryController,LibraryController,AdminLandingController,AdminSplashScreensController,AdminAssessmentsController,AdminLookupsController,CareHomeProfileMediaController,CareHomeRoomMediaController,FamilyReportsController,CaregiverBookingsController}.cs`,
`src/API/Sanad.API/Controllers/Requests/LibraryContentValidation.cs`,
`tests/Sanad.UnitTests/Cms/{LibraryVideoStorageTests.cs,LibraryTests.cs}`,
`tests/Sanad.UnitTests/API/{AdminLibraryVideoControllerTests,LibraryVideoReadControllerTests,MediaUploadHardeningTests}.cs`,
`docs/app/library/README.md`. Historical handback checkpoint: the four new W3
test files and two amended assertions were then **NOT RUN**; that status was
superseded by the coordinated final suite below.
Mastermind integrated the three Postman routes (`POST`/`DELETE` admin
`…/content/{id}/video`, app `GET …/content/{id}/video`) and corrected stale
external-playback wording; final JSON parsing and route parity passed. Accepted
bounded signature correction (covered by the final suite): byte-signature checks
for AuthController, CaregiverController and `FamilyController.SavePrivatePhotoAsync`,
plus no-write regressions for mismatched declarations. `CareHomeProfileMediaController.Upload`
remains unchanged because its command handler already verifies JPEG/PNG signatures;
the raw declared type affects stored metadata only. Publish-time media guard for
library video remains a follow-up finding: publishing without uploaded bytes does
not advertise playback, so it does not bypass the locked playback rule. Real R1
replacement video content remains runtime/external and NOT RUN. Current W3
correction adds actual test-host multipart binding/auth and HTTP 206 byte-range
coverage. These host cases passed in the coordinated suite.
`docs/admin/library.md` does not exist, so the guide update landed only in
`docs/app/library/README.md`. W3 host tests were included in the passing full suite.
The earlier note that real-disk storage and PostgreSQL migration verification
were unproven is **superseded by the final gate**: the slice-schema nonce-DB
proof applies all three new migrations (3/3), and the storage/media suite proves
MP4 durability across a new storage instance and a simulated host restart, plus
real-file 206 and 416 responses (34/34). Those checks still use a synthetic
12-byte `ftyp` asset, so genuine media content and consumer playback remain
unverified; do not treat them as those proofs.

W4 Finance/payments (`ses_e95ea8e6effeWTNtoeqagEmM6b`) has handed back
`ready_for_review`; its endpoints, units, F3 invariant/under-refund consequence,
Paymob wallet diagnostics, cap semantics and the disposable-PostgreSQL safety
contract are in `contracts/payments-finance-2026-10-23.md`. W4 tests were included
in the full suite; both F3 PostgreSQL budget cases passed on the isolated nonce
database. R5/R6 remain BLOCKED without sanitized provider evidence or an approved
test-wallet intention. Real provider and consumer checks were not run.

## Final local acceptance / stop point

The implementation is locally accepted for this slice based on the Release build
(0 warnings/errors), the final full suite (5,324 passed, 26 skipped, 0 failed),
focused care-record HTTP tests,
nonce-database PostgreSQL proofs for W4-F3 and the three new schema changes,
Families F1 and Chat cleanup relational suites, the Identity nonce-base suite,
real-disk storage durability/range/restart checks, final route/Postman parity,
collection JSON parsing, and a clean diff check.

This is not end-to-end closure or release authorization. The authorized real
Paymob card/wallet test could not run: no test secret key is configured, and the
ambient public key is not a Paymob `pk_` key, so the API is wired to the
development double. Real provider verification, the genuine replacement video
asset, consumer-screen playback/payment/care-record checks, and the fixed-name
FUR-02/03/04 relational targets remain explicitly open and are recorded as
BLOCKED/NOT RUN, never as passes. Release/deployment/migration against any
persistent target, production/provider/storage operations, commit and push all
remain unauthorized; stop here pending separate Owner direction.
