# Frontend landing follow-up — 2026-10-27 (ACTIVE BRIEF)

Phase state: `approved` (preparation complete; implementation NOT started — start
only on parent/Mastermind direction). Release state: `authorized` per the
approved plan — commit, push, publish and deploy to the **same Staging rehearsal
(72.62.92.144)** only after all green gates below.

## Approval and scope

- Owner approved the follow-up scope in conversation on 2026-10-27.
- Scope authority: the approved plan/response draft (approved working copy at
  `C:\Users\mosad\.opencode\plan\frontend-landing-response-draft-2026-10-27.md`);
  sections 1.1–1.4 are the backend tasks here. Frontend/mobile actions (§2–3)
  and deferred infrastructure (§4) are out of backend scope.
- Deliverable to the frontend team after deployment: an accessible updated
  money-action matrix and the English response based on that draft (§5 delivery
  record). Do not copy private host credential details or stale deployed-SHA
  preconditions into it.

## Base pin

- Worktree HEAD: `812493d92e247cda5614dbcd82b7bc746278ca54` (short `812493d9`).
- Deployed rehearsal revision (last independently observed): `47329bf5e9bc22a0ba8d01a8b7d145830dbb924e`
  (receipt/docs commit `812493d9`) from the verified mobile-integration release —
  NOT `e664592e`; a stale private-handoff line must not override the current
  verified release. Verify `GET /api/v1/deployment/info` read-only only if
  needed and record what is actually observed.
- Preserved local delta (unstaged; never stage): private handoff edit
  `docs/internal/Mastermind_Handoff.md` (+20 private release record), untracked
  `docs/internal/Mastermind_Handoff_Historical_2026-10-23.md`, and debris
  (`.opencode-audit-tmp/`, `workflow-audit/`, `buildout*.txt`, `testout*.txt`,
  `testout*_exit.txt`, `migout.txt`, `e07-library-smoke.ps1`).
- Migrations expected: none. If a task proves one is required, stop for Owner
  review first.
- Read first: `AGENTS.md`, `docs/internal/governance/AGENTS.md`,
  private `docs/internal/Mastermind_Handoff.md` (top = current next action),
  approved draft above. Inspect `git status`/`diff` before editing.

## Actual state vs target (source-traced at `812493d9`)

| Route | Current policy | Current roles | Target roles | Change |
|---|---|---|---|---|
| `GET /api/v1/admin/caregivers/lookup` (`AdminCaregiverLookupController.cs:20`) | `CaregiverReviewAdmin` (`DependencyInjection.cs:406-420`) | SuperAdmin + SupportAdmin | SuperAdmin + FinanceAdmin | **policy change**: remove SupportAdmin, add FinanceAdmin |
| `GET /api/v1/admin/elderly/lookup` (`AdminElderlyLookupController.cs:19`) | `OperationalReadAdmin` (`DependencyInjection.cs:440-446`) | SuperAdmin + SupportAdmin | SuperAdmin + SupportAdmin | **none** — verify only |
| ContentAdmin on either lookup | not in either policy | 403 | not allowed | **none** — verify only |

**Critical implementation constraint:** `CaregiverReviewAdmin` is shared with
`AdminCaregiversController.cs` and `AdminCaregiverRatingsController.cs`, and
`OperationalReadAdmin` is shared with families/care-records/subscriptions/
feedback/feedback-ratings controllers. Never edit the shared policies; the
caregiver change must be a new lookup-scoped policy (or equivalent
route-scoped requirement) touching only the lookup route. Prior SupportAdmin
access on the caregiver lookup is removed explicitly and stated in docs.

## Tasks

This brief is edited only by the GLM prep/Task-A worker. Other workers return
handbacks to Mastermind/parent; their results are recorded here by the brief
owner. Workers never edit this brief.

| Task | Owner | Scope (exact) | Acceptance | Status |
|---|---|---|---|---|
| A1 lookup role matrix | River GLM 5.3 Flash `#high` (this session) | New lookup-scoped SuperAdmin+FinanceAdmin policy for the caregiver lookup (SupportAdmin explicitly removed); wire only `AdminCaregiverLookupController`; elderly/Content behavior verified unchanged; focused policy/role tests (extend `tests/Sanad.UnitTests/API/AdminNameIdLookupContractTests.cs` and the house authorization-test pattern); docs `docs/admin/caregivers-review.md` (incl. verifying the line-3 phrase "ContentAdmin … plus caregiver lookups" — correct if drifted), `docs/admin/families.md` (elderly section), `docs/admin/admin-overview.md`; minimal API-contract corrections and contact idempotency retry-doc wording (`docs/app/public/partner-contact.md`, `docs/app/public/landing.md` if drift proven) | Strict build 0 warnings/0 errors; lookup role tests green and state the exact matrix; shared-review-surface access unchanged (existing tests green); docs match verified behavior | `ready_for_review` (authored; gates deferred until BOTH source workers frozen — see A delivery) |
| A2 doc typo | River GLM 5.3 Flash `#high` (this session) | `docs/app/public/site-settings.md:12` `**Success:** `200**` typo → `200`; verify `/partner-contact` (public landing form) vs `/support/contact` (separate authenticated surface) wording | Docs-only; matches source routes | `ready_for_review` (delivered with A1 — see A delivery) |
| B1 notification language | River DeepSeek V4.1 Flash `#high` (separate worker) | Verify current notification producers: readable text, no raw routing IDs in display copy, language from recipient's saved account language; bounded fixes/tests/docs only where verification proves a current defect. NO historical inbox-row rewrites; NO live events by default (inspect side effects — email/push/booking state — before any event; isolated tests preferred) | Evidence distinguishes source/authored tests vs real runtime; no historical rows touched; defects fixed only with demonstrated proof | `completed by worker handback; accepted` — verification found NO source defect: current producers resolve the recipient's saved account language with a defined fallback and keep ids off the human copy; two new pins (`ReadableNotificationTests` onboarding producer, `CareHomeBookingNotificationTests` English-owner decision) + existing chat pins green in the focused gate; no historical rows touched, no live events fired |
| B2 money matrix + Paymob test prep | River DeepSeek V4.1 Flash `#high` (separate worker) | Prepare an accessible updated money-action matrix (based on `docs/internal/work/frontend-report-2026-10-26.md` §B, current-state verified; no private credentials, no stale DEPLOYED-SHA preconditions) + English response built from the approved draft; inspect/prepare minimal actual-Paymob test-refund verification: max TWO test records, reuse existing rows first, no new framework/account seeds. Runtime provider actions SERIAL only after parent review of the exact safe protocol AND source-fix freeze; verify effective test config first; permissible testing account only; secrets never printed; no broad unseamed refunds | Matrix current and accessible; provider steps either not run (prep) or run under the exact reviewed protocol with full sanitized evidence | `executed` (2026-10-27) — see "B2 result" below; Owner-authorized real-flow capture + refund completed, evidence recorded. Earlier prep: `in_progress` (worker handed evidence; NOT executed, NOT closed). Worker first proposed executing refunds on existing captures `560118491`/`560726538` + a duplicate-charge claim/approve "duplicate verification" — **rejected in review as unverified** (a valid single sale is never a duplicate without proven duplication). Corrected inventory: `care_homes.bookings` = 26 rows, one possible REAL capture txn `556970084` on booking `01a1559c-10e3-76b1-ba13-7753b1b7de77` (11,500, checked out 2026-10-19) — **not refund-eligible** via existing APIs; other care-home money fixtures synthetic seam; the two existing real subscription captures are valid single sales, not duplicates. Therefore **no existing eligible provider-refund capture is evidenced**. Proposed next stage (under parent review, NOT authorized for execution): ONE new funded TEST care-home booking through the real flow — demo family `01a17542-1983-7ba5-9b29-30b5980ddad7`, owner `01a17542-12d2-7bd4-bd85-59d13a5bd98f`, facility `01a17542-29e5-7fdd-a55f-d2465be8ad63` (room type `01a17542-2c5c-729d-a211-0f3c238db413`), existing facility owner `01a17542-16ee-70f5-9305-7f2d174ab28c`; re-check approval/doc validity/capacity/effective TEST provider config first; use the CURRENT checkout request DTO (not the worker's partial example); flow = family checkout → payment intent → real test hosted checkout 3DS → verified callback PaidAwaitingDecision → owner reject before check-in → legitimate full refund; no fabricated captured rows, no manual paid state, no duplicate claims; count = max TWO new designated business records (test bookings; existing family/facility not counted), required normal payment/audit dependent rows explicitly accounted; no extra accounts. Hosted 3DS may need Owner interaction — no automatic-completion promise, no fabricated evidence. Execution resumes only after this release (serialized) on parent review |
| C integration/release | separate worker, after A+B review | Strict 0/0 build; focused regressions on actual current contracts; docs parse/link/secret-scan check; exact staging excluding private/debris (never stage `docs/internal/Mastermind_Handoff.md` delta, `Mastermind_Handoff_Historical_2026-10-23.md`, debris files); commit+push; publish+deploy the same Staging `72.62.92.144` preserving seam/env/unit/storage; verify 9/9 heads (0 pending — none expected), manifest, new policy tests, smoke | Release verified and recorded; receipt; phase `accepted`→`completed` only by Mastermind after verification | `pending` |

Dependencies: C after A and B review. B2 provider runtime after parent-reviewed
protocol; otherwise A and B owners are independent (keep overlapping files and
runtime serialized).

## Gates

- Every `dotnet` build/test gate: successful exit, zero warnings.
- Integration: strict Release 0/0; focused regression set for the changed
  policy/behavior; full suite only if cross-cutting risk appears.
- Docs-only subtasks: docs/script validation only, no product build.
- Never invent results; distinguish authored vs executed checks and
  source-verified vs live-runtime evidence in every report.

## Release protocol (Task C, per approved plan)

1. Green gates above + Mastermind review of the actual diff.
2. Exact-stage: changed code/tests/public docs + this brief (under
   `docs/internal/work/`); NEVER the private handoff delta, the historical
   handoff, or debris.
3. Commit, push `origin/main`, publish, deploy the same Staging rehearsal
   (72.62.92.144, unit `sanad-api`, `Staging`, DB `SanadDb`), preserving the
   FixtureRefundSeam block, `/etc/sanad/sanad.env`, unit file and unit storage.
4. Verify: live revision match, 9/9 migration heads 0 pending/0 applied
   (no migrations expected), manifest, new policy tests green, anonymous smoke
   `GET /api/v1/lookups/services` 200, config fingerprints unchanged.
5. Record release revision + verification once in this brief and update the
   private handoff current-active/next-action minimally. Send the accessible
   money-action matrix + English response to the frontend team only after
   verified deployed outcomes.

## Safety

- Never expose credentials, stage private handoffs or debris, or print secrets.
- No infrastructure changes (CORS, domains, proxy, deployment topology) — deferred.
- No new caps/content/account creation/legal authoring. No historical rewrites.
- No real charges/refunds outside the exact approved protocol; seam ≠ blanket
  sandbox safety; the rehearsal stays `Staging` with the seam intact.
- Contract approval ≠ release approval beyond the plan-scope authorization
  recorded above; report and stop at each handback.
- Model: Task A / orchestration River GLM 5.3 Flash `#high`; Task B River
  DeepSeek V4.1 Flash `#high`. No model switch; quota errors reported, not
  auto-switched.

## A delivery (Task A/A2 handback, 2026-10-27)

Authored at worktree HEAD `812493d9`; **no build, no tests run, no commit/push,
no provider/auth/DB/env action** — dotnet gates are deferred until BOTH source
workers are frozen, per parent direction.

- **Code (4 files):** `AuthorizationPolicies.cs` adds `CaregiverLookupAdmin`
  (Normal + SuperAdmin/FinanceAdmin, doc-commented); `DependencyInjection.cs`
  registers it beside — never touching — the shared `CaregiverReviewAdmin`;
  `AdminCaregiverLookupController.cs` switches its attribute to
  `CaregiverLookupAdmin` and its doc comment (SupportAdmin removal stated);
  `AdminCaregiverLookups.cs` query doc comment updated. No handler/runtime
  behavior change; elderly lookup code untouched (target already matched).
- **Tests (authored, NOT run):**
  - `AdminNameIdLookupContractTests.cs` — caregiver policy fact renamed to
    `Caregiver_lookup_uses_the_dedicated_caregiver_lookup_policy` and pinned to
    `CaregiverLookupAdmin`; new
    `Caregiver_review_surfaces_keep_the_shared_caregiver_review_policy`
    (AdminCaregiversController + AdminCaregiverRatingsController stay on
    `CaregiverReviewAdmin`); existing route/payload/contract facts unchanged.
  - NEW `AdminNameIdLookupRoleMatrixTests.cs` — real role matrix over the
    app's **registered** policies via the isolated `PortalApiMetadataHost`'s
    real `IAuthorizationPolicyProvider` + `IAuthorizationService`: caregiver
    lookup admits Normal SuperAdmin/FinanceAdmin and denies
    SupportAdmin (the explicit removal), ContentAdmin, Family and
    restricted tokens; elderly (OperationalReadAdmin) admits
    SuperAdmin/SupportAdmin and denies ContentAdmin/FinanceAdmin; shared
    review policy stays SuperAdmin/SupportAdmin.
  - NEW `AdminNameIdLookupApiAuthorizationTests.cs` — real HTTP through the
    isolated host: caregiver lookup 401 anonymous, 403 Family/ContentAdmin/
    SupportAdmin; elderly lookup 401/403 for the denied roles and 200 empty
    pages for SuperAdmin/SupportAdmin. Honest limitation recorded: the
    caregiver lookup's admitted-role 200 cannot be served on the in-memory
    host (raw PostgreSQL-specific projection), so those facts assert
    "not 401/403 — passed auth+policy and reached the handler" and admission
    itself is proven by the registered-policy matrix facts.
- **Docs/Postman:** `caregivers-review.md` line-3 drift fixed (ContentAdmin
  never had lookup access) + lookup section rewritten to the exact new
  matrix/policy; `claims-and-policies.md` `CaregiverReviewAdmin` entry
  corrected + new `CaregiverLookupAdmin` section; `site-settings.md:12`
  `200**` typo fixed; `landing.md` adds the public-form `/partner-contact` vs
  authenticated `/support/contact` clarification; `partner-contact.md` adds a
  "Retries" section documented from the production handler semantics (replay
  checked before the rate limit — same-key retry never consumes the 5/hour
  budget; same key = replay, new submission = new key; no runtime policy
  change, no new frontend architecture); `Sanad.Admin.CaregiverNotes`
  Postman lookup item description updated to the new policy. `families.md`
  and its Postman item verified already accurate (SuperAdmin/SupportAdmin;
  ContentAdmin/FinanceAdmin 403) — unchanged. Link check across the edited
  docs: no broken relative links. Envelope/pagination names
  (`items/page/pageSize/totalCount/totalPages`) as implemented, no invented
  limits (search has no max length; the docs now say so).
- **Proposed gate filters when unfrozen:** build strict Release 0/0;
  `--filter "FullyQualifiedName~AdminNameIdLookup"` (contract + matrix +
  HTTP tests) plus `--filter "FullyQualifiedName~AdminCaregiverReviewAuthorizationTests|AdminOperationalReadAuthorizationTests"` style
  shared-policy regression set; existing caregiver review test set must stay
  green unchanged.

## Release receipt (2026-10-27, Owner-authorized)

Release `cf065d4bb479669462d026e58be6a9a744d3fd3f` (18 files, +1118/−27)
pushed `origin/main` (`812493d9..cf065d4b`), staged-diff secret scan clean
(1,136 added lines, 0 findings, values never printed). Package: manifest
revision = pushed SHA, 9/9 required-migration heads (none added; startup
confirmed **9/9 contexts `0 pending / 0 applied`**), zip sha256
`c2b15c59…a97ae81f7` verified byte-identical on target before extraction.
Deployed to the rehearsal (`srv1604711`, 72.62.92.144, unit `sanad-api`,
Staging, loopback 5001, DB `SanadDb`): live `47329bf5` → `cf065d4b`; binary
backup `/root/sanad-api-backup-pre-cf065d4b` (110 top-level entries) taken
before stop; host `appsettings.json` + `appsettings.Staging.json` excluded
from replacement; service active, `NRestarts=0`. Config fingerprints
byte-identical before/after: `/etc/sanad/sanad.env` `fad7e7fc…`,
`appsettings.json` `200332b4…`, `appsettings.Staging.json` (7-id seam block
present) `fd96e26d…`, unit `c018192e…` — the seam's explicit startup line was
not re-emitted this start (recorded honestly); effective seam configuration is
proven unchanged by the byte-identical fingerprint.
`Verify-SanadDeployment.ps1` **exit 0** over public 8091 (revision match,
required/applied 9/9, smoke `GET /api/v1/lookups/services` 200). Feature
smoke: anonymous `GET /api/v1/admin/caregivers/lookup` **401 (not 404)**;
both lookup paths present in public OpenAPI. No provider call, no DB write,
no auth login, no provisioning run (provisioner sources unchanged — tool NOT
refreshed, per instruction). Team artifacts shipped in the same commit:
[frontend-landing-response-2026-10-27.md](frontend-landing-response-2026-10-27.md)
+ [frontend-money-actions-2026-10-27.md](frontend-money-actions-2026-10-27.md).
Limitations: no full-suite rerun this phase (focused contract per direction:
strict build 0/0, focused 146/146/0 skipped, architecture 1/1); caregiver-role
admission is runtime-proven by the registered-policy matrix, not by an
authenticated live call (no login performed, per constraint); notification
defect-absence is source+test-proven, no live events fired. **Refund
verification: pending, not executed, requirements NOT closed** — the
proposed one-funded-test-booking protocol awaits parent review; max two real
test refund records; serialized after this release; existing real captures
are valid single sales, not duplicates; the `556970084`/`01a1559c…` care-home
capture is not refund-eligible via existing APIs.

## B2 result — Paymob TEST-mode end-to-end capture + refund (2026-10-27, Owner-authorized)

Executed the bounded real-flow verification against the same Staging rehearsal
(72.62.92.144, SanadDb, deployed revision cf065d4b, 9/9 heads). ONE new test
booking (max two designated; one used). No new accounts, no manual paid states,
no webhook/signature bypass, no config/env/seam change, no commit/push.

**Provider test class + merchant/integration association (real, not prefix-only).**
`POST /api/v1/family/care-home-bookings/{id}/payments/intent` returned HTTP 200
from the real provider: `intentionOrderId` `640412142`, `paymobOrderId`
`chb_01a17ac86e2475c8b5962d70499fd2a4`; the returned public key is test-mode
(`egy_pk_test…`). The secret-key-authenticated intention was accepted by Card
integration `5897206` on the account — association proven by the provider
accepting the call, not by the prefix alone. The 7-id FixtureRefundSeam
allowlist did not intercept this path (the real capture is not allowlisted; seam
preserved separate).

**Checkout + capture.** Booking `01a17ac8-6e22-75a4-aa1f-9c1ad743e0eb` created
via `POST …/checkout` with a unique `Idempotency-Key` (HTTP 200): status 1
(PendingPayment), total **11,500.00 EGP** (base 10,000 + platform fee 1,000 +
tax 500), `refundPolicyVersion=1` (legacy). Completed the documented hosted test
checkout (Unified Checkout → `eg.checkout.paymob.com`) with the documented
frictionless Mastercard test card — no OTP/anti-abuse challenge, no real card
data. Booking then moved to **status 2 PaidAwaitingDecision / PaymentStatus 2
Paid** with `PaymentCompletedOnUtc` set and `PaymobTransactionId 561442756` —
persisted through the **normal HMAC-authenticated callback**
(`ConfirmCareHomePaymentCommand`), not a DB-manual state. Provider read
(`GET /api/acceptance/transactions/561442756`): success=true, pending=false,
amount 1,150,000 cents, order `640412142` = our merchant reference.

**Refund (full, pre-check-in).** Owner-reject was NOT used: the facility owner
is a real account (`@gmail`), not an authorized demo user, and no credential for
it exists — so the deterministic **family-cancel** source route was used
(`POST …/{id}/cancel`; legacy v1 + no check-in ⇒ award = full TotalAmount).
HTTP 200; booking → status 8 RefundInitiated / PaymentStatus 4 / refundStatus 2
Initiated, `refundAmount 11500.00`, `refundReference 561442898`, not ambiguous.
Provider read (`GET /api/acceptance/transactions/561442898`): **success=true,
pending=false, is_refund=true, amount 1,150,000 cents**. Honest status: the app
records the refund as **Initiated**, not Completed (`refund_completed_on_utc`
null) — no false "Completed" is claimed; provider-side the refund is settled.

**Side effects.** `finance.refund_ledgers`/`refund_ledger_entries` are
subscription-scoped — 0 rows for this booking (care-home refunds are tracked on
the booking row). Booking notification outbox: 3 processed rows —
`PaymentSucceeded`, `OwnerDecisionNeeded`, `FamilyCancelledPaidBooking`. No
destructive cleanup; the one test booking and its append-only evidence are
preserved.

**Deviations / findings.** The designated room type `01a17542-2c5c…` (facility
`01a17542-29e5…`) is **not bookable**: checkout returned
`CareHomes.Inventory.RoomMediaRequired` (cover present, no gallery photo).
Reusing existing rows only, the sole bookable type in the corpus is
`01a12b1b-902d…` (facility `01a12162-4bb9…`, the existing eligible facility),
which was used. Not a code bug — a fixture media gap; no source fix proposed.

**Secret incident (sanitized).** While probing the checkout redirect, a
`Location` header echoed the **test-mode, single-use client secret** once into
the operator tool transcript (analogous to the earlier HMAC incident). It is
test-mode and scoped to one intention; nothing was written to git/docs/repo; the
ephemeral secret file and the operator session token were removed after use.

**Limitations (carry forward).** Test mode ≠ future live merchant/integration
IDs; the HTTPS webhook and account-specific credentials are not guaranteed in
production; provider-side refund success does not by itself prove the app's own
"Completed" reconciliation step; the operator session was revoked
(`sessions/logout` → 204, subsequent call → 401). No claim that future live
Paymob is fully working is made from this test alone.

### B2 follow-up — why the provider-settled refund stays app `Initiated` (read-only trace, 2026-10-27)

Read-only trace, no code/DB/provider/auth action. Answers the open item in
"Limitations (carry forward)" above.

- **Intended lifecycle = automated.** The refund call returns the provider
  transaction id; `CareHomeRefundProcessor.InitiateAsync` maps it to
  `RefundReference` via `MarkRefundInitiated` (`CareHomeRefundHandlers.cs:129`,
  `CareHomeBooking.cs:710-721`). Completion is designed to arrive on the
  provider's refund webhook: `POST /api/v1/payments/webhooks/paymob` →
  `CompleteCareHomeRefundCallbackCommand` (`PaymobWebhookController.cs:75-102`)
  → `CompleteCareHomeRefundCallbackHandler` (`CareHomeRefundHandlers.cs:309-329`)
  → `MarkRefundCompletedFromProviderCallback` (`CareHomeBooking.cs:733-743`) →
  `CompleteRefund` sets `Status=Refunded`, `PaymentStatus=Refunded`,
  `RefundStatus=Completed`, `RefundCompletedOnUtc` (`:745-752`). The operator
  manual route is the fallback, not the primary.
- **Actual states.** Provider-known (retained sanitized read): refund txn
  `561442898` success=true, pending=false, is_refund=true, 1,150,000 cents.
  App reference (re-read): `Status 8` RefundInitiated, `PaymentStatus 4`,
  `refund_status 2` Initiated, `RefundReference 561442898`,
  `refund_completed_on_utc NULL`; `payment_completed_on_utc 00:36:59`,
  `UpdatedOnUtc 00:37:48` — no movement after the refund. Provider refunded ≠
  app Completed.
- **Callback expected; no delivery evidence retained.** Contractually expected:
  the same HMAC'd endpoint is proven live (capture txn `561442756` moved the
  booking to Paid via `ConfirmCareHomePaymentCommand`), and the controller
  explicitly branches on refund callbacks. But nothing retains per-callback
  evidence: no care-home payment/refund webhook event table exists (only
  `families.paymob_subscription_callbacks`); the service journal is EF SQL with
  parameters not inlined (no request paths/txn ids); token counts for
  `payments/webhooks`, `561442898`, `561442756`, `is_refunded` are all 0.
  **Absence ≠ proof of non-delivery** — the app logs/persists no per-callback
  row.
- **Confirmed source gap (provider-proven, 2026-10-27).** Read-only
  `GET /api/acceptance/transactions/{id}` (existing credentials via
  `POST /api/auth/tokens` → Bearer; allowlisted stdout) proves the contract for
  this exact refund: the **refund is a child transaction**
  (`561442898`: `is_refund=true`, **`is_refunded=false`**,
  `has_parent_transaction=true`, `parent_transaction=561442756`, `success=true`,
  `pending=false`, `amount_cents=1150000`, `currency=EGP`, `order.id=640412142`)
  and the **`is_refunded=true` flag lands on the parent payment**
  (`561442756`: `is_refunded=true`, `refunded_amount_cents=1150000`,
  `is_refund=false`, `is_standalone_payment=true`). The pre-fix
  `CompleteCareHomeRefundCallbackHandler` early-returned on `!IsRefunded`
  (`CareHomeRefundHandlers.cs:314`), and the controller passed `IsRefunded` =
  strictly `is_refunded==true` (`PaymobWebhookController.cs:97-99`) while still
  routing to the refund command when only `has_parent_transaction==true`
  (`:75-79`). So the legitimate child refund callback was acknowledged `Ok()`
  but **never applied**, leaving the booking `Initiated`. Official Paymob docs
  confirm the refund API response carries `is_refund:true` and that
  `is_refund`/`parent_transaction` are **not** HMAC-covered while
  `has_parent_transaction`/`is_refunded`/`is_voided`/`id`/`order.id`/`success`/
  `pending`/`amount_cents` are; the app's `PaymobHmacCalculator.FieldOrder`
  matches the documented HMAC string exactly.
- **Manual route contract (not executed).** `POST /api/v1/admin/care-homes/bookings/{bookingId}/refund/record-completed`
  → `AdminCareHomeRefundsController.cs:34-41` → `RecordCareHomeRefundCompletedHandler`
  (`CareHomeRefundHandlers.cs:278-307`) → `MarkRefundManuallyCompleted`
  (`CareHomeBooking.cs:754-771`): **DB-only, no gateway call**; requires
  non-blank `reference`+`reason`; eligible when `RefundStatus ∈ {Pending,
  Failed, Initiated}` and `RefundClaimedOnUtc == null` (this booking qualifies);
  sets `Status=Refunded`, `RefundStatus=ManuallyCompleted`, `RefundCompletedBy=actor`.
  Policy `CareHomesOperationalAdmin`. The genuine provider ref `561442898` is
  usable as the reference.
- **Fix implemented (owner-authorized, uncommitted; 2026-10-27; corrected round 2).**
  Smallest verified change; no schema, ledger redesign, new webhook store or
  framework:
  - `PaymobWebhookController.cs` (refund branch): the `is_voided` no-op guard is
    now scoped **inside** the refund/child branch (an ordinary non-child payment
    callback keeps its existing routing); refund branch sends
    `CompleteCareHomeRefundCallbackCommand(TransactionId, Success, Pending,
    IsRefunded, AmountCents, OrderId, UtcNow)` read from HMAC-covered fields only;
    the uncovered `parent_transaction` field is not used, and `order.id` (signed)
    is carried only so the handler can recognise a raced callback. A signed refund
    payload missing `id`/`amount_cents`/`success`/`pending` is rejected `400`
    rather than silently acknowledged.
  - `CareHomeRefundHandlers.cs`: command record reshaped; handler requires
    settled (`!Success || Pending` → no-op), completes **only** on the
    transaction id (`RefundReference == id`, or `PaymobTransactionId == id` with
    `IsRefunded`), **requires** the recorded award for the child match (no silent
    skip when the award is absent), and on `DbUpdateConcurrencyException` re-reads
    the persisted row — succeeding only when it is completed against **this**
    refund reference, never acknowledging an uncommitted completion. A settled
    callback that matches only by the signed `order.id` (the race) is **refused**
    (`CareHomes.Refund.CallbackNotYetApplicable`), never completed.
  - `CareHomeBooking.cs`: `MarkRefundCompletedFromProviderCallback(string?, long,
    DateTime)` anchors on the HMAC-covered transaction id (was
    `long? parentTransactionId`). The round-1
    `CompleteRefundFromProviderCallbackClaim` was **removed**: completing from the
    order id alone attributed refund *kind* to unsigned flags and could falsely
    complete a booking on a non-refund child that shared the order and amount.
  - `CareHomeRefundProcessor.InitiateAsync`: tolerates losing the
    `MarkRefundInitiated` write to a concurrent writer — a re-read showing a
    settled refund is success, never overwritten with `Initiated` (and a
    non-settled re-read rethrows, so nothing is silently dropped).
  - Tests: new `tests/Sanad.UnitTests/API/CareHomeRefundWebhookCallbackTests.cs`
    (signed-raw-callback controller + handler + domain tests, incl. the
    false-completion negative) and the existing refund-callback test updated at
    `CareHomeBookingLifecycleTests.cs:929`.
- **Race semantics (chosen).** `is_refund` is **not** in Paymob's signed field
  list, so a child's routing flags (`has_parent_transaction`, `is_refunded`)
  prove the transaction is *a child*, not that it *is a refund*. Completion
  therefore keys **only** on the transaction id (the stored refund reference, or
  a refunded payment). A settled, HMAC-verified callback that matches only by the
  signed `order.id` to a booking whose refund is **claimed in-flight**
  (`RefundStatus=Pending`, `RefundClaimedOnUtc≠null`, `RefundReference=null`) and
  whose amount equals the recorded award is **refused as not-yet-applicable**
  (retriable non-2xx): it is neither acknowledged-and-lost nor used to fabricate a
  completion. The refund processor then finishes and persists the reference; the
  redelivered callback matches the stored reference and completes idempotently.
  Any other child/void/unmatched/mismatched callback is acknowledged untouched
  (never 500-poisoned, never marked refunded). Paymob redelivery on non-2xx is
  **not** assumed (its retry policy is undocumented), so the refused window's
  guaranteed close is a redelivery or the existing operator `record-completed`
  fallback — see the residual note.
- **Gates (Release, frozen state).** `dotnet build Sanad.slnx -c Release`
  → Build succeeded, **0 warnings / 0 errors**. Focused
  `dotnet test tests\Sanad.UnitTests\Sanad.UnitTests.csproj -c Release --no-build
  --filter "FullyQualifiedName~CareHomeRefundWebhookCallbackTests|
  FullyQualifiedName~CareHomeBookingLifecycleTests|
  FullyQualifiedName~LegacyRentalWebhookCallbackTests|
  FullyQualifiedName~PaymobHmacCalculatorTests|
  FullyQualifiedName~CareHomeRefundPolicyBookingTests"` → **Passed 88, Failed 0,
  Skipped 0**.
- **Residual causal uncertainty (report, do not overclaim).** No per-callback
  delivery row is retained (the app persists no care-home webhook event), so
  delivery of the child refund callback to the app is not directly observed; the
  fix is **contract-proven** (provider record + official HMAC/refund docs), not
  delivery-observed. The `DbUpdateConcurrencyException` race branches cannot be
  exercised by the in-memory provider (it ignores concurrency tokens), so they are
  code-reviewed, not unit-tested. **Open question for review:** the refused
  in-flight window closes automatically only on a redelivery; closing it *without*
  a redelivery or the operator fallback would require either (a) treating the
  processor's refund HTTP response as a settled refund (the response exposes only
  `id`; the documented design keeps the callback as the settlement proof, so this
  is a deliberate design change), or (b) an authenticated provider transaction
  readback validating `is_refund=true` before completing (a new gateway path).
  Both exceed the "same existing files" scope and are reported, not implemented.
  Nothing was committed/pushed/deployed, no runtime config changed, no secret
  rotated, no manual `record-completed` run.
- **Smallest remaining runtime verification (after review + deploy, separate
  permission).** Observe a real/Provider-redelivered refund callback advance a
  booking from `Initiated` to `Completed` on Staging (or the provider's own
  callback/delivery log for txn `561442898`), then confirm no false completion
  from a void/unrelated child. Until then the DB-only
  `POST /api/v1/admin/care-homes/bookings/{bookingId}/refund/record-completed`
  route with ref `561442898` remains the intended operator fallback.
- **Finance consistency (report, do not rewrite).** Care-home refunds write **no**
  `finance.refund_ledgers`/`refund_ledger_entries` rows (subscription-scoped) —
  the refund lives only on the booking row (confirmed in the B2 result above).
  Platform-level refund accounting therefore has no care-home entry; actionable
  only if platform accounting is required to reconcile care-home refunds
  centrally.

### B2 round-3 — authenticated readback completion (owner-authorized amendment, 2026-10-27)

**Approval amendment.** The open question at the end of the round-2 residual note
(a)/(b) was decided by the Owner in favour of **(b)**: a small authenticated,
**read-only** Paymob transaction-status readback, so a settled refund can be
verified and completed independent of callback redelivery. Processor
auto-completion from the id-only 2xx was explicitly rejected; the callback
handler stays the fallback. Implemented in the working tree only — **not**
committed/pushed/deployed; no runtime config/service change, no secret rotation,
no provider redelivery, no state write, no manual `record-completed`, no new
refund POST.

**Design (smallest, existing files only — no new framework/schema/migration/
webhook store).**
- `IPaymobClient.cs`: new `PaymobRefundVerification` DTO and an **abstract**
  `VerifyRefundTransactionAsync(string, CancellationToken)` member, so every
  implementation must adapt (no silent production default).
- `PaymobClient.cs`: real readback — `POST /api/auth/tokens {api_key}` → Bearer →
  `GET /api/acceptance/transactions/{id}`. Authorization comes from the API-key
  token (the secret key is rejected by that namespace); no credential ever
  reaches a URL or the log; the token is cached ~55 min. A missing scalar field
  is surfaced as `null`; a response without the required identity/outcome fields,
  a non-JSON body, or a non-2xx read fails closed with
  `Paymob.VerificationUnavailable`.
- `DevelopmentPaymobClient` (no provider channel) and the test-host
  `ScriptedPaymobClient` both **fail closed** (never fabricate a settled refund).
- `FixtureRefundSeamPaymobClient`: its own synthetic `Completed` reference
  (`fixture-refund-…`) is **refused** (never sent to a provider) **unconditionally**
  — even when the seam is disabled — while any real (non-synthetic) id is
  delegated read-only to the inner client.
- `CareHomeRefundProcessor.InitiateAsync` (`CareHomeRefundHandlers.cs`): after the
  reference is durably persisted, calls `VerifyAndCompleteAsync`, which completes
  to `Completed` (+ one `RefundCompleted` outbox event) **only** when
  `IsVerifiedSettledRefund` proves the provider's authenticated view for the
  **exact** returned id is a settled refund (`is_refund=true`, `success=true`,
  `pending=false`) that cross-correlates with the server's own state: award ==
  `amount_cents`, currency `EGP`, `order.id` == stored `IntentionOrderId`,
  `parent_transaction` == stored `PaymobTransactionId`. Any refusal, timeout,
  unavailable readback, or mismatch (incl. a null/absent reference) leaves the
  booking at `Initiated` and **never** sends a second refund. The readback
  completion-save keeps the round-2 race-safe rule: on
  `DbUpdateConcurrencyException` it re-reads and accepts only a row completed
  against **this** reference, else rethrows.
- Round-2 behaviour is preserved: callback completion still keys **only** on the
  HMAC-signed transaction id; `is_refund` remains unsigned and is never trusted;
  the order-only completion that could falsely complete a non-refund child was
  already removed.

**Tests (authored and RUN).**
- NEW `tests/Sanad.UnitTests/Families/PaymobRefundTransactionReadbackTests.cs` —
  10 gateway HTTP-contract facts: API-key token exchange (never the secret key),
  exact-id GET with `Bearer`, cached token, no-key/401/404/non-JSON/incomplete/
  empty-id fail-closed, missing optional → null, nested `parent_transaction.id`.
- `CareHomeRefundWebhookCallbackTests.cs` — round-3 app facts: raced callback
  **without redelivery** then verified readback ⇒ persisted `Completed` + exactly
  one `RefundCompleted`; idempotent second attempt (no extra event); pending /
  unavailable / null-reference never complete and never repeat the POST; an
  8-case `[Theory]` proving every mismatch (kind, outcome, amount, currency,
  order, parent, id) refuses.
- `FixtureRefundSeamTests.cs` — 3 readback guard facts (synthetic refused even
  when disabled, never reaching the inner client; real id delegated read-only).
- NEW `tests/Sanad.UnitTests/CareHomes/CareHomeRefundReadbackConcurrencyPostgresTests.cs`
  — 2 relational facts on a disposable, nonce-named local PostgreSQL (established
  `SlicePostgresFact` pattern: loopback:5432 only, name verified absent, only
  CareHomes migrations, only that exact DB dropped): (1) a completion race
  against an unrelated writer is **never** reported as success; (2) a race
  against the **same** reference is accepted exactly once. The in-memory provider
  ignores concurrency tokens, so these branches are only reachable relationally.

**Gates (Release, frozen state).** `dotnet build Sanad.slnx -c Release
/warnaserror` → **Build succeeded, 0 warnings / 0 errors**. Focused
`dotnet test tests\Sanad.UnitTests\Sanad.UnitTests.csproj -c Release --no-build
--filter` (callback/booking/refund/gateway/fixture/payment classes, incl. the
Postgres race class) → **Passed 479, Failed 0, Skipped 0**. New readback +
callback classes alone → **Passed 50, Failed 0**. Postgres race class alone →
**Passed 2, Failed 0, Skipped 0** (a disposable local PostgreSQL was available;
this is a real relational run, not a skip). 19 existing test fakes were updated
to implement the new abstract member (failure default) so the interface cannot be
silently bypassed.

**Not done / deferred.** Runtime verification is **NOT** marked done: no
provider call, no redelivery, no state write, no `record-completed`, no
commit/push/deploy/config change. No new public diagnostic API was added.

**Smallest staging verification plan (after parent review + deploy; separate
permission).** Reuse the existing refund `561442898` (booking
`01a17ac8-6e22-75a4-aa1f-9c1ad743e0eb`, capture `561442756`, award
11,500.00 EGP / 1,150,000 cents) — already provider-settled, so **no new refund
POST and no manual completion**: (1) confirm the booking is still `Initiated`
with reference `561442898`; (2) trigger the production verification path once —
either a provider redelivery of the refund callback, or a narrow operator command
that invokes the production readback — and observe the booking advance to
`Completed` with `RefundCompletedOnUtc` set and exactly one `RefundCompleted`
outbox event; (3) confirm no second refund was sent and no false completion for a
void/unrelated child. Absent either trigger, the DB-only
`POST /api/v1/admin/care-homes/bookings/{bookingId}/refund/record-completed`
route with reference `561442898` remains the intended operator fallback. **No
commit/push/deploy/config rotation/redelivery/manual completion until parent
review.**

### B2 round-4 — reviewer P3 fix: parent-payment-path concurrency acceptance

**Reviewer verdict.** PASS, no blockers, with one P3: the callback handler's
concurrency re-read accepted a committed row only when
`persisted.RefundReference == incoming id`. On the **refunded-parent** path the
incoming id is the capture (the booking's `PaymobTransactionId`), which can never
equal the stored **child** `RefundReference`, so a genuinely applied parent-path
callback that raced another writer was spuriously refused
(`CareHomes.Refund.CallbackConcurrency`, retriable non-2xx).

**Fix (smallest).** `CompleteCareHomeRefundCallbackHandler.CommitCompletionAsync`
now carries the path and, on `DbUpdateConcurrencyException`, accepts the
committed row when it is `Completed`/`ManuallyCompleted` **and** either the stored
child `RefundReference` equals the incoming id (child path — unchanged,
generic reference identity **not** weakened) **or** this delivery proved the
parent payment refunded (it reached completion via that path and passed the
`IsRefunded` guard) **and** the stored `PaymobTransactionId` equals the incoming
capture. An arbitrary completed row, or a parent payment not verified refunded,
is never credited to the callback. The processor's own readback re-read is
unchanged (it always matches the exact child reference).

**Regression (RUN, relational).** Added to
`CareHomeRefundReadbackConcurrencyPostgresTests` (same disposable nonce target,
loopback:5432, name verified absent, only CareHomes migrations, only that DB
dropped): (3) a **parent-payment** callback race is **accepted** via the recorded
payment id → `Success`, `Completed`, child reference retained, one
`RefundCompleted`; (4) a **child** callback race against a completion with a
**different** reference is **refused** (`CallbackConcurrency`) — the generic
reference identity is not weakened. Negative control: with the parent branch
disabled the parent fact fails (1 failed / 0 passed) — the test genuinely
discriminates the fix.

**Gates (Release, frozen state — final code).** `dotnet build Sanad.slnx -c
Release /warnaserror` → **0 warnings / 0 errors**. Focused set (same 21 classes +
the Postgres race class) → **Passed 481, Failed 0, Skipped 0**. Postgres race
class alone → **Passed 4, Failed 0, Skipped 0**.

### Release authorization (Owner, 2026-10-27 — bounded lift)

The Owner explicitly approved "latest review recommendation: small P3 cleanup →
commit/push/deploy → ONE second/final TEST booking payment/cancel-refund end-to-end
to prove app `Completed`", and **lifted the prior push/release hold for this
approved bounded fix only**. Authorized: commit + push `origin/main`, deploy the
SAME approved rehearsal (`72.62.92.144`, `sanad-api`, loopback 5001, DB `SanadDb`)
preserving infra/CORS/environment/seam/unit/storage/keyring; then ONE new
designated TEST care-home booking (max two total, the first already used) through
the real flow — existing demo-family credentials only, family cancel before
check-in (legit v1 full refund), documented frictionless TEST card, no new
accounts/manual paid states/extra charges — then the app's own `Completed` proof.
Not authorized: infrastructure/CORS/environment/seam-disable/secret rotation,
second binary deploy for docs, blind refund retry, `record-completed` to fabricate
completion. The prior HMAC exposure remains a **separate** coordinated-rotation
follow-up.

## Release receipt — refund-completion fix (2026-10-27, Owner-authorized)

Release `b302a3fc33124fb840435cc24052dc12ffd750e4` (32 files, +2768/−36)
pushed `origin/main` (`fafb6370..b302a3fc`), no mend/force. Staged set exactly
the approved 32 files (refund source/interfaces/decorators/tool fake/new tests/
19 adapted test fakes/care-home doc/active brief); private handoff + debris left
unstaged. Staged-diff secret scan: 2,768 added lines, **0 real-secret findings**
(flags only synthetic `sk_test`/`pk_test` placeholders, the B2 test-mode
public-key prefix in this brief, and a `HmacSecret = secret` code line — values
never printed).

Package: `Publish-SanadApi.ps1 -Configuration Release` → manifest revision =
pushed SHA, 9/9 required-migration heads (none added), no
`appsettings.Development.json` / `.env` / keys in the output; zip
`deploy/sanad-api-b302a3fc.zip` sha256
`a5f30acfd53b13e31044b74a0e93bfda0858a7e63ef2a10f432505520eaa280b`, **verified
byte-identical on target before extraction**.

Deployed to the same approved rehearsal (`srv1604711`, 72.62.92.144, unit
`sanad-api`, `Staging`, loopback 5001, DB `SanadDb`): live `cf065d4b` →
`b302a3fc`; binary backup `/root/sanad-api-backup-pre-b302a3fc` (110 top-level
entries) taken before stop; extraction overlaid application files **without**
replacing host `appsettings.json` / `appsettings.Staging.json`; service
`active`, `NRestarts=0`. Startup log: **9/9 contexts, each `0 pending
migration(s) / 0 migration(s) applied`** (0 migrations, as scoped), listening
`http://127.0.0.1:5001`. Config fingerprints byte-identical before/after
(also equal to the prior release): `/etc/sanad/sanad.env` `fad7e7fc…`,
`appsettings.json` `200332b4…`, `appsettings.Staging.json` (`FixtureRefundSeam`
with exactly **7** ids) `fd96e26d…`, unit `c018192e…`.
`Verify-SanadDeployment.ps1` **exit 0** over public 8091 (revision match,
required/applied 9/9, 9 startup contexts, smoke `GET /api/v1/lookups/services`
HTTP 200).

Provisioner tool (its `Sanad.API` dependency changed): re-published fresh
(framework-dependent `linux-x64`, Release, 0 warnings), zip sha256
`d24d6f7f203a2365bce2dc98df2348b039c169a6b11272c034866f458dceadb8` verified on
target; `/root/sanad-demo-provisioner` backed up to
`/root/sanad-demo-provisioner-backup-pre-b302a3fc` and refreshed —
`Sanad.PortalProvisioner.dll` sha256
`1024dedbeb9b5745c71312f17d93bfe57073f51fb795f3000ec6ae0e090d8f05` (matches
local publish), host `appsettings.json` preserved, `--help` responds. **No
provisioning run, no DB write, no seeder.**

## B2 final result — second TEST booking proves app `Completed` (2026-10-27)

ONE new designated TEST booking (max two total; both now used). Existing
demo-family credentials only (`demo1.family.owner`), reusing the last valid
facility `01a12162-4bb9-782a-b386-8a1cb2c136ef` / room type
`01a12b1b-902d-7b0b-b7cd-3af2274c25d8`; no new accounts, no manual state, no
extra charges, no owner impersonation.

- **Checkout.** `POST …/checkout` with a fresh `Idempotency-Key` → booking
  `01a17b7a-5524-75e3-8601-f548e7544559`, status 1, total **11,500.00 EGP**
  (base 10,000 + fee 1,000 + tax 500), `refundPolicyVersion` 1.
- **Provider test intent.** `POST …/payments/intent` HTTP 200 →
  `intentionOrderId 640433504`, `paymobOrderId
  chb_01a17b7a55257be897cf2f8fc19a4d4a`, public key prefix `egy_pk_te…`
  (test-mode); completed the documented frictionless TEST Mastercard checkout
  (no OTP, no real card data).
- **Capture.** Booking → status 2 `PaidAwaitingDecision` / payment 2 `Paid`,
  `payment_completed_on_utc` set, `PaymobTransactionId 561461962`, through the
  **normal HMAC callback** (nginx log: `POST
  /api/v1/payments/webhooks/paymob … 03:50:22 … 200`).
- **Refund.** Family cancel before check-in (`POST …/{id}/cancel`, legacy v1 ⇒
  full award). Cancel response HTTP 200 in 2.66 s already carried
  **`status 9` Refunded / `paymentStatus 5` Refunded / `refundStatus 4`
  Completed**, `refundAmount 11500.00`, `refundReference 561462075`,
  `refundCompletedOnUtc 2026-10-27T03:51:20.7347297Z`.
- **Which path completed it — readback, directly observed.** The persist store
  shows `refund_completed_on_utc = 03:51:20.734729`, identical to the cancel
  request's own `UtcNow` (the readback path stamps that value; the callback
  stamps `DateTime.UtcNow`). The service journal records, inside that request,
  the outbound `POST …/acceptance/void_refund/refund` **and** the readback
  `GET https://accept.paymob.com/api/acceptance/transactions/561462075`
  (with `POST …/auth/tokens`). Paymob's refund callback did also arrive
  (`POST /api/v1/payments/webhooks/paymob … 03:51:22 … 200`) but was an
  idempotent no-op: only **one** `RefundCompleted` (event 7) outbox row exists.
  So the app's own **readback completion is runtime-proven**, not merely
  test-proven, for this run.
- **Provider read-only confirmation.** capture `561461962`: `success=true`,
  `pending=false`, `is_refunded=true`, `refunded_amount_cents=1150000`,
  `amount_cents=1150000`, `currency=EGP`, `order.id=640433504`, merchant
  `chb_01a17b7a55257be897cf2f8fc19a4d4a`. refund `561462075`: `success=true`,
  `pending=false`, `is_refund=true`, `has_parent_transaction=true`,
  `parent_transaction=561461962`, `amount_cents=1150000`, `currency=EGP`,
  `order.id=640433504`.
- **Outbox / side effects (minimal, normal).** 4 processed rows for the
  booking (`PaymentSucceeded`, `OwnerDecisionNeeded`, `FamilyCancelledPaidBooking`,
  `RefundCompleted`), each `attempt_count=1`. `refund_status` ambiguity flag
  false; refund attempt claim cleared. `finance.refund_ledgers` /
  `refund_ledger_entries` are refund-case-scoped (no booking linkage): 0 rows
  added today (7/11 unchanged). Care-home booking count 27 → 28.
- **First booking unchanged.** `01a17ac8-…` still `Initiated` (8/4/2,
  `refund_completed_on_utc` null); no manual state action taken on it.
- **Operator sessions.** Both created sessions logged out
  (`sessions/logout` → 204; the token then returns 401); a fresh probe saw
  exactly one session (its own) and was logged out. Private artifacts
  (checkout URL, tokens, response JSONs) removed; no clientSecret / token /
  HMAC value or full URL was printed or staged throughout.

## Current result

Preparation complete at `812493d9` (2026-10-27); Task A/A2 delivered
authored-not-run (see A delivery above) and handed back `ready_for_review`.
Task B1 accepted. Task B2 result delivered (provider-settled refund proven, app
`Initiated`). **B2 follow-up implemented** (owner-authorized, uncommitted,
corrected round 2): the smallest verified refund-webhook fix plus dedicated
regression tests. Completion keys only on HMAC-signed identity (the stored refund
reference, or a refunded payment); the refund-callback/refund-HTTP race is
**refused** as not-yet-applicable (retriable non-2xx) rather than completed from
the signed-but-unsigned-`is_refund` order id, so a non-refund child sharing the
order and amount can never fabricate a completion, while the processor persists
the reference and the redelivered callback completes idempotently. Release build
0/0 and 88 focused tests passing (incl. the false-completion negative); handed
back to Mastermind for review. **Not** committed/pushed/deployed; no runtime
config/service change, no secret rotation, no manual `record-completed`. Task C
still pending. Base correction to deployed `47329bf5` recorded above. No business
behavior changed beyond the caregiver lookup policy and this refund-webhook
correction; elderly and shared surfaces verified unchanged.

**Round-3 amendment (owner-authorized, uncommitted).** Per the Owner's decision
for option (b), the refund processor now has a guaranteed completion path
independent of callback redelivery: an authenticated, read-only Paymob
transaction readback verifies the exact returned refund id and cross-correlates
it with the booking's stored state before completing; the callback handler stays
the fallback and round-2 id-only semantics are preserved. Fixture synthetic
references are never sent to the provider, and every implementation/fake adapts.
Gates re-run at the frozen state: strict Release build **0/0**; focused set
**479 passed / 0 failed / 0 skipped** (incl. **2 real relational Postgres race
facts**, not a skip); new readback + callback classes **50 passed**. Runtime
verification is **NOT** marked done — no provider call/redelivery/state write/
`record-completed`; no commit/push/deploy/config rotation. Smallest staging plan
reuses refund `561442898` with no new refund POST and no manual completion; see
"B2 round-3" above. Handed back ready for review.

**Round-4 (reviewer P3 fix) + release authorization (Owner, 2026-10-27).**
Reviewer PASS with one P3 (parent-payment-path concurrency re-read spuriously
refused a genuinely applied callback): fixed in
`CompleteCareHomeRefundCallbackHandler.CommitCompletionAsync`, which now accepts
the committed row via the stored child reference **or**, on the verified
refunded-parent path, via the recorded payment id — without weakening generic
reference identity or crediting an arbitrary parent completion. Regression added
relationally (parent path accepted; child mismatched reference refused) and
negative-control-proven. Final-code gates: strict Release **0/0**; focused set
**481 passed / 0 failed / 0 skipped** (incl. **4 real relational Postgres facts**).
The Owner then **lifted the push/release hold for this bounded fix only** and
authorized commit/push/deploy to the same rehearsal plus ONE second/final TEST
booking proving the app's own `Completed`. Phase `ready_for_review` → release
authorized; recorded below once executed.

**Executed (2026-10-27).** Pushed `b302a3fc` and deployed it to the same
rehearsal (verifier exit 0, 9/9 heads / 0 pending, smoke 200, fingerprints
unchanged; provisioner refreshed, no run). The second/final TEST booking
(`01a17b7a-5524-75e3-8601-f548e7544559`, 11,500.00 EGP) paid through the real
flow and was refunded by a family cancel; the app itself recorded
`refundStatus Completed` **within the cancel request**, and the service journal
shows the new authenticated readback `GET /api/acceptance/transactions/561462075`
— so the **app's own `Completed` is runtime-proven via the readback path**, with
the provider settlement independently confirmed. Provider maximum of two test
records reached; the first booking stays `Initiated` (untouched). Both
operator sessions logged out. Security follow-up (coordinated HMAC rotation)
remains **open and separate**. Not a production-readiness claim: test account
only; live merchant keys and HTTPS webhook delivery still need separate
verification.
