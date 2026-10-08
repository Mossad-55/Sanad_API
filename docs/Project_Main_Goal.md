# Project main goal

## Goal and priority

Sanad is an Arabic/English elderly-care platform. Current agreed scope is the Care homes backend: operator onboarding/review, inventory, Family discovery/stays/visits, payments/refunds/fees, ratings, Admin operations and in-app/email notifications.

Only Care Homes is currently planned, and that slice is complete. The next priority is Chat, followed by Notifications and full application UI reconciliation; those are directions, not an approved task backlog. Before planning Chat, review what already exists in the owner's application, discuss its scope and tasks, and obtain explicit approval of the assembled plan. Family/Elderly inputs are supplied; broader caregiver requirements will be discussed separately.

Current Care Homes checkpoint (2026-10-09): HC-TASK-037 receipts/revenue/CSV/internal notes, HC-TASK-040 visits, HC-TASK-050 Admin license expiry, HC-TASK-051 Admin booking inspection, HC-TASK-053 caregiver-review authorization, and HC-TASK-060 notifications are complete. HC-TASK-061 guide/Postman synchronization and HC-TASK-062 evidence/handoff closeout are complete. HC-060 focused tests passed 69/69 and the Release solution build passed with 0 warnings/errors; its outbox migration remains generated and unapplied. Documentation closeout commits `f6c4648` and `8353352` are pushed; `main` and `origin/main` were verified synchronized. See the checklist for verification limits and deferred work. HC-TASK-052 is consolidated into HC-TASK-053.

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
