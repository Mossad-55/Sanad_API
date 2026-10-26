# Mobile integration review — closing items (ACTIVE BRIEF)

Phase state: `in_progress`. Release state: `authorized` (Owner directive
2026-10-26: "lock in the contract right away, spawn workers and get everything
done" — read as authorization to implement, gate and release after a green
integrated gate; the Owner may veto at any handback).

Base: remote `main` HEAD `b22d9fa0`; deployed rehearsal revision `e664592e`
(`Staging`, seam preserved). No migrations are expected unless a task proves one
is required — that stops for Owner review first.

Single active brief: this file owns scope, task status and current result for
this slice. The Mastermind handoff carries only the next action.

## Read first

1. `AGENTS.md` and `docs/internal/governance/AGENTS.md`.
2. `docs/internal/Mastermind_Handoff.md` (compact checkpoint).
3. This brief. Inspect `git status`/`diff` first; preserve unrelated and private
   edits (`docs/internal/Mastermind_Handoff.md`, `.opencode-audit-tmp/`, `build*`,
   `testout*`, `migout.txt`, `e07-library-smoke.ps1`).

## Source report (frontend/mobile → backend, translated)

**1. Family sent-invitations list.** `GET /api/v1/family/invitations` currently
returns only invitations *received* by the user. Need an authenticated endpoint
returning invitations *sent by the family*, with fields `id`, `invitedEmail`,
`role`, `relationshipType`, `status`, `createdOnUtc`, `expiresOnUtc`. Goal:
show them after app reopen and allow cancelling a pending invitation via the
existing cancel endpoint.

**2. Confirm and correct Premium plan benefits.** The last log shows two
benefits missing from the catalog and the current subscription: `Library`
(key 2) and `PremiumContent` (key 8). Confirm the approved plan benefits. If
they include the library and premium content, correct the **plan and current
subscription settings, not just the catalog**, and ensure allowed content
returns `canAccess: true`.

**3. Enforce the no-plan-change-during-paid-period policy on the server.** The
app decision: upgrade, downgrade or re-purchase is not allowed before the
current paid period ends. Server must return a documented `409` when a plan
change is attempted during an active paid period, and must not create a new
payment. Turning off auto-renew does **not** end the paid period and does not
lift the block. Blocking purchase with an existing subscription and blocking
early renewal are already documented; confirm they are implemented and test
concurrent requests and two-device scenarios.

**4. Confirm and document CareHome-source care records.** The app displays
`CareHome`-source records for the family and the elderly, but the available
files only document creation from the family and the caregiver. Need the
care-home dashboard record-creation contract with a Request/Response example,
and confirmation that the record appears in
`GET /api/v1/family/dependents/{dependentId}/care-records` and
`GET /api/v1/elderly/care-records`. If already implemented, document it and add
the Postman collection only — do not reimplement.

## Tasks

Worker sets `in_progress`/`blocked`/`ready_for_review`; only Mastermind sets
`completed`.

| Task | Owner | Depends | Acceptance | Status |
|---|---|---|---|---|
| M1 — family sent-invitations endpoint | River GLM 5.3 `#high` | — | New authenticated outgoing-invitations endpoint returns exactly the 7 fields; existing cancel works on returned ids; focused API/handler tests; docs + Postman | ready_for_review |
| M2 — Premium plan benefits + plan-change policy | River DeepSeek V4.1 `#high` | — | (a) approved benefits confirmed with source evidence; if `Library`/`PremiumContent` are approved, correct plan + subscription source so `canAccess` is true, catalog-only fix rejected; (b) plan change during an active paid period returns documented `409`, creates no payment, auto-renew off does not lift it; duplicate-purchase and early-renewal blocks confirmed; concurrency/2-device tests | ready_for_review |
| M3 — CareHome-source care records documentation | River GLM 5.3 `#high` | — | Confirm and document the CareHome dashboard create contract (Request/Response) and its appearance in both read routes; docs + Postman only if already implemented; report if not implemented | ready_for_review |

## Bounded scope and owned files

Edit only the files needed by the owned task; if a required path is missing or
the task needs another file, stop and report path/reason — do not self-expand.
Do not edit another task's files; on a shared file, report the overlap instead of
editing.

### M1 — family sent invitations
- `src/API/Sanad.API/Controllers/FamilyController.cs` (new outgoing route near
  the existing `invitations` routes)
- `src/Modules/Families/Application/Sanad.Modules.Families.Application/Invitations/FamilyInvitations.cs`
- `src/Modules/Families/Domain/Sanad.Modules.Families.Domain/Invitations/FamilyInvitation.cs`
  (read-only unless a projected field is missing)
- `tests/Sanad.UnitTests/Families/FamilyInvitationTests.cs`
- `docs/app/families/*` invitation guide + the app-family Postman collection
- Scope rule: outgoing query must be scoped to the caller's family, authenticated
  (`NormalAccess`/family policy as the sibling routes use), and return only the
  7 named fields. Reuse the existing cancel endpoint; do not add a second cancel.

### M2 — Premium benefits + plan-change policy
- `src/Modules/Families/Domain/Sanad.Modules.Families.Domain/Subscriptions/SubscriptionBenefit.cs`,
  `SubscriptionPlan.cs`
- `src/Modules/Families/Application/Sanad.Modules.Families.Application/Subscriptions/SubscriptionQueries.cs`,
  `SubscriptionCommands.cs`, `PlanChangeCommands.cs`
- `src/API/Sanad.API/Controllers/FamilySubscriptionsController.cs`
- `src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Library/LibraryPremiumAccess.cs`
  (read-only unless the access rule is the defect)
- `tests/Sanad.UnitTests/Families/Subscriptions/SubscriptionPlanChangeTests.cs`
  and the subscription/benefit test class(es) actually covering the change
- `docs/app/families/subscriptions.md` + the app-family Postman collection
- Scope rule: confirm the approved benefit set from source before changing it.
  If `Library`/`PremiumContent` are approved, the fix must correct the plan
  definition **and** the current-subscription benefit grant, not the catalog
  listing alone. Do **not** invent or drop benefits; report the exact approved
  set you find with file/line evidence. Any persisted-data correction (existing
  subscriptions on a target) is **proposed with exact idempotent SQL for Owner
  authorization** — do not write any database.

### M3 — CareHome-source care records
- `docs/app/families/care-records.md`
- `docs/care-homes/*` care-record/record-creation guide
- app-family + care-home Postman collections as needed
- Read-only source references: `src/Modules/Families/Domain/.../CareRecords/CareRecordSource.cs`,
  `src/Modules/Families/Application/.../CareRecords/*`, care-home care-record
  handlers, and the two read routes
- Scope rule: documentation/Postman only. If the CareHome create contract does
  not exist in source, report it precisely instead of implementing.

## Constraints

- No database writes, no provider/financial calls, no auth mutation, no
  migrations, no config/runtime change without explicit Owner approval.
- No commit/push/deploy by workers; the Mastermind runs the authorized release
  after a green integrated gate.
- Preserve unrelated/private edits. Never print secrets.
- Strict Release build must be 0 warnings / 0 errors; no fake passes, no
  file moves to make a gate green.
- Distinguish authored vs executed checks; record every unrun check as NOT RUN
  with a reason.

## Gates (integrated, at handback)

- `dotnet build Sanad.slnx -c Release --nologo /p:TreatWarningsAsErrors=true` → 0/0.
- Focused tests per changed path (M1 invitation, M2 subscription benefit +
  plan-change/concurrency, M3 none — docs only).
- Docs links resolve; Postman collections parse as UTF-8 JSON;
  `git diff --check` exit 0.
- Reuse valid gates; rerun only invalidated checks.

## Lifecycle

Workers hand back `ready_for_review` with changed files, commands/counts,
authored-vs-run and blockers. Mastermind reviews the actual diff, runs the
integrated gate, marks tasks/phase, then (release `authorized`) commits, pushes,
publishes, deploys to the rehearsal (preserve `Staging`, seam, env, unit,
storage) and verifies, then records the receipt here.

## Current result

- M1: ready_for_review — new `GET /api/v1/family/invitations/sent`
  (authenticated, FamilyAccess, family-scoped via `FamilyAccess.ResolveFamilyAsync`)
  returns all statuses of the caller's family's sent invitations, each with
  exactly `id`, `invitedEmail`, `role`, `relationshipType`, `status`,
  `createdOnUtc`, `expiresOnUtc` (`FamilySentInvitationResponse` in
  `FamilyInvitations.cs`; all 7 fields already projected from the existing
  `FamilyInvitation` aggregate — domain file unchanged, no migration). Cancel
  reuses the existing `DELETE /api/v1/family/invitations/{invitationId}`
  (owner-only) — no second cancel; other-family callers still get
  `Families.Invitation.AccessDenied`. Changed:
  `FamilyController.cs`, `Invitations/FamilyInvitations.cs`,
  `tests/Sanad.UnitTests/Families/FamilyInvitationTests.cs` (+3 handler tests:
  family scoping/no cross-family leak + exactly-7-fields, returned-pending-id
  cancels via the existing revoke incl. cross-family 403 pin, and a received-list
  regression), `docs/app/families/invitations.md` (+outbox section; revoke
  section id-source updated), `docs/postman/app/Sanad.App.Family.postman_collection.json`
  (+ sent-list request feeding `invitationId`). Executed: focused
  `dotnet build` of Sanad.API Release → 0/0;
  `dotnet test --filter FullyQualifiedName~FamilyInvitation` → 7/7 passed
  (4 pre-existing + 3 new); Postman JSON parses (ConvertFrom-Json);
  `git diff --check` exit 0. NOT RUN: full sln gate (parent runs it),
  live-endpoint readback (no runtime started).
- M2: ready_for_review — (a) approved benefit set confirmed from source, no
  source change needed. `SubscriptionPlan.cs:112-127` defines Free (keys
  1,2,3,5), Premium (1–7) and PremiumPlus (all 8) via `CreatePackage`
  unavailable-list semantics (:129-145); `Library` (key 2) is granted by every
  plan while `PremiumContent` (key 8) is granted by PremiumPlus only
  (`SubscriptionValueObjectTests.cs:51-85`, key 8 false at :72 for Premium and
  true at :84 for PremiumPlus). `LibraryPremiumAccess.cs:51-53` consults key 8
  alone and `FamilySubscription.Create`/`ApplyPlan` copy the plan's own set, so
  the shipped end state already computes `canAccess` correctly; a catalog-listing
  change alone is not the contract, so neither the plan definition nor the
  subscription grant was altered. Pinned by a new
  `SubscriptionPremiumBenefitAccessTests.cs` (snapshot equals the approved set on
  all three plans; Premium `Library=true`/`PremiumContent=false` → `canAccess`
  false; PremiumPlus `PremiumContent=true` → `canAccess` true, `VideoUrl` null).
  (b) an immediate plan change (`POST
  /api/v1/family/subscriptions/plan-change/payment-intent`) is refused with the
  documented `409 Subscriptions.PlanChange.PaidPeriodActive` while the current
  subscription price is greater than `0` and `UtcNow < currentPeriodEndsOnUtc`,
  and a duplicate / second-device retry is refused with the documented `409
  Subscriptions.PlanChange.PaymentPending` while a plan-change attempt is still
  pending. Both refusals happen before any provider call and before any attempt
  write, so **no payment row is created**; turning auto-renew off does not end the
  paid period and does not lift the block (the request still fails — `409
  Subscriptions.PlanChange.CancelRenewalRequested` once cancellation is
  requested, otherwise `PaidPeriodActive` — with no payment). The deferred
  `PUT /pending-downgrade` (applies only at the period boundary, no payment) and
  the read-only plan-change quote stay deliberately ungated. Changed:
  `PlanChangeCommands.cs` (+86/-2: `PlanChangePolicy` + paid-period and
  pending-attempt gates), `FamilySubscriptionsController.cs` (+9: explicit 409
  mapping), `SubscriptionPlanChangeTests.cs` (+194/-8: 6 Free-source fixtures +
  6 new tests including
  `Concurrent_plan_change_requests_during_a_paid_period_create_no_payment` and
  `Two_device_retry_while_a_plan_change_payment_is_pending_creates_no_second_payment`),
  `FamilySubscriptionsControllerTests.cs` (+16: 409 route theory), new
  `SubscriptionPremiumBenefitAccessTests.cs` (+183), `docs/app/families/subscriptions.md`
  (+40/-6: frozen approved set + plan-change/paid-period policy), `docs/postman/app/Sanad.App.Family.postman_collection.json`
  (+35/-6, also corrected the plan-change error code to `Subscriptions.Quote.ChargesNotConfigured`).
  Executed: Release build of `Sanad.UnitTests` with `/p:TreatWarningsAsErrors=true`
  → 0/0; focused
  `dotnet test --filter "FullyQualifiedName~Families.Subscriptions|FullyQualifiedName~Cms.Library|FullyQualifiedName~API.FamilySubscriptionsController"`
  → Passed 305 / Failed 0 / Skipped 3 / Total 308; Postman JSON parses as UTF-8
  (ConvertFrom-Json, 19 items); `git diff --check` exit 0; `subscriptions.md`
  anchors resolve. NOT RUN: full integrated sln gate (parent), live two-device
  HTTP readback (no runtime started), DB-level unique-index enforcement of the
  pending-attempt guard (would need a migration — report only). A full
  unit-suite run (`--no-build`, same Release binaries as the focused pass) was
  attempted and **aborted**: the test host crashed with
  `Import refused: ReportAlreadyExists` / `Import refused: ReviewedPlanHash`
  (a data-import guard, not subscriptions) → Failed 13 / Passed 5705 / Skipped 3.
  Those 13 are out of M2 scope (Identity account/support/verification and CMS
  HelpCenter unsupported-audience tests, the same cluster recorded in the prior
  `testout3.txt`/`testout5.txt`); none are in
  `Families.Subscriptions` / `Cms.Library` / `API.FamilySubscriptionsController`,
  which the focused filter covers green. No persisted-data
  correction required: seeded plan versions already carry the approved set (no
  plan withholds key 2) and no existing subscription row grants a benefit its
  plan withholds; the read-then-write pending guard shares the existing renewal
  TOCTOU pattern.
- M3: ready_for_review — CareHome create confirmed in source (no
  reimplementation). Route
  `POST /api/v1/care-homes/residents/mine/{residentId}/care-records`,
  `CareHomeCareRecordsController.Create` (CareHomeCareRecordsController.cs:190,
  policy `CareHomeOwnerAccess` at :35), source/attribution server-derived via
  `CareRecord.CreateForCareHome` (CareRecord.cs:138, `CareRecordSource.CareHome`
  at :153). Surfaces in both reads confirmed: family read combines all three
  sources when no `source` filter (FamilyCareRecords.cs:73-81 with
  CareRecordContracts.cs:124-133); elderly read same (ElderlyCareRecords.cs:61-69).
  Documented: new `docs/care-homes/care-records.md` (create Request/Response +
  both read routes), extended `docs/app/families/care-records.md`, index row in
  `docs/care-homes/README.md`. Postman refreshed: Sanad.CareHomes.Owner folder
  11 description, family elderly-read descriptions. Existing Postman entries
  already covered the create and both reads; no new entries were needed.
  Validated: 3 collections parse as UTF-8 JSON; `git diff --check` exit 0;
  doc links resolve. Limitations: doc-only — examples are authored, not run;
  care-home create has no Idempotency-Key layer (by source), noted in docs.
