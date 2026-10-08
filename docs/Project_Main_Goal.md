# Project main goal

## Goal and priority

Sanad is an Arabic/English elderly-care platform. Current agreed scope is the Care homes backend: operator onboarding/review, inventory, Family discovery/stays/visits, payments/refunds/fees, ratings, Admin operations and in-app/email notifications.

Only this slice is planned. Future direction is Care homes → Chat → Notifications → full application UI reconciliation, not a prewritten task backlog. Family/Elderly inputs are supplied; broader caregiver requirements will be discussed separately.

Current Care Homes checkpoint (2026-10-08): HC-TASK-037 receipts/revenue/CSV/internal notes, HC-TASK-040 visits, HC-TASK-050 the Admin expiring-license view, and HC-TASK-051 Admin booking inspection are complete. HC-051 focused tests passed 5/5 and the final Release solution build passed with 0 warnings/errors; its guide/Postman updates are synchronized. No migration, Bruno/runtime, database/provider, production, reset, or deployment activity occurred. The Admin navigation recommendation is Overview, Bookings, Refunds & disputes, with payouts separate. Three active feature checklist items remain: HC-060–062. The HC-051 commit is rebased on latest `origin/main`; final push/remote synchronization is the remaining closeout. HC-TASK-052 is consolidated into completed HC-TASK-053; details live in the checklist and execution handoff.

## Canonical files

- [Mastermind handoff](Mastermind_Handoff.md): current state and next action.
- [Care homes decisions](operations/care-homes/Care_Homes_Decisions.md): consolidated owner requirements and remaining contract blockers.
- [Care homes tasks](operations/care-homes/Care_Homes_Tasks.md): the single implementation checklist, grouped by dependencies.
- [UI evidence](operations/care-homes/UI_Review.md): 29 reviewed screenshots.
- [Execution handoff](operations/care-homes/Execution_Handoff.md): current execution guidance for the active slice.
- [Care Homes API audience map](care-homes/README.md) and [Postman collection guide](postman/care-homes/README.md): Family, facility-owner, and Admin routes, permissions, and matching collections.
- [Governance](governance/AGENTS.md) and [workflow](operations/codex-workflow.md): flexible worker roles, focused risk-based verification, optional Bruno, and fixture-first preparation.

## Boundaries

Backend only; no frontend changes. Include the bounded caregiver review permission fix (SuperAdmin/SupportAdmin, ContentAdmin CMS-only) and Family caregiver ratings/top-10. Do not expand caregiver bookings or other future slices.

All 24 consolidated intake questions have responses; do not reopen resolved requirements. HC-TASK-038 payout eligibility/timing is implemented: manual transfer recorded by SuperAdmin/FinanceAdmin only after completed stay; later post-payout refunds/reversals become facility balance owed. The owner confirmed that the payout fee after a pre-payout refund uses the remaining payable, and post-payout debt is proportional to the facility net payout for the refunded share. Additive migrations are applied only to the authorized disposable database; no provider transfer was initiated.

Before any later slice is planned or developed, ask what the owner has in the application and agree its scope/tasks. Obtain explicit approval of the assembled plan before execution. Worker execution phases remain scout → implementer → test author → reviewer → documenter → mastermind verification/handoff.

The requested repository guidance default for the mastermind and Sanad workers is `gpt-6-luna` at medium reasoning. Platform-pinned role tools may continue to report `gpt-5.6-luna` as fixed and non-overridable; repository guidance cannot change a running role runtime.

## Cleanup

The former 103-file whole-project generated backlog is withdrawn. [Cleanup proposal](operations/care-homes/Cleanup_Proposal.md) records the cleanup proposal and retained-reference mapping.
