# Project main goal

## Current VPS-readiness checkpoint

Prompt 5 delivered 3c9918d from 1120434; mastermind correction 64d372e is
integrated into main. Build zero warnings/errors; VPS 17/17; full unit 3093
passed with 3 existing PG skips; architecture 1/1; Postman mapping zero gaps.
[Tasks](work/chat-plus/tasks.md) and [handoff](work/chat-plus/handoff.md) own
status/next action. Owner-confirmed CORS stays unchanged and is not a gap.
Prompts 1 (session security) and 2 (physical orphan cleanup/storage bounds) are
integrated; see the Chat Plus handoff for current main SHA and evidence. The
Chat cleanup index migration is authored only. Prompt 3's Care Homes processor
translation fixes are integrated through Prompt 6 on pushed main; the Pkcs
transitive dependency is pinned to patched `10.0.13` and the security audit is
clear. Final automated gates: build 0 warnings/errors; Unit 3,120 passed,
8 skipped (5 PG integration cases, 3 existing Families skips); Architecture 1/1.
Actual Staging/production environment
readiness remains open. Prompt 4 is optional. Next: name/authorize the isolated
staging target and backup boundary, publish a fresh package from final main,
deploy only after approval, then run Prompt 7.

## Goal and priority

Sanad is an Arabic/English elderly-care platform. Agreed backend scope: Care homes (operator onboarding/review, inventory, Family discovery/stays/visits, payments/refunds/fees, ratings, Admin operations, in-app/email notifications), Chat (1:1 conversations, files, blocks, messages, reads, reports, alerts), Notifications (push delivery + email overhaul), and the Opus package (community categories/search/favorites, rating unification, CMS library, caregiver payout reads, chat deltas).

Baseline evidence from 2026-10-11: Care Homes, Chat, Notifications, and Opus were implemented and merged at `5edd341`: Chat Lane A (`5d26f9f`) + Lane B (`4864a93`), Notifications Lanes A+B (`9005f14`), Opus phases 1–6 (`2db70cc`), plus the care-home finance translation fix (`5edd341`, live-verified). Then-current gates were unit 2907/0 with 3 PostgreSQL-only skips, architecture 1/1, and 519 controller signatures with 0 missing/orphan Postman routes. The migration-unapplied claim from that checkpoint is superseded for Chat Plus by the disposable-target Phase 7 evidence. Family/Elderly inputs are supplied; broader caregiver requirements will be discussed separately.

Care Homes checkpoint: all HC tasks complete through HC-TASK-062 (guide/Postman/evidence closeout, pushed). Later integrations landed on top: caregiver-money chain, community lane, Chat, Notifications, Opus, and the finance-read translation fix — see [Mastermind handoff](Mastermind_Handoff.md) for the current state. HC-TASK-052 is consolidated into HC-TASK-053.

Cross-slice note: the caregiver-money chain (T0 FinanceAdmin role, T1 bank catalogue, T2 payout account with IBAN encryption, T3 payout timing policy, T4 payout ledger with review workflow) and the community lane (T5 image upload with gated serving, T6 recommendations, T7 medical recipients) are implemented, merged, and gated green.

## Chat Plus integration — 2026-10-13

Builder delivered `ded4dd1` from `41f9c91`; mastermind corrections are committed
in `9ca63b3`, integrated into main without conflicts. Build zero warnings/errors,
Chat Plus 64/64, unit 3076 passed with 3 existing PG skips, architecture 1/1.
[Chat Plus tasks](work/chat-plus/tasks.md)
owns status; [handoff](work/chat-plus/handoff.md) owns the next action and release
gaps; [integration evidence](work/chat-plus/reports/integration.md) records the
result. The historical baseline HEAD/gates above do not describe current main.
Production release readiness remains unproven until the listed external/runtime
and security/storage requirements close. Voice remains excluded.

## Canonical files

- [Mastermind handoff](Mastermind_Handoff.md): current state and next action.
- [Care homes decisions](operations/care-homes/Care_Homes_Decisions.md): consolidated owner requirements and remaining contract blockers.
- [Care homes tasks](operations/care-homes/Care_Homes_Tasks.md): the single implementation checklist, grouped by dependencies.
- [UI evidence](operations/care-homes/UI_Review.md): 29 reviewed screenshots.
- [Execution handoff](operations/care-homes/Execution_Handoff.md): current execution guidance for the active slice.
- [Care Homes API audience map](../care-homes/README.md) and [Postman collection guide](../postman/care-homes/README.md): Family, facility-owner, and Admin routes, permissions, and matching collections.
- [Governance](governance/AGENTS.md) and [workflow](operations/codex-workflow.md): flexible worker roles, focused risk-based verification, optional Bruno, and fixture-first preparation.

## Boundaries

Backend only; no frontend changes. Include the bounded caregiver review permission fix (SuperAdmin/SupportAdmin, ContentAdmin CMS-only) and Family caregiver ratings/top-10. Do not expand caregiver bookings or other future slices.

All 24 consolidated intake questions have responses; do not reopen resolved requirements. HC-TASK-038 payout eligibility/timing is implemented: manual transfer recorded by SuperAdmin/FinanceAdmin only after completed stay; later post-payout refunds/reversals become facility balance owed. The owner confirmed that the payout fee after a pre-payout refund uses the remaining payable, and post-payout debt is proportional to the facility net payout for the refunded share. Additive migrations are applied only to the authorized disposable database; no provider transfer was initiated.

Before any later slice is planned or developed, ask what the owner has in the application and agree its scope/tasks. Obtain explicit approval of the assembled plan before execution. Worker execution phases remain scout → implementer → test author → reviewer → documenter → mastermind verification/handoff.

The requested repository guidance default for the mastermind and Sanad workers is `gpt-6-luna` at medium reasoning. Platform-pinned role tools may continue to report `gpt-5.6-luna` as fixed and non-overridable; repository guidance cannot change a running role runtime.

## Cleanup

The former 103-file whole-project generated backlog is withdrawn. [Cleanup proposal](operations/care-homes/Cleanup_Proposal.md) records the cleanup proposal and retained-reference mapping.
