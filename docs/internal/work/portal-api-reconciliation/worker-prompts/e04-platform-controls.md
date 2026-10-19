# E04 — Platform controls and public business status

**Whole-phase Worker brief.** Execute only after the Mastermind confirms this
contract, tracker, and brief are committed/pushed, records the pushed base SHA,
and verifies the Owner-selected Worker model. The Owner approved the linked
contract on 2026-10-19. This brief authorizes implementation only within the
manifest below; it does not authorize release, deployment, production access,
or database migration application.

Owner-selected Worker model for this phase: `opencode/beam-free#medium`.

## Read first

1. Repository `AGENTS.md` and `docs/internal/governance/AGENTS.md`.
2. `docs/internal/Mastermind_Handoff.md` and the E04 tracker:
   [`tasks/e04-platform-controls.md`](../tasks/e04-platform-controls.md).
3. The complete approved contract:
   [`contracts/e04-platform-controls.md`](../contracts/e04-platform-controls.md).
4. Inspect the exact pinned base SHA and worktree before editing. Preserve all
   pre-existing and unrelated edits. If the pinned base/manifest is not the one
   recorded by the Mastermind, stop and report `BLOCKED`.

## Start here

Start **E04-W01** in the phase tracker. It is one whole-phase task owned by this
Worker; implement its numbered contract responsibilities in order and keep its
live status/checkpoint current. Do not start any other phase or agent.

## Objective and bounded behavior

Implement the Owner-approved contract without broadening it:

- A single database-backed operational/maintenance state, bilingual public
  message, Card/Wallet switches, append-only actor/time/before/after audit, and
  optimistic concurrency; use the existing Finance persistence boundary and
  `finance` schema. Do not add a general settings registry or new DbContext.
- SuperAdmin-only control read/update APIs and a sanitized anonymous public
  status API using the approved contract routes and response shape.
- Maintenance blocks only the named new caregiver booking checkout, Care Home
  booking checkout, and new caregiver/Care Home/subscription/plan-change payment
  intent creation paths. Payment-mode switches are enforced server-side.
- Keep status/health, authentication, Admin control access, provider callbacks,
  existing intent reuse/callback settlement, reads, cancellations, care-delivery,
  refunds, and other unlisted operations available as specified by the contract.
- Advertise Card/Wallet only when enabled and configured. Do not advertise or
  implement ApplePay, new providers, gateway-secret changes, or UI features.
- Missing/unreadable platform control state must fail closed for the gated
  operations; never treat a failed read as Operational. Do not apply migrations.

## Exact owned-file manifest

Modify/create only these files. If a necessary file is missing or the scope
requires another path, stop and report the path/reason; do not self-expand.

**API and authorization**
- `src/API/Sanad.API/Authorization/AuthorizationPolicies.cs`
- `src/API/Sanad.API/DependencyInjection.cs`
- `src/API/Sanad.API/Controllers/AdminPlatformControlsController.cs` (new)
- `src/API/Sanad.API/Controllers/PlatformStatusController.cs` (new)

**Finance persistence and application**
- `src/Modules/Finance/Domain/Sanad.Modules.Finance.Domain/PlatformControlState.cs` (new)
- `src/Modules/Finance/Application/Sanad.Modules.Finance.Application/PlatformControlCommands.cs` (new)
- `src/Modules/Finance/Application/Sanad.Modules.Finance.Application/PlatformChargeCommands.cs`
- `src/Modules/Finance/Infrastructure/Sanad.Modules.Finance.Infrastructure/FinanceDbContext.cs`
- `src/Modules/Finance/Infrastructure/Sanad.Modules.Finance.Infrastructure/DependencyInjection.cs`
- `src/Modules/Finance/Infrastructure/Sanad.Modules.Finance.Infrastructure/PlatformControlStore.cs` (new)
- `src/Modules/Finance/Infrastructure/Sanad.Modules.Finance.Infrastructure/Persistence/Migrations/20261019061622_AddE04PlatformControls.cs` (new)
- `src/Modules/Finance/Infrastructure/Sanad.Modules.Finance.Infrastructure/Persistence/Migrations/20261019061622_AddE04PlatformControls.Designer.cs` (new)
- `src/Modules/Finance/Infrastructure/Sanad.Modules.Finance.Infrastructure/Persistence/Migrations/FinanceDbContextModelSnapshot.cs`

**Business enforcement**
- `src/Modules/Families/Application/Sanad.Modules.Families.Application/Bookings/BookingCommands.cs`
- `src/Modules/Families/Application/Sanad.Modules.Families.Application/Bookings/BookingPaymentCommands.cs`
- `src/Modules/Families/Application/Sanad.Modules.Families.Application/Subscriptions/SubscriptionPaymentCommands.cs`
- `src/Modules/Families/Application/Sanad.Modules.Families.Application/Subscriptions/PlanChangeCommands.cs`
- `src/Modules/CareHomes/Application/Sanad.Modules.CareHomes.Application/Bookings/CareHomeBookingCommands.cs`

**Focused tests**
- `tests/Sanad.UnitTests/Finance/PlatformControlsTests.cs` (new)
- `tests/Sanad.UnitTests/API/PlatformControlsControllerTests.cs` (new)
- `tests/Sanad.UnitTests/API/PortalOpenApiContractTests.cs`
- `tests/Sanad.UnitTests/Families/BookingPaymentIntentTests.cs`
- `tests/Sanad.UnitTests/Families/BookingRemediationTests.cs`
- `tests/Sanad.UnitTests/Families/Subscriptions/SubscriptionPaymentTests.cs`
- `tests/Sanad.UnitTests/Families/Subscriptions/SubscriptionPlanChangeTests.cs`
- `tests/Sanad.UnitTests/CareHomes/CareHomeBookingLifecycleTests.cs`

**Feature docs and Postman**
- `docs/admin/platform-controls.md` (new)
- `docs/app/families/bookings.md`
- `docs/app/families/subscriptions.md`
- `docs/care-homes/booking-lifecycle.md`
- `docs/postman/admins/Sanad.Admin.Finance.postman_collection.json`
- `docs/postman/app/Sanad.App.Public.postman_collection.json`
- `docs/postman/app/Sanad.App.Family.postman_collection.json`
- `docs/postman/app/Sanad.App.Family.CareHomes.postman_collection.json`

**Phase evidence**
- `docs/internal/work/portal-api-reconciliation/tasks/e04-platform-controls.md`
- `docs/internal/work/portal-api-reconciliation/reports/e04-platform-controls-worker-delivery.md` (new)

## Required implementation/checklist output

- Add exactly one initial `Operational` state safely via the migration; keep the
  audit append-only. Concurrency conflicts must not overwrite current state.
- Verify unauthorized roles cannot read/change controls; SuperAdmin can update
  and read back state/history. Public status exposes no internal reason,
  provider IDs/secrets, or infrastructure detail.
- Cover maintenance and method gates in each listed business path; explicitly
  test that allowed exceptions and unlisted lifecycle operations still work.
  Verify Card/Wallet disabled or unconfigured behavior and ApplePay exclusion.
- Check provider callback/duplicate settlement continuity without calling a real
  provider or applying migrations.
- Sync docs and relevant Postman examples to implemented, verified behavior.
  Do not invent Admin UI/screens; no UI files are authorized.
- Run focused tests for all changed paths and a strict Release solution build
  with zero warnings/errors. The Mastermind will decide whether broader tests
  are required; do not run stateful tests against shared/Production targets.
  Distinguish authored from executed checks. Record every unrun check as
  `NOT RUN` with reason where known.
- Update E04-W01 to `ready_for_review`, never `completed`; only the Mastermind
  may mark it completed after validation.

## Checkpoint, stop, and final report

Before work, after each meaningful milestone, on block, and at handback, record
task ID, session if available, timestamp/timezone, pinned base/current revision,
changed files, checks and PASS/FAIL/NOT RUN state, blockers, and the next exact
action in the tracker. Do not claim progress past the last recorded checkpoint.

Return `COMPLETE` (ready for Mastermind review) or `BLOCKED`, the changed-file
list, commands/results/counts/warnings/skips, authored-vs-run checks, migration
authored/not-applied statement, limitations, checkpoint location, and the exact
next action. Never commit, push, publish, deploy, apply migrations, contact real
providers, or edit files outside this manifest.
