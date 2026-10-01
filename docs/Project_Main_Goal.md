# Project main goal

## Goal and priority

Sanad is an Arabic/English elderly-care platform. Current agreed scope is the Care homes backend: operator onboarding/review, inventory, Family discovery/stays/visits, payments/refunds/fees, ratings, Admin operations and in-app/email notifications.

Only this slice is planned. Future direction is Care homes → Chat → Notifications → full application UI reconciliation, not a prewritten task backlog. Family/Elderly inputs are supplied; broader caregiver requirements will be discussed separately.

## Canonical files

- [Mastermind handoff](Mastermind_Handoff.md): current state and next action.
- [Care homes decisions](care-homes/Care_Homes_Decisions.md): consolidated owner requirements and remaining contract blockers.
- [Care homes tasks](care-homes/Care_Homes_Tasks.md): the single implementation checklist, grouped by dependencies.
- [UI evidence](care-homes/UI_Review.md): 29 reviewed screenshots.
- [Execution handoff](care-homes/Execution_Handoff.md): prompt for the next mastermind, gated by plan approval.
- [Governance](governance/AGENTS.md) and [workflow](operations/codex-workflow.md): unchanged worker sequence and mandatory verification.

## Boundaries

Backend only; no frontend changes. Include the bounded caregiver review permission fix (SuperAdmin/SupportAdmin, ContentAdmin CMS-only) and Family caregiver ratings/top-10. Do not expand caregiver bookings or other future slices.

All 24 consolidated intake questions have responses; do not reopen resolved requirements. Fee/payout details and a few verification/lifecycle edge contracts remain explicit blockers for dependent work. No payment business rule may be invented.

Before any later slice is planned or developed, ask what the owner has in the application and agree its scope/tasks. Obtain explicit approval of the assembled plan before execution. Worker execution phases remain scout → implementer → test author → reviewer → documenter → mastermind verification/handoff.

## Cleanup

The former 103-file whole-project generated backlog is withdrawn. [Cleanup proposal](care-homes/Cleanup_Proposal.md) identifies unused generated drafts for separately confirmed deletion; retain historical migration evidence and all original source evidence until reconciled. No deletion or product implementation has occurred.
