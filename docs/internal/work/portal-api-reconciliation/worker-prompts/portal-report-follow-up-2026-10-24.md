# Portal report follow-up — active plan

## Control record

- Owner approved locking this plan and preparing parallel worker tasks on 2026-10-24.
- Phase: in_progress. Wave A is locally accepted; Wave B has not started. Nothing is
  committed, released or deployed.
- Baseline: `eb218a002b598e66d3e62013d2c1d198d66c3768`; released runtime
  `e29bc2c91e38c2ff76f2c1143ed943e416d9b13f`. Tracked workspace clean at kickoff.
  Existing untracked build/test logs, audit directories, historical handoff and
  smoke script are preserved and excluded. Release state: not_requested.
- Start dependency: the preceding media/care-records/payments/Rentals slice was
  released and its receipt verified before this phase was activated.
- This plan does not authorize release of either slice, VPS fixture writes,
  production test clocks, deletion of existing data, or real refunds.
- At activation pin the actual released SHA and residual local delta. Never use
  today's dirty workspace as an assumed clean release baseline.
- Worker models: all Wave A lanes use `river/zai-org/GLM-5.3-Flash`.
  Owner approved W3 fallback from `opencode/big-pickle` after its free quota error.
  Retain configured reasoning; no unapproved provider/model changes.
- Mastermind owns this status record, integration, final review and release coordination.

## Inputs and settled decisions

Source: Owner-supplied backend report dated 2026-10-23, items 1–11. Its live
observations are historical, not current-source proof. Compare deployed revision,
current source, and supplied report before labeling any finding fixed or reproduced.
The host called “dev” in the report is classified as Production in repository
decisions. Fixtures default to a verified local target; a remote fixture request
requires exact-target authority and must not enable a production test clock.

1. Duplicate-charge claims require **Support approval**. Submission must not
   approve a ledger award or execute a payout. Preserve the full-refund policy
   for a verified duplicate/erroneous charge, server-calculated and bounded by
   the remaining capture budget. No partial automatic approval.
2. Review state and refund execution state are separate. Approval is not proof
   that money moved. Family sees its own claim/result; authorized Support sees
   list/detail and audited approve/reject actions.
3. Replays, concurrent approvals/execution, failed responses and unconfirmed
   provider outcomes cannot cause a second payout. Reconcile uncertainty before
   retry. Existing recorded awards are not newly executable claims.
4. Preserve existing cancel-renewal and service-shutdown policies. Changes to
   those policies are excluded.
5. Confirmed caregiver bookings must be discoverable by Admin with family and
   caregiver names, booking date and refund state. Preserve the current closed
   list default and existing finance filters.
6. Prefer clarifications over duplicate features: numeric visit days and current
   auth request shapes already exist; public legal publication is a content task;
   partner optional fields and notification producers require version-aware checks.
7. Keep medical audit ID-only, gateway secrets server-configured, FAQ deactivation,
   and registration-only growth in this scope. Additional audit PII, gateway secret
   management and permanent FAQ deletion are deferred, not silently approved.
8. App-rating Admin reads and a controlled wellness-category taxonomy are optional
   follow-ups pending explicit product/role decisions. Document current wellness
   free-text categories and existing image upload behavior now.

## Dispatch topology and ownership

### Current dispatch checkpoint

- W1 ready_for_review — `ses_e923b6fb0ffe6rPdSumtLqr8NZ`,
  River GLM 5.3 Flash; initial handback's focused tests exposed CLR InMemory
  ordering failure on `BookingId` (not `IComparable`). Corrected tie-breaker to
  `BookingId.Value` (`Guid`), preserving descending ID order and SQL translation.
  Parent re-ran the focused suite after correction: pass, included in combined 201/201.
- W2 ready_for_review — `ses_e923b3779ffekQqr04aKr3YC8V`,
  River GLM 5.3 Flash; authoring complete in the exact owned manifest: Finance
  (`DuplicateChargeClaim` aggregate + enums; `ISubscriptionRefundGateway` seam;
  `DuplicateChargeClaimCommands.cs` errors/role-guard/views/store-reader
  interfaces/handlers incl. NotMoved-evidence and provider-reference guards;
  `DuplicateChargeClaimStore` with transactional approve, fenced
  E1-before-provider execution, E3 outcome mapping, reconciliation matrix;
  `DuplicateChargeClaimConfiguration`; `FinanceDbContext` DbSet + configuration;
  Finance DI registrations), Families (`DuplicateChargeClaimSubmissionCommands.cs`
  full-basis submission + family claim reads; legacy handler fail-closed
  `Subscriptions.Refund.LegacyRetired` guard, command kept;
  `SubscriptionRefundGateway` over `IPaymobClient.RefundPaymentAsync` + Families
  DI), API (`AdminFinanceDuplicateChargeClaimsController` list/detail/approve/
  reject + SuperAdminOnly execute/reconcile; `FamilySubscriptionsController`
  legacy POST rebound to `claimSubmission: true` opt-in with 400
  `Subscriptions.Refund.MigratedToClaim`, family list/detail reads). BOUNDED
  CORRECTIONS APPLIED (review round 1): (1) restored R1 admission rule — active
  (non-rejected) partial unique index on (family, payment_attempt) filtered
  `state <> 2`; rejected claim stays historical/readable and no longer keeps
  its slot; resubmission with a distinct idempotency key + new evidence opens a
  fresh live claim while the case + capture budget keep the one-award guarantee;
  store checks narrowed to live claims; new store tests: rejected-claim
  resubmission (replay still 200-history, new key -> new pending claim, late
  re-decision refused, full basis approved once) + raced second live claim hits
  the partial index (DbUpdateException) via shared Options context; (2)
  gateway E2 calls wrapped — an unexpected thrown gateway exception after
  committed E1 records Ambiguous with `Paymob.GatewayError` (never a raw 500
  with a misleading Executing row), caller cancellation rethrows leaving the
  committed intent honest; new tests: throwing-gateway execution asserts
  Ambiguous + unresolved-timeout gate blocks retry until reconciliation, and
  cancellation rethrow leaves Executing/reconcilable; (3) new endpoint contract
  test file `AdminDuplicateChargeClaimEndpointTests.cs` (authored only): policy
  pins + runtime matrix (anonymous 401 / Family+FinanceAdmin 403 / SupportAdmin
  OK-review+403-money / SuperAdmin OK-all) over the production pipeline with an
  ISender double, nested SuperAdminOnly reflection pin, seam forwards the real
  account type (never rotated), legacy POST fail-closed 400 MigratedToClaim with
  zero dispatch, opt-in 201 + Location + replay 200, family owner reads,
  probe-safe 404 (NotOwner 403), family DTO property set + JSON + OpenAPI
  schema internal-field bans. Docs: family + admin subscription guides updated
  for rejected-resubmission semantics. CHECKS (parent-authorized verification
  round): Release build of Sanad.UnitTests with TreatWarningsAsErrors = PASS
  (0 warnings, 0 errors) over the combined tree including Sanad.API. Fixes en
  route (W2 test files only): duplicate nested `FakeGateway.Behavior` collapsed
  by renaming the enum type to `GatewayBehavior` (both throw cases and all
  seven behaviors preserved; CS0102 (765,26) + CS0229 (754,33) resolved), and
  `AdminDuplicateChargeClaimEndpointTests.cs` gained usings
  `Sanad.API.Controllers` + `Microsoft.AspNetCore.Mvc.Routing` (CS0246/CS0103
  on the controller type and HttpMethodAttribute). The follow-up build surfaced
  no further errors. Parent review correction (test-file only, this session):
  in `AdminDuplicateChargeClaimEndpointTests.cs` the two family-DTO property-set
  tests' expected literals were re-sorted to exact ordinal order to match the
  `OrderBy(StringComparer.Ordinal)` actuals (declared-order literals could never
   compare equal); verified by direct ordinal sort equivalence in isolation. R2
   server-authority correction
   COMPLETE (this session): `ApproveDuplicateChargeClaimRequest`/`RejectDuplicateChargeClaimRequest`
   lost `DecidedOnUtc` and `ReconcileDuplicateChargeClaimRequest` lost `ReconciledOnUtc`;
   the controller stamps all three commands from `IDateTimeProvider.UtcNow` (execute
   already did); family submit keeps its optional customer-reported `recordedOnUtc`;
   admin+family docs updated; endpoint test payloads cleaned, plus a new DTO-reflection
   fact (no timestamp members on the four request DTOs) and seam timing captures
   (`LastDecisionUtc`/`LastExecuteUtc`/`LastReconcileUtc`, UTC kind + bounded window);
   store/domain commands unchanged, no settle/claim-state behavior touched; not built/
   not run per parent instruction. Correction round 1 (parent's 5 store-test failures,
   this session): (1) meaningful-error production fix in
   `DuplicateChargeClaimStore.SubmitAsync` fast-fail — insufficient remaining capture
   for the FULL basis now returns `Finance.DuplicateClaim.CaptureBasisUnavailable`
   (was `CaptureAlreadyClaimed`); the Families mapper already translated that code to
   `Subscriptions.Refund.ExceedsCaptured` and the family guide's error contract pinned
   it, so contracts now agree (live-claim/already-awarded paths keep CaptureAlreadyClaimed);
   family doc line for the submission refusal corrected to `ExceedsCaptured`.
   (2)-(5) store tests fixed: budget seeding keeps the aggregate reference instead of
   querying Added-but-unsaved rows ("Sequence contains no elements" root cause);
   `Approval_is_refused_...` reshaped deterministically (submits with exactly-remaining
   capacity, budget then drains, approval's authoritative in-transaction guard refuses
   CaptureBasisUnavailable, rollback leaves claim Submitted + budget 6000 + no ledger);
   `Approval_reserves_...` ledger load gained the missing `.Include(l => l.Entries)`
   (LatestEntry was null only because of that; production/harness wiring verified
   correct — store + recorder share one scoped FinanceDbContext, one commit);
   `An_in_flight_attempt_can_never_be_reconciled_as_not_moved` now genuinely in-flight
   via the seeded-cancellation seam (E1 committed Executing, attempt id) instead of a
   stale AwaitingExecution setup, asserts IntentInFlight + attempt stays Executing;
   `Execution_fails_closed_when_no_provider_transaction_is_resolvable` now asserts the
   approval's recorded award (single Approved entry, TotalCompleted 0) with no execution
   movement, instead of a false zero-entry count. Settle/claim states, full-basis
    no-trim and no-trim-award guarantees preserved. Parent verification: combined
    strict Release build passed (0 warnings/errors); W1–W4 focused suite 201/201.
    Finance migration `20261024133842_AddDuplicateChargeClaims` + snapshot generated
    using a process-scoped dummy design-time connection string; `dotnet ef migrations
    has-pending-model-changes` PASS and did not connect to a database. Admin Finance
    and Family Postman collections updated and both parse as JSON. Added guarded
    parent-owned `DuplicateChargeClaimPostgresConcurrencyTests` against a unique
    database derived from the verified local `localhost:5432/SanadIntegrationDb`
    integration server; confirms absence before create, applies Finance migrations,
    exercises simultaneous active-claim inserts, and drops only its own nonce target.
    Strict Release build after adding this gate passed (0 warnings/errors); guarded
    PostgreSQL migration/race test passed 1/1 on its nonce-named local database, which
    was cleaned up by the fixture. First full-suite run (with fixed-name Identity
    integration connection unset) found 2 stale legacy expectations + one skip-marker
    issue; both were corrected. Legacy refund correction COMPLETE (W2, round 2): the
    two stale malformed-input tests
    in `SubscriptionRefundCommandsTests.cs` were re-anchored to the explicit
    fail-closed legacy contract (renamed `..._is_still_fail_closed_retired`) —
    the retired gate precedes input validation, so non-UTC instant and
    no-subscription-link requests assert `Subscriptions.Refund.LegacyRetired`
    with the recorder seam never consulted (`LastPaymentAttemptId`/`LastKey`
    null); legacy execution NOT restored, test count preserved. Malformed-input
    coverage now lives with the workflow: non-UTC report instant already covered
    by `A_non_utc_report_instant_is_a_clean_malformed_400`; the migrated
    no-subscription-link rule gained the minimal new submission-handler test
    `An_attempt_without_a_subscription_link_is_a_clean_malformed_400`
    (MalformedInput 400, store never called). Mastermind changed the guarded PG test
    to a standard xUnit skip when the local integration variable is absent. Final
    strict Release build passed (0 warnings/errors); dedicated PostgreSQL test passed
    1/1; full suite passed 5,481, skipped 51, failed 0 with the fixed-name Identity
    integration target unset (all PG tests in that suite intentionally skipped).
    No production DB or provider calls. Exact diff review/acceptance pending.
    Migration `20261024133842_AddDuplicateChargeClaims` + snapshot generated and
    checked; `finance.duplicate_charge_claims` has partial unique active claim index
    WHERE `state <> 2`, family/idempotency unique index, and named check constraints.
    Postman merge completed in Admin Finance and Family collections. No
    `FinanceDbContextAdapter` change needed (store typed on concrete context).
- W3 ready_for_review — `ses_e923b088effeILXipE45GnWBVj`,
  River GLM 5.3 Flash; Owner-approved switch from quota-blocked Big Pickle. Parent
  run showed `[ApiController]` client-error mapping attaches ProblemDetails schemas
  even to status-only 4xx annotations; W3 revised assertions/docs to match the
  generated contract and scoped avatar schema checks to its operation. Focused
  suite now passes in combined 201/201. Original report absent; inventory bounded
  to verified current-source issues, not claimed exhaustive.
- W4 ready_for_review — `ses_e923ad9f9ffeTXMIZ2gBAlyUfr`, River GLM 5.3 Flash;
  IdentityPresent flag/guide/tests implemented; isolated target verified. Earlier
  focused check passed 40/40; combined W1–W4 suite passed 201/201.
- Mastermind verification: strict Release build with TreatWarningsAsErrors passed
  (0 warnings, 0 errors); combined focused W1–W4 suite passed 201/201; full suite
  passed 5,481, skipped 51, failed 0 (fixed-name Identity PostgreSQL tests intentionally
     skipped); dedicated Finance migration/concurrency proof passed 1/1. Mastermind
     completed exact diff review and accepted Wave A. No commit/push/deploy/provider
     calls or Production DB actions. W5 is unblocked by accepted W1/W2 contracts.
  W3 quota blocker was resolved by the explicitly approved model switch.

### W1 accepted ownership and contract

- `src/API/Sanad.API/Controllers/AdminBookingsController.cs`
- `src/Modules/Families/Application/Sanad.Modules.Families.Application/Bookings/AdminBookingQueries.cs`
- `src/Modules/Families/Application/Sanad.Modules.Families.Application/Abstractions/Caregivers/IMedicalCaregiverGateway.cs`
- `src/API/Sanad.API/CaregiversIntegration/MedicalCaregiverGateway.cs`
- `tests/Sanad.UnitTests/Families/AdminBookingStatusFilterTests.cs`
- `tests/Sanad.UnitTests/Families/AdminBookingOutcomeCommandTests.cs`
- `tests/Sanad.UnitTests/API/AdminBookingsControllerTests.cs`
- `docs/admin/bookings.md`

Absent status preserves existing valid finance-filter behavior. Explicit status
with All returns that defined status. Non-All combinations require membership in
the existing finance predicate: Cancelled={6,7,8}, FailedRefund={6,7,8,10} plus
paid/unrefunded, Refunded={9}; other combinations are 400. Validate undefined
numeric status/finance explicitly. Stable unique paging tie-breaker and batched
nullable privacy-preserving names for medical and companion caregivers required.
Outcome permissions/eligibility unchanged. Shared mapper, Postman and existing
test-adapter changes return to parent. Existing Paymob error-mapping gaps remain
an integration-review item; no implicit policy or status-code change authorized.

### W2 accepted design and ownership

The corrected design is accepted subject to the explicit evidence rule above.
W2 delivered its exact feature/domain/controller/test/guide manifest. Mastermind
owns the Finance migration + snapshot, shared test adapter, API composition,
shared error mapper and Postman collection merges; those integration items are
complete and reviewed for this phase.
No begin-review, notification, wallet capability claim, historical-award backfill,
or change to shutdown/cancel policies. Preserve old duplicate-charge auto-award
command as fail-closed (do not delete without separate exact-list approval).
Legacy POST transition is explicit and fail closed: the claim contract does not
award on submission and never emits the old approved-refund response. Tests ran
in the focused/full suites; the disposable Finance migration/race proof also
passed. No external provider requests were made.

### W4 accepted ownership and current stage

- `src/Modules/Caregivers/Infrastructure/Sanad.Modules.Caregivers.Infrastructure/Persistence/CaregiversDbContext.cs`
- `src/Modules/Caregivers/Application/Sanad.Modules.Caregivers.Application/Onboarding/AdminCaregiverReviews.cs`
- `tests/Sanad.UnitTests/Caregivers/CaregiverIntegrityFixture.cs`
- `tests/Sanad.UnitTests/Caregivers/CaregiverIdentityIntegrityTests.cs`
- `tests/Sanad.UnitTests/API/CaregiverAdminListApiTests.cs`
- `docs/admin/caregivers-review.md`
- `tests/Sanad.UnitTests/Notifications/ReadableNotificationTests.cs`

Baseline proof completed: focused SQLite/API tests 26/26 passed before product
edits. The accepted API flag and guide are implemented; combined W1–W4 and full
suite gates passed. `IdentityPresent` indicates joined row existence on the
configured query target, not authoritative absence across targets nor all four
identity causes. Preserve authorization, anonymity, joins, ordering and counts.
Shared DI identity-notice idempotency observation remains for parent review;
no producer/policy change authorized. W4 must not assert existing identity notice
idempotency as a pass when source discovery identified its absence.

### W3 accepted ownership and contract

Owned controllers: `ElderlyWellnessTipsController.cs`,
`AdminWellnessTipsController.cs`, `CareHomeVisitsController.cs`, `AuthController.cs`,
`SupportController.cs`, `AdminSplashScreensController.cs`. Owned new test:
`tests/Sanad.UnitTests/API/PortalCmsVisitsAuthSchemaContractTests.cs`. Owned docs:
`docs/admin/wellness-tips.md`, `docs/app/elderly/wellness-tips.md`,
`docs/care-homes/visits.md`, and partner-contact guide only for an explicit note
if useful. Annotation-only: match real response body/status; no runtime behavior
changes. Partner Idempotency-Key remains runtime-required but nullable in generated
OpenAPI; defer correction (no parameter nullability/validation change, no test
cementing mismatch). Original item-11 route inventory is unavailable; limit work
to reproduced gaps listed in W3 handback. No shared route metadata tests, DI,
mapper, Postman, migration, project files, W1/W2/W4 ownership. Focused and full
suite execution passed under Mastermind's serialized verification slot.

Parallel authoring is approved, but only on the disjoint manifests below. Workers
may read shared files. They return proposed shared-file edits to Mastermind rather
than modifying them concurrently. One serialized build/test/database/provider slot
belongs to Mastermind; workers author focused tests and hand back commands.

| Task | State/session | Wave | Owned scope | Acceptance |
|---|---|---|---|---|
| W1 Booking discovery/outcome | completed / `ses_e923b6fb0ffe6rPdSumtLqr8NZ` | A, parallel | `AdminBookingsController.cs`; Families `Bookings/AdminBookingQueries.cs`; booking-list filter definitions; dedicated booking-list/outcome tests and booking consumer guide | Exact diff reviewed; focused and full gates passed |
| W2 Duplicate-charge claims | completed / `ses_e923b3779ffekQqr04aKr3YC8V` | A, parallel | Families subscription refund commands/claim reads; Finance new claim/review/execution types and handlers; dedicated Admin claim controller; family subscription controller; related dedicated tests and subscription guide | Exact diff reviewed; migration/Postman, focused/full gates and disposable PostgreSQL race passed |
| W3 Contract/documentation audit | completed / `ses_e923b088effeILXipE45GnWBVj` | A, parallel | Report item 11 controller annotations outside W1/W2; CMS/Help/PartnerContact schema corrections only where reproduced; auth/visits/landing/help/wellness guides; dedicated schema tests | Bounded audit reviewed; focused and full gates passed |
| W4 Identity/notification investigation | completed / `ses_e923ad9f9ffeTXMIZ2gBAlyUfr` | A, parallel | Caregiver Admin projection/query files outside W1; CareHomes/Chat/Identity notification producers and dedicated tests | Exact diff reviewed; focused and full gates passed |
| W5 Portal fixture pack | ready_for_review / `ses_e91c0592dffeG6g5bWbnzd6dC1` - River DeepSeek V4.1 Flash HIGH (approach H). Phase base `eb218a002b598e66d3e62013d2c1d198d66c3768`. The rejected persistent real-DI tool is superseded by a bounded test-only Kestrel host (`tools/Sanad.PortalFixtureHost/`, absent from `Sanad.slnx`, no new package) that composes the real `AddSanadApi`/`UseSanadApi` pipeline on loopback and isolates providers STRUCTURALLY: it removes and replaces Families/CareHomes `IPaymobClient`, Finance `ISubscriptionRefundGateway`, Identity `ISmsSender`/`IEmailSender`, Notifications `IPushSender` and `IDateTimeProvider` with explicit scripted/inert doubles and PROVES each seam by DI resolution (plus zero surviving `Sanad.*` hosted services) before the listener or the provisioner runs; a live key in appsettings/user-secrets/env cannot make it call a provider, and ambient live-key observations are reported but never decide pass/fail. A read-only guard requires all nine module contexts to target exactly `localhost:5432/SanadDb` with zero pending migrations before any stateful startup (fail-closed on empty/unreadable evidence). A bounded domain provisioner delivers all twelve pinned groups covering the full W5 inventory (caregiver and CareHome refund ambiguity/failure, PaidAwaitingDecision, evidenced external refund, checkout/dispute, completed booking/payout via `CaregiverPayout.Record`, extension via `ExtensionOfBookingId`, current paid subscription plus a new duplicate-charge claim, complaint Received/Investigating). The FULL halves are persisted, not merely reachable: two run-owned PaidAwaitingDecision accept/reject slots plus a persisted rejected example; a successful paid service-shutdown refund (prorated award + `RefundLedgerEntry` + budget claim read back) on its OWN run-owned subscription/capture, distinct from the duplicate-claim capture so the two never share one refund budget; a duplicate claim read back `Submitted` (ready for Support) with collision refusal if a foreign claim already owns the capture; and each complaint linked to the fixture's OWN duplicate-claim `RefundCaseId`, never a random Guid. Each group asserts an actual persisted readback. Refund ambiguity/failure/evidenced states are built at the DOMAIN level (markers) so no gateway call is issued, and every synthetic reference carries the `sanad-portal-fixture-synthetic` prefix. Identity and aggregate reuse is guarded: deterministic fixture-specific emails/phones (`{tag}:{role}` hash), explicit ownership markers and account-linkage checks, and collision refusal before any reuse (no whole-`Users`-table materialization); no password/account state is ever reset, so a manually progressed case survives a rerun. The reworked wrapper is a safe lifecycle (Plan, Preflight, Start, Reuse, Stop, Cleanup) with explicit `-ConfirmStart`/`-ConfirmStop`, a loopback-only base URL, and REPORT-ONLY cleanup with no delete-SQL/psql surface. Secret handling fails closed: the private credential file must resolve OUTSIDE the repository, is created empty, ACL-hardened (correct Windows/POSIX-0600 detection, verified, delete-on-failure) BEFORE any secret byte is written, and EVERY mode streams both stdout AND stderr through a redactor (bearer/basic tokens, password-like keys, `Host=`/`Server=` fragments, `credentials-<tag>.json`, `SANAD_W5_*`) with a redacted non-zero failure path. The host also exposes a side-effect-free `--mode selfcheck` that runs the shared `PortalFixtureDomainChecks` domain drivers in memory (no credential, connection, listener or provider call). PASS (offline only): host and tests build Release/TreatWarningsAsErrors 0 warnings/0 errors; `dotnet test --filter FullyQualifiedName~PortalReportFixturePackTests` 17/17 (includes a `--mode selfcheck` spawn test); a focused offline/domain filter (Portal/BookingRefund/SubscriptionRefund/DuplicateCharge/Complaint) 282/282; selfcheck exit 0 with `passed:true`; wrapper `-Mode Plan` and host `--mode plan` display runs side-effect free. NOT RUN by design: no listener, no provisioner/DB write, no migration/seed, no HTTP scenario step, no provider call, no Reuse/Stop/Cleanup execution. Stage is NOT runtime-verified. Next: Mastermind acceptance; a first live `Start` is a separate authorized operator action on the verified local target. | B, after W1/W2 contracts accepted | New `tools/Sanad.PortalFixtureHost/` project (5 files, absent from `Sanad.slnx`); reworked `tools/Invoke-PortalReportFixturePack.ps1`; reworked `tests/Sanad.UnitTests/PortalReportFixturePackTests.cs`; reworked `docs/internal/work/portal-api-reconciliation/portal-report-fixture-pack.md`; this brief. | Deterministic requested states; isolated local/test-double money operations; secrets supplied only at runtime; readback and report-only cleanup; clock confined to the test host; no production/provider calls; all twelve W5 groups provisionable. The prior persistent real-DI proposal stays rejected as the default; approach H addresses each blocker (structural DI isolation instead of config trimming, all-context target proof before any seed, all groups provisioned, ownership-tagged records, report-only cleanup). W5 NOT accepted and not runtime-verified. |
| M Integration/review | completed / Mastermind | C, after handbacks | Shared DI, error mapper, test-host composition, project files, EF snapshots/migrations, shared Postman collections, this brief and Mastermind handoff | Strict build, 201 focused tests, 5,481-test full suite (51 skipped), migration/snapshot, Postman JSON, disposable PostgreSQL race and exact diff review passed |

Paths in the table abbreviate existing module roots under `src/Modules` and
controller root `src/API/Sanad.API/Controllers`. At kickoff Mastermind resolves
each lane's exact file manifest and records ownership before authoring. A newly
discovered shared dependency blocks only that file until ownership is assigned.
W3 excludes W1/W2 controllers, notification producers and caregiver projections.
Workers own their feature documentation; M merges shared collections serially.
No lane may create a second status ledger or edit the current release brief.

## W1 contract

- Extend `GET /api/v1/admin/bookings` with optional `status`, keeping absent-status
  behavior backward compatible. Explicitly define status + finance interaction;
  do not silently return closed rows when Confirmed is selected.
- Add family/caregiver display names without leaking extra contact/medical data.
  Missing identities have an explicit nullable representation.
- Document `POST /admin/bookings/{id}/outcome`: 204 empty success, permitted states,
  outcome reasons including admin treatment of values 1/2, reason categories 1–5,
  maximum reason length and domain-error mappings from actual source/tests.
- Test auth, filter isolation, names, ties/paging, invalid enums/combinations and
  fresh reads. Do not broaden outcome eligibility as a documentation fix.

## W2 contract and mandatory pre-authoring design

Proposed route family (confirm existing route collisions at kickoff):

- Family: retain existing duplicate-charge POST address with an explicit migration
  contract to claim submission; add own list/detail/status reads.
- Admin: `/api/v1/admin/finance/subscription-refunds/duplicate-charge` list/detail
  and claim-specific approve/reject actions; execution/reconcile routes only if
  existing mechanisms cannot safely supply them.

The retained family route is an explicit opt-in transition (`claimSubmission: true`);
absent/false is fail-closed and never pays. Persisted review/execution states,
SupportAdmin/SuperAdmin role split, server-stamped audit attribution, and the
provider adapter were implemented and verified. Existing shutdown/cancel policies
remain unchanged. Acceptance does not authorize release.

- Submission checks ownership, succeeded capture, duplicate claim identity and
  evidence, but a successful capture alone is not proof of duplication.
- Reviewer records verified basis and decision reason. Full eligible capture
  amount is server-derived; already refunded/claimed funds cannot be awarded again.
- Approval authorizes execution, not completion. Persist intent/attempt claim
  before external execution and record confirmed settlement only from evidence.
- Transport timeouts become unconfirmed, not safe-to-retry failures. No blind
  retry or callback-driven second payout. Approval and payout retries are idempotent.
- Existing subscription refund ledger entries remain historical records; never
  auto-submit them to Paymob or infer provider success from an approved ledger row.
- Tests: foreign-family denial, unauthorized decision denial, rejected claim has
  no payout, repeated submission/approval, concurrent decisions, budget contention
  with shutdown refund, provider decline/timeout/reconciliation, fresh status reads,
  ledger atomicity and legacy compatibility. Real-money tests are excluded.

## W3/W4 boundaries

W3 produces an itemized response-schema/limit inventory for every item-11 route
before editing annotations. Existing 404 support-contact behavior remains documented
unless a compatible empty-state change is explicitly accepted; no blanket 204 change.
Legal content is not authored or published by the worker. Verify current enum-name
parsing, public landing image path and optional DTO schema rather than repeat old fixes.

W4 investigates caregiver list versus Identity reads: distinguish authorization,
missing identity, anonymization and deactivation. Never infer orphanhood solely from
a 404. No deleting six reported rows. Any new identity-availability flag must avoid
reviving hidden names. Local fixture proof precedes a minimal correction.
Notification tests exercise new producer output and user-language selection without
raw identifier text; no rewriting historical inbox data in this scope.

## W5 fixture inventory

Provide bounded scenarios for both caregiver and CareHome refund ambiguity/failure,
PaidAwaitingDecision accept/reject, evidenced external refund, checkout without
family confirmation/dispute, completed paid caregiver booking/payout, booking
extension, current paid subscription/shutdown and new duplicate-claim review,
complaint Received/Investigating. Test doubles must be explicit, and never portray
synthetic financial state as a verified Paymob movement. Clock control is local
test-host-only. Missing live portal test data is not solved by writing to Production.

## Review, release and estimates

1. M pins post-release baseline, resolves exact manifests and dispatches A lanes.
2. M accepts W2 design and lane handbacks; fixes return to the same sessions.
3. W5 consumes accepted contracts; M executes checks sequentially on verified
   local targets. Workers never share a runtime slot.
4. Run strict zero-warning Release build, focused API/auth tests, meaningful
   PostgreSQL transaction/race tests, and full suite for cross-cutting money changes.
   Skips are explicitly accounted for. Parse Postman and verify route parity.
5. Review exact diff, migration/compatibility impacts, fixtures and consumer handoff.
   Request separate release authority; after deployment verify the actual revision,
   migration heads and bounded smoke results. No previous receipt proves this batch.

The earlier 2–3 working-day estimate applied to the smaller discovery/docs/fixture
scope. Support-approved claim execution is a substantive financial workflow; the
broader 4–7 working-day estimate is provisional until W2 design/dependencies are
reviewed. Parallelism does not remove serialized money verification and release work.

## Next action

Current W5 stage: **accepted — runtime-verified on 2026-10-24** (receipt:
`reports/w5-portal-fixture-runtime-2026-10-24.md`). On the guarded local target
(`localhost:5432/SanadDb`, nine contexts, zero pending after 15 approved additive
migrations behind a validated backup) the host passed preflight, proved all six
provider seams scripted/inert with zero surviving hosted services, provisioned
all twelve groups with persisted readbacks over three consecutive idempotent
runs, served authenticated family (private login) and admin HTTP readbacks for
subscription, payments, duplicate claim, family bookings, care-home bookings,
check-in disputes, complaint reviews and admin bookings, and produced a
report-only cleanup classification with no delete. No provider was called and no
real money moved. The runtime pass also caught and fixed a real Wave A defect:
`GET /api/v1/admin/bookings` returned 500 because `b.Id.Value`/`f.Id.Value`/
`caregiver.Id.Value` are not translatable inside EF lambdas; ordering now uses the
mapped id (with `BookingId : IComparable`) and the lookups filter on typed ids and
unwrap after materialization.

Superseded earlier stage note (kept for history): Approach H is implemented and offline-verified only: the bounded test-only host and its tests build strict Release 0/0; the focused offline suite is 17/17 (including a `--mode selfcheck` spawn test that runs the shared domain drivers in memory); a focused offline/domain regression filter is 282/282; the host `--mode selfcheck` exits 0 with `passed:true`; and the wrapper/host Plan display runs are side-effect free. A correction round was applied to eight independent review findings: provisioner chronology + domain-path assertion; fail-closed credential ACL (correct OS detection, empty-file-then-ACL, outside-repo) and both-channel redaction for every mode; streamed host invocation preserving exit status; deterministic fixture-specific identities with ownership/collision refusal (no whole-`Users`-table scan); persisted full halves (accept/reject slots + rejected example, service-shutdown refund/ledger on a separate capture, submitted duplicate claim ready for Support); complaints linked to the fixture's own refund case; caregiver refund-failure read back as portal-visible `Failed`; and a persisted readback per group.

Wave A is locally accepted. Wave B is blocked before dispatch because no Owner-
selected Worker model is recorded for it. Reviewer `ses_e91743c12ffedo7RKBe8D9l7tV`
(River DeepSeek V4.1 Flash) confirms W5 NOT accepted and rejects the prior
persistent real-DI proposal as the default: it reaches only read/probe states while
the requested refund-ambiguity, PaidAwaitingDecision, evidenced-external-refund,
checkout/dispute, extension, duplicate-claim-lifecycle and complaint open/investigate
states remain stop steps; it starts a Full persistent seed before effective
all-context target proof; its environment trimming cannot exclude
appsettings/user-secrets/inherited configuration; an empty available-methods probe
does not prove the Development payment double is registered; record selection is not
constrained to owned fixtures; and cleanup trusts manifest IDs against an unbound
database. Concrete blockers: (1) approved W5 fixture scope must prefer existing
test-only host/DI/domain seams, so the rejected expansion (production Families DI
scripted Paymob client, API payment-availability override, Identity fixed-clock DI,
new PortalFixture seeder scope + CareHomes constructor, deployment gateway
descriptor) must not be the default; (2) all eight scenarios are reachable in-process
via existing seams but no browsable manifest is pinned yet; (3) an in-process
A-only shortcut is insufficient — private family login and portal-browsable local
fixtures are explicit original W5 scope.

Owner decision (2026-10-24) supersedes that A-only inference: KEEP FULL W5 and
deliver usable portal-browsable LOCAL fixtures plus a private family login before
release; do not substitute automated in-process evidence or defer manual fixtures.
Model selection is settled: River DeepSeek V4.1 Flash HIGH and GLM 5.3 Flash HIGH.
The Owner rejected production-source test flags, clock, payment-DI and public
diagnostics expansion as the DEFAULT, not local test-only hosting/tooling; no
additional product policy is authorized. Design discovery is ongoing only — no
implementation, runtime, DB write, migration, API start, provider call, cleanup or
commit is authorized yet. Constraints for that design: local-only, no external
integrations or production environment hooks; explicit payment doubles with
provenance and payment calls structurally impossible via DI; all eight groups
including the previously stopped states; owner/admin/caregiver/family actors;
start/reuse/stop lifecycle with a concrete loopback base URL; repeatable with no
unrelated mutations; report-only cleanup until exact ownership/delete authority;
real PostgreSQL for relational Finance transactions (InMemory is not proof); and no
assumption that a single clock override controls all DateTime.UtcNow. Persistent
`SanadDb` existing L1 authority is distinct from a disposable target, which would
need explicit Owner approval for exact-target creation and migration. The current
persistent tool stays unexecuted. Release authorization remains separate and has
not been requested.
