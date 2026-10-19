# E04 - Platform controls and public business status (ACTIVE WORKER BRIEF)

This is the single active E04 Worker brief. It consolidates the approved contract
and implementation mapping; detailed requirements remain in the approved contract,
which stays as scope reference. Documentation cleanup is locally complete;
E04 dispatch is Owner-directed. Owner selected
`opencode/deepseek-v4.1-flash-free#high` for this dispatch. Terminal blocked initial session
`ses_eadc7b0b6ffeHzyPTYMVbeUbzV`; zero implementation. The cleanup Worker model
is not a new E04 selection.

## Read first

1. Repository `AGENTS.md` and `docs/internal/governance/AGENTS.md`.
2. `docs/internal/Mastermind_Handoff.md` (compact checkpoint).
3. Approved contract (scope reference): `docs/internal/work/portal-api-reconciliation/contracts/e04-platform-controls.md`.
4. This brief. Inspect pinned base SHA and worktree before editing. Preserve
   all pre-existing/unrelated edits. If pinned base/manifest is not recorded by
   the Mastermind, stop and report `BLOCKED`.

## Current checkpoint/result

- Phase state: `accepted`; release state: `not_requested`. E04-A, E04-B and E04-C
  are complete and the Mastermind accepted the phase locally. "Accepted" means the
  implementation, focused verification and consumer delivery pass review; it is
  not committed, published, deployed, or migrated.
- Base: `b847de46226e9f5089e8a044d3c2d23458aef3ac` (re-verified; unchanged by any
  stage). The accepted, uncommitted E04-A delta is the base for E04-B.
- Worker session: `ses_ead905588ffeTEWjxUdplK8WPt` on
  `opencode/deepseek-v4.1-flash-free#high` (A and B in the same session); the
  earlier `ses_eadc7b0b...` session stays terminal and is superseded for E04-A.
- E04-A delta (uncommitted, manifest files only): API
  `AuthorizationPolicies.cs`, API `DependencyInjection.cs`,
  `ProblemDetail/ResultProblemDetailsMapper.cs`, new
  `Controllers/AdminPlatformControlsController.cs`, new
  `Controllers/PlatformStatusController.cs`; Finance Domain
  `PlatformControlState.cs` (new), `PlatformControlAuditEntry.cs` (new: actor,
  versions, before/after state, before/after bilingual maintenance messages,
  before/after Card/Wallet switches, optional internal reason, UTC), Finance
  Application `PlatformControlCommands.cs` (new), `PlatformChargeCommands.cs`
  (`IFinanceDbContext` DbSets), Finance Infrastructure `FinanceDbContext.cs`,
  `DependencyInjection.cs`, `PlatformControlStore.cs` (new),
  `20261019061622_AddE04PlatformControls.cs`/`.Designer.cs` (new) and
  `FinanceDbContextModelSnapshot.cs`; tests `Finance/PlatformControlsTests.cs`
  (new), `API/PlatformControlsControllerTests.cs` (new),
  `API/PortalOpenApiContractTests.cs`, `API/ResultProblemDetailsMapperTests.cs`.
  No business handler, existing Family controller, E04-B/C path, feature doc or
  Postman collection was touched.
- Preserved unrelated work (untouched by E04-A): the approved docs-cleanup deletions,
  `.codex/`, README, bridge tooling, audit directories and the E03 prompts were
  kept out of the E04 change set. The cleanup deletions were committed
  separately in `1ae5c50` and the E05/E06 scope approvals in `07e4da9`; the
  remaining unrelated work is still uncommitted.
- Authored, not applied: migration `20261019061622_AddE04PlatformControls`
  creates the two `finance` tables with the unique
  `ux_platform_control_states_version` index and exactly one deterministic
  Operational seed row (`0198e2c2-4444-7777-8888-0000000000e4`, Card+Wallet
  enabled, version 1) via `InsertData`. The append-only audit table persists the
  full before/after value set, including four nullable
  `before/after_maintenance_message_arabic|english` text columns. The seed is
  deliberately not model `HasData`, so an isolated fixture can still represent an
  unreadable/missing control row. `dotnet ef migrations has-pending-model-changes`
  (Release, `--no-build`, dummy connection strings) → "No changes have been made
  to the model since the last migration." No database was connected, created,
  migrated or seeded, and no provider was contacted, at any point.
- A correction (Mastermind finding, same session): the audit record now captures
  before/after bilingual maintenance messages in addition to the state and
  Card/Wallet values. Focused tests cover the set→clear and clear→set
  transitions, blank/whitespace normalization, the over-length guard, the audit
  column set/nullability in the model and in the authored migration, and the
  HTTP update path storing the published message with a null before value.
- A second correction (Mastermind finding, same session): the Admin
  `GET`/`PUT` operations now document `401`/`403` using the existing untyped
  `ProducesResponseType` overload, matching the repository convention in
  `AdminUsersController.Statistics`, and the anonymous public status operation
  documents neither. Focused reflection assertions and the generated-OpenAPI
  contract assertions cover both facts (the published document carries `401`/`403`
  for the Admin pair and none for `/api/v1/platform/status`). Behavior, policy,
  error codes and DTOs are unchanged.
- A: `dotnet build Sanad.slnx -c Release --nologo /p:TreatWarningsAsErrors=true`
  → Build succeeded, 0 Warning(s), 0 Error(s) (rerun after the compiled model and
  controller changed).
- A: isolated focused `dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj -c
  Release --no-build --no-restore --filter
  "FullyQualifiedName~PlatformControls|FullyQualifiedName~PlatformControlPersistenceModelTests|FullyQualifiedName~PortalOpenApiContractTests|FullyQualifiedName~PortalResponseSerializationTests|FullyQualifiedName~ResultProblemDetailsMapperTests"`
  (dummy `ConnectionStrings__*Database` for all nine modules, migrations/seeders
  off, temporary storage root) → Failed: 0, Passed: 164, Skipped: 0 (rerun after
  the corrections).
- A: same isolation, affected-regression subset (`NewEndpointsAuthorizationContractTests`,
  `PayoutOperationalAdminPolicyTests`, `SanadApiDependencyInjectionTests`,
  `PortalHostIsolationTests`, `StartupMigrationPolicyTests`,
  `AdminFinanceControllerTests`, `ApiControllerBaseTests`,
  `AdminUsersReactivationControllerTests`, `FamilySubscriptionsControllerTests`,
  the last two because they assert documented status responses) → Failed: 0,
  Passed: 61, Skipped: 0 (rerun after the OpenAPI metadata correction).
- A: `git diff --check` → exit 0 (only pre-existing CRLF conversion warnings).
- A: consistency evidence without a database — `dotnet ef migrations script`
  (Release, `--no-build`, output written to a temp file outside the repo and
  deleted, never executed against any database) emits
  `CREATE TABLE finance.platform_control_audit_entries` with the four nullable
  `before/after_maintenance_message_arabic|english` columns, the primary key, the
  state/version check constraints and the single seed `INSERT`/`DELETE` pair; the
  migration `.Designer.cs` target-model blocks for both platform entities are
  identical to `FinanceDbContextModelSnapshot.cs` (extracted block comparison).
- A: authored vs executed — the control plane, migration and focused tests are
  authored; build, focused tests, the regression subset and the EF model check
  were executed. NOT RUN: whole Unit suite, `Test-SanadPortalGate.ps1` (no filter;
  runs whole suites), any E04-B enforcement/HTTP behavior (not implemented yet),
  E04-C docs/Postman, live database and provider checks.
- B delta (uncommitted, E04-B manifest files only):
  `Families/Application/.../Bookings/BookingPaymentCommands.cs` (new shared
  `PlatformControlGate` + booking-intent gate),
  `.../Bookings/BookingCommands.cs` (caregiver checkout gate),
  `.../Subscriptions/SubscriptionPaymentCommands.cs` (initial + renewal intent
  gates; the initial handler also carries the enrollment 3DS configuration
  prerequisite after plan resolution), `.../Subscriptions/PlanChangeCommands.cs` (plan-change intent gate),
  `CareHomes/Application/.../Bookings/CareHomeBookingCommands.cs` (Care Home
  checkout gate; Care Home intent gate placed after the existing-intent readback),
  API `Controllers/FamilySubscriptionsController.cs` (explicit
  `Platform.Maintenance`/`Platform.ControlStateUnavailable` → 503,
  `Platform.PaymentMethodUnavailable` → 400 mappings in the initial, renewal and
  plan-change helpers — the plan-change helper's 409 fallback no longer swallows
  platform outcomes — plus plan-change response metadata),
  `Controllers/FamilyBookingsController.cs` and
  `Controllers/FamilyCareHomeBookingsController.cs` (response metadata only: 503
  for the two checkouts, 400/503 for the payment intents), master amendment 2:
  `API/DependencyInjection.cs` (`PaymobPaymentMethodConfiguration` implements the
  path-specific `CardSubscriptionEnrollmentConfigured` truth),
  `Finance/Application/.../PlatformControlCommands.cs` (one new read-only member on
  `IPaymentMethodConfiguration`), tests
  `Families/BookingPaymentIntentTests.cs` (shared explicit in-memory
  `FixedPlatformControls` double + gate tests),
  `Families/BookingPaymentWebhookTests.cs`, `Families/BookingRemediationTests.cs`,
  `Families/CancelBookingCommandHandlerTests.cs`,
  `Families/CaregiverCancelBookingCommandHandlerTests.cs`,
  `Families/CaregiverDeclineFactTests.cs`, `Families/FamilyRoleMatrixLockTests.cs`,
  `ChatPlus/ChatPlusPricingTests.cs`,
  `Families/Subscriptions/SubscriptionPaymentTests.cs`,
  `Families/Subscriptions/SubscriptionPlanChangeTests.cs`,
  `CareHomes/CareHomeBookingLifecycleTests.cs`,
  `API/FamilySubscriptionsControllerTests.cs`, `API/PortalOpenApiContractTests.cs`,
  `Finance/PlatformControlsTests.cs` and `API/PlatformControlsControllerTests.cs`
  (the last two inside the Mastermind-approved B-stage amendment 2: one new
  configuration/policy separation assertion each).
  Apart from the two master-approved amendments above, no E04-A
  entity/command/store/controller/migration/snapshot file, no feature doc,
  Postman collection, handoff, governance or model-config file was touched.
  `ResultProblemDetailsMapper.cs` needed no B edit: the five E04 error mappings and
  the generic 503 presentation added by A already produce the documented statuses.
- B gated operations (exactly the contract-named new paths): new caregiver booking
  checkout, Care Home booking checkout, and new payment-intent creation for
  caregiver bookings, Care Home bookings, subscription initial, subscription
  renewal and plan changes. The gate is the first statement of each gated handler
  (the Care Home intent gate runs after the existing-intent readback, which is
  returned before any gate), so a rejection happens before reservations, claims,
  provider calls and pending-transaction writes. Deliberately ungated: verified
  provider callbacks/confirmations (booking, Care Home, subscription, plan-change),
  existing-intent readback/reuse, reads/quotes, cancellations and refunds, care
  delivery and decision lifecycle, Care Home extension and local
  room/bed MaintenanceBlock behaviour, and all admin operations.
- B fail-closed semantics: the gate reads `IPlatformControlReader` and maps a
  missing record or an unreadable store to the generic
  `Platform.ControlStateUnavailable` (503) — never to Operational. Maintenance →
  `Platform.Maintenance` (503); a disabled switch or an unconfigured Paymob
  integration (`IPaymentMethodConfiguration`) → `Platform.PaymentMethodUnavailable`
  (400); `PaymentMethod.ApplePay` is rejected as unavailable **before** any
  provider call instead of being handed to the gateway. The Card initial
  subscription-enrollment branch adds one path-specific technical prerequisite on
  top of the generic Card truth: when the resolved plan carries a
  `PaymobSubscriptionPlanId`, the intent is sent through the dedicated 3DS
  enrollment integration, so a missing `Card3dsIntegrationId` returns the same 400
  `Platform.PaymentMethodUnavailable` before the attempt is created and before any
  provider call. No default-Operational or
  test-only bypass exists in production code or handlers; every test constructs an
  explicit control record.
- B: strategy/compat note — only the Care Home intent path had existing intent
  readback/reuse, and it is preserved and proven during Maintenance/disabled
  states. The caregiver-booking and subscription intent paths never had a
  readback/reuse path (a retry has always created a new attempt/provider intent);
  E04-B did not add or remove one, so nothing had to be kept available there.
- B: manifest accounting (review-stage amendment) — the required
  `IPlatformControlReader` constructor parameter made
  `tests/Sanad.UnitTests/CareHomes/CareHomeRoomEnrichmentTests.cs` (line 515,
  target-typed local `CheckoutHandler` factory) uncompilable. That path was **not**
  in the approved Worker manifest and was **not** Owner-authorized; it was applied
  during B after an interactive operator confirmation, which the Mastermind does
  not record as prior approval. The Mastermind now accepts it as a narrow
  **review-stage manifest amendment (constructor argument only)** and the path is
  recorded in the supporting-callsite manifest above. The diff for that file is
  exactly one `using Sanad.UnitTests.Families;` plus the
  `FixedPlatformControls.Operational()` argument (`git diff --name-only` and the
  full hunk were re-checked); no assertion, no other content and no other file was
  added inside or outside the manifest. The explicit fixture remains; no
  production default/Operational bypass was introduced anywhere.
- B review correction (Mastermind finding, same session): the subscription
  overload of `PlatformControlGate.CheckNewPaymentIntentAsync` no longer maps
  "anything that is not Card" to Wallet. It is now an exhaustive `switch`
  (Card→Card, Wallet→Wallet, anything else→Unsupported → `Platform.PaymentMethodUnavailable`
  before any provider call or attempt write), because the renewal command has no
  `IsInEnum` validator and handlers can be called directly. ApplePay support is
  still not implemented or advertised for subscriptions.
- B: regression for that correction — `Subscription_gate_maps_only_defined_methods`
  (theory over `0/1/2/99`: only the defined `Card`/`Wallet` values pass and `0`/`99`
  return `Platform.PaymentMethodUnavailable`), plus
  `Undefined_method_on_renewal_is_rejected_before_any_provider_call` and
  `Undefined_method_on_initial_intent_is_rejected_before_any_provider_call`, each
  asserting `IntentCalls == 0` and no persisted `SubscriptionPaymentAttempt`.
- B review correction 2 (Mastermind finding, same session) — integration gap in the
  initial subscription-enrollment branch: `PaymobClient.CreateSubscriptionPaymentIntentAsync`
  selects `PaymobOptions.Card3dsIntegrationId` when the method is Card **and** the
  input carries a provider subscription plan id (`cardEnrollment`), while E04's
  `IPaymentMethodConfiguration.CardConfigured` only checks the generic
  `CardIntegrationId`. A Card initial subscription intent for a plan with a
  `PaymobSubscriptionPlanId` could therefore pass the E04 gate and be rejected later
  by the gateway layer as `Paymob.MethodNotAvailable` (409) instead of the stable E04
  `Platform.PaymentMethodUnavailable` (400). Corrected with a path-specific technical
  configuration check; product policy was neither broadened nor changed.
- B: fix — `IPaymentMethodConfiguration` gained one read-only member
  `CardSubscriptionEnrollmentConfigured` (truth for the enrollment branch only),
  implemented by `PaymobPaymentMethodConfiguration` from the existing options as
  `SecretKey` + `Card3dsIntegrationId`. `CardConfigured` (generic Card integration)
  and `WalletConfigured` are unchanged, and the public status response still
  advertises methods from the generic truth only. The check runs in
  `CreateSubscriptionPaymentIntentCommandHandler` **after** plan resolution and
  **before** the attempt is created or any provider is called, and applies only when
  `request.Method == SubscriptionPaymentMethod.Card` and
  `plan.PaymobSubscriptionPlanId is not null` — the exact `cardEnrollment` condition.
  Missing 3DS configuration → 400 `Platform.PaymentMethodUnavailable`, no
  attempt/pending write and no provider call. Plans without a provider plan id keep
  the generic Card rule; Wallet, renewal and plan-change Card intents are untouched
  (renewal and plan-change never send an enrollment plan id); no ApplePay or new
  provider support was invented.
- B: manifest accounting (2nd amendment, this time Mastermind-approved) — the
  Mastermind approved this precise B-stage amendment for exactly:
  `src/Modules/Finance/Application/Sanad.Modules.Finance.Application/PlatformControlCommands.cs`,
  `src/API/Sanad.API/DependencyInjection.cs`,
  `src/Modules/Families/Application/Sanad.Modules.Families.Application/Subscriptions/SubscriptionPaymentCommands.cs`,
  `tests/Sanad.UnitTests/Families/Subscriptions/SubscriptionPaymentTests.cs`,
  `tests/Sanad.UnitTests/Finance/PlatformControlsTests.cs`,
  `tests/Sanad.UnitTests/API/PlatformControlsControllerTests.cs` and the B-owned test
  double in `tests/Sanad.UnitTests/Families/BookingPaymentIntentTests.cs`. No
  migration/entity/store/snapshot path and no docs/Postman (E04-C) path was touched.
  The authorization for this technical path is the Mastermind's: the Owner did **not**
  separately approve it, and no Owner approval is claimed for it.
- B: regression for that fix — `Card_enrollment_intent_without_the_3ds_integration_is_rejected_before_any_provider_call`
  (generic Card configured, 3DS enrollment integration missing, plan with a provider
  plan id → 400 `Platform.PaymentMethodUnavailable`, `IntentCalls == 0`,
  `LastInput == null`, no persisted `SubscriptionPaymentAttempt`),
  `Card_enrollment_intent_with_the_3ds_integration_keeps_the_existing_success_flow`
  (3DS configured → existing success flow: provider called,
  `SubscriptionPlanId == 6755`, `RecurringRenewalSupported == true`, pending attempt
  persisted), `Card_intent_for_a_plan_without_a_provider_plan_id_keeps_the_generic_card_rule`
  (no provider plan id + 3DS missing → still succeeds through the generic Card rule),
  `Wallet_intent_for_an_enrollment_plan_is_unaffected_by_the_3ds_prerequisite` and
  `Renewal_intent_is_unaffected_by_the_3ds_enrollment_prerequisite`.
  Configuration/policy separation is additionally asserted through the real DI class
  at the `IPaymentMethodConfiguration` interface by
  `PlatformControlsHostTests.PaymentMethodConfiguration_RequiresTheEnrollmentIntegrationOnlyForTheEnrollmentBranch`
  (3DS id empty → `CardConfigured` true while `CardSubscriptionEnrollmentConfigured`
  is false, and the public status still advertises Card+Wallet) and at the handler
  level by `PlatformControlsTests.Public_status_ignores_the_subscription_enrollment_prerequisite`.
  No real provider was called (per-file fakes and HTTP stubs only).
- B: `dotnet build Sanad.slnx -c Release --nologo /p:TreatWarningsAsErrors=true` →
  Build succeeded, 0 Warning(s), 0 Error(s) (rerun after both review corrections).
- B: isolated focused `dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj -c
  Release --no-build --no-restore --filter` over
  `Families.BookingPaymentIntentTests|Families.BookingRemediationTests|
  Families.BookingPaymentWebhookTests|Families.CancelBookingCommandHandlerTests|
  Families.CaregiverCancelBookingCommandHandlerTests|Families.CaregiverDeclineFactTests|
  Families.FamilyRoleMatrixLockTests|Families.Subscriptions.SubscriptionPaymentTests|
  Families.Subscriptions.SubscriptionPlanChangeTests|CareHomes.CareHomeBookingLifecycleTests|
  CareHomes.CareHomeRoomEnrichmentTests|ChatPlus.ChatPlusPricingTests|
  Finance.PlatformControlsTests|API.FamilySubscriptionsControllerTests|
  API.PortalOpenApiContractTests|
  API.ResultProblemDetailsMapperTests|API.PlatformControlsControllerContractTests|
  API.PlatformControlsHostTests|API.AdminUsersReactivationControllerTests`
  (dummy `ConnectionStrings__*Database` for all nine modules,
  `Database__ApplyMigrationsOnStartup=false`, `FinanceMigrations__ApplyOnStartup=false`,
  both seeders disabled, temporary `Storage__Local__Root`) → Failed: 0, Passed: 394,
  Skipped: 0 (rerun after the enrollment fix; the changed classes alone
  — `SubscriptionPaymentTests`, `PlatformControlsHostTests` (the host class in
  `PlatformControlsControllerTests.cs`), `Finance.PlatformControlsTests` and
  `BookingPaymentIntentTests` → Failed: 0, Passed: 71, Skipped: 0;
  `SubscriptionPaymentTests` alone → 31; `Finance.PlatformControlsTests` alone → 26;
  `PlatformControlsHostTests` alone → 22; a filter naming only the new
  enrollment/policy tests → 8 passed. Earlier B runs: the review-correction set →
  361; `SubscriptionPaymentTests` → 26; 7 business classes → 115; API/contract
  subset → 70; first full B set → 355).
- B: `git diff --check` → exit 0 (only pre-existing CRLF conversion warnings).
- B: `dotnet ef migrations has-pending-model-changes --context FinanceDbContext --configuration
  Release --no-build` (dummy connection strings) → "No changes have been made to the
  model since the last migration." E04-B changed no model or snapshot file; the
  authored `20261019061622_AddE04PlatformControls` migration remains authored and
  NOT applied, and no database or provider was contacted.
- B: focused proofs — per-path maintenance/method denial with
  `IntentCalls == 0` and no persisted booking/attempt/pending transaction;
  missing and unreadable control records fail closed as 503; disabled switch and
  unconfigured integration both rejected; ApplePay rejected without a provider
  call; Care Home existing intent read back during Maintenance and with the method
  disabled; confirmed callbacks still settle exactly once while the record says
  Maintenance (`Paid` then `AlreadyProcessed`, and `Paid` for the booking path);
  plan-change quote and pending-downgrade replace/cancel still work during
  Maintenance; controller-level status mapping for the three subscription intent
  actions (503/503/400) including the plan-change helper, and generated-OpenAPI
  assertions that document `503` for both checkouts, `400/503` for the five
  payment-intent routes and the plan-change route.
- B: authored vs executed — enforcement code, metadata and tests are authored;
  the strict Release solution build, the focused/regression test set and
  `git diff --check` were executed. NOT RUN (do not claim): whole Unit suite,
  `Test-SanadPortalGate.ps1` (no filter option; runs whole suites), any migration
  application or database/server start (the E04 migration remains authored and NOT
  applied), Bruno collections, live Paymob/provider calls, E04-C docs/Postman and
  any E05/E06 work.
- Release authorization: the Owner approved the E04 release on 2026-10-19
  (commit/push, publish/deploy and migration application). The authorized target
  is the Owner-designated Production VPS carried by the immediately prior E03
  receipt; it is revalidated before any write.
- Next action: commit and push the accepted phase, publish the package from
  pushed `main`, deploy to the authorized target, verify with the standard
  deployment verifier, then record the release revision, verification and
  limitations here and mark the phase `completed`. No later phase starts before
  that.

- C (Mastermind, direct — no Worker): consumer delivery was written against the
  implemented behavior, not against a plan. New guides
  `docs/admin/platform-controls.md` and `docs/app/public/platform-status.md` document
  the SuperAdmin control routes (full-replacement PUT, `expectedVersion`
  concurrency, `400`/`409`/`503` outcomes, append-only attributable audit, no
  gateway secrets or integration ids) and the anonymous public status (state,
  bilingual message, available methods, advisory-only semantics, fail-closed
  `503`). `docs/app/families/bookings.md`, `docs/app/families/subscriptions.md`
  and `docs/care-homes/booking-lifecycle.md` document the maintenance and payment
  method gates, the fail-closed `503`, the operations that stay available, the
  existing-intent replay exception, and the Card 3DS enrollment prerequisite.
- C: Postman — the three new routes (anonymous `GET /api/v1/platform/status`,
  SuperAdmin `GET`/`PUT /api/v1/admin/platform-controls`) were added, and the
  gated Family and Care Homes request descriptions now state the `503`/`400`
  outcomes. The Admin update example is labelled as a live-availability change
  for an owner-authorized target only.
- C: route coverage — the legacy `docs/internal/tools/Verify-ApiContractMapping.ps1`
  prints missing/orphan counters it never computes, so its exit code is not
  coverage evidence. An explicit read-only comparison with correct ASP.NET route
  semantics (class route plus relative suffix, absolute action routes, `~/` roots)
  was used instead: before the Postman change 3 missing — exactly the three E04
  routes — and 9 orphan; after it 0 missing and the same 9 orphan. Those 9 are
  pre-existing Care Homes media/file routes (`/care-homes/.../media/...`) outside
  E04's manifest and untouched by it; they are reported, not claimed as covered.
- C: validation — 11 Markdown links/anchors across the five changed or new guides
  resolve; all four Postman collections parse as UTF-8 JSON; `git diff --check`
  exit 0. E04-C itself changed no compiled source, so no rebuild was required for
  it. No API, database, migration or provider action; the E04 migration is still
  authored and NOT applied.
- Mastermind final acceptance evidence, re-run by the Mastermind on the final tree
  (not reused from the Worker): strict Release solution build
  `dotnet build Sanad.slnx -c Release --nologo /p:TreatWarningsAsErrors=true` ->
  Build succeeded, 0 Warning(s), 0 Error(s); focused E04 + affected-regression
  test set in a process-local isolated child (dummy `ConnectionStrings__*Database`
  for all nine modules, `Database__ApplyMigrationsOnStartup=false`,
  `FinanceMigrations__ApplyOnStartup=false`, both seeders off, temporary storage
  root) -> Failed: 0, Passed: 403, Skipped: 0, Total: 403; `dotnet ef migrations
  has-pending-model-changes --context FinanceDbContext --configuration Release
  --no-build` -> "No changes have been made to the model since the last migration.";
  `git diff --check` -> exit 0. NOT RUN (not claimed): whole Unit suite,
  `Test-SanadPortalGate.ps1`, migration application, any database or API-server
  start, Bruno, live provider calls, and any authenticated Production request.

## Tasks (update these rows, not a separate tracker)

| Task | Depends on | Acceptance | Status | Evidence |
|---|---|---|---|---|
| E04-A — Control plane | Approved scope + dispatch direction | Versioned state/audit, SuperAdmin and public APIs, fail-closed reads, focused Finance/API tests | completed | Accepted by Mastermind after actual diff/evidence review. Session `ses_ead905588ffeTEWjxUdplK8WPt` on base `b847de46`; versioned Finance control state + append-only before/after audit (state, bilingual maintenance messages, Card/Wallet, actor, UTC) + optimistic conflict; SuperAdmin `GET/PUT /api/v1/admin/platform-controls` (documented `200/400/409/503` + `401/403`), anonymous sanitized `GET /api/v1/platform/status` (no auth responses); fail-closed 503; migration `20261019061622_AddE04PlatformControls` authored (NOT applied, `has-pending-model-changes` clean); strict Release build 0 warnings/0 errors; focused tests 164 passed/0 skipped, affected regressions 61 passed; OpenAPI auth/error assertions pass; both bounded review findings corrected; `git diff --check` clean |
| E04-B — Enforcement | Mastermind accepts A | All named new checkout/intent paths gated; reuse/callback/lifecycle exceptions preserved; HTTP errors and focused regressions verified | completed | Same session `ses_ead905588ffeTEWjxUdplK8WPt` on `opencode/deepseek-v4.1-flash-free#high`; base `b847de46` + accepted A delta. Accepted by Mastermind after review of the actual diff; four review findings were corrected in the same session (audit completeness, OpenAPI auth metadata, undefined subscription-method enum, path-specific Card 3DS enrollment configuration). Shared fail-closed `PlatformControlGate` (`Families.Application.Bookings`) gating the new caregiver booking checkout, Care Home booking checkout and the five payment-intent paths (caregiver booking, Care Home booking, subscription initial/renewal, plan-change); Care Home reuse returned before the gate; checkouts/intents rejected before reservations, claims, provider calls and pending writes; Maintenance 503, disabled/unconfigured method 400, ApplePay rejected pre-provider, missing/unreadable record 503; callbacks, reads, cancels/refunds, lifecycle and local Care Home maintenance untouched. Subscription method mapping is an exhaustive switch, so an undefined enum value (e.g. the unvalidated renewal cast) fails closed as 400 instead of falling through to Wallet (Mastermind review correction 1, regressions added). A second Mastermind review finding is fixed too: because the gateway client sends a Card initial subscription-enrollment intent through the dedicated 3DS enrollment integration when the resolved plan has a `PaymobSubscriptionPlanId`, a new path-specific read-only `IPaymentMethodConfiguration.CardSubscriptionEnrollmentConfigured` (SecretKey + `Card3dsIntegrationId`, implemented by `PaymobPaymentMethodConfiguration`) is checked after plan resolution and before any attempt/provider call, so a missing enrollment integration returns the stable 400 `Platform.PaymentMethodUnavailable` instead of a gateway-level `Paymob.MethodNotAvailable` (409); generic `CardConfigured`, the public method list and all non-enrollment Card/Wallet/renewal/plan-change flows are unchanged (Mastermind-approved B-stage manifest amendment; the Owner did not separately approve this technical path). Family subscription/booking/care-home controllers map platform errors (400/503, plan-change fallback no longer 409) with matching OpenAPI metadata; controller + generated-OpenAPI status assertions pass. Strict Release solution build 0 warnings/0 errors; focused + affected-regression set Failed 0 / Passed 394 / Skipped 0 (changed classes alone 71); `git diff --check` exit 0; migration still authored and NOT applied. Manifest accounting: `tests/Sanad.UnitTests/CareHomes/CareHomeRoomEnrichmentTests.cs` was not in the approved manifest and was not Owner-authorized; it was applied pre-review and is now accepted by the Mastermind as a narrow review-stage manifest amendment (mechanical `CheckoutHandler` constructor argument only) and listed in the supporting-callsite manifest. |
| E04-C — Consumer delivery | Mastermind accepts B | Listed guides/Postman match implemented routes, permissions, payloads/errors and lifecycle; final relevant checks pass | completed | Mastermind delivered C directly (no Worker): new `docs/admin/platform-controls.md` and `docs/app/public/platform-status.md`; maintenance/method-gate documentation added to `docs/app/families/bookings.md`, `docs/app/families/subscriptions.md` and `docs/care-homes/booking-lifecycle.md`; the three new routes added to Postman and the gated Family/Care-Home request descriptions updated. Route coverage 3 missing → 0 missing (9 pre-existing Care Homes media orphans unchanged); 11 Markdown links/anchors resolve; 4 Postman collections parse; `git diff --check` exit 0; no compiled source changed so A/B build and test evidence stands. |

E04-A owned files: `AuthorizationPolicies.cs`, API `DependencyInjection.cs`,
new `AdminPlatformControlsController.cs` and `PlatformStatusController.cs`;
Finance `PlatformControlState.cs`, `PlatformControlAuditEntry.cs`,
`PlatformControlCommands.cs`, `PlatformChargeCommands.cs`, `FinanceDbContext.cs`,
Finance infrastructure `DependencyInjection.cs`, `PlatformControlStore.cs`, the
named migration/designer and model snapshot; `ResultProblemDetailsMapper.cs` for
the control-state-unavailable 503; tests `PlatformControlsTests.cs`,
`PlatformControlsControllerTests.cs`, `PortalOpenApiContractTests.cs`, and
`ResultProblemDetailsMapperTests.cs`; plus this brief's E04-A status/result only.
E04-A is accepted. E04-B now owns only the enforcement stage paths in the shared
manifest; do not begin E04-C documentation/Postman delivery before B acceptance.

Worker uses `in_progress`, `blocked`, `ready_for_review`; Mastermind alone marks
tasks `completed`. After C, Mastermind reviews the combined phase and marks it
`accepted` only when all acceptance criteria pass. Request release approval once;
record authorization and verified release outcome here, then mark phase `completed`.
No E04 task completion or model selection authorizes deployment.

## Objective and bounded behavior

Implement the Owner-approved contract without broadening it:

- Single database-backed operational/maintenance state, bilingual public message,
  Card/Wallet switches, append-only actor/time/before/after audit, optimistic
  concurrency; use existing Finance persistence boundary and `finance` schema. No
  general settings registry or new DbContext.
- SuperAdmin-only control read/update APIs and sanitized anonymous public status
  API using approved contract routes/response shape.
- Maintenance blocks only named new caregiver booking checkout, Care Home booking
  checkout, and new caregiver/Care Home/subscription/plan-change payment intent
  creation paths. Payment-mode switches enforced server-side.
- Keep status/health, authentication, Admin control access, provider callbacks,
  existing intent reuse/callback settlement, reads, cancellations, care-delivery,
  refunds, other unlisted operations available per contract.
- Advertise Card/Wallet only when enabled and configured. Do not advertise or
  implement ApplePay, new providers, gateway-secret changes, or UI features.
- Missing/unreadable platform control state must fail closed for gated operations;
  never treat failed read as Operational. Do not apply migrations.

## Bounded implementation stages

Use the same brief and Worker session for sequential stages; do not create stage
trackers, reports or documentation-only commits. At a stage handback replace the
current result below; Mastermind reviews the actual diff before continuation.

1. **Control plane:** Finance state/audit/persistence, Admin/public APIs and their
   focused tests. Author the initial Operational migration without applying it.
2. **Enforcement:** after control-plane acceptance, gate the named business paths
   and test callbacks, existing-intent reuse and lifecycle exceptions. Rejections
   must neither create provider intents nor persist misleading pending payments.
3. **Consumer delivery:** update the listed feature guides and Postman examples
   against verified implementation, including permissions, errors and exceptions.

Stage ownership: A owns API/authorization (except existing Family controllers),
Finance files and Finance/PlatformControls/OpenAPI tests; B owns business handlers,
existing Family controllers and related regressions; C owns feature docs/Postman.
All stages may update this brief. A/B may update the shared HTTP error mapper and
OpenAPI/mapper tests for their changed behavior; no concurrent file ownership.

### HTTP boundaries that must not be missed

- Use the contract's `GET /api/v1/platform/status`, SuperAdmin
  `GET/PUT /api/v1/admin/platform-controls`, `503 Platform.Maintenance` and
  `400 Platform.PaymentMethodUnavailable` behavior. Match existing response
  conventions; document actual DTO shapes and concurrency/unavailable errors.
- Shared `ResultProblemDetailsMapper` defaults unknown codes to 400. Add the
  E04 mappings and safe service-unavailable presentation; preserve other codes.
- `FamilySubscriptionsController.ToPlanChangeResult` defaults failures to 409:
  explicitly preserve the E04 status mapping there as well. Cover initial,
  renewal and plan-change payment intents, not only the initial subscription.
- Enforce after existing-intent readback when reusing an intent, but before
  new provider calls, claims or pending transaction writes. Never gate callbacks.
- Match enabled/configured Card/Wallet truth to existing Paymob options/client
  behavior; do not add provider configuration or infer ApplePay support from enums.

## Exact owned-file manifest

Modify/create only these files. If a necessary file is missing or scope requires
another path, stop and report path/reason; do not self-expand. Paths verified
against the current tree and pre-cleanup E04 brief/stage prompts.

API and authorization:
- `src/API/Sanad.API/Authorization/AuthorizationPolicies.cs`
- `src/API/Sanad.API/DependencyInjection.cs`
- `src/API/Sanad.API/Controllers/AdminPlatformControlsController.cs` (new)
- `src/API/Sanad.API/Controllers/PlatformStatusController.cs` (new)
- `src/API/Sanad.API/ProblemDetail/ResultProblemDetailsMapper.cs`
- `src/API/Sanad.API/Controllers/FamilySubscriptionsController.cs`
- `src/API/Sanad.API/Controllers/FamilyBookingsController.cs` (E04 response metadata only)
- `src/API/Sanad.API/Controllers/FamilyCareHomeBookingsController.cs` (E04 response metadata only)

Finance persistence and application:
- `src/Modules/Finance/Domain/Sanad.Modules.Finance.Domain/PlatformControlState.cs` (new)
- `src/Modules/Finance/Domain/Sanad.Modules.Finance.Domain/PlatformControlAuditEntry.cs` (new)
- `src/Modules/Finance/Application/Sanad.Modules.Finance.Application/PlatformControlCommands.cs` (new)
- `src/Modules/Finance/Application/Sanad.Modules.Finance.Application/PlatformChargeCommands.cs`
- `src/Modules/Finance/Infrastructure/Sanad.Modules.Finance.Infrastructure/FinanceDbContext.cs`
- `src/Modules/Finance/Infrastructure/Sanad.Modules.Finance.Infrastructure/DependencyInjection.cs`
- `src/Modules/Finance/Infrastructure/Sanad.Modules.Finance.Infrastructure/PlatformControlStore.cs` (new)
- `src/Modules/Finance/Infrastructure/Sanad.Modules.Finance.Infrastructure/Persistence/Migrations/20261019061622_AddE04PlatformControls.cs` (new)
- `src/Modules/Finance/Infrastructure/Sanad.Modules.Finance.Infrastructure/Persistence/Migrations/20261019061622_AddE04PlatformControls.Designer.cs` (new)
- `src/Modules/Finance/Infrastructure/Sanad.Modules.Finance.Infrastructure/Persistence/Migrations/FinanceDbContextModelSnapshot.cs`

Business enforcement:
- `src/Modules/Families/Application/Sanad.Modules.Families.Application/Bookings/BookingCommands.cs`
- `src/Modules/Families/Application/Sanad.Modules.Families.Application/Bookings/BookingPaymentCommands.cs`
- `src/Modules/Families/Application/Sanad.Modules.Families.Application/Subscriptions/SubscriptionPaymentCommands.cs`
- `src/Modules/Families/Application/Sanad.Modules.Families.Application/Subscriptions/PlanChangeCommands.cs`
- `src/Modules/CareHomes/Application/Sanad.Modules.CareHomes.Application/Bookings/CareHomeBookingCommands.cs`

Focused tests:
- `tests/Sanad.UnitTests/Finance/PlatformControlsTests.cs` (new)
- `tests/Sanad.UnitTests/API/PlatformControlsControllerTests.cs` (new)
- `tests/Sanad.UnitTests/API/PortalOpenApiContractTests.cs`
- `tests/Sanad.UnitTests/API/ResultProblemDetailsMapperTests.cs`
- `tests/Sanad.UnitTests/API/FamilySubscriptionsControllerTests.cs`
- `tests/Sanad.UnitTests/Families/BookingPaymentIntentTests.cs`
- `tests/Sanad.UnitTests/Families/BookingRemediationTests.cs`
- `tests/Sanad.UnitTests/Families/Subscriptions/SubscriptionPaymentTests.cs`
- `tests/Sanad.UnitTests/Families/Subscriptions/SubscriptionPlanChangeTests.cs`
- `tests/Sanad.UnitTests/CareHomes/CareHomeBookingLifecycleTests.cs`

Supporting test call sites (B only; adapt explicit control-reader fixtures if
handler constructors change, preserve existing assertions and run affected tests):
- `tests/Sanad.UnitTests/Families/BookingPaymentWebhookTests.cs`
- `tests/Sanad.UnitTests/Families/CancelBookingCommandHandlerTests.cs`
- `tests/Sanad.UnitTests/Families/CaregiverCancelBookingCommandHandlerTests.cs`
- `tests/Sanad.UnitTests/Families/CaregiverDeclineFactTests.cs`
- `tests/Sanad.UnitTests/Families/FamilyRoleMatrixLockTests.cs`
- `tests/Sanad.UnitTests/ChatPlus/ChatPlusPricingTests.cs`
- `tests/Sanad.UnitTests/CareHomes/CareHomeRoomEnrichmentTests.cs` — added by a
  narrow **Mastermind-approved review-stage manifest amendment** (constructor
  argument only). This direct `CheckoutHandler` call site was not in the original
  approved manifest and was **not** Owner-authorized; the Mastermind accepted it
  after B review because the approved source constructor change otherwise leaves
  the file uncompilable. The change is exactly one `using` for the test fixture
  type plus the `FixedPlatformControls.Operational()` constructor argument; no
  assertions and no other content changed.

Do not retain a production bypass/default-Operational fallback just to avoid
updating test constructors. Read-only reference files may be inspected outside
this edit manifest; any additional necessary edit needs a bounded manifest
correction from Mastermind, not a new phase or a repeat of product intake.

Feature docs and Postman:
- `docs/admin/platform-controls.md` (new)
- `docs/app/public/platform-status.md` (new; public status, methods and maintenance handling)
- `docs/app/families/bookings.md`
- `docs/app/families/subscriptions.md`
- `docs/care-homes/booking-lifecycle.md`
- `docs/postman/admins/Sanad.Admin.Finance.postman_collection.json`
- `docs/postman/app/Sanad.App.Public.postman_collection.json`
- `docs/postman/app/Sanad.App.Family.postman_collection.json`
- `docs/postman/app/Sanad.App.Family.CareHomes.postman_collection.json`

Current result:
- `docs/internal/work/portal-api-reconciliation/worker-prompts/e04-platform-controls.md`

## Required implementation/checklist output

- Add exactly one initial Operational state safely via migration; keep audit append-only. Concurrency conflicts must not overwrite current state.
- Verify unauthorized roles cannot read/change controls; SuperAdmin can update and read back state; audit history is stored attributably and tested. Public status exposes no internal reason, provider IDs/secrets, or infrastructure detail.
- Cover maintenance and method gates in each listed business path; explicitly test that allowed exceptions and unlisted lifecycle operations still work. Verify Card/Wallet disabled or unconfigured behavior and ApplePay exclusion.
- Check provider callback/duplicate settlement continuity without calling a real provider or applying migrations.
- Sync docs and relevant Postman examples to implemented, verified behavior. No UI files authorized.
- Run focused tests for all changed paths and strict Release solution build with zero warnings/errors. Distinguish authored from executed checks. Record every unrun check as NOT RUN with reason.
- Update the assigned task to `ready_for_review`; only Mastermind marks it
  `completed` and changes phase/release state after verification.

## Verification and delivery

- A/B: focused tests for the task's acceptance above, including actual HTTP status
  and OpenAPI behavior rather than handler-only assertions. Use fake providers and
  synthetic in-memory/SQLite fixtures; no live DB, migrations or provider sends.
- Before API-host tests, use the process-local isolation pattern in
  `docs/internal/tools/Test-SanadPortalGate.ps1`: clear integration targets, isolate
  all nine module connections, disable startup migrations/seeders and use temporary
  storage. That script runs whole suites and has no filter option; do not run it
  merely to obtain focused coverage or invent a `-Filter` parameter for it.
- Build changed code with `dotnet build Sanad.slnx -c Release --nologo /p:TreatWarningsAsErrors=true`.
  Run selected tests using `dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj -c Release --no-build --no-restore --filter "<affected test classes>"`
  in the isolated process. Record the actual
  filter/command, exit, counts, warnings and limitations. No placeholder filter
  counts as executed evidence. Rebuild only after compiled inputs change.
- C: validate changed Markdown links, parse the four listed Postman collections,
  and verify new/changed methods/routes/examples against source/OpenAPI. Compare
  actual normalized method/path sets and report missing/orphan entries. The legacy
  `docs/internal/tools/Verify-ApiContractMapping.ps1` currently prints missing/orphan
  variables without computing those sets; its exit 0 alone is not valid evidence.
  Use an explicit read-only comparison rather than expanding this phase into tool
  repairs. Run `git diff --check`. Reuse A/B build/test
  evidence if compiled inputs are unchanged; no automatic full suite or Bruno.
- Deliver one current result in this brief with task/session, changed files,
  checks, findings/blockers and next action. Evidence cells retain short accepted
  results per task. No separate report, handoff, tracker or new template file.

## Mastermind acceptance and release

Review the actual diff, focused evidence and consumer examples against the full
approved contract. Keep the phase `accepted` while release is unapproved or pending;
do not label authored migrations applied. After Owner authorization, follow the
existing publish/deploy runbooks, including target backup and migration verification.
Record exact release revision, result/receipt and unverified live-feature limits;
only then mark E04 `completed`, update the project handoff once and stop.

This brief is the lifecycle example for future Masterminds. Copy its small task
table and current-result pattern only; never copy E04's approvals, model, base,
manifest, test results or release authority into another phase.

## Kickoff after Owner dispatch direction

Read this complete brief and its scope reference in `D:\Sanad_API`. Execute
**E04-A only** on the verified base plus documented local delta, using
`opencode/deepseek-v4.1-flash-free` at high effort. Preserve unrelated edits. Update A and the current result here, return
the focused evidence/blocker, then stop for Mastermind review. Do not begin B/C,
create reports/trackers, commit/push or deploy.

## Checkpoint, stop, and final report

Current Worker session: `ses_ead905588ffeTEWjxUdplK8WPt` (E04-A accepted; E04-B handed
back `ready_for_review`; E04-C pending).
At handback/block, replace this
brief's current result with stage, session/base, changed files, check results and
next action. Preserve valid earlier-stage evidence without diary/new reports.

Return COMPLETE (ready for Mastermind review) or BLOCKED, changed-file list, commands/results/counts/warnings/skips, authored-vs-run checks, migration authored/not-applied statement, limitations, checkpoint location, exact next action. Never commit, push, publish, deploy, apply migrations, contact real providers, or edit files outside manifest.
