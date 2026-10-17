# Project main goal

## Current slice — Admin and Care Homes portal API reconciliation

2026-10-16 follow-up: owner authorized a phased report/plan setup for the new
46-item frontend intake. The [FUR sequence](work/portal-api-reconciliation/plan.md#frontend-follow-up-sequence--2026-10-16)
is the task source of truth; [dated Admin/App handoffs](../frontend-handoffs/README.md)
are audience-facing evidence/actions. FUR-00 documentation is Done; subsequent
backend contracts are not approved by this structure. Owner then approved the
19-phase full-report roadmap (FUR-00–10 plus E01–08). FUR-01 is now accepted,
pushed and deployed at `f9231fb`: 29/29 metadata contracts, zero config/schema
delta, full unit 3421/0/8, architecture 1/1. See the
[release receipt](work/portal-api-reconciliation/reports/fur-01-deployment.md).
FUR-02 is accepted and deployed at `5cd3526` with its one additive Identity
migration; see its [phase receipt](work/portal-api-reconciliation/reports/fur-02-deployment.md).
FUR-03 is accepted/pushed at `858da09`; verification-only, deployment N/A. See
its [phase receipt](work/portal-api-reconciliation/reports/fur-03-release.md).
FUR-04 delivery is accepted and its commit/push/deployment are owner-authorized;
the release is in progress. See the current Mastermind
handoff for exactly one next action. Earlier PORTAL completion remains valid.
Owner's standing phase workflow: workers implement and return complete reports;
Mastermind plans/reviews/integrates. After full-phase acceptance and any worker
corrections, Mastermind commits/pushes and deploys/verifies before advancing.
No per-task deploy or silent Mastermind takeover of feature implementation.

Current update: owner-approved V1 geography preserve-and-add provisioning is
Done. Final 27 governorates / 349 Cities / 5714 Areas (legacy extras retained),
all nine original lookup records and five selections unchanged, zero orphans.
Private DB backup retained, API/public hierarchy verified; see
[executed receipt](work/portal-api-reconciliation/reports/geography-import-preserve-add.md).
Focused 119/119 and strict build zero warnings/errors. Earlier pending-import
checkpoints below are historical. No new API binary/config change was needed.

Owner supplied a frontend issue report for verification, planning and targeted
worker tests. The [portal reconciliation plan](work/portal-api-reconciliation/plan.md)
owns the finding verdicts, approved tasks, fixture strategy and audit evidence
at `8cbeedd`. The owner approved core execution on 2026-10-15. PORTAL-01
reactivation delivery `4f71493` is accepted after mastermind review/test
strengthening (focused 57/57, Identity model 7/7, architecture 1/1, strict build
zero warnings/errors). PORTAL-02 delivery `4962053` is accepted after a bounded
mastermind parser-compatibility correction and added example/regression tests
(Library/controller 56/56; strict build zero warnings/errors; architecture 1/1;
contract mapping zero missing/orphans). PORTAL-03 delivery `57f9d3e` plus
correction `13a85ca` is accepted: safe host, finance empty HTTP 204, valid nullable
enum union; mastermind rerun 85/85 and mapping zero missing/orphans, valid
worker strict build zero warnings/errors and architecture 1/1 reused.
PORTAL-04 delivery `33af090` is accepted after bounded diagnostic SQL/runbook
corrections and mastermind rerun 24/24. Deployed caregiver mismatches remain
unverified pending operator evidence; no production data repair is authorized.
PORTAL-05 delivery `6b32906` is accepted with mastermind test strengthening:
Draft/Approved owner scenarios and no-facility finance, stable ID/date assertions;
strict build zero warnings/errors, combined 126/126, mapping zero gaps. No
production-code change or deployment. PORTAL-07 delivery `0561298` is now accepted
locally: mastermind guarded full unit 3308/0/8, architecture 1/1, Postman 22/22,
mapping zero gaps, corrected [frontend handoff](work/portal-api-reconciliation/frontend-handoff.md).
The excluded first worker run's local-test-DB reset violation remains documented.
Owner has now authorized Phase 06 preparation: the
[06A source qualification brief](work/portal-api-reconciliation/worker-prompts/06a-geography-source-qualification.md)
has delivered research (`cdf8a2a`), reviewed/accepted without approving a dataset.
The partial levels-1/2 proposal does not close the three-level picker gap.
Owner explicitly chose **All Egypt**. The
[06A2 national catalogue brief](work/portal-api-reconciliation/worker-prompts/06a2-national-catalogue.md)
delivered candidate `5918813` (27/346/5706), but review found wrong governorate
assignments for Armant/Esna, validator false passes, and evidence/doc gaps.
[Bounded 06A2 corrections](work/portal-api-reconciliation/worker-prompts/06a2-review-corrections.md)
delivered `1b0527b` and are accepted as correction work: structural 27/347/5712,
12/12 fixtures; readiness still fails on unresolved evidence and coverage.
Owner then requested completion: direct OCHA/HDX comparison now qualifies the
historical Arabic backbone, but the reference explicitly has partial Area
coverage/2017 boundaries and no Area English names. See
[remaining source dependency](work/portal-api-reconciliation/reports/phase-06-data-qualification.md).
The owner subsequently accepted the collected coverage for V1 subject to a full
scan. [V1 scan](work/portal-api-reconciliation/reports/phase-06-v1-scan.md) is Done
with findings: real-handler compatibility over all 6086 records, focused 90/90,
zero-warning/error strict build; four question-mark labels and 12 delimiter
flags documented. Owner approved the exact 16-label display cleanup, applied
locally with original values retained and all IDs/parents/counts unchanged.
See [cleanup closeout](work/portal-api-reconciliation/reports/phase-06-label-cleanup.md).
Owner then authorized local importer development. The standalone
[06B importer](work/portal-api-reconciliation/reports/phase-06-importer.md)
is implemented/tested locally: strict build zero warnings/errors, focused
113/113, architecture 1/1; full catalogue initial/repeat SQLite-memory import.
Owner-requested shortest-route rehearsal also passed on a new disposable local
PostgreSQL 18.4 cluster: 6086 initial inserts, zero repeat inserts, final counts
27/347/5712 with zero orphans; server stopped, existing databases untouched.
Owner then approved commit/push, publication and deployment. Release `9167e71`
is now deployed and externally verified after SSH setup: Production, 9/9 applied
migration heads match, zero migrations applied at startup, services/OpenAPI/
governorates HTTP 200. Config preserved and recoverable file backup retained;
see [receipt](work/portal-api-reconciliation/reports/deployment-9167e71.md).
Next is the separate owner decision on the geography data operation;
not another exhaustive national-source prerequisite for the accepted V1 scope.
No records were marked geographically/linguistically certified by the display cleanup.
Production lookup replacement/reset is not authorized. Runtime/device and caregiver operator
evidence are not closed.
The current VPS runs Production and is used for developer testing by owner
direction; a separately hardened VPS is future work. Existing public deployment
smoke/revision/migration evidence does not establish complete portal or Chat Plus
runtime coverage. CORS remains unchanged; Bruno is excluded.

## Previous VPS-readiness publishing checkpoint

Chat Plus Prompts 1–6 and VPS-readiness implementation are integrated; Prompt 4
is optional, and Prompt 7 owns authorized post-deploy/runtime/device checks.
Owner-confirmed CORS stays unchanged and is not a gap. The Chat cleanup-index
migration is authored and included as the latest Chat migration in the release
manifest; deployment applies it only under the approved startup/migration policy.
Release gates passed on clean pushed `main`: strict build 0 warnings/errors;
Unit 3,120 passed, 0 failed, 8 skipped (5 PostgreSQL integration cases disabled
for this non-destructive run and 3 pre-declared Families skips); Architecture
1/1; Postman mapping 556 signatures / 620 requests / zero missing/orphans.
The Release package is generated from pushed `main`; see [Chat Plus tasks](work/chat-plus/tasks.md)
and [handoff](work/chat-plus/handoff.md) for remaining owner/runtime gates.
Staging and production runtime readiness are not claimed. The owner deploys;
Prompt 7 must wait for the exact authorized target and deployment report.

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
