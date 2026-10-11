# Project main goal

## Goal and priority

Sanad is an Arabic/English elderly-care platform. Agreed backend scope: Care homes (operator onboarding/review, inventory, Family discovery/stays/visits, payments/refunds/fees, ratings, Admin operations, in-app/email notifications), Chat (1:1 conversations, files, blocks, messages, reads, reports, alerts), Notifications (push delivery + email overhaul), and the Opus package (community categories/search/favorites, rating unification, CMS library, caregiver payout reads, chat deltas).

Care Homes, Chat, Notifications, and Opus are all implemented, merged to `main`, and gated green. Current head is `5edd341` (`origin/main` in sync): Chat Lane A (`5d26f9f`) + Lane B (`4864a93`), Notifications Lanes A+B (`9005f14`), Opus phases 1–6 (`2db70cc`), plus the care-home finance translation fix (`5edd341`, live-verified). Full unit suite passes 2907/0 with 3 PostgreSQL-only skips; architecture 1/1; contract mapping shows 519 controller signatures with 0 missing / 0 orphan Postman routes (Bruno stays risk-based, 0 orphans). Opus/Chat migrations remain authored-only and unapplied anywhere. Family/Elderly inputs are supplied; broader caregiver requirements will be discussed separately.

Care Homes checkpoint: all HC tasks complete through HC-TASK-062 (guide/Postman/evidence closeout, pushed). Later integrations landed on top: caregiver-money chain, community lane, Chat, Notifications, Opus, and the finance-read translation fix — see [Mastermind handoff](Mastermind_Handoff.md) for the current state. HC-TASK-052 is consolidated into HC-TASK-053.

Cross-slice note: the caregiver-money chain (T0 FinanceAdmin role, T1 bank catalogue, T2 payout account with IBAN encryption, T3 payout timing policy, T4 payout ledger with review workflow) and the community lane (T5 image upload with gated serving, T6 recommendations, T7 medical recipients) are implemented, merged, and gated green.

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
