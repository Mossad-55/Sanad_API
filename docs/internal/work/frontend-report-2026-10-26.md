# Frontend report follow-up — backend tranche (2026-10-26)

Status: `gates_complete` — release executing 2026-10-26 under the Owner's
release order. Owner **authorizes development and release** for this slice
(2026-10-26: "complete independent tasks, then commit/push/deploy via a
separate release worker after review"). The rehearsal revision before this
release was API `2bccdb3e`
(`2bccdb3e5b59c65ba1cad91abd640bbb7fb92b81`, `Staging`, service active, 9/9
migration heads, DB reachable — read-only 2026-10-26). **Owner decisions
(accepted 2026-10-26, closed — do not reopen):** item 15 ships AS-IS (no new
numeric caps; only the raw closure-500 annotated, trimmed visit-1000 /
support-2000 documented, booking reason + room-type description + maintenance
reason unannotated/unbounded); item 13 — the 6 legacy orphan caregiver
profiles stay untouched, no deletion; item 7 — refund policy v2 stays DORMANT
(no activation, no date, no code task).

**Gate (final, complete current tree):** strict Release solution build
0 warnings / 0 errors. Full unit suite **5,737 passed / 27 skipped / 0
failed** — the run's single failure was
`RequestLengthBoundOpenApiContractTests.VisitSettingsClosureReason_...`,
root-caused to a record positional-parameter attribute targeting gap
(`[StringLength]` needs the `[property:]` modifier to reach the generated
property schema), fixed in `CareHomeVisitsController.cs`, strict rebuild 0/0,
and the focused contract group re-run green on the fresh binary
(**14/14**, actual OpenAPI metadata). Provisioner **75/75**; architecture
**1/1**. No files moved or excluded for gates.

**Task partition (2026-10-26).**
- This brief (DeepSeek `#high`) — exact money-action frontend guidance +
  consolidated 16-item sanitized send-ready answer:
  `docs/internal/work/frontend-response-2026-10-26.md`. `ready_for_review`.
- Money-source contract re-verified this pass against the **actual** routes /
  controller authorization / request DTOs plus live read-only rehearsal state
  (full ids, no ellipses): `done`. The prior "ambiguous fixture" vs "no reconcile
  eligible" contradiction is resolved in the response — booking-level reconciles
  are ineligible; the duplicate-charge reconcile **is** eligible in principle
  (claim `01a17542-3ce7-7fc6-b3dc-a322eb2a351e`, ambiguous) and still alters
  money records, so it stays out of scope.
- Item 3 (credentials) — **Owner-private transfer, pending**; no secret,
  credential path or private-handoff content may enter any document.
- Companion worker A — caregiver-picker mapped-name Npgsql fix:
  **implemented** (`CaregiverCareRecords.cs` + 4 new translation tests, 34
  existing caregiver tests pass, Release build 0 warnings); **no live endpoint
  read-back** was performed. `ready_for_review`.
- Companion worker B — notification self-preference docs: **completed**
  (`docs/admin/notifications.md`, `docs/care-homes/owner-onboarding.md`),
  including the corrected bilingual `ChatNewMessage` fact in Task C.
  `ready_for_review`.
- Companion worker C — field-lengths / existing-limits audit: **FINAL
  (Owner-accepted: no new caps, ship as-is; brief Task D and response §3.1
  frozen)**. Raw `string` length narrowing is approved but reason
  ignored/trim semantics are still being corrected; do **not** freeze the old
  attribute/`maxLength` claims yet.
- GLM `#high` tranche (FAQ 500, eligible-residents / EF translation, money
  matrix, exact-9 fixture run): `ready_for_review`; the combined-gate re-run is
  the open local check.

Owner also approved demo fixtures for the existing demo family on the
rehearsal (contract: 1 SOS + mandatory history, 1 help + mandatory history,
2 check-ins, 1 finite-past medication + 2 terminal Taken/Skipped logs; no
notification/outbox rows, no future late slots). Provisioner group/tests for
that fixture set are owned by worker `ses_e8790639dffeljbhyiip4HejjR`
(implementation first, then a serialized gate/exact-data run; the exact-data
run is now DONE — see section F). DeepSeek
worker `ses_e879efa15ffe02YvIWE7MgVKQl` owns dedicated names+ID lookup routes
(elderly/caregiver, SuperAdmin+Support, no phones); the earlier "search
proposal" in Task C is superseded by that assignment. Tranche-2 timing:
dispatch→handback ≈ 35 min against the 25-min cap (overrun: source reads for
the corrected money matrix and OpenAPI inventory; the shared test gate was
blocked by the other worker's in-flight edit and retried once only).

## Owner direction and partition

- Owner selected two River workers on 2026-10-26: GLM 5.3 Flash `#high`
  (`river/zai-org/GLM-5.3-Flash#high`, backend defects) and DeepSeek V4.1 Flash
  `#high` (care-record docs/comments items). Mastermind reviews; workers execute.
- Goal: 2h45 total, 15 min contingency. Investigation actual: ~40 min
  (20-min aim / 30-min cap exceeded — DB schema discovery on the rehearsal;
  no side effects).
- Current main `7b77aad1` (docs-only on top of deployed API `2bccdb3e`).
  Rehearsal VPS `72.62.92.144`, service `sanad-api`, `Staging`, seam enabled
  with 7 ids. Untracked debris preserved. Model: GLM 5.3 Flash #high (as
  dispatched; no switching).

## Tasks and evidence (this tranche)

### A — FAQ list 500 (GLM) — DONE (code + tests green)

- Root cause: in-memory `OrderBy(...).ThenBy(faq => faq.Id)` over
  `HelpFaqId` (readonly record struct, no `IComparable`). Ties on
  audience+display_order force id comparison →
  `InvalidOperationException/ArgumentException: At least one object must
  implement IComparable`. Rehearsal data: ties only at audience=1
  (display_order 1×2, 2×2) — exactly the failing filter set; journald
  2026-10-26 02:55/02:57 shows the exception.
- Fix: `HelpFaqId` now implements `IComparable<HelpFaqId>` (same pattern as
  `BookingId`, `src/BuildingBlocks/.../Ids/BookingId.cs:3-14`). Files:
  `src/Modules/Cms/Domain/Sanad.Modules.Cms.Domain/Help/HelpFaqId.cs`;
  regression in `tests/Sanad.UnitTests/Cms/HelpCenterTests.cs`
  (`AdminList_TiedAudienceAndDisplayOrder_OrdersDeterministicallyById`,
  `AppRead_TiedDisplayOrder_OrdersDeterministicallyById`) — both exercise the
  real handlers' in-memory sorting with tied rows over an in-memory
  `CmsDbContext` (no unrelated DB).
- Shared path `GetHelpCenterQueryHandler` has the same ordering shape and is
  fixed by the same change (no handler edits).
- 31-site audit (`.ThenBy(x => x.Id)`): 28 are EF `IQueryable` chains
  translated to SQL (ordering composed before `ToListAsync`/`Skip/Take`/paging;
  provider orders the converted column). In-memory sites: the two FAQ handlers
  (fixed) and `AdminCareHomeApplications.Map` line 176 (sorts
  `CareHomeProfileMedia` whose key is plain `Guid` — comparable). No other
  demonstrated analog. Classification by reading each call shape; not a
  blanket claim.
- Gate (superseded by tranche-3 combined gate below): strict Release build
  0 warnings/0 errors; `dotnet test ... --filter
  FullyQualifiedName~Cms.HelpCenterTests` → 23/23 passed;
  `dotnet test tests\Sanad.ArchitectureTests\... -c Release` → 1/1 passed.

### A3 — Combined gate (tranche 3, GLM exclusive gate owner; all workers stopped)

- Repairs first: added `using Sanad.Modules.CareHomes.Domain.Facilities;` to
  `CareHomeEligibleResidentsTranslationTests.cs` (CareHomeId) and the
  established `<InternalsVisibleTo Include="Sanad.UnitTests" />` item to
  `Sanad.Modules.CareHomes.Application.csproj` (same pattern as the
  Caregivers/CareHomes domain csprojs) so the test compiles against the
  internal production composer — the helper's visibility is unchanged.
  DeepSeek had held the untracked test aside for a subset run and restored
  it byte-identical; that subset run was not a fulltree pass. No files were
  moved or excluded for gates.
- `dotnet build tests\Sanad.UnitTests\Sanad.UnitTests.csproj -c Release
  /warnaserror` → **0 warnings / 0 errors** on the COMPLETE current tree
  (all workers' in-tree code included).
- `dotnet test tests\Sanad.UnitTests\Sanad.UnitTests.csproj -c Release
  --no-build` (full suite) → **5,710 passed / 27 skipped / 0 failed**
  (8m5s). Skipped rows are the guarded persisted-Postgres facts that skip
  without their env vars — NOT executed Postgres proofs and not claimed.
- `dotnet build tests\Sanad.PortalProvisionerTests\Sanad.PortalProvisionerTests.csproj
  -c Release /warnaserror` → 0 warnings/0 errors (one transient in-flight
  error on the first attempt while the ElderlyOps worker file settled;
  verified clean rerun against the freshly built binary).
- `dotnet test tests\Sanad.PortalProvisionerTests\... -c Release --no-build`
  → **75 passed / 0 failed**, including the new worker-ready
  `ElderlyOpsFacts` — confirmed separately by filter → **11/11 passed**.
- Prior narrower evidence (HelpCenter 23, caregiver 936, architecture 1)
  remains valid — their dependencies were not changed — and is now
  subsumed by the full-tree runs above.
- No provisioning was performed in that gate; the 9-row fixture execution stayed
  with its owner worker under its own authorization (now executed and verified —
  section F). Provider quota errors: none.

### B — Safe-money action matrix (GLM) — CORRECTED tranche 2 (source-traced; no endpoints called)

Provider boundary: every refund reaches
`IPaymobClient.RefundPaymentAsync(txnId, amount)` (`src/Modules/Families/
Application/.../Abstractions/Payments/IPaymobClient.cs:55`) under the
`FixtureRefundSeamPaymobClient` decorator (Families DI, `AddFamiliesInfrastructure`).
`IPaymobClient` has **no status/poll methods** — nothing polls the gateway;
reconciliation is operator-stated. Non-refund ops always pass through.
**No sandbox claim**: outcomes here are seam-scripted for the 7 armed ids on
the Staging rehearsal; they do not prove provider sandbox or production
behavior.

| Route (exact) | Role/policy | Handler / provider call | Effective txn id | Demo row — current state (rehearsal DB, read-only) → expected result |
|---|---|---|---|---|
| `POST /api/v1/admin/bookings/{bookingId}/refund` — **no body** | `CaregiversAdmin` | `AdminBookingCommands.cs:162` | FIRST SUCCEEDED `payment_transactions.PaymobTransactionId`, else booking's stored id (lines 96-101) | family demo booking `01a17542-3659-7158-9c52-83bc1325f916` (Status 8 CancelledByCaregiver, `is_refund_attempt_in_flight=false`, `is_refund_outcome_ambiguous=false`, recorded award 100 minor units, arm status "retryable by design" per the D3 keyed-repair evidence): **eligible** → seam intercepts the repaired colon-free armed id → scripted `Rejected` released as definitive refusal (no money moved, booking back to failed-refund, bounded retry possible) |
| `POST /api/v1/admin/bookings/{bookingId}/outcome` — `AdminRecordBookingOutcomeRequest(int OutcomeReason, string? Reason, int? ReasonCategory, int? ProviderLatenessMinutes, bool CustomerConsentedToLateness, bool ForceMajeure)` | `CaregiversAdmin` | `BookingOutcomeCommands.cs:334-337` → `SettleWithProviderAsync(..., paymob)` | executor uses the booking's stored capture | **NOT EXECUTABLE on the demo row**: outcome requires an ACCEPTED booking; the demo booking is CancelledByCaregiver → state guard refuses before any provider path |
| caregiver `POST .../provider-outcome` — `CaregiverRecordProviderOutcomeRequest(int OutcomeReason, ...)` | assigned caregiver | `BookingOutcomeCommands.cs:145-148` (same settlement executor) | same | same accepted-booking guard; not executable on the demo row |
| `POST /api/v1/admin/bookings/{bookingId}/refund/reconcile` — `AdminReconcileRefundAmbiguityRequest(string Evidence, bool RefundDidMove, Guid? ExpectedAttemptClaimId, string? VerifiedProviderReference)` | `CaregiversAdmin` | `BookingOutcomeCommands.cs:389-509` — **DB only, no provider call**; requires an ambiguity or in-flight marker (300-char evidence bound) | n/a | **NOT EXECUTABLE on the demo row**: no ambiguity, no in-flight attempt → "no unconfirmed or interrupted refund attempt to reconcile". Note: even DB-only, a successful reconcile changes money records — never run against a fixture that has not been proven no-money; never submit a fabricated `VerifiedProviderReference` |
| `POST /api/v1/admin/care-homes/bookings/{bookingId}/refund/retry` — no body (**frontend's `/refund/retry` is correct; my earlier table's bare `/refund` was wrong**) | `CareHomesOperationalAdmin` | `RetryCareHomeRefundHandler:239` → `CareHomeRefundProcessor.InitiateAsync:84` (`RefundPaymentAsync`); claim persisted BEFORE the call; claim gate `TryClaimRefund` `CareHomeBooking.cs:505-523` | `booking.PaymobTransactionId` (columns match the armed numerics), amount `booking.RefundAmount` | demo `01a17542-2fc6-75bb-a082-ccb0b4c143f0`: Status 7 RefundPending, PaymentStatus 2, RefundStatus 1 Pending, no claim, not ambiguous, amount 4025.00 → **eligible**; demo `01a17542-308a-73a5-b5a1-d604ffa57ad8`: same but RefundStatus 3 Failed → **eligible**. Either call is seam-intercepted (armed numerics) → scripted `Rejected` → `Paymob.RefundRejected` surfaced directly as 502, row lands RefundStatus Failed, still retryable |
| `POST /api/v1/admin/care-homes/bookings/{bookingId}/refund/record-completed` — `RecordCompletedCareHomeRefundRequest(string Reference, string Reason)` | `CareHomesOperationalAdmin` | `RecordCareHomeRefundCompletedHandler:278` — **DB only** | n/a | manual record; idempotent; no provider call. Only meaningful with a verified real-world refund |
| `POST /api/v1/admin/care-homes/bookings/{bookingId}/refund/reconcile` — `ReconcileCareHomeRefundRequest(Guid ExpectedAttemptClaimId, bool RefundDidMove, string? VerifiedProviderReference, string Evidence)` | `CareHomesOperationalAdmin` | `ReconcileCareHomeRefundHandler:395` — **DB only**; binds `expectedAttemptClaimId` (CAS; empty → 400); inconclusive attempts without a recorded provider outcome are refused (`refundReconciliationAttemptInconclusive`) | n/a | **NOT EXECUTABLE on either demo row**: `is_refund_outcome_ambiguous=false` on both → refused before any mutation. A truthful reconcile needs a known-no-money ambiguity fixture and `refundDidMove=false` with real evidence — not a pretend provider reference |
| `POST /api/v1/admin/finance/subscription-refunds/duplicate-charge/{claimId}/execute` | SuperAdmin (execute/reconcile narrowed beyond the `RefundWorkflowAdmin` list) | `DuplicateChargeClaimStore.cs:364,399` → `SubscriptionRefundGateway.RefundAsync:82` → `RefundPaymentAsync` | claim's `payment_attempt_id` capture id | approved claim `01a17542-3bc9-7033-a39a-1233d0ffa7af` (state 3, execution_state 2) → seam intercepts `...-approved-capture` → scripted `Rejected` → claim execution failed with `Paymob.RefundRejected` (retryable, attempt-counted) |
| `POST /api/v1/admin/finance/subscription-refunds/duplicate-charge/{claimId}/reconcile` — `ReconcileDuplicateChargeClaimRequest(bool RefundDidMove, ..., string? VerifiedProviderRefundId)` | SuperAdmin | `DuplicateChargeClaimStore` — **DB only**; a moved verdict requires the provider's own refund id | n/a | ambiguous claim `01a17542-3ce7-7fc6-b3dc-a322eb2a351e` (execution_state 4, gateway_error `Paymob.RefundOutcomeUnknown`) is the reconcilable target; no provider call. Same truth rule: `refundDidMove` must state a verified fact |

Non-allowlisted reality: pre-existing claim `01a1731a-433e-7f99-9e08-76b16e0e1f4f`
(state 1) is NOT allowlisted — if approved and executed it would reach the real
Paymob client. Seam enabled ≠ blanket safety: exactly the 7 configured ids are
intercepted; everything else passes through. **This matrix is investigation
evidence, not authorization.** Eligibility claims above are current-state
reads; they go stale after any action.

**Money-eligibility reconciliation (read-only, 2026-10-26) — supersedes every
contradictory statement elsewhere in this brief.** Verified from live persisted
rows and the source handlers:

- **Booking-level reconciles are NOT eligible on any demo row.**
  `POST /api/v1/admin/bookings/{bookingId}/refund/reconcile`: family booking
  `01a17542-3659-7158-9c52-83bc1325f916` has `is_refund_outcome_ambiguous=false`
  and `is_refund_attempt_in_flight=false`. Care-home
  `POST /api/v1/admin/care-homes/bookings/{bookingId}/refund/reconcile`: both
  rows are ineligible (`01a17542-2fc6-75bb-a082-ccb0b4c143f0` and
  `01a17542-308a-73a5-b5a1-d604ffa57ad8` have `is_refund_outcome_ambiguous=false`).
  Both are refused by the state guard before any mutation.
- **The duplicate-charge reconcile IS eligible in principle (DB-only).**
  `POST /api/v1/admin/finance/subscription-refunds/duplicate-charge/{claimId}/reconcile`:
  claim `01a17542-3ce7-7fc6-b3dc-a322eb2a351e` is ambiguous (state 3,
  execution_state 4, recorded `Paymob.RefundOutcomeUnknown`, execution attempt
  `01a17542-3cf9-7377-a805-bee4b9ad6ded`). It is still **DB-only and still
  alters money records**, so it stays out of scope for any frontend test and is
  an owner-authorized operator action only. Therefore "all reconciles are
  ineligible" anywhere in this brief is **wrong**; it holds only for the
  booking-level surfaces.

### C — Notification producers, text contract, lookup inventory (GLM) — DONE (bounded)

- Preference category → fanout key: `PushFanoutPolicy.cs:7-28`
  (BookingUpdates, SystemNotifications, CheckInAlerts, MedicationReminders,
  HelpRequestAlerts, CommunityNotifications, ChatMessages). User preference
  booleans: `NotificationPreferences.cs` (10 fields incl. FamilyActivityAlerts,
  NewOrders, MessagesFromFamilies legacy).
- Producers (proven):
  - `OnboardingStatus` — `CareHomeReviewNotificationProcessor.cs:151`
    (category `CareHomes`; recipients: facility owner users +, when
    `notifyAdmins`, ALL active SuperAdmin+SupportAdmin users, lines 58-91).
    Copy catalog `Copy(action)` lines 110-123; per-recipient localization by
    `identity.users.ui_language` captured at creation; the same event also
    writes an `email_outbox` row (bilingual template, `WriteRecipientAsync`
    lines 125-145). Push mapping key: `SystemNotifications`.
  - `BookingDecisionRequired` — `CareHomeBookingNotificationProcessor.cs:152`
    ("Booking needs your decision" / "حجز بانتظار قرارك"); owner-decision
    events target the facility owner; fanout key `BookingUpdates`.
  - `NewMessage` — `CreateChatMessageNotificationCommand.cs:67-73` →
    `NotificationTextCatalog.ChatNewMessage(UiLanguage language)`
    (`NotificationTextCatalog.cs:50-52`) — **bilingual, Arabic default**
    (the English pair is returned only for an explicit English recipient), so
    the earlier "canonical English baseline / English-only" wording in this
    bullet is **retracted**; fanout key `ChatMessages`
    (`PushFanoutPolicy.cs:23-24`).
- Text contract (qualified per producer; **ChatNewMessage claim corrected
  2026-10-26**): the two CareHomes processors render the per-recipient-localized
  copy at creation using `identity.users.ui_language` (review copy catalog
  `CareHomeReviewNotificationProcessor.Copy` lines 110-123; booking copy in
  `CareHomeBookingNotificationProcessor`). **`NotificationTextCatalog.ChatNewMessage`
  IS bilingual** — `ChatNewMessage(UiLanguage language)` returns Arabic by
  default and the English pair only for an explicit English recipient
  (`NotificationTextCatalog.cs:50-52`; catalog rule: every entry exists in
  Arabic and English, and an absent/unrecognized language falls back to Arabic).
  The earlier GLM "NewMessage is English-only / fixed English pair" statement is
  **FALSE and retracted**. Creation-time language is **not retroactive**: a row
  keeps the language it was written with, so a UI-language change does not
  re-render existing rows — quote the row's stored timestamp and the account's
  stored `ui_language` at that time. No new localization policy is needed. Push
  data contract in `PushFanoutPolicy.BuildData`. No claim is made here that
  specific notification rows for `r2.carehome.owner` (`01a1731a-...8c61`) exist
  in the rehearsal DB — that row check was not performed; the fixture sources
  below produce them when the flows run.
- Recipient category matrix (Applicability fanout): the fanout consults 7
  effective keys via `PushFanoutPolicy` — BookingUpdates (CareHomes
  Booking* types, BookingUpdates, CaregiverBookings categories),
  SystemNotifications (CareHomes fallback), CheckInAlerts,
  MedicationReminders, HelpRequestAlerts, CommunityNotifications (Post
  Published/Rejected only), ChatMessages (NewMessage, plus legacy
  MessagesFromFamilies rows via the compat rule). Useful legacy booleans in
  `NotificationPreferences` with NO fanout mapping: `FamilyActivityAlerts`,
  `NewOrders` (2 unused); `MessagesFromFamilies` is the compat-mapped legacy
  boolean. Applicability vs settable are different questions: the per-user
  booleans live on every `User`, and the self-service surface is now
  **verified** — `GET`/`PUT /api/v1/account/notification-preferences`
  (`AccountController.cs:207-242`) require only `NormalAccess`, available to
  every admin role and to a CareHomeOwner for its own row, and expose **8
  required + 2 nullable compatibility fields** (documented in
  `docs/admin/notifications.md` and `docs/care-homes/owner-onboarding.md`).
  Suppression semantics (worker-verified): admin / care-home-owner care-home and
  chat preferences suppress **push only** — the in-app inbox row remains and
  emails are unaffected; family / caregiver wellness preferences are filtered
  **before recipient creation**, so they can suppress the in-app notification
  too; Support wellness is push-only.
- Fixture actions without new events: demo booking-notification sources
  (a pending owner-decision demo booking produces `BookingDecisionRequired`
  to the facility owner when the processor runs) and demo corpus
  `OnboardingStatus` source rows re-materialize through the review
  processor. **Do not create booking/onboarding/chat events for display:
  OnboardingStatus also writes email_outbox rows; NewMessage fans out push.**
  Existing demo chat messages exist in the corpus; whether r2's owner has
  unread notification rows is unverified.
- Lookup inventory (existing): `GET /api/v1/admin/users?search=` already
  searches Arabic name + English name + phone and returns user ids
  (`AdminUserQueries.cs:168-178`) but returns user/account ids, not
  caregiverId/elderlyId/dependentId; `AdminCaregiversController` list is
  page/status/type only (no name/phone search); `AdminElderlyCheckInsController`
  filters by elderlyId/date/answer only; `AdminCareRecordsController` filters
  by dependentId + content `search` (no dependent name search; the caregiver
  care-record surface already searches dependents by name but only the
  caregiver's own, `CaregiverCareRecords.cs:85`).
- Lookup proposal status: the earlier "minimal reusable endpoint proposal"
  (search params on the three admin surfaces) is **SUPERSEDED** by the
  Owner's assignment — DeepSeek `ses_e879efa15ffe02YvIWE7MgVKQl` owns
  dedicated names+ID lookup routes (elderly/caregiver, SuperAdmin+Support,
  no phone exposure). The inventory above stays as capability evidence only.

### D — Field length constraints (GLM) — FINAL (correction landed, gate-passed)

- OpenAPI generation (proven): the API uses Microsoft.AspNetCore.OpenApi
  (`services.AddOpenApi` in `src/API/Sanad.API/DependencyInjection.cs`) with
  exactly three custom transformers — `PortalApiSchemaTransformer`
  (enum shape), `PortalNullableSchemaUnionTransformer`,
  `PortalRequiredQueryTransformer`. None emit maxLength, and OpenAPI request
  schemas are generated from the request record types — **EF model metadata
  does NOT automatically flow into request schemas** (my earlier paragraph
  implying it does was unproven and is retracted). Column max lengths matter
  at the DB boundary only.
- Existing bounded (proof): FAQ Q/A 300/3000 (`HelpFaq.cs:13-14` + validators
  + column maxLength); family-refund reconcile evidence 300
  (`BookingOutcomeCommands.cs:465-475`); many validators across lookups,
  care records, payouts carry `MaximumLength` (grep evidence).
- The OWNER-ASKED five surfaces, exact request fields (all located; none
  carries a length attribute or FluentValidation length rule in my sweep):
  - Booking **reject/cancel** (care-home owner decisions): owner decision /
    cancellation request records in the care-home booking surface (`Reason`
    string). My earlier substitution of the families `outcome` note did not
    answer reject/cancel — corrected; the exact families admin
    cancel/reject reason fields live in the admin booking commands and are
    `string`-typed unbounded in the sweep.
  - Visit reasons: `CareHomeVisitsController.cs:17-20` —
    `CareHomeVisitDecisionRequest(bool Approve, string? Reason)`,
    `CareHomeVisitCancellationRequest(string? Reason)`,
    `CareHomeVisitClosureRequest(DateOnly Date, string? Reason)`.
  - Room-type description: `CareHomeInventoryController.cs:13` —
    `RoomTypeRequest(..., string? ArabicDescription, string? EnglishDescription, ...)`.
  - Maintenance/closure: `CareHomeInventoryController.cs:17-18` —
    `MaintenanceRequest(..., string? Reason)`,
    `MaintenanceAmendRequest(..., string? Reason)` (closure handled via
    maintenance/closure target records).
  - Support message: `src/API/Sanad.API/Controllers/Requests/SupportTicketRequests.cs:6`
    — `ReplyToSupportTicketRequest(string Body)` (single unbounded `Body`).
- Consequence (**FROZEN final, 2026-10-26**): the API request DTOs carried no
  length attributes and OpenAPI request schemas emitted no `maxLength`, so the
  API layer was genuinely unbounded; the DB text column is the demonstrated
  backstop. The field-lengths correction is now landed and final: the
  **per-field domain guards** stand — visit reject/cancel reason 1000
  **trimmed**, visit closure reason 500 **raw** (measured before trimming),
  support reply body 2000 **trimmed**, booking reject/cancel reason non-blank
  **unbounded**, room-type description DB column 4000, maintenance reason DB
  column 1000 — and the schema policy is final: **only the raw-measured
  closure reason publishes a request-schema annotation
  (`[StringLength(500)]` on `CareHomeVisitClosureRequest.Reason`)**; the
  trimmed 1000/2000 caps stay documented in controller/request comments, NOT
  annotated, because a raw `[StringLength]` would falsely reject previously
  accepted whitespace-padded values and approve-ignored decision reasons.
  Verified by `RequestLengthBoundOpenApiContractTests` against actual OpenAPI
  metadata (green 14/14 after the `[property:]` targeting fix) and
  `SupportTicketReplyTrimBoundaryTests`/trimmed-cap tests. **Owner decision
  (accepted 2026-10-26): no new numeric caps this phase — ship as-is.**
  Comment-only change on `SupportTicketRequests.cs`.

### E — CareHomeBooking EF translation 500 (GLM) — FIXED tranche 2 (focused)

- journald 2026-10-26 09:38:32: `The LINQ expression 'DbSet<CareHomeBooking>()...
  .OrderBy(c => c.ElderlyArabicName).ThenBy(c => c.ElderlyEnglishName)
  .ThenBy(c => c.ElderlyId.Value)' could not be translated` at
  `GetEligibleCareHomeVisitResidentsQueryHandler.Handle` — route
  `GET /api/v1/family/care-home-visits/eligible-residents?familyId&facilityId&startsAt`
  (`CareHomeVisitsController.cs:112-125`, `FamilyAccess` policy). Translation
  is query-shape dependent, data-independent: every valid call 500s.
  Present in deployed `2bccdb3e` and identical in HEAD `7b77aad1` (docs-only
  on top). Not conflated with the residents surface the frontend reports
  working — that is a different endpoint.
- Fix (this tranche, minimal): extracted the exact production composition
  into `GetEligibleCareHomeVisitResidentsQueryHandler.EligibleResidentRows`
  (`CareHomeVisitHandlers.cs`) and ordered the resident key by the mapped
  typed id `.ThenBy(x => x.ElderlyId)` instead of unwrapped
  `.ElderlyId.Value` (identical SQL column ordering); business filters,
  auth, materialization shape unchanged. The GroupBy/First per-resident
  selection is unchanged.
- Dedicated regression (new) `tests/Sanad.UnitTests/CareHomes/
  CareHomeEligibleResidentsTranslationTests.cs`: (1) compiles the exact
  production composition through `ToQueryString()` on Npgsql options
  WITHOUT any connection (the existing `CareHomeBookingNotificationTranslationTests`
  pattern) and asserts WHERE/ORDER BY/ElderlyId in SQL and no `.Value`
  projection; (2) pins the failure mode by asserting the OLD
  `.ThenBy(x => x.ElderlyId.Value)` shape throws `InvalidOperationException`
  — so the regression cannot silently pass on an InMemory provider.
- Gate: CareHomes.Application strict build 0 warnings/0 errors. Dedicated
  translation tests: authored, then (tranche 3) compiled and executed green
  inside the full-tree run recorded in section A3 — the earlier shared-gate
  blockage (the other worker's CS0535 fake) and the using/InternalsVisibleTo
  repairs are superseded by A3. No migration/dependency added beyond the
  csproj InternalsVisibleTo item.

### F — elderly-ops 9-row fixture execution (GLM) — DONE (Owner-approved, exact-9, zero-delta replay)

- **Authorization.** Owner answered the parent's ask "adding missing SOS,
  help-request, check-in and medication-log fixtures to existing demo family on
  rehearsal VPS? scope excludes notification/outbox creation, preserves existing
  records" with **"Yes I approve but don't add a lot of data just few"** (2026-10-26).
  Local HEAD `7b77aad1` (frozen provisioner sources + `ElderlyOpsFacts`, gate
  0/0 warnings, 75/75 + 11/11). No code was mutated, nothing committed/pushed/
  deployed, service/env/revision untouched.
- **Target identity verified before writes.** Fresh linux publish of the
  provisioner built from HEAD `7b77aad1` (NOT the stale 03:14 artifact, NOT the
  permanent `/root/sanad-demo-provisioner`), staged privately at
  `/root/elderly-ops-exec-2026-10-26/tool/` (zip sha256 `cce847a89d4ba5620f04…`).
  `--mode verify` exit 0 on the VPS host side (SSH `root@72.62.92.144`, BatchMode):
  reached `::1/128:5433` = `SanadDb`, cluster system_identifier MATCH (19 chars,
  compared in memory against the D3-pinned `/root/demo-secrets/credentials.txt`
  line, never printed), deployed revision `2bccdb3e5b59…92b81` read from
  `GET /api/v1/deployment/info` (not assumed), all nine migration heads equal to
  the retained pinned evidence `/root/demo-repair/v2-verify.json`, 0 pending.
- **Plan.** Fresh plan shows ONLY `elderly-ops` (marker `demo:demo1:elderly-ops`),
  runTag `demo1`, explicit instant `2026-10-25T12:00:00Z`, 0 refused groups.
- **Pre snapshots** (read-only, `/root/elderly-ops-exec-2026-10-26/`, mode 700):
  exact counts of all 177 tables, full row dumps of the 10 relevant
  `families.*` tables, notification + email_outbox id sets, file inventory of
  `/var/sanad/files` + `/var/sanad/files-private` (76 files).
- **Provision run 1** exit 0 — exactly **9 rows** in exactly 7 tables
  (`elderly_sos +1`, `elderly_sos_history +1`, `elderly_help_requests +1`,
  `elderly_help_request_history +1`, `elderly_check_ins +2`, `medications +1`,
  `medication_dose_logs +2`); no other table of 177 changed; no file added.
- **Replay run 2** exit 0 — **0 changed tables, 0 changed rows** post1→post2;
  idempotent re-read of the same 9 records (reused `state` none — all identical
  ids in both manifests).
- **Existing rows byte-stable** pre-existing rows re-verified identical in
  post snapshots; the one pre-existing medication (بانادول on a different,
  pre-existing elderly `01a0a5c8-…`) untouched.
- **Zero notification/email/push proof:** `notifications.notifications` 91 rows
  and `notifications.email_outbox` 77 rows with identical id-set hashes across
  pre1/post1/post2 — no new notification, outbox or push row was created by
  either run.
- **New fixture ids (all under demo1).** Owner `demo1.family.owner@test.sanad.local`
  = identity user `01a17542-12d2-7bd4-bd85-59d13a5bd98f`; family
  `01a17542-1983-7ba5-9b29-30b5980ddad7` ("عيلة سند التجريبية الأولى
  [demo:demo1]") owns exactly one elderly: `01a17542-1a17-7c54-9e18-a6f52372813e`
  ("Demo Grandfather [demo1:dependent]", elderly.1 phone, tz Africa/Cairo).
  New rows: SOS `01a17896-160c-7241-b9ff-4b6008820afd` (**Open**, consent false,
  no location) + Created history `01a17896-160d-7305-85f5-b5f4aaecffd2`;
  help request `01a17896-1712-7241-aa56-ca75dfb17599` (**Pending**,
  self/need/water/now, stored bilingual labels) + Created history
  `01a17896-1713-7a34-9442-cce8edb37f3f`; check-ins
  `80ca5cb2-8855-4faf-890a-3d74616fcde4` (**2026-10-19, no**, 10:00 local) and
  `459e67c3-fe0c-418e-bcab-6c377e450720` (**2026-10-20, yes**, 09:30 local;
  answered_by = the elderly's own identity per the real submission path);
  medication `01a17896-17d8-7f67-8a73-0e3e5cb07cc1` name "SANAD DEMO مسكن تجريبي
  [demo:demo1:elderly-ops]" (**Completed**, 2026-09-06..2026-09-26, 08:00 daily
  dose — course over ⇒ excluded from the due/lateness schedule); terminal dose
  logs `01a17896-1877-7782-ad14-905c7737af05` (2026-09-11 **Taken**) and
  `01a17896-18b3-738f-aa8d-b8d20a2c39f1` (2026-09-12 **Skipped**).
  **Frontend filters:** elderlyId `01a17542-1a17-7c54-9e18-a6f52372813e`,
  check-in dates 2026-10-19 (false) / 2026-10-20 (true), SOS list shows the one
  Open row, help-requests list the one Pending row, medications list the one
  Completed course whose dose log is split Taken/Skipped.
- **Evidence / exact commands** retained at
  `/root/elderly-ops-exec-2026-10-26/` (plan.out, verify.json + .err,
  provision-run1/run2.json + .err, both manifests, counts-*/rows-*/ids-*/files-*
  snapshots, compare diffs). No auth/provider/login calls; API readback limited
  to the anonymous `GET /api/v1/deployment/info` identity check.
- **Limitations.** Executed host-side via SSH psql peer access
  (`su postgres`) for snapshots; the tool connected through its own connection
  string (private file, never printed). No live-API behavior was exercised
  beyond the deployment-info identity read; the deployed revision is unchanged.
  Concurrent operator activity is distinguished by actual ids only (id-set
  hashes show none occurred during the run window).

## Open items for parent

1. ~~Run the new translation tests~~ DONE — full-tree gates in A3: unit
   project 5,710/0/27 skipped, provisioner 75/75 incl. ElderlyOpsFacts 11/11.
   Gate ownership returns to parent for the integrated lookup+provisioner
   stage (the 9-row fixture execution is now DONE too — section F).
2. Task E fix enters review together with the FAQ fix (both are read-path
   defects in this report's reconciliation).
3. ~~Product decision still needed for Task D~~ RESOLVED by Owner decision
   (accepted 2026-10-26): **no new numeric caps this phase — ship as-is**; only
   the raw-measured closure reason publishes `[StringLength(500)]`; trimmed
   caps stay documented (see Task D FINAL and response §3.1).
4. ~~Preference self-service authorization check per account type~~ RESOLVED —
   verified `GET`/`PUT /api/v1/account/notification-preferences` under
   `NormalAccess` for every admin role and for a CareHomeOwner's own row;
   applicability vs settable remain distinguished, and the suppression
   semantics are recorded in Task C.
5. The money matrix remains investigation evidence; no frontend execution
   authorization implied. Non-allowlisted claims (e.g. `01a1731a-433e-...`)
   must stay excluded. Any reconcile must use a truthful known-no-money
   fixture and `refundDidMove=false`; no fabricated provider references;
   DB-only reconciles still change money records.

## Rules honored

No commit/push/deploy; no provider calls; no restarts/config changes/deletes; no
credentials printed; unrelated untracked files preserved. DeepSeek-owned
care-record docs/comments items untouched. DB writes on the rehearsal occurred
only in the Owner-approved, exact-9 `elderly-ops` fixture run (section F); all
other rehearsal access was read-only.
