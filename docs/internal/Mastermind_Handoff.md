# Mastermind handoff

## Current operational handoff — 2026-10-19

- **Active task:** WF-M06 — commit/push the approved isolated workflow cleanup;
  see the [workflow refresh tracker](work/workflow-refresh/tasks.md).
- **Current checkpoint:** Owner renewed commit/push authorization for the isolated
  cleanup scope; the [workflow refresh tracker](work/workflow-refresh/tasks.md)
  records the staged file list and verification. Pre-existing overlapping and
  unrelated edits remain excluded. No product phase is active.
- **Blockers:** broader cross-machine task-state policy remains undecided.
- **Next exact action:** commit and push only the reviewed cleanup scope. E04
  remains at options/intake; prepare and approve its exact contract before any
  worker starts.

## Historical handoff record — retained, not operational

The dated material below is retained as historical evidence. Its old next-action
and status wording is not current instruction; use the compact checkpoint above,
the active phase tracker, and linked release receipts for current truth. Do not
rewrite or delete historical receipts as part of routine handoff updates.

FUR-07 is Done at pushed/deployed revision `303a131`; see its
[release receipt](work/portal-api-reconciliation/reports/fur-07-release.md).
The approved Community moderation contract and worker delivery are recorded in
the [contract](work/portal-api-reconciliation/contracts/fur-07-community-moderation.md)
and [delivery report](work/portal-api-reconciliation/reports/fur-07-worker-delivery.md);
Mastermind review acceptance is recorded in this historical checkpoint.
Its additive Community migration was applied to the existing production
database. The deployment verifier passed through the SSH tunnel: exact
revision, database reachable, all 9/9 migration heads matched, all nine
startup checks present, and anonymous services smoke HTTP 200. The worker's
153 focused tests and strict zero-warning build were reused; no tests were
rerun during release. The PostgreSQL advisory-lock path was not exercised
against a live PostgreSQL server.

FUR-08 Owner dispute readback was accepted, committed, pushed and deployed as
`14757ccce6ad1fb59482ca26d4df5e17ca8c56d9`. See the [worker delivery](work/portal-api-reconciliation/reports/fur-08-worker-delivery.md)
and [release receipt](work/portal-api-reconciliation/reports/fur-08-release.md).
Worker-reported gates: strict Release build 0 warnings/errors, focused tests
20/20, Postman JSON/route/defaults and diff check passed. The deployment
verifier passed through the SSH tunnel: exact revision, Production, database
reachable, 9/9 migration heads matched, 9 startup checks with zero migrations
applied, and services smoke HTTP 200. Service active/NRestarts=0; protected host
configuration fingerprints unchanged. No live authenticated FUR-08 endpoint
or Bruno verification was performed. Direct public HTTP redirected to an ISP
HTML page; tunnel verification was used. No database data, provider, or migration
change occurred. FUR-09 is Done for its approved local diagnostic/API-test scope;
the six reported Production rows remain unverified.

## Completed phase — FUR-09 Caregiver integrity disposition

Owner approved the amended local-only API reproduction contract on 2026-10-18.
Worker session `ses_eb35f9129ffemhsaCSp0hd8GJJ` delivered the in-process
`GET /api/v1/admin/caregivers` synthetic SQLite reproduction. Mastermind reviewed
the new test and sanitized [disposition report](work/portal-api-reconciliation/reports/fur-09-caregiver-integrity-disposition.md).
Worker-reported strict Release build passed with 0 warnings/errors; focused
caregiver API/integrity tests passed 24/24; `git diff --check` passed. No
Production/shared DB access or changes. The six Production rows remain
unverified, and no repair/quarantine is recommended on synthetic evidence.
Deployment N/A (test/report only).

**Next action:** stop after FUR-09 closeout and await the Owner's direction for
any next separately approved phase; do not advance automatically.

## Completed phase — FUR-10 Integration and release handoff

FUR-10 was accepted as a documentation-only cross-phase closeout. Its [contract](work/portal-api-reconciliation/contracts/fur-10-cross-phase-integration-handoff.md),
[worker brief](work/portal-api-reconciliation/worker-prompts/fur-10-cross-phase-integration-handoff.md),
and [closeout report](work/portal-api-reconciliation/reports/fur-10-integration-release-handoff.md)
record the FUR-00–FUR-09 ledger, audience handoffs, index and project-summary
sync. Worker-reported validation resolved 166/166 relative links and passed
`git diff --check`; no runtime/API, Production, database, provider, build, test,
commit, or deployment action occurred. FUR-09 Production rows remain unverified;
E01–E08 remain Not started/uncontracted. Deployment N/A.

**Next action:** stop after FUR-10 closeout and await the Owner's direction for
the next separately approved roadmap phase; do not advance automatically.

## Completed phase — E01 User statistics and growth

Owner approved the aggregate-only E01 contract on 2026-10-18, based on the
provided Super Admin dashboard screenshot. The [contract](work/portal-api-reconciliation/contracts/e01-user-statistics-growth.md)
and [whole-phase Worker brief](work/portal-api-reconciliation/worker-prompts/e01-user-statistics-growth.md)
are saved. E01 does not include financial monitoring, the undefined services
series, PII, user-level drill-down, or broader roles. Existing unrelated
worktree changes are preserved.

The Owner selected OpenCode Zen `opencode/longcat-2.5-preview-free` for E01;
after its rate limit, the Owner authorized the same child
`ses_eb0cce48effeiIbvjsmV0itcR6` to continue on `opencode/beam-free#high` and
approved numeric enum-valued arrays. The local implementation is complete and
reviewed. Strict Release build passed with zero warnings/errors; focused
endpoint/OpenAPI tests passed 67/67. The broader portal response-contract class
had one unrelated owner-dispute nullable-schema failure (39/40). Postman JSON
and `git diff --check` passed. See the [E01 delivery report](work/portal-api-reconciliation/reports/e01-user-statistics-growth-delivery.md).

E01 implementation was committed as `d7f1e086043ba4807b1d5b1cd4ab3b10f9e2b857`
and pushed to `origin/main`; the documentation receipt is
`ea14e668a0dee95a7b4aac61e507abb235ba8d7c`. At the Owner's instruction, that
exact pushed main revision was packaged and deployed to the existing owner-
designated Production VPS. The standard deployment verifier passed; see the
[E01 deployment receipt](work/portal-api-reconciliation/reports/e01-user-statistics-growth-deployment.md).
No authenticated request to the new statistics endpoint was made in Production;
the contract's live-feature-request exclusion remains observed. No E02–E08 work
was authorized by the E01 contract; E02 was subsequently approved separately.

## Completed phase — E02 Owner legal support

The Owner approved the [E02 contract](work/portal-api-reconciliation/contracts/e02-owner-legal-support.md)
on 2026-10-18. The [whole-phase Worker brief](work/portal-api-reconciliation/worker-prompts/e02-owner-legal-support.md)
is saved. Worker session `ses_eb062abc4ffe0GKTIBTVVJMHBG` completed the approved
implementation and bounded review corrections on pinned `main` revision
`cae46032fc5448592a2a324122bedd41503e7e1f`, using the Owner-selected OpenCode Zen
model `opencode/beam-free#high`. Mastermind review accepted the source, Owner
legal/Help/OpenAPI tests, guides, and Admin/Owner Postman updates. The strict
Release build passed with 0 warnings/errors; focused Legal/Help/Owner API/OpenAPI
tests passed 20/20; both Postman collections validated; `git diff --check` was
clean apart from pre-existing CRLF warnings. The broader
`PortalRemainingResponseContractTests` run had one unrelated owner-dispute
`checkoutOnUtc` nullability failure (52 passed/1 failed), retained and unmasked.
E02 implementation `80d87bb1cf9059f3dcb7efbdd8df4ef6219ec627` was committed,
pushed, and deployed to the existing Owner-designated Production VPS. The
standard deployment verifier passed with all 9/9 migration heads matched,
zero migrations applied, service active with zero restarts, and anonymous
services smoke HTTP 200. No authenticated legal feature request was made in
Production. See the [release receipt](work/portal-api-reconciliation/reports/e02-owner-legal-support-release.md).

E02 closeout is complete; E03 was separately selected and approved on 2026-10-18.

## Completed phase — E03 Caregiver medical-access decision audit

The Owner approved the [E03 contract](work/portal-api-reconciliation/contracts/e03-medical-access-audit.md)
on 2026-10-18. Mastermind completed bounded review corrections after several
Worker deliveries stopped incomplete. A relational filter test exposed and
verified the fix for EF translation of strongly typed IDs. Strict Release build
passed with 0 warnings/errors; focused Families/Admin/retention/OpenAPI tests
passed 39/39. Both Postman collections and route mappings were verified. The
additive Families migration was applied; no authenticated E03 feature request
was made in Production. See the [release receipt](work/portal-api-reconciliation/reports/e03-medical-access-audit-release.md).

**Next action:** stop after E03 closeout and await the Owner's direction; do not
start E04 or another phase automatically.

## Active slice — Admin and Care Homes portal report reconciliation

### Current follow-up — 2026-10-16

Owner requested a step-by-step plan and dated frontend reports separated by
Admin/App audience, phase and issue. FUR-00 documentation setup is Done;
the [canonical follow-up sequence](work/portal-api-reconciliation/plan.md#frontend-follow-up-sequence--2026-10-16)
owns status. [Audience index](../frontend-handoffs/README.md) links the Admin and
Care Home Owner alignment reports; the [full audit](work/portal-api-reconciliation/frontend-report-audit-2026-10-16.md)
remains detailed evidence. No frontend repo inspection or external sending is
claimed. No API/data/provider/config changes or commit/push are authorized by
this setup. Future features and role decisions need their own locked contracts.
Validation: 17/17 report file links, 46/46 intake references; documentation-only
checks, no new runtime tests. Reports are ready for owner review/forwarding.
Audience correction: Admin = Admin portal + Care Home Owner portal; App =
Family/Caregiver/Elderly only. Reports relocated without changing findings.
FUR-01's [approved contract](work/portal-api-reconciliation/contracts/fur-01-contract-accuracy.md)
now bounds 29 existing-operation metadata corrections, docs/examples and tests;
no runtime behavior, endpoint, configuration or database change. Phase Done.
Owner subsequently approved the **19-phase roadmap** (FUR-00–10 plus E01–08),
with FUR-00 Done / 18 remaining. This is roadmap approval, not blanket approval
of detailed feature/policy contracts or release operations. FUR-01
[execution brief](work/portal-api-reconciliation/worker-prompts/fur-01-contract-accuracy.md)
is now executable after separate owner approval of the exact FUR-01 contract.
Latest owner workflow (2026-10-16) supersedes earlier release-approval wording for
this roadmap: Mastermind plans/contracts/instructs/reviews/integrates/reports;
owner's workers implement code/tests and feature docs/Postman. Internal worker
interrupted before production/test edits; do not silently resume or replace it.
Worker completes a **whole phase**, saves report and stops; owner returns report
here. Mastermind reviews actual source/tests/docs and reports concrete findings
and required modifications; owner routes corrections to the worker. After
acceptance Mastermind presents final evidence and asks the owner for explicit
commit/push/publish/deploy approval. Contract approval is not release approval.
Only after that approval does Mastermind release and verify before advancing.
No per-task/partial-phase deploy. Done requires accepted evidence plus verified
deployment (or explicit N/A for pure docs/research with no API release delta).
No current release is being performed. Target/config/backup/migration preflight,
privacy and no-reset boundaries remain; new material decisions need owner input.
Worker FUR-01 delivery received at unchanged HEAD `4b65369`; reported build clean,
172 focused / architecture 1/1 (worker evidence, not independently rerun).
Mastermind review: production changes are metadata-only, actual route sets
556 unique each / zero missing-orphan. Changes requested: stale login/refresh
wrapper examples and activation prose, tests not reading actual examples,
11 broken audience links and wrong backend/frontend attribution/status wording.
See [review](work/portal-api-reconciliation/reports/fur-01-mastermind-review.md).
Subsequent corrected delivery re-reviewed and accepted: R1–R5 resolved; Mastermind
strict build zero warnings/errors, guarded full unit 3421/0/8, architecture 1/1,
32/32 audience links and actual full route sets 556 each / zero missing-orphaned.
Earlier restricted test-runner timeouts discarded; valid guarded reruns passed.
Release complete: `f9231fb` pushed/published/deployed; active/running, zero
restarts, all 29 metadata operations verified, nine migration heads match/zero
applied and configuration checksums unchanged. Private rollback backups retained.
Standard verifier passed through a temporary SSH tunnel; direct public HTTP from
this machine returned HTML, so that network path is explicitly unproven.
See [receipt](work/portal-api-reconciliation/reports/fur-01-deployment.md).
At the FUR-02 checkpoint, FUR-00 through FUR-02 were Done / 16 phases remained;
the current FUR-03 closeout is recorded below.
FUR-02 [contract](work/portal-api-reconciliation/contracts/fur-02-verification-continuity.md)
approved by owner on 2026-10-16. Scope: additive restricted-login recovery
IDs, send-first resend with safe 502 provider error, one Identity concurrency
migration and disposable PostgreSQL proof. Expired pending IDs remain
recoverable for resend. [Whole-phase worker brief](work/portal-api-reconciliation/worker-prompts/fur-02-verification-continuity.md)
was executed by the owner's worker with fake providers; no live-provider test occurred.
Worker FUR-02 [delivery](work/portal-api-reconciliation/reports/fur-02-worker-delivery.md)
and [Mastermind review](work/portal-api-reconciliation/reports/fur-02-mastermind-review.md):
R1–R3 and S1–S2 corrected; complete local phase accepted for release preparation.
Worker final gates: strict build zero warnings/errors; focused resend 19/0;
guarded full unit 3458/0/12; architecture 1/1; earlier disposable PostgreSQL
proof 4/4. Mastermind independently confirmed strict build zero warnings/errors,
Postman JSON parsing and clean `git diff --check`; independent full-unit
rerun aborted/stalled at testhost startup, so worker's successful guarded run
is the valid full-suite evidence. Owner authorized commit/push and deployment
of the complete phase, including necessary fresh package publication; do not
request a separate literal `publish` authorization for this release. The
implementation commit `5bde52383071a2c20d17e3059038f729c1b64a70` and
release-tool/workflow correction `5cd3526b53e8555d3233810b8a024fcb6aa0322e`
were pushed to `origin/main`. The latter is the exact deployed binary revision.
The publisher now derives all nine migration heads from source, avoiding the
stale Identity pin. See [publish](operations/publish.md) and
[deploy runbook](operations/deploy-runbook.md) for the standing workflow.
FUR-02 [deployment receipt](work/portal-api-reconciliation/reports/fur-02-deployment.md):
fresh package and exact-target private database backup verified; Production
service active/running, `NRestarts=0`, exact revision `5cd3526`, Identity one
migration applied, other eight zero, all 9/9 heads matched, configuration
unchanged. On-host and SSH-tunneled standard deployment verification passed;
no real-provider test, reset or seed. Direct public HTTP from this machine is
still unproven. FUR-02 is Done; a subsequent documentation-only receipt commit
does not warrant redeployment. Owner approved the verification-first
[FUR-03 contract](work/portal-api-reconciliation/contracts/fur-03-finance-verification.md)
on 2026-10-16. Its [whole-phase worker brief](work/portal-api-reconciliation/worker-prompts/fur-03-finance-verification.md)
was executed. Worker reports no current defect reproduced for `FR-20261016-02`
or `FR-20261016-03` (both historically fixed in `5edd3419`), and reports 12/12
PostgreSQL tests on authorized disposable database `SanadFur03VerifyDb`. The
initial Mastermind review required a dedicated target and expanded dashboard
coverage; the worker completed those bounded corrections. See [review](work/portal-api-reconciliation/reports/fur-03-mastermind-review.md)
and [worker delivery](work/portal-api-reconciliation/reports/fur-03-worker-delivery.md).
The original contract's database-creation/selection boundary was underspecified;
this was a Mastermind omission. Owner explicitly authorized only
`SanadFur03VerifyDb` on local `localhost:5432` for the correction round. Mastermind
accepted R1–R5 after source, test-file, report and handoff review. Owner approved
commit/push; phase commit
`858da09` is on `origin/main`, and post-push `HEAD`/`origin/main` matched. The
[release receipt](work/portal-api-reconciliation/reports/fur-03-release.md)
records FUR-03 Done, deployment N/A (no runtime delta), and the worker-reported
verification evidence.

### Completed phase — FUR-04 Care Home readback

FUR-04 contract and test-schema addendum were owner-approved; the complete
worker delivery and C1–C4 corrections were reviewed and accepted. See the
[contract](work/portal-api-reconciliation/contracts/fur-04-care-home-readback.md),
[worker prompt](work/portal-api-reconciliation/worker-prompts/fur-04-care-home-readback.md),
[correction brief](work/portal-api-reconciliation/worker-prompts/fur-04-review-corrections.md),
and [delivery report](work/portal-api-reconciliation/reports/fur-04-worker-delivery.md).
Evidence is worker-reported: focused 15/15; C1 focused correction run 131/0;
PostgreSQL 7/7 on `SanadFur04VerifyDb` (not rerun during corrections); strict
build 0 warnings/errors; guarded full unit 3473/0/31; Architecture 1/1. The
Admin handoff, Owner handoff, feature docs and Postman examples are updated.

The delivery report records an **unauthorized local database setup deviation**:
three successive unintended lowercase `sanadfur04verifydb` databases were
created and dropped during target setup. The only approved target was the exact
`SanadFur04VerifyDb`; the correction report records available pre-drop evidence
and resulting state. No further database action is authorized or planned.
Owner authorized FUR-04 commit, push and Production deployment on 2026-10-17.
FUR-04 is **Done**: `31b13cc` is pushed/deployed; Mastermind guarded Unit
3475/0/31 and Architecture 1/1 passed, strict build had zero warnings/errors,
the standard verifier passed, all nine migration heads match with zero applied,
service is active with zero restarts, and host configuration fingerprints are
unchanged. The unauthorized local database setup deviation remains disclosed;
no further database action occurred. See the [phase release receipt](work/portal-api-reconciliation/reports/fur-04-release.md).

FUR-05's owner-scoped settlement contract was approved on 2026-10-17: include
both eligible unpaid balances and recorded transfers, reusing existing payout,
refund, fee and debt truth. The implementation worker completed its report;
Mastermind review accepted the code, tests, guide and Postman update. The phase
was committed, pushed and deployed as `ca2ef2f`; see the [worker delivery](work/portal-api-reconciliation/reports/fur-05-worker-delivery.md),
[Mastermind review](work/portal-api-reconciliation/reports/fur-05-mastermind-review.md),
and [release receipt](work/portal-api-reconciliation/reports/fur-05-release.md).
No migration was added or applied and no provider or payout state changed.
### Historical FUR-06 pre-release checkpoint — superseded

FUR-06's [eligible caregiver payout selector contract](work/portal-api-reconciliation/contracts/fur-06-eligible-caregiver-payouts.md)
and [worker brief](work/portal-api-reconciliation/worker-prompts/fur-06-eligible-caregiver-payouts.md)
were executed. OpenCode initially returned initialization text that the local
bridge treated as a final report, briefly overlapping two producer sessions.
The worker disclosed and removed duplicate implementation. See the
[worker delivery](work/portal-api-reconciliation/reports/fur-06-worker-delivery.md)
and [Mastermind review](work/portal-api-reconciliation/reports/fur-06-mastermind-review.md).
The phase was subsequently committed, pushed, published, and deployed; current
evidence is in the [FUR-06 release receipt](work/portal-api-reconciliation/reports/fur-06-release.md).
The phase-specific scale observation remains documented in the review.

FUR-07 was paused pending its report intake/storage, counters, and blocking
policy contract. It remains Not started; no FUR-07 work is authorized by this
workflow review.

Historical checkpoints
below must not override this follow-up or the latest deployment/import receipts.

Owner workflow override after `f932c46`: report findings, agree/apply cleanup,
validate and present the final result BEFORE committing or pushing. No further
commit/push without explicit owner approval; no publish/deploy/import approval.
Update: owner explicitly approved commit/push, publication and deployment on
2026-10-15 after the disposable PostgreSQL rehearsal. Release `9167e71` is now
published and deployed following owner-enabled SSH key access. Service active,
Production revision verified externally, all nine migration heads match with
zero applied at startup; services/OpenAPI/governorates HTTP 200. Existing
environment/service/appsettings checksums unchanged; private file/config backup
retained. See [deployment receipt](work/portal-api-reconciliation/reports/deployment-9167e71.md).
No geography import/replacement/reset occurred; API deployment does not load it.
Subsequent owner-approved preserve-and-add import is now **Done**; see
[executed receipt](work/portal-api-reconciliation/reports/geography-import-preserve-add.md).
6081 inserted; final 27/349/5714, including intentional legacy extras. All nine
old lookup records and five selections verified unchanged, seven profiles
retained, zero orphans, repeat preview zero. Private full DB backup retained;
API active, public lookup hierarchy and deployment verifier pass (9/9 heads).
Opt-in four-link profile tested 119/119; strict build zero warnings/errors.
No new API config/binary change. Earlier no-import/pending statements below
are historical checkpoints, superseded by this receipt.
Owner subsequently approved read-only replacement preflight. Done: actual
lookups 4/3/2; five area-selection rows across five caregivers; no booking has
an Area ID; no declared lookup FKs. One existing Area has conflicting bilingual
names. See [exact impact/proposal](work/portal-api-reconciliation/reports/geography-replacement-preflight.md).
No data changed. Await exact acceptance before clearing those five choices;
never guess replacement mappings or delete profiles/accounts/bookings.
Superseding owner choice: preserve-and-add, not clear/reset. Actual-target
read-only importer preview In progress; existing IDs, names and all five choices
must remain unchanged. Do not bypass reported conflicts or silently load only
unblocked branches.
Preview now Done: two root governorate spelling conflicts block 944 dependent
rows; Asyut/Assiut would duplicate without explicit matching, and Damanhur needs
an explicit grouping choice. No apply. Exact proposed links are in the preflight
report; current CLI has no alias mechanism. Await owner reconciliation decision.
Current [16-label cleanup proposal](work/portal-api-reconciliation/reports/phase-06-label-cleanup-proposal.md)
was approved on 2026-10-15 and applied locally. Original names preserved in the
correction manifest; exactly 16 display rows changed, no IDs/parents/counts/flags.
Validation complete locally: structural 12/12, name flags zero, pinned publisher
comparison passes all exact approved pairs, isolated lookup suite 90/90.
See [cleanup closeout](work/portal-api-reconciliation/reports/phase-06-label-cleanup.md).
Owner then authorized local importer implementation/testing. Standalone 06B
tool is Done locally: strict solution build zero warnings/errors, focused
113/113 (23 importer + 90 lookup compatibility/regressions), architecture 1/1.
The entire 6086-record catalogue imported and repeated in SQLite memory;
no live PostgreSQL, target preview, import or deployment was performed.
Subsequent owner-authorized disposable rehearsal is Done: actual CLI on a fresh
local PostgreSQL 18.4 cluster imported 6086 then zero on repeat; final 27/347/5712
and no orphans. Temporary server stopped, evidence retained locally. No existing
database or VPS touched. The preceding no-PostgreSQL statement is historical.
See [06B closeout](work/portal-api-reconciliation/reports/phase-06-importer.md)
and [operator runbook](../../tools/Sanad.GeographyImport/README.md).

The owner supplied an unverified frontend report on 2026-10-15 and requested
an evidence-based audit and fix/test plan before worker implementation. The
audit at code/deployment baseline `8cbeedd` is complete; approved tasks and
evidence are in [portal reconciliation plan](work/portal-api-reconciliation/plan.md).
Confirmed problems include reactivation error handling, library JSON casing/
validation feedback, and OpenAPI metadata; caregiver data repair needs a
diagnostic first. Existing audit tests passed 257/257; its live checks were
anonymous GETs. PORTAL-01 has since changed the scoped Identity implementation
as recorded below. No remote data or deployment change occurred in this review.

The current VPS is Production, used for production-mode developer testing by
owner direction. The owner deployed the package; previous anonymous verification
confirmed the revision and all nine applied migration heads. Do not treat that
database as disposable or enable Development seeding there. CORS is owner-
confirmed unchanged, and Bruno is excluded. Chat Plus runtime/device/soak work
remains separate; deployment smoke evidence does not complete its Prompt 7.

Owner approved the core plan on 2026-10-15. PORTAL-01 worker delivery
`4f71493` is reviewed and accepted on main. Mastermind strengthened test coverage:
strict build 0 warnings/errors; focused tests 57/57; Identity model 7/7;
architecture 1/1; Postman mapping 556/620/0/0. See
[Phase 01 report](work/portal-api-reconciliation/reports/phase-01-reactivation.md).
Production application code remains at deployed `8cbeedd`; these fixes are
committed source work awaiting a later owner publish/deploy order.
PORTAL-02 worker delivery `4962053` is reviewed and accepted after correcting
unintended quoted-number coercion while keeping case-insensitive section keys.
Mastermind added regression/literal example coverage: strict build 0 warnings/
errors; Library/controller 56/56; architecture 1/1; Postman mapping 556/620/0/0.
See [Phase 02 report](work/portal-api-reconciliation/reports/phase-02-library.md).
Nothing published/deployed; source fixes remain separate from deployed code.
PORTAL-03 delivery `57f9d3e` plus correction `13a85ca` reviewed and accepted:
test host isolated, finance-current empty HTTP proven 204 and documented,
nullable-enum schema valid without a speculative transformer change. Mastermind
rerun 85/85; mapping 556/620/0/0; reused valid correction strict build zero
warnings/errors and architecture 1/1. See the Phase 03 report's final appendix.
PORTAL-04 delivery `33af090` reviewed and accepted with bounded SQL/runbook
corrections (UUID extrema, row caps, null/blank fallback, partial-target caveat).
Mastermind rerun 24/24; valid worker zero-warning/error build reused. Production
files untouched; no live SQL or repair. Diagnostic/operator handoff is delivered,
but the six reported deployed rows remain unverified pending operator evidence.
PORTAL-05 delivery `6b32906` reviewed and accepted after test strengthening:
approved plus Draft facility cases, no-facility finance HTTP, exact foreign IDs,
and Cairo-local payment-day fixture. Strict build zero warnings/errors; combined
owner/related 126/126 (19 owner); mapping 556/620/0/0. Real handlers/HTTP,
visit advisory-lock guard fake; no concurrency proof or production-code changes.
PORTAL-07 delivery `0561298` reviewed and accepted locally. Mastermind corrected
three frontend-handoff statements and added Test-SanadPortalGate.ps1, which
prepares the exact child environment before tests start. Final mastermind run:
unit 3308 passed / 0 failed / 8 explained skips; architecture 1/1; Postman 22/22
parse, mapping 556/620/0/0; relative portal links and diff clean. Strict build
zero-warning/error evidence reused with unchanged compiled inputs.
Worker's first run violated the no-reset instruction (reported two local test
DB resets); excluded, documented, not excused by the guarded rerun. No further
reset or data recovery was attempted by the mastermind.
The [frontend handoff](work/portal-api-reconciliation/frontend-handoff.md) is ready;
nothing published/deployed. Local core reconciliation is complete, not global
production readiness. Caregiver operator evidence and separate Chat runtime/
device/soak gates remain open.
Owner said "let's go" for Phase 06 preparation on 2026-10-15. Dataset/mapping
is not yet approved: the City/Area model and existing Smouha seed need explicit
alignment with any administrative hierarchy. Candidate-source counts/provenance
are not independently qualified; see the new source intake, not a claimed
national dataset. No importer, API, data, build or deployment action occurred.
PORTAL-06A delivery `cdf8a2a` reviewed/accepted as research. Mastermind checked
the pinned district hash/count (365), 18 English collisions and 365 empty source
objects. Worker proposal to import only levels 1–2 and leave Areas manual is
not recommended as closure of the three-level picker gap; locality semantics,
upstream provenance and coverage remain unresolved. See report's final review.
Owner explicitly chose **All Egypt**. Coverage choice is now resolved: prepare
a national bilingual three-level catalogue, not selected launch regions or a
levels-1/2 import with Areas manual. No specific dataset/mapping is approved.
06A2 delivery `5918813` reviewed; acceptance held. Hash/counts and 5/5 offline
checks reproduced, but the validator accepts empty and Arabic-only-English
catalogues. Armant/Esna wrongly fall under Qena, affecting 36 child Areas;
official GOPP Luxor evidence contradicts that mapping. Tiba exclusion needs
evidence; coverage prose/gap counts drift from JSON. All Areas remain provisional.
06A2 correction delivery `1b0527b` reviewed and accepted as bounded correction
work, not as an import-ready catalogue. Mastermind reran the offline structural
gate: exit 0, 12/12 fixtures, 27/347/5712; readiness remains FAIL (exit 2).
Armant/Esna now map to Luxor and Tiba is restored; remaining boundary, provenance,
consumer-locality and coverage evidence is explicitly unresolved.
Owner requested completion. Mastermind directly qualified historical Arabic
name/key correspondence against OCHA/HDX (CAPMAS attributed), with a hash-pinned
read-only comparison script and recorded evidence. Source explicitly says
partial ADM3 coverage/2017 boundaries; all included Area English names are
blank there. Current national readiness is therefore still Blocked, not fixed
by matching the same old source. See the
[data qualification report](work/portal-api-reconciliation/reports/phase-06-data-qualification.md).
Owner subsequently accepted the collected coverage for V1, subject to a full
name/application compatibility scan. [V1 scan](work/portal-api-reconciliation/reports/phase-06-v1-scan.md)
is Done with findings: 6086 records structurally/source scanned; 27 real-handler
branch scenarios plus existing regressions 90/90, zero-warning/error strict
build. Four question-mark labels need display review; 12 underscore delimiters
are optional readability flags. No candidate edits or verification-flag flips.
The stronger historical nationwide-certification gate is not claimed passed.
The owner approved the exact 16 display edits; they are now applied locally.
**Next action:** hand off completed V1 geography provisioning; await owner tasks.
No further data correction, deletion, runtime testing or deployment implied.
API deployment is verified; disposable PostgreSQL rehearsal passed. Production
lookup replacement/reset still needs exact target/data authority.
Do not demand exhaustive national data again
as the V1 scope choice, or treat scope acceptance as proof of name accuracy.
06B local implementation/testing was authorized; no live import/write/deploy authorized.
Every worker brief must enforce the owner's testing order: verify existing
configuration and seeded fixtures, seed only missing prerequisites on the
designated disposable target, read them back, then launch the API/run scenarios.
The mandatory readiness gate is in the portal plan; do not seed the Production
VPS or use startup side effects to prepare test prerequisites.

## Previous publishing checkpoint — superseded by owner deployment

Chat Plus Prompts 1–6 and VPS-readiness implementation are integrated. Prompt 4
is optional; Prompt 7 remains the authorized deployment/runtime/device
verification step. Owner-confirmed CORS (`AllowAnyOrigin`, `AllowAnyMethod`,
`AllowAnyHeader`) is unchanged and is not a gap; Bruno is excluded. Code and
automated gates are recorded in the linked integration reports. Staging and
production environment/runtime readiness are not claimed: target configuration,
secrets, backup/restore, device delivery, edge behavior, and soak evidence remain
external gates. The Chat cleanup-index migration is authored, included in the
release manifest, and unapplied until deployment authorization.

The release gate battery passed on clean pushed `main`: strict build 0 warnings/
errors; Unit 3,120 passed, 0 failed, 8 skipped (5 PostgreSQL integration cases
intentionally skipped with database targets blank for that process; 3
pre-declared Families skips); Architecture 1/1; Postman mapping 556 signatures /
620 requests / 0 missing / 0 orphan. A stale Chat migration identifier in the
package script was corrected before publication. The Release package is generated
only from the final pushed `main`; its manifest is the source of the exact
revision and nine required migration IDs. No VPS, database, provider, or
deployment action is included. See [Chat Plus handoff](work/chat-plus/handoff.md)
for remaining owner/runtime gates.

**Next:** owner deploys the supplied artifact. After the owner reports deployment
and supplies the exact authorized target details, resume Prompt 7 verification.
Do not deploy or access the VPS on the owner's behalf.

## Previous Chat Plus integrated checkpoint — 2026-10-13

Owner authorized mastermind integration of `builder/chat-plus-production`
(delivered `ded4dd1`, base `41f9c91`) into main, push, and docs sync.
Builder Phases 0–6 are complete; disposable-target runtime/migration evidence
is recorded, with release gaps retained. Main fast-forwarded without conflict
to correction commit 9ca63b3 (expiry/liveness/dispatcher/topology/publish manifest).
Final gates: build zero warnings/errors; Chat Plus 64/64; unit 3076 passed,
0 failed, 3 existing PG skips; architecture 1/1. Postman mapping 0 missing/
orphans. Script exit 1 comes only from 148 optional Bruno gaps (documented).
See [Chat Plus checklist](work/chat-plus/tasks.md),
[integration evidence](work/chat-plus/reports/integration.md), and
[current handoff](work/chat-plus/handoff.md) for exact status and remaining work.
No production operation or provider send is authorized by integration.
Owner's latest direction prioritizes development VPS shipping and excludes
Bruno entirely. [Copy-ready worker prompts](work/chat-plus/worker-prompts.md)
cover security, storage, processor correction, optional test page, priority VPS
readiness and target/device verification. Existing package manifest is 41f9c91,
before Chat Plus; it must be rebuilt from the reviewed pushed main revision.
**Next action:** execute Prompt 5 on its own branch, then mastermind integrates
and publishes; deploy only with exact VPS/test-database/startup authorization.
Voice excluded; single-instance only; public Development environment forbidden.

## Previous integration evidence — 2026-10-11 (baseline slices)

`main` = `origin/main` = `5edd341`, tree clean. Landed in order: Chat Lane A (`5d26f9f`) + Lane B (`4864a93`), Notifications Lanes A+B (`9005f14`, Codex-owned), Opus phases 1–6 (`2db70cc`), care-home finance translation fix (`5edd341`, live-verified on PostgreSQL: owner `dashboard/mine` + `revenue/mine` and admin filtered/unfiltered reads all 200). Gates on merged `main`: solution build 0 warnings/0 errors, full unit 2907 passed / 0 failed / 3 PostgreSQL-only skips, architecture 1/1, contract mapping 519 signatures with 0 missing / 0 orphan Postman routes (Bruno risk-based, 0 orphans). All task branches pruned; only `main` exists locally and on origin. Opus/Chat migrations remain authored-only, never applied. No API/DB/production operation beyond the authorized disposable-target live verification (port 5235, `SanadBrunoTestDb`, stopped afterwards).

**Next action:** release flow now owns `main` — see `docs/operations/publish.md` (trigger word: `publish`) and `docs/operations/deploy-runbook.md`. Live Firebase verification still needs explicit owner authorization + a test device. Opus Phase 0-style intake applies to any future slice: agree scope/tasks first, then explicit plan approval before execution.

## Previous state — 2026-10-09 (superseded by 2026-10-11 above)

HC-TASK-060 is committed and pushed: `fd90975` is included in `b693ff9`. HC-TASK-061/062 guide, Postman, and evidence synchronization is complete in `f6c4648`, with the handoff closeout in `8353352`. Local `main` and `origin/main` were verified synchronized. New Postman examples were statically checked, not executed. No API, database, fixture, provider, production, reset, or deployment operation was performed. HC-040 and HC-060 migrations remain generated and unapplied; see the checklist for runtime and migration limits.

The Care Homes slice is complete and the working tree is clean. **Next priority:** Chat. It is not planned yet; first review what exists in the owner's application, agree scope/tasks, and obtain explicit plan approval. Do not begin implementation before that intake and approval.

**Cross-slice update:** the caregiver-money chain (T0 FinanceAdmin role, T1 bank catalogue, T2 payout account with IBAN encryption, T3 payout timing policy with future-effective fix, T4 payout ledger with review workflow) and the community lane (T5 image upload with gated serving, T6 recommendations, T7 medical recipients) are implemented with gates green; T0–T2 are merged, T3–T7 await integration on their task branches. The owner-approved Chat execution package lives in `docs/chat/` with no implementation started. A docs cleanup pass is in progress on a task branch (fixes only; deletions need separate approval).

## Historical handoff checkpoints

The dated execution notes below preserve historical evidence. Their past “In Progress,” blocker, and next-action statements may have been superseded; current slice status is in the checklist and the summary above. Do not treat the historical next actions as current instructions.


**HC-023 latest continuation (2026-10-07):** official Shared request 93 now passes on the pinned worktree API. Final selected run passed Owner login, transfer (200), operational history readback with source/destination/date/actor, and Bruno logout-all (204): 4 requests, 5/5 assertions, one unrelated malformed Wellness Tips parser skip. Two earlier 200 responses failed only in the post-response script because of a wrong source override and then a stale variable reference. The date-aware assertion was restored and the correct source fixture was used. The data from all four future-dated attempts remains in `SanadBrunoTestDb`. Owner sessions were cleared via Bruno logout-all. API stopped; port 5236 is closed; port 5235 was untouched.

HC-023 is Done per the owner's decision to skip fresh-database migration verification. The full suite passed Architecture 1/1 and Unit 2,288 passed, 3 skipped, 0 failed after migration correction; Release build had 0 warnings/errors and focused Care Homes/API tests passed 129/129. Startup found migration histories current and applied none, so fresh migration application remains explicitly unverified. Request 87 passed with unused in-stay date 2026-11-10: transfer 200 and request 88 history readback; targeted logout-all returned 204 (4 selected requests, 6/6 assertions), plus the unrelated parser skip. Local outbox retry state and retry/backoff tests are evidenced; live delivery/runtime redispatch were not run with SMTP disabled. Early check-in before stay start is an adjacent lifecycle observation, not an HC-023 gate. No extra database was created, no DB reset/drop occurred, and no provider/SMTP action occurred.

Security note: a local diagnostic displayed generated test-account credentials and the fixture HMAC once; values are not copied into documentation and need rotation before future reuse. `.hc032-start-api.ps1` was not run.

**HC-023 latest continuation update (2026-10-07):** the correct worktree API ran on port 5236 after port 5235 was found occupied by an unidentified process; that process was not touched. API startup confirmed all module migration histories current and applied none; this does not verify fresh migration application. The API was stopped and 5236 is closed. Five stale fixture Owner sessions were revoked only in the authorized disposable `SanadBrunoTestDb` using a target-guarded helper, then successful Bruno sequences used targeted logout (204). On the verified API, request 96 checked-in transfer passed with history readback, scratch Shared transfer/readback passed, and requests 97/98 passed on a fresh future maintenance block. Official request 93 still fails `409 CareHomes.Bookings.AssignmentConflict` on its old fixture. Request 87's consumed booking was not replayed after its script correction. The full suite passed Architecture 1/1 and Unit 2,288 passed, 3 skipped, 0 failed after the migration correction. Build/focused results remain clean (0 warnings/errors; 129/129). HC-023 remains In Progress: fresh migration application, official Shared request 93 fixture success, corrected request 87 assertion, notification retry/idempotency runtime proof, and review of early check-in remain open. `.hc032-start-api.ps1` was not run; no secret was printed.

## HC-TASK-021 runtime checkpoint — 2026-10-06 (current)

HC-021 functional acceptance passed in `care-homes-main` at HEAD `e7e4e7b` plus the current delta. Supported APIs created a Private type with two rooms, one additional Suite room, and three Accepted/Paid bookings using existing identities. Bruno verified Private/Suite null-bed assignment, room readback, invalid bed allocation, overlapping conflict, zero availability after assignment, and owner/Admin one-reservation availability counts. Four explicit Bruno runs passed 26 selected requests and 34/34 assertions; each had one unrelated malformed Wellness Tips parser skip. Release build is clean; Care Homes 97/97; full suite Architecture 1/1 and Unit 2,271 passed with 3 PostgreSQL-only skips.

There was one unintended payment-intent attempt with the inherited nonempty Paymob secret; it returned `502 Paymob.GatewayError`, and no order, intention, or transaction was persisted locally. The owner confirms Paymob did not receive the request. Later payment setup used a process-only empty `Paymob:SecretKey`, a loopback provider URL, the local fixture HMAC, and the verified `pk_dev` Development stub. No further provider calls were made. The API is stopped, HC-021 sessions are revoked, and final database readback confirms the intended fixture/assignment state. HC-021 is **Done**. Details are in [Care Homes Tasks](operations/care-homes/Care_Homes_Tasks.md).

HC-TASK-020 is also closed as **Done**: its former empty-occupancy-provider blocker is resolved and the inventory guide/checklist reflect the verified booking integration. No integration was performed.

HC-TASK-023 remains **In Progress**. Current delta adds injected-clock Cairo date handling for maintenance amend/cancel, deterministic midnight-boundary tests, HC-023 route/policy mapping tests, retry-claim/backoff domain tests, and a 25-path authored Bruno manifest with fixture requirements/readbacks. Release solution build passed with 0 warnings/errors; focused Care Homes/API tests passed 129/129; Postman JSON parses, `git diff --check` is clean, and a static manifest check confirms 25 unique contiguous HC-023 sequences with one HTTP method block per request. The Bruno offline preflight was blocked: script execution policy denied direct invocation and a nested `pwsh` launch was denied. The API contract mapping script also could not launch because native `pwsh` process execution returned Access is denied. No Bruno requests, API startup, migration application, database/provider/SMTP operation occurred in this continuation. The prior full-suite result remains 2,275 passed, 3 skipped, 5 API-host failures from the PostgreSQL-invalid migration default; it was not rerun after the migration correction. Exact-target PostgreSQL and stateful Bruno authorization is still required.

**Next action:** obtain owner authorization naming the exact disposable DB/API target, approved fixture writes/readbacks, startup side effects, and local notification observation boundary. Do not run API-host tests, apply migrations, or send Bruno requests before that authorization.

## HC-TASK-034 completion checkpoint — 2026-10-05 (newest; supersedes older HC-034 checkpoints below)

HC-034 required endpoint coverage is complete; the active checklist is **Done**. Reused gates: Release solution build 0 warnings/errors; focused fixture tests 3/3; Care Homes tests 90/90; full suite Architecture 1/1 and Unit 2,264 passed with 3 PostgreSQL-only skips, 0 failures. No production API code changed in this continuation.

Runtime was pinned to this worktree, `http://localhost:5235`, and `localhost:5432/SanadBrunoTestDb` for all seven module connections. `dotnet run` reported every module current/no migrations applied; TestUserSeed and Finance startup migrations were disabled. API stopped after verification. Existing Development DPAPI/DataProtection key-ring errors were nonfatal. The signed Development Paymob callback was local only; no provider was called. No DB reset/drop, onboarding replay, commit, push or deployment occurred. Temporary artifacts were removed; `.hc034-build.props` and `.hc034-build` remain absent.

Within the approved scope, the reset-only provisioner ran three additional times for only the existing allowlisted Family (six invocations total across the latest checkpoints). Passwords were process-only, never printed or persisted. The supported Family dependent endpoint created exactly one Elderly Identity/dependent, `Bruno Disposable Photo` (`01a10915-7391-7ed2-9c74-113cb7514daa`). No other Family or inventory was created. Existing facility/room/bed were reused. Booking `01a10915-9623-757f-9ddb-ef111350f270` is Accepted/Paid, assigned and checked in; booking `01a1091a-a03b-7140-bdd4-4aa2362f0122` is PendingPayment, unassigned and without check-in. Valid Family dispute opened case `01a1091c-727a-7194-aaca-9cdd3fc52164`; readback confirmed trimmed reason and unchanged check-in/out timestamps. These test records remain in the disposable DB; no unsupported delete/reset was attempted.

Newly unblocked dispute manifest: 7 selected requests / 19 assertions passed (one unrelated malformed Wellness Tips parser skip). Expected=actual statuses: request 74 login `200` JSON; request 75 prerequisite GET `200`; request 75 valid dispute `POST` JSON (`Content-Type: application/json`) `200`; request 76 readback GET `200`; request 77 repeat `POST` JSON `200` with the same case/reason; request 84 no-check-in dispute `POST` JSON (`Content-Type: application/json`) `409 CareHomes.CheckInDispute.InvalidState`; request 78 targeted logout `204` with no body. No unexpected response error or API exception occurred in these selected paths. Request 51 had already passed expected=actual `404 CareHomes.Bookings.NotFound`. Earlier request 80/81/82/83 and request 29–60 results are recorded in the checkpoints below. Corrected assignment/check-in recovery passed 4 requests / 7 assertions: login `200`, request 29 JSON assignment `200`, request 30 no-body check-in `200`, targeted logout `204`.

One first setup manifest bailed at request 29 because its booking/room/bed variables were omitted; the request file encodes JSON with `Content-Type: application/json`, 13 earlier requests passed, and later check-in/logout requests were skipped. The reporter-suppressed failure did not retain exact actual status or response error code; no correlated application exception was seen in the API log. Supplying verified IDs corrected the request without code changes. The six-path no-check-in setup exited 0; its per-request summary was not retained, while DB readback confirmed the exact PendingPayment/no-check-in booking. New cleanup request 86 revoked only the Owner session created by the bailed run; request 65 logged out the cleanup login (3 requests / 3 assertions passed, one unrelated parser skip). HC-034 Family active sessions are 0 and Owner session count is back to its starting baseline of 3. No logout-all was used.

**Next action:** hand off HC-034 as complete and await the owner’s next explicitly scoped Care Homes task; do not start another slice from this checkpoint.

## HC-TASK-034 current checkpoint — 2026-10-05 (newest; supersedes older HC-034 checkpoints below)

The reset-only fixture option and its three tests are implemented and verified. Standard `dotnet --artifacts-path` was used for verification; no `.hc034-build*` file/directory or repository build-property workaround was created. Release solution build passed with 0 warnings/errors; focused fixture tests 3/3; focused Care Homes tests 90/90; full suite Architecture 1/1 and Unit 2,264 passed with 3 PostgreSQL-only skips (0 failures). The existing date-sensitive expiry test now uses a start date three days from current UTC; the full suite passed after that correction.

Runtime target is now verified. A read-only PostgreSQL query confirmed `SanadBrunoTestDb` on localhost:5432. All seven module connection strings were pinned to that database for API start. `dotnet run` on port 5235 reported all module migrations current/no migration applied; test-user seeding and Finance startup migrations were disabled. The API was stopped after Bruno. Nonfatal Development DataProtection DPAPI/key-ring errors were logged. No reset/drop, onboarding replay, provider call, commit, push, or deployment occurred.

The owner-approved password reset was run against only the existing allowlisted HC-034 Family identity. Three in-place reset invocations occurred because the first two runner attempts failed before a usable credential/result was retained; the final random password stayed process-local and was not printed or persisted. No Family/dependent/booking/inventory was created. The final Family login session was specifically logged out; a read-only session check showed 0 active and 1 total session for that HC-034 device name.

Seven selected Bruno paths passed (13/13 assertions; CLI summary 7 passed, 1 unrelated parser-skipped file): request 74 login `POST` JSON `200`; request 51 cross-Family confirmation `POST` with no body `404 CareHomes.Bookings.NotFound`; request 80 unauthenticated dispute `POST` JSON `401`; request 81 whitespace reason `POST` JSON `400 CareHomes.CheckInDispute.InvalidReason`; request 82 2001-character reason `POST` JSON `400` same code; request 83 wrong-Family dispute `POST` JSON `404 CareHomes.CheckInDispute.NotFound`; request 78 targeted logout with no body `204`. The unrelated skip was malformed Wellness Tips request `05-admin-create-manual-multipart.bru`; none of the selected requests failed, and no correlated API exception was observed. Bruno used `--reporter-skip-body`; status and stable error-code assertions are the retained response evidence.

HC-034 remains **In Progress**. Five Family-dispute paths remain: request 75 prerequisite read, request 75 valid submission, request 76 readback, request 77 repeat, and request 84 no-check-in conflict. Read-only inspection confirmed the provisioned Family currently owns zero dependents. The supported `POST /api/v1/family/dependents` flow creates an Elderly Identity account when the supplied phone has no existing Elderly account, so creating the required dependent would create another identity and requires explicit authorization under the owner’s no-unapproved-identities constraint. The two booking states can then be made with the existing facility/room/bed and supported local APIs; no new Care Home inventory is needed. Temporary artifacts were removed; `.hc034-build.props` and `.hc034-build` remain absent.

**Next action:** obtain owner authorization to create exactly one disposable Elderly Identity account/dependent through the supported Family dependent endpoint; then set up the two minimal non-overlapping Family-owned booking states and run only the five remaining explicit paths.

## HC-TASK-034 current checkpoint — 2026-10-05 (supersedes earlier HC-034 checkpoints below)

Owner approved an in-place reset of the exact existing fixture identity, with no second identity. Added `ResetExistingPassword` (default false; also explicitly false in Development config) to the one-shot provisioner. When explicitly true, reset fails unless the exact allowlisted HC-034 email already resolves to an active Family with its existing Family aggregate. Reset mode cannot create either aggregate; it changes the password hash and revokes active sessions with the production reset reason. Added three focused tests for successful reset/session revocation, missing identity, and wrong email. No reset has yet been executed; the database and fixture remain unchanged by this continuation.

The first focused test build and two serialized repeats all failed with `CS2012` access denied writing `src/API/Sanad.API/obj/Debug/net10.0/Sanad.API.dll`. The API listener is stopped; the output file was readable and an exclusive write-open diagnostic succeeded between build attempts, but the subsequent compiler still failed. Do not repeat unchanged builds, kill unidentified dotnet processes, or create custom `.hc034-build*` outputs. Existing previously valid build/focused/full-suite gates predate this fixture-only code change and do not validate it.

**Next action:** identify and clear the process/condition denying the normal `Sanad.API.dll` intermediate write, then run the bounded build and focused fixture-provisioner tests before executing the authorized password reset and Bruno work. HC-034 remains **In Progress**; requests 51 and authenticated dispute requests 74–84 remain open.

## HC-TASK-034 current checkpoint — 2026-10-04 (supersedes earlier HC-034 checkpoints below)

HC-034 remains **In Progress**. The product implementation, reviewed Family dispute contract, migration, docs/Postman sync, deterministic race tests and previously completed runtime manifest remain recorded below. Build passed with 0 warnings/errors; focused Care Homes tests passed 90/90; the second full-suite run passed Architecture 1/1 and Unit 2,261 with 3 PostgreSQL-only skips. Temporary `.hc034-build.props` and `.hc034-build` remain absent.

This continuation pinned all seven module DB connections to `localhost:5432/SanadBrunoTestDb` by deriving only the database name from the existing local connection string. The guarded Development provisioner succeeded for the one authorized Family fixture. Its random password was process-only, never printed or persisted, and that process ended before Bruno used it; the fixture identity now exists but has no retained credential. No second identity or password change occurred. API ran via `dotnet run` from this worktree on `http://localhost:5235`; every module reported migrations current/no migration applied, test-user seeding was disabled, existing DataProtection DPAPI/key-ring access errors were logged, and the API was stopped after testing.

Explicit Bruno request 80 passed: expected/actual `401`, JSON request (`Content-Type: application/json`), 1/1 assertion. One unrelated malformed Wellness Tips request was parser-skipped by CLI collection scanning; selected request passed. This expected anonymous denial had no response-error/API-exception investigation requirement. No booking, bed, dependent, payment, or dispute-case setup occurred in this continuation. One earlier guarded provisioner attempt using guessed DB credentials failed before writes; the corrected attempt using the existing local connection succeeded. No reset/drop, onboarding replay, provider request, commit, push, or deploy occurred.

Requests 51 and authenticated Family-dispute paths 74–84 remain incomplete: the single new Family fixture has no retained login credential, and the required owned checked-in / no-check-in bookings are not set up. The dispute contract is implemented; the remaining gap is fixture access/state. **Next action:** obtain authorization for a bounded in-place password reset of this exact identity (no new identity), then keep the generated password process-local through minimum approved booking setup/readback and the remaining explicit Bruno manifest. Keep HC-034 In Progress until every required endpoint case passes.

## HC-TASK-034 current checkpoint — 2026-10-04

The reviewed Family-submitted dispute contract is now synchronized in the Care Homes lifecycle guide, Postman collection, and Bruno README. The supported route is `POST /api/v1/family/care-home-bookings/{bookingId}/check-in-dispute` with `{ "reason": "..." }`; the trimmed reason must be 1–2000 characters. `FamilyAccess` is required; anonymous is `401`, wrong-family/unknown booking is `404 CareHomes.CheckInDispute.NotFound`, missing facility check-in is `409 CareHomes.CheckInDispute.InvalidState`, and invalid reason is `400 CareHomes.CheckInDispute.InvalidReason`. A valid first submission opens one `Open` Admin case with `familyReason`; repeats return the same open case without replacing the reason. Facility actual check-in/out timestamps remain unchanged. Admin list/resolve remains the follow-up flow.

HC-034 remains **In Progress**. The bounded reviewer corrections and deterministic race tests are incorporated. Verification: `dotnet restore Sanad.slnx --force-evaluate --nologo` succeeded; project-isolated `dotnet build Sanad.slnx --no-restore -m:1 --nologo` succeeded with 0 warnings/errors; focused Care Homes tests passed 90/90; full suite passed Architecture 1/1 and Unit 2,261 passed with 3 PostgreSQL-only skips (2,264 total). Offline Bruno preflight passed for 12 explicit HC-034 request paths using Bruno CLI 4.2.0; this does not verify runtime. The listener on port 5235 answers OpenAPI with HTTP 200, but its process command line was access-denied and its loaded build/database identity could not be verified; its OpenAPI does not contain the Family dispute route, so it was not reused or stopped. The temporary `.hc034-build.props` and `.hc034-build` output are absent and will not be regenerated; project-isolated output avoided changing files locked by that unverified listener but did not accelerate the build. Migration `20261004150000_AddFamilyCheckInDisputeReason` is authored and **not applied**. No Bruno runtime request or database/API state was changed. No HC-034 identity or booking was created. The opt-in provisioner creates only one active Family identity. A random `App__Hc034FamilyFixture__Password` was set only in a dedicated PowerShell process and was not printed or persisted; that process had `ConnectionStrings__IdentityDatabase` pointed at `localhost:5432/SanadDb`, not the required test DB, so the provisioner was not run. Environment/connection settings must be verified and set to Development and `localhost:5432/SanadBrunoTestDb` before its guard can pass. In `local-fixtures.bru`, the HC-034 inputs `hc034OtherFamilyEmail`, `hc034OtherFamilyPassword`, `hc034SharedBookingId`, `hc034FamilyDisputeBookingId`, and `hc034FamilyDisputeUnassignedBookingId` are absent. Request 51 needs a distinct Family login and an eligible existing booking owned by another Family. Family-dispute success needs a checked-in booking owned by the new Family, and its 409 case needs a separate owned booking without facility check-in; the identity-only fixture cannot provide these bookings. The separate approved booking provisioning scope and exact migration target/startup authorization remain to be confirmed.

**Latest continuation:** user requested completion of the remaining checklist. The 12 explicit Bruno paths for request 51 and Family-dispute requests 74–84 passed offline preflight on CLI 4.2.0 at HEAD `609077fb34346bf92066002570209c0066f663ab`; `RuntimeTargetVerified=false`. The required paths and invocation were statically validated only; Bruno sent no requests. Read-only listener inspection found no process listening on either port 5235 or PostgreSQL port 5432. The current shell has only `ConnectionStrings__IdentityDatabase`, targeting `localhost:5432/SanadDb`; all other module-specific connection strings are unset and would fall back to Identity. Normal API startup calls `Database.Migrate()` for Identity, CMS, Caregivers, Families, Care Homes, and Notifications, so it was not started against the wrong DB and no migration or fixture write was attempted. No API, database, fixture, or Bruno runtime state was changed.

**Next action:** restore the already-authorized disposable PostgreSQL service/connection for the exact target `localhost:5432/SanadBrunoTestDb` in the execution environment, then reverify all module targets and API port before startup. Do not start the API while the effective target is `SanadDb`; once exact target is verified, proceed with migration review/application, the approved identity fixture, the minimum authorized booking states, and explicit Bruno runtime coverage. Keep HC-034 In Progress until every required case passes.

## HC-TASK-034 current gates — 2026-10-04 (historical checkpoints below are superseded)

**Owner scope update (2026-10-04):** Owner accepted adding Family-submitted check-in disputes: required reason, one open Admin-review case, repeated submissions return the existing open case, and booking check-in/out timestamps remain unchanged. Owner also accepted a narrowly scoped local-only fixture mechanism for one active distinct-Family identity; request 51 reuses the existing booking and requires no new booking/inventory. This addition is In Progress. Scout confirmed the current dispute aggregate requires checkout time, so a schema migration may be needed; any migration will be authored only and not applied without exact-target authorization. Do not use the broad test seeder for this fixture.

**Latest continuation after owner instruction to create the missing fixtures:** API is running via `dotnet run` from this worktree on `http://localhost:5235`, process-pinned to `localhost:5432/SanadBrunoTestDb`; OpenAPI returned 200. Startup reported no migrations applied across Identity, CMS, Caregivers, Families, Care Homes, and Notifications. Test-user seeding and Finance migration startup were disabled. The build emitted NU1900/NU1905 warnings because the NuGet advisory feed was unreachable; startup also logged DataProtection DPAPI/key-ring access errors but completed. No Bruno request or fixture creation occurred. Investigation of supported Family registration found Development email/SMS senders discard the generated OTP; the OTP is six random digits stored only as a PBKDF2 hash, and pending-verification login yields a restricted token that cannot pass the FamilyAccess policy. Therefore no usable distinct-Family JWT can be created through the existing supported local flow without a new fixture mechanism, which the prior instruction prohibited. Family dispute submission still has no route/request model or approved outcome contract. HC-034 remains In Progress. **Next action:** obtain either existing distinct-Family credentials or approval for a scoped local-only active Family fixture mechanism, and the expected Family dispute action/outcome contract; then run the newly unblocked coverage.

**Latest revalidation (2026-10-04; supersedes the runtime claim immediately below):** Preserved the dirty worktree at HEAD `609077fb34346bf92066002570209c0066f663ab`. The listener on `localhost:5432` is reachable, but the database identity was not revalidated. Port 5235 was briefly observed as listening PID 20356; process details were access-denied. The wrong Swagger path returned 404, then the correct `/openapi/v1.json` probe found no listener. The API build/worktree/database fingerprint is unverified; no API was started and no Bruno request was sent. Offline preflight passed for explicit requests 31, 33, and 51 on Bruno CLI 4.2.0 (`RuntimeTargetVerified=false`). Source inspection confirms Family list/detail/checkout/payment-intent/check-in-confirmation routes, with no Family dispute-submission route or request model. Request 51 remains blocked: no HC-034 other-Family token is configured, and available Family identities belong to the same family. No identity or fixture was created. Earlier Bruno results below remain the last runtime evidence. HC-034 remains In Progress. **Next action:** obtain an existing distinct-family identity/booking for request 51 and the owner’s supported Family dispute-submission contract decision, then reverify the exact API/database target and run newly unblocked coverage.

**Current runtime checkpoint (supersedes the older continuation/action notes below):** User freed sessions; API remains `dotnet run` on `http://localhost:5235` against the authorized `localhost:5432/SanadBrunoTestDb`. No reset/drop or restart was performed in this continuation. HC-034 Bruno manifest requests 29–50, 52–57, and 59–60 passed; 51 is blocked because no distinct other-Family identity is configured (available Family fixture identities resolve to the same family). Same-bed concurrency was proven using separate Bruno processes and the same existing bed/date: booking A `01a107af-efd3-73a9-af05-2e5ae8342f6b` returned 200 and booking B `01a107b0-11a0-7fba-8791-4c8215ea3589` returned 409; each process passed login/request/targeted logout, 3/3 assertions. Confirmed stay readback passed for `01a10791-8621-7e9f-8275-c25bc8119b67`; no-confirmation checkout opened a case for `01a10798-a021-7394-b181-c3e665f47170`, and its distinct null-Family-confirmation readback passed. Admin list/resolve/repeat-resolve/unknown-case and ContentAdmin denial passed, including exact-session logout. An initial incorrect operational assertion was retained as evidence and covered by new request 70 with the correct no-confirmation contract. Request 52/53 invalid allocation shape is correctly asserted as 400 per approved contract; affected paths were rerun. No product API change or unchanged retry occurred. HC-034 stays In Progress: request 51 needs a distinct-family fixture identity; separately, the approved-contract coverage for a Family-submitted disputed confirmation is blocked because the API has no such route/request model. Do not fabricate either. **Next action:** owner supplies an existing distinct-family test identity/booking for request 51 and decides/adds the supported Family dispute-submission contract; then run only the newly unblocked paths and full affected endpoint coverage.

Continuation update: the owner authorized the required disposable booking setup and separately authorized one additional bed plus a same-port API restart. Port 5235 remains the pinned Development API against `localhost:5432/SanadBrunoTestDb`; the restarted process loaded the existing local fixture HMAC without printing or recording it. Bruno verified the live signed callback (HTTP 200). Existing shared room `01a106b9-92a7-7b08-bbf5-83f6c9775282` now has two active beds: pre-existing `01a106b9-941d-7636-922c-16534582838c` and authorized addition `01a10786-757a-7402-8197-c41d81debd4c`. One disposable booking `01a10791-8621-7e9f-8275-c25bc8119b67` was created for `2026-12-10`, payment intent and signed local webhook passed, and owner acceptance/readback passed (10 selected requests, 13/13 assertions, one unrelated malformed Wellness Tips parser skip). The follow-on confirmed-assignment sequence stopped at its first actionable failure: Family login 200, owner login expected 200 but actual 409; no assignment or stay-lifecycle endpoint ran. JSON login returned the established domain error `Identity.Login.SessionLimitReached` (five active device sessions), not an API exception. The exact response body was suppressed by the required `--reporter-skip-body`; the handler/mapper establish the error code/message/status. No unchanged retry was made. Six additional unauthenticated lifecycle/admin routes passed 6/6 assertions (one unrelated parser skip). No DB reset, onboarding replay, fixture helper, provider call, commit, push, or deployment occurred. HC-034 remains In Progress.

Follow-up safe coverage: the existing ContentAdmin login path succeeded and HC-034 dispute-list denial returned expected 403 (2/2 assertions; one unrelated parser skip). It created an auth session; that legacy login request does not expose the created session ID for targeted logout, so it was not revoked. No destructive `logout-all` was used.

Latest continuation after the owner freed sessions: the first Accepted/Paid fixture `01a10791-8621-7e9f-8275-c25bc8119b67` passed shared assignment, check-in, Family confirmation, confirmed check-out, and owner operational readback (7 selected requests, 17/17 assertions, one unrelated parser skip). A second fixture `01a10798-a021-7394-b181-c3e665f47170` for `2027-02-10` was paid and accepted (9 selected requests, 11/11 assertions). Assignment initially returned 404 because the request interpolates `hc034SharedBookingId` while the CLI supplied only `hc034UnconfirmedBookingId`; the unresolved GUID route returned 404 before reaching the API handler (JSON body configured, no API exception). After binding the request’s required variable, login+assignment passed 4/4 assertions. The second fixture is assigned to the authorized second bed; its check-in and no-confirmation check-out have not yet run. Family-forbidden assignment/check-in/check-out all passed 403, and the new Family session was logged out by its exact session ID (5 selected requests, 5/5 assertions; one parser skip).

The five successful Care-home Owner logins since the owner’s last session cleanup have used all five allowed active slots; their legacy Bruno login request did not preserve the IDs, so no selective cleanup is possible for those sessions. It now captures the device-session ID, with HC-034 targeted logout request `65-hc034-owner-logout-current-session.bru` added for the next run. HC-034 also has guarded SuperAdmin login/targeted logout helpers (66/67) and Family/ContentAdmin targeted logout helpers (68/69); no `logout-all` is used.

**Next action:** owner must free the Care-home Owner sessions again. Then, in one explicit Bruno sequence, log in, check in and check out second booking `01a10798-a021-7394-b181-c3e665f47170` without Family confirmation, read it back, and logout that exact Owner session. Next use the guarded SuperAdmin login helper to list and resolve the resulting case, including repeated-resolve/unknown-case coverage, then logout that exact Admin session. HC-034 remains In Progress pending all runnable required coverage and the separate Family-submitted dispute-route/contract decision.

Additional independent Admin coverage after the latest continuation: guarded SuperAdmin login, unknown-case resolve (`404 CareHomes.CheckInDispute.NotFound`), and exact-session logout passed (3 selected requests, 4/4 assertions; one unrelated parser skip). Session cleanup helper preflight passed offline. The Owner-session blocker is unchanged; no Owner login was retried.

Verified code gates: `dotnet build Sanad.slnx --no-restore -m:1 --nologo` passed with 0 warnings/errors; `dotnet ef migrations has-pending-model-changes` reports no model changes; focused Care Homes tests passed 82/82; full suite passed Architecture 1 and Unit 2,248 with 3 PostgreSQL-only skips. The owner authorized the exact target and migration. `20261004120000_AddCareHomeStayLifecycle` is applied to `localhost:5432/SanadBrunoTestDb`; no reset/drop occurred. API is running via `dotnet run` on `http://localhost:5235`, with all module connection strings process-pinned to that test DB, `App:TestUserSeed:Enabled=false`, and Finance auto-migration disabled. OpenAPI returned 200. Six selected HC-034 unauthenticated endpoint requests passed (6/6 status assertions: assignment, check-in, check-out, Family confirmation, Admin case list, Admin resolve); each Bruno scan skipped the same unrelated malformed Wellness Tips file. Full HC-034 explicit preflight (31 requests, sequences 34–64) and partial-run preflight both passed. No fixture generation, onboarding replay, commit, push, or deployment occurred.

The authorized test DB was inspected read-only: 7 Care-home bookings exist and all are Expired/Pending; there are **0 Accepted+Paid bookings** and 0 open disputes. Existing inventory has 2 active Shared room types, 1 active Private/Suite room type, 3 active rooms, and 2 active beds. Roles exist (6 Family, 6 Elderly, one each SuperAdmin/ContentAdmin/SupportAdmin, 2 Care-home Owners), but no HC-034 booking/resource IDs or Admin JWTs are configured in `local-fixtures.bru`; the required second-family token and cross-facility/private/concurrency/unassigned/unknown IDs are also absent. Therefore stateful success, capacity, cross-family/facility, and Admin-case resolution Bruno coverage remains blocked; no records or identities were seeded. API startup logged a nonfatal DataProtection key-ring write `UnauthorizedAccessException`; OpenAPI and the unauthenticated route tests still passed. The selected owner check-out-without-confirmation trigger is approved (immediate, no timeout); the separate Family-submitted disputed-confirmation behavior remains undefined and unauthored. **Next action: obtain the Family dispute-submission decision and existing eligible HC-034 fixture identifiers/credentials (or explicit authority for a bounded fixture setup), then run the complete required Bruno coverage.**

Recheck after the owner's latest “Continue”: `tests/Bruno/environments/local-fixtures.bru` now exists at the pinned base URL and contains Care-home owner credentials plus nonempty fixture HMAC settings; secret values were not displayed or copied. It still has no HC-034 booking/resource IDs. OpenAPI remains HTTP 200. Two deliberately invalid, non-mutating webhook diagnostics returned HTTP 400 and did not establish that the live API's HMAC matches the fixture setting; no payment callback or booking was created. No API restart, database/inventory change, or fixture helper was performed. The current blockers remain an existing shared room with at least two beds and verified HMAC compatibility with the already-running API. If resolving those requires inventory mutation or an API restart, obtain separate authorization before proceeding.

## HC-TASK-034 planning intake — 2026-10-04 (superseded by current verification checkpoint)

Owner confirmed no app changes and approved HC-034. Scout, implementation, additive migration `20261004120000_AddCareHomeStayLifecycle` (not applied), tests, and ordered Bruno coverage are authored. Build passed with 0 warnings/errors; focused Care Homes tests passed 82/82. Full suite: Architecture 1 passed; Unit 2,243 passed, 3 PostgreSQL-only skipped, 5 failed because API-host startup detected EF `PendingModelChangesWarning` for `CareHomesDbContext`. Migration/snapshot alignment correction is active; no suppression or DB application. Owner-approved missing-confirmation trigger: facility check-out without Family confirmation opens an Admin case, with no timeout. Family-explicit dispute submission detail is pending owner reply. Next action: align migration/model snapshot and rerun the invalidated build/full suite before Bruno preflight/runtime.

## HC-TASK-032 continuation — 2026-10-04

Latest execution update: all four remaining HC-032 cases now have runtime evidence. Private/suite capacity: 9 CLI entries, 12/12 assertions, checkout 200. Cross-family privacy: 9 entries, 11/11 assertions, 404. Concurrency was rerun on unused `2027-09-15` with two distinct eligible dependents and the existing single-capacity shared room type; separate Bruno processes captured A=200 and B=409, each 6 entries (5 selected passes, 8/8 assertions, one unrelated parser skip). Cross-facility: the approved second facility received a dedicated shared room/bed, an eligible dependent and a pending booking; the original owner received the expected 404. Its setup/coverage manifest passed 15 selected requests and 20/20 assertions (16 CLI entries including one unrelated Wellness Tips parser skip). Facility/type/booking readbacks are recorded in the active checklist. Shared capacity, maintenance, core lifecycle, payment failure, acceptance and expiry gates remain passed as separately recorded. No endpoint coverage remains blocked; HC-032 is Done.

Pinned runtime remained `http://localhost:55819`, PID 21112, Release executable from this worktree, against the authorized `localhost:5432/SanadBrunoTestDb`; no API code or database reset/migration change occurred in this continuation. The owner revoked the device sessions after the first cross-facility login hit the five-session limit; subsequent login and the complete cross-facility sequence passed. Build/test gates remain the previously verified clean gates; this continuation changed only Bruno scripts/evidence and ran Bruno. Next scope candidate: HC-TASK-034 (physical room/bed assignment, check-in/out, Family check-in confirmation, and Admin dispute lifecycle); HC-TASK-033 is already Done, while HC-TASK-021 depends on HC-034. Governance requires owner intake and explicit approval of the assembled plan before starting this new slice. No HC-034 scout or implementation has started. Next action: owner confirms what has changed in the app since the supplied/reviewed Care Homes screens and confirms HC-034 as the next slice for a bounded planning intake. Detailed HC-032 results remain in [Care Homes Tasks](operations/care-homes/Care_Homes_Tasks.md).

## Bruno tooling correction completed — 2026-10-03

Owner requested Bruno-only reliability work. [Bruno reliability](operations/Bruno_Reliability_Tasks.md) is complete: offline worktree/CLI/request preflight, 24 passing checks and mandatory [Bruno recovery rules](operations/bruno-failure-first.md). Changes remain uncommitted in this `care-homes-main` worktree. On 2026-10-03, the explicit four-request HC-031 discovery manifest passed offline preflight at HEAD `609077fb34346bf92066002570209c0066f663ab` with Bruno CLI 4.2.0, targeting the documented local port 55819; no listener currently owns that port. The existing full HC-031 Bruno gate remains passed as recorded below, so it was not replayed. No API/DB/runtime Bruno action occurred in this checkpoint. Preserve completed gates and continue the existing Care homes worker sequence at its next unfinished authorized task; use the runbook on its affected Bruno manifest and correct the first evidenced failure directly.

Updated: 2026-10-04. Active role: execution mastermind integrating HC-TASK-032. Preserve the existing uncommitted Bruno tooling and unrelated edits in this worktree.

### HC-TASK-032 prior verified gates — 2026-10-04 (runtime target superseded by continuation at top)

Previously verified build/test/expiry gates (unchanged): Development-only test clock focused tests 25/25; full suite Architecture 1 + Unit 2,234, 3 skipped; build 0 warnings/errors; Release publish succeeded. Original run target `localhost:5235`, fixture `hc03220261004b`; expiry results and five-session cleanup remain as recorded in this historical gate evidence. Current runtime target and remaining HC-032 action are stated at the top of this handoff. Preserve prior core/payment-failure/owner-acceptance gates; do not rerun them absent invalidating changes.

Continuation checkpoint (2026-10-04; supersedes the previous next-action sentence): inspection found no runnable dedicated capacity/maintenance/concurrency/cross-owner fixture configuration. The pinned port 5235 had no listener; generated `local-fixtures.bru` was absent. Checked-in `local.bru` points to `https://localhost:7296` and has only generic credentials. Five explicit replay-safe family login/list/dependent/discovery/room-type paths passed offline preflight on CLI 4.2.0 against `http://localhost:5235`; `RuntimeTargetVerified=false`. No API startup, Bruno request, DB query, or fixture mutation occurred. Checked-in API settings have no database connection string, so physical records could not be rechecked. Missing configured fixture inputs: shared room type with bed inventory/date; private/suite room type with room inventory/date; maintenance-blocked room type/date; two eligible elderly identities and common last-capacity inventory/date for concurrency; another Family and its booking; another facility booking and owner context. The environment also lacks care-home owner credentials and `fixtureHmacSecret`. Prior readback records one facility and one shared room type. Next action: obtain owner direction/authorization to provision only those missing dedicated disposable identities and inventory on the same exact DB target, then run their explicit requests after readback. Do not invoke fixture generation, restart the API, reset/regenerate the DB, replay onboarding, or weaken coverage.

Historical execution checkpoint (superseded by completed continuation above): after owner authorization, a process-scoped connection override targeted only `localhost:5432/SanadBrunoTestDb`; shared environment settings were unchanged. `dotnet run --no-build -c Release --no-launch-profile --project src/API/Sanad.API/Sanad.API.csproj` started Development on port 5235 with test-user seeding and Finance migrations disabled; startup applied no migrations. Five explicit booking/family readbacks passed 8/8 assertions; owner inventory baseline passed; the expanded seven-path run passed 7 requests/10 assertions with the known unrelated Wellness Tips parser skip. For the shared-capacity sequence, explicit preflight passed but family login stopped the run: expected 200, actual 409, JSON request encoding. API queried active device-session count and `LoginCommandHandler` returned the normal `Identity.Login.SessionLimitReached` domain error (five-session limit); no exception occurred. Bruno’s required `--reporter-skip-body` omitted the response body, with code/message confirmed from the handler and mapper. No capacity request ran and no booking was created at that checkpoint; HC-032 was then In Progress.

Those readback sequences created two active Family Owner device sessions and one Care Home Owner device session; no logout/cleanup request ran. Do not retry Family login until authorized session cleanup restores a slot.

Initial target guard (superseded after owner authorization): the inherited shell connection targeted `localhost:5432/SanadDb`; it was not used. It was overridden only in the API process to the authorized test database.

Historical runtime continuation (2026-10-04; superseded target): an earlier process used port 55819/PID 2240 and was stopped. The later checkpoint above is the operative API/port record. Fixture identity readback: facility `01a101fa-ccc4-7728-8b4b-1eaf6eb475d4`, shared room type `01a1041a-206d-7b00-b8e5-779a9844ffa6`.

The signed payment-failure path passed: 11 selected requests, 14/14 assertions; one unrelated malformed Wellness Tips request was parser-skipped. Booking `01a10440-aa04-781e-a9ac-9a8868298b26` read back Cancelled/Failed (`6`/`3`). Separate paid acceptance passed: 12 selected requests, 16/16 assertions; one unrelated parser skip. Accepted/Paid readback IDs: `01a10442-ed80-77a4-a946-b9ec1202b6bf` and `01a10449-b2d6-7ad1-be12-bb6c8c761740` (`3`/`2`). First acceptance run exposed the true response contract (HTTP 200, not 204); corrected assertion then passed in the separate fixture run. An attempted Jan 2 overlap returned 409 because the prior stay ended Jan 3; Jan 4 was the correct adjacent start. A viewer-account checkout returned privacy-preserving `CareHomes.FamilyIntake.NotFound`; the owner account later hit session quota until the existing opt-in seeder ran again with the same run ID. No contract or authorization was weakened.

Historical earlier runtime checkpoint (superseded by the completed checkpoint at the top): an unpaid hold matured and explicit expiry paths passed, confirming status 5. At that earlier point the 24-hour decision timeout could not mature and dedicated capacity/privacy fixtures were outstanding; later Development test-clock and fixture runs completed those gates as recorded above. The 33 request files passed offline preflight/parser checks with unique contiguous seq 0–32; that is not a claim that one 33-request invocation was run. Focused CareHomes/CareHomeBooking tests remained 76/76 from the validated Release source. No provider/production call, commit, push, package, or deployment occurred in that checkpoint.

Earlier core-run evidence remains unchanged: all 20 core request files passed in the original ordered runs; inventory setup passed 5/5; replay-safe prerequisites passed 7/7 assertions; focused tests passed 76/76; Release publish passed. The original core sequence was exercised before current `meta.seq` normalization. Keep that distinction from the new runtime continuation above; do not call all 33 paths runtime-passed.

### HC-TASK-032 documenter checkpoint — 2026-10-04

The authored booking guide and Postman folder remain synchronized with the implemented contract. The current Bruno folder contains 33 requests, including replay-safe prerequisite and state readbacks. Earlier migration, parser, and build corrections are recorded below. This checkpoint is superseded by the current runtime checkpoint above.

### HC-TASK-032 approved execution checkpoint — 2026-10-04

- Read-only scout completed at HEAD `609077fb34346bf92066002570209c0066f663ab`; no source changes. HC-032 is the next eligible task after HC-031; HC-021 remains dependent on HC-032/034. Existing Care Homes availability accepts hold intervals, but no booking/hold persistence exists. Caregiver booking storage and webhook confirmation cannot safely represent facility decisions without a distinct Care-home booking identity and dispatch path.
- Approved behavior remains one prepaid calendar month, a 15-minute checkout hold, verified-payment 24-hour facility decision hold, 24-hour earliest arrival, accept/reject/timeout and capacity concurrency. Existing Care Homes elderly-intake resolver can supply the verified snapshot. HC-034 owns assignment/check-in; HC-035 owns full refund policy/retry/manual recovery; HC-038 owns payouts.
- Owner approved the bounded route/payment integration contract on 2026-10-04. The detailed execution prompt, file manifest, acceptance coverage, and exact-target runtime authority boundary are in the [Care Homes execution handoff](operations/care-homes/Execution_Handoff.md). No provider/DB/Bruno runtime was started. Next action: a new execution mastermind starts the implementer phase using the completed scout map and approved handoff, preserving completed gates and the dirty Bruno tooling delta.

## Current state

### HC-TASK-031 Bruno resolution (2026-10-03; supersedes pending runtime notes below)

- Root cause of HTTP 415: Bruno CLI 4.2 copied the HTTP method `body: multipart-form` value directly into an internal mode, while its runner dispatches on `multipartForm`; it sent an empty `application/x-www-form-urlencoded` request. Care Homes upload requests now use `body: multipartForm` in the method declaration and retain the standard `body:multipart-form { ... }` field block. A local mock confirmed pre/post wire headers and body length; a no-mutation valid multipart diagnostic reached the handler and returned expected validation 400.
- After the owner dropped the DB, configured local connection strings were pinned to `localhost:5432/SanadBrunoTestDb`; guarded startup migrated/recreated only that target and seeded test users. Resuming the existing draft, a direct Node Bruno invocation with 20 explicit paths passed 20 requests and 52/52 assertions, with one unrelated malformed Wellness Tips parser skip. It uploaded all four documents, submitted, Admin-verified/approved, and passed discovery list, invalid pagination, eligible detail, and unknown-ID detail. Build: 0 warnings/errors; focused discovery: 13/13; full suite: Architecture 1 + Unit 2,201; 3 PostgreSQL-only skips. API stopped, port 55819 closed. The approved fixture remains in the disposable DB. No HC-033 coverage, provider/production system, or deployment was touched.
- The guarded fixture helper now supports `-ReuseServiceIconFixture`: it byte-validates the known generated PNG, preserves an existing exact fixture, rejects any different protected file, and only deletes an image it created itself. PowerShell syntax validation passed. Durable invocation and multipart rule are in the Care Homes onboarding/discovery READMEs. HC-031 public discovery/detail is verified; rating ranking remains blocked on HC-041.

### HC-TASK-031 Bruno resolution (2026-10-03; supersedes pending runtime notes below)

- Root cause of HTTP 415: Bruno CLI 4.2 copied the HTTP method `body: multipart-form` value directly into an internal mode, while its runner dispatches on `multipartForm`; it sent an empty `application/x-www-form-urlencoded` request. Care Homes upload requests now use `body: multipartForm` in the method declaration and retain the standard `body:multipart-form { ... }` field block. A local mock confirmed pre/post wire headers and body length; a no-mutation valid multipart diagnostic reached the handler and returned expected validation 400.
- After the owner dropped the DB, configured local connection strings were pinned to `localhost:5432/SanadBrunoTestDb`; guarded startup migrated/recreated only that target and seeded test users. Resuming the existing draft, a direct Node Bruno invocation with 20 explicit paths passed 20 requests and 52/52 assertions, with one unrelated malformed Wellness Tips parser skip. It uploaded all four documents, submitted, Admin-verified/approved, and passed discovery list, invalid pagination, eligible detail, and unknown-ID detail. Build: 0 warnings/errors; focused discovery: 13/13; full suite: Architecture 1 + Unit 2,201; 3 PostgreSQL-only skips. API stopped, port 55819 closed. The approved fixture remains in the disposable DB. No HC-033 coverage, provider/production system, or deployment was touched.
- The guarded fixture helper now supports `-ReuseServiceIconFixture`: it byte-validates the known generated PNG, preserves an existing exact fixture, rejects any different protected file, and only deletes an image it created itself. PowerShell syntax validation passed. Durable invocation and multipart rule are in the Care Homes onboarding/discovery READMEs. HC-031 public discovery/detail is verified; rating ranking remains blocked on HC-041.

### HC-TASK-033 final status checkpoint (2026-10-03)

- HC-TASK-033 is **Done** for the bounded shared Finance consumer slice: Admin-configured percentage platform fee and tax on base price, immutable transaction snapshots, and existing caregiver and Family subscription consumers. Finance Admin Bruno passed 18/18 assertions; the explicit 24-request subscription lifecycle passed 24/24 assertions; the corrected caregiver quote/Family checkout sequence passed 11 requests with 22/22 assertions, with one known unrelated Wellness Tips parser skip while scanning. Build passed with 0 warnings/errors; focused Finance + Families.Subscription tests passed 168 with 3 PostgreSQL-only skips; the full solution passed Architecture 1 and Unit 2,178 with 3 PostgreSQL-only skips. Required docs and Postman evidence are synchronized.
- Care Homes booking checkout is explicitly not included: HC-TASK-032 must create that consumer before integration, and facility payout fee/ledger remains HC-TASK-038. No downstream booking, payout, or ledger behavior is claimed here. HC-TASK-033 was committed and pushed as `3ea6a27bbfc8a841628c97c5849377ca25f60cb5`, packaged at `deploy/publish-out-3ea6a27bbfc8`, and deployed by the owner. It is a historical revision; the current `main`/`origin/main` revision is recorded in the active slice below.
- Older HC-TASK-033 failure and pending-gate bullets below are retained as historical evidence only; this final checkpoint supersedes their former “In Progress”/unverified wording.

- Goal: deliver the Care homes backend slice from the supplied operator/Family UI and owner-confirmed requirements.
- Completed: planning intake and cleanup; owner-resolved shared percentage fee/tax basis and manual payout eligibility/recovery; caregiver review permission correction and Family caregiver ratings/top-10 with unit/Bruno coverage and synchronized docs/Postman; Care Homes onboarding/Admin lifecycle; HC-TASK-014 license expiry alerts and durable email outbox; HC-TASK-020 bounded inventory, maintenance, owner/Admin APIs, derived availability seam, docs, Postman, and Bruno coverage. All Care Homes work remains on linked `main` worktree `D:\Sanad_API\.codex\worktrees\care-homes-main`.
- Verification checkpoint: after network-enabled NuGet restore, main-worktree solution build passed with 0 warnings/errors; focused tests 57/57; full suite Architecture 1 + Unit 2,145 passed. The owner-authorized disposable DB was dropped by the owner and rebuilt by the guarded local fixture script; one owner facility was created through the owner API. Inventory Bruno passed 30/30 assertions (30 passed, 1 unrelated Wellness Tips parser skip). API and generated fixture env/image were cleaned. No production/provider action occurred.
- HC-TASK-020 remains In Progress only because `ICareHomeOccupancyProvider` awaits hold/stay data from HC-TASK-032/034; do not implement booking creation in this bounded inventory task. The wider Care Homes slice still includes occupancy/booking, discovery, Family stays/visits/ratings, shared percentage fee configuration/usage, manual payout ledger, notifications, and remaining endpoint coverage.
- Latest static contract mapping: 339 controller route-method signatures, 385 Postman API requests, 0 missing/orphan Postman mappings, 1,111 Bruno API requests, and 0 missing/orphan Bruno mappings. `git diff --check` passed with CRLF replacement notices.
- Authoritative decisions: [Care homes decisions](operations/care-homes/Care_Homes_Decisions.md).
- Task status: [Care homes tasks](operations/care-homes/Care_Homes_Tasks.md).
- UI evidence: [UI review](operations/care-homes/UI_Review.md).
- Execution guidance: [Care homes execution handoff](operations/care-homes/Execution_Handoff.md).
- Final decisions: separate Admin-configured percentage fee and tax, each on payment base price, shared across caregiver/Care homes/Family subscription payments; Family refunds apply proportionally to its platform fee; manual bank payout only after completed stay with later refunds recorded as facility balance owed; Admin may approve genuinely non-expiring documents; SMS verification OTP allowed; SuperAdmin/SupportAdmin may adjudicate check-in with evidence/effective time. Do not repeat resolved questions.
- Release handoff: after every successful commit and push to `main`, prepare and verify a Release package from that exact pushed revision as required by [deployment handoff](operations/deployment.md), then report its SHA-specific package path and upload command in the active execution handoff. The owner handles upload/deployment; no production/provider action is authorized.
- Active slice: HC-TASK-031 public discovery/detail implementation and docs are complete pending changed-endpoint Bruno verification; solution build, focused tests, and full suite pass after the reviewed eligibility-query translation fix. The latest runtime did not produce a passing Bruno gate. The revised read-only flow has request `00-list-default.bru` derive the first eligible `careHomeDiscoveryId` for request `02-detail-approved-fixture.bru`; request `03-detail-unknown-is-404.bru` uses the all-zero GUID and asserts `404 CareHomes.Discovery.NotFound`. No manual eligible ID is required. `local-fixtures.bru` and approved fixture IDs are absent, so do not reset/migrate/provision the DB or start the guarded API without fresh owner authorization. HC-TASK-031’s top-10 Care-homes ratings ranking is blocked on HC-TASK-041; no placeholder ranking will be added. HC-TASK-030 remains Done: its implementation/evidence commit `76cf32727f873071fba152c6fa11ddfc3151e347` and closeout commit `68f13c971a57f467c23cb8e04e8eb619847b6361` are pushed to `main`; the owner confirmed deployment of Release package `deploy/publish-out-68f13c971a57`. Its build, focused tests (43/43), and full suite (Architecture 1 + Unit 2,188, 3 PostgreSQL-only skips) passed. HC-TASK-033 remains Done and was deployed by the owner from commit `3ea6a27bbfc8a841628c97c5849377ca25f60cb5`, packaged at `deploy/publish-out-3ea6a27bbfc8`. Care Homes booking checkout remains deferred to HC-TASK-032 and facility payout fee/ledger to HC-TASK-038.
- HC-TASK-031 verification: `dotnet build Sanad.slnx --no-restore -m:1 --nologo` passed with 0 warnings/errors; focused discovery tests passed 11/11; full solution tests passed Architecture 1 and Unit 2,199 with 3 PostgreSQL-only skips. A first focused attempt exposed InMemory sorting by non-comparable `CareHomeId`; ordering now uses its `Guid` value, corrected focused rerun passed, and reviewer re-approved. No relational PostgreSQL integration execution occurred. Bruno's four explicit requests target guarded worktree port 55819 (not root Family API 5235) and remain pending fresh owner authorization/fixture availability. No HC-TASK-031 commit, push, package, or deployment has occurred.
- HC-TASK-031 authorized preflight correction checkpoint (2026-10-03): the owner authorized normal guarded API startup only against `localhost:5432/SanadBrunoTestDb` on port `55819`; test-user seeding and Finance migrations were disabled. Module migration checks ran; visible history logs reported no migrations applied in Identity, CMS, Caregivers, or Families. The response query reached PostgreSQL count successfully, then one read-only preflight GET returned HTTP 500 because `OrderBy(x => x.Id.Value)` could not translate. The API was stopped; no Bruno sequence ran and no reset, drop, provider, or production action occurred. The bounded correction changed ordering to `OrderBy(x => x.Id)` and made `CareHomeId` comparable over its Guid-backed value; reviewer approval is recorded. Rebuild passed with 0 warnings/errors, focused tests passed 12/12, and the full suite passed Architecture 1 plus Unit 2,200 with 3 PostgreSQL-only skips. The corrected endpoint has not been retried. Per fail-stop, fresh owner authorization is required before any repeat DB/API startup. No HC-TASK-031 commit, push, package, or deployment is claimed.
- HC-TASK-031 second fresh-authorized preflight checkpoint (2026-10-03): with test-user seeding and Finance migrations disabled, PostgreSQL count plus facility paging/ordering succeeded, then the read-only preflight GET returned HTTP 500 in the `MapEligible` room-type query because `facilityIds.Contains(x.FacilityId.Value)` could not translate. The API was stopped; no Bruno sequence ran and no reset/drop, provider, or production action occurred. The bounded fix uses scalar `CareHomeId` equality over at most 50 page IDs, filters archived room types in SQL, and guards an empty page before querying; reviewer approval is recorded. Build passed with 0 warnings/errors, focused discovery tests passed 13/13, and the full suite passed Architecture 1 plus Unit 2,201 with 3 PostgreSQL-only skips. The corrected endpoint has not been retried. Fresh owner authorization is required before any next API/DB startup. No HC-TASK-031 commit, push, package, or deployment is claimed.
- HC-TASK-031 final guarded-startup checkpoint (2026-10-03): the owner authorized one final startup against `localhost:5432/SanadBrunoTestDb` on port `55819`, with every configured module database pinned to that target, test-user seeding and Finance migrations disabled, and SMS/Paymob credentials blank. Startup reported all migrations current with none applied and the API became ready. The exact four-path Bruno command did not execute because PowerShell blocked `bru.ps1` under its execution policy; no Bruno HTTP request ran. No alternate launcher or retry was requested or performed. The API was stopped, port `55819` closed, and temporary ignored `local-fixtures.bru` removed; no reset/drop, provider/production action, or database cleanup occurred. Build, focused discovery tests 13/13, and full suite Architecture 1 + Unit 2,201 with 3 PostgreSQL-only skips remain green. HC-TASK-031 remains open pending changed-endpoint Bruno authorization and an executable CLI invocation; no commit, push, package, or deployment is claimed.
- Bruno terminal runbook (authoritative for future masterminds): in this PowerShell environment, `bru` resolves to policy-blocked `bru.ps1`, while `bru.cmd` stalled on a version check. Do not change PowerShell policy or rediscover launchers. The verified runner is `C:\Program Files\nodejs\node.exe` with `C:\Users\mosad\AppData\Roaming\npm\node_modules\@usebruno\cli\bin\bru.js`; `--version` and `run --help` returned Bruno CLI 4.2.0. From `tests/Bruno`, invoke that pair directly with all explicit request paths and `--env local --env-var baseUrl=http://localhost:55819 --env-var appEnvironment=Development --insecure --bail --reporter-skip-body`. This avoids both the shell-policy blocker and a generated `local-fixtures.bru`; exact HC-031 paths are in `docs/operations/care-homes/Execution_Handoff.md` and the feature README. Verify the direct CLI before API startup; resolve terminal/setup blockers in place instead of abandoning the gate. A runner failure is not an implementation failure. For a genuine implementation failure, fix its identified cause and rerun the affected gate; retain successful unrelated build/test evidence. This runbook supersedes the earlier fresh-authorization note for this same guarded read-only endpoint verification: the owner authorized continuing and fixing/retrying within the existing `localhost:5432/SanadBrunoTestDb`/55819 scope. It does not authorize reset/drop, cleanup, external providers, production, or deployment.
- HC-031 direct-CLI runtime result (2026-10-03): the verified Node/Bruno invocation reached the list endpoint and received HTTP 200 with an empty `items` array (no eligible approved facility exists in the disposable target). Five of six list assertions passed; `items isNotEmpty` failed and the post-response script also dereferenced missing `items[0]`. The script now stores an empty ID safely, leaving the missing fixture as the single actionable test failure. `--bail` skipped the other three requests. No endpoint implementation failure or positive-detail result is established. API stopped, port 55819 closed, migrations remained current; no fixture write, reset/drop, provider/production, or cleanup occurred. Positive detail still requires an approved eligible fixture; do not create one through the one-time onboarding/Admin workflows without authorization because there is no facility-delete API.
- HC-031 authorized fixture provision attempt (2026-10-03): the owner authorized one reusable approved Care Home fixture in the exact disposable `SanadBrunoTestDb`; no reset/drop or manual cleanup was authorized. The guarded onboarding/Admin sequence was run once through the direct Node Bruno CLI with explicit paths and `--bail`. Owner login, facility create (201), mine readback (200), and draft save (200) passed. The first document upload then returned HTTP 415, so `--bail` skipped all later uploads, submission, review, approval, and discovery requests. The combined invocation used `--reporter-skip-body`, and the response body was therefore unavailable; the current application maps `CareHomes.Document.InvalidContent` to 400, so the observed 415 is not evidence of a file-signature rejection. Local Bruno 4.2 source inspection confirms its multipart builder sets a generated boundary; the actual rejected request/response details remain unobserved. Do not replay facility creation: a draft facility now exists in the disposable DB. API stopped and port 55819 is closed; no reset/drop, provider/production action, or deployment occurred. Next: once the authorized DB connection is loaded, resume from owner login/readback and capture the response code (not tokens/bodies) for one safe existing-draft upload, then make the cause-specific correction and continue. Keep explicit paths/`--bail`; do not rerun HC-033 coverage.
- HC-031 gate status update (2026-10-03): solution build passed with 0 warnings/errors; focused discovery tests passed 13/13; full suite passed Architecture 1 and Unit 2,201 with 3 PostgreSQL-only skips. This does not replace required changed-endpoint Bruno. In the current terminal session, the inherited `ConnectionStrings__IdentityDatabase` is empty and no User/Machine connection setting is available; therefore the guarded API cannot be restarted against the authorized disposable target from this session. No API startup, migration, DB write, or repeat Bruno request was attempted after that check. Resume only after the existing `localhost:5432/SanadBrunoTestDb` connection is loaded into the process environment; do not guess credentials or target another database.
- NuGet auditing is fixed through repository `NuGet.Config` using NuGet.org's official vulnerability-only endpoint; restore and solution build now pass with zero warnings/errors. The owner dropped the exact disposable DB; guarded fixture startup applied all migrations there, with no other DB/provider/production action. The root Family listener on port 5235 was left untouched. This Care Homes main worktree reserves port 55819 as its fixture default; other worktrees must use different ports. All verified Sanad.API processes from this worktree were stopped.
- Finance Bruno corrections were reviewed and approved. The latest run on the dedicated main-worktree port 55819 completed 16 requests: 15 passed, 1 unrelated Wellness Tips parser skip; 18/18 assertions passed. Create returned 201; current/history readback, unique invalid-rate validation (400), duplicate-version conflict, SupportAdmin access, Family/ContentAdmin denials, anonymous denial, and logout passed. The disposable DB was already up to date; no drop/reset or provider action occurred. API processes verified inside this worktree were stopped and generated fixture files removed. HC-TASK-020 Bruno 30/30 remains valid and was not rerun. Subscription positive payment/renewal/plan-change/invoice/callback coverage remains incomplete. No commit/push/release/deployment occurred.
- Corrected positive subscription lifecycle Bruno run used 24 explicit request paths with `--bail`; metadata and unrelated requests were excluded, while the known Wellness Tips parser warning remained. The corrected `bru.getEnvVar` payment-intent scripts passed, as did payment/callback/idempotency/renewal/plan-change and invoice-list steps. The first actionable failure was `07a-invoice-detail.bru`: HTTP 200, but its assertion used a Bruno placeholder inside a JavaScript string and expected `totalAmount`; source DTO review confirms detail exposes computed `Amount` (JSON `amount`). Corrected the checks using `bru.getVar("positiveEnrollmentInvoiceId")` and `amount ?? Amount`. The actual post-response script passed offline against DTO-shaped positive/negative cases; it was not rerun through Bruno. Logout was skipped. Do not rerun the lifecycle sequence.
- Offline checks passed with fresh fixture ID `d63e0106d40c`/seed 4097 and representative JSON-object/HMAC payloads. Startup targeted only the authorized disposable `localhost:5432/SanadBrunoTestDb`; it reported no migrations applied. Disposable lifecycle users and payment/subscription/invoice records remain, including the pending enrollment attempt from the earlier failed run. Do not reset/drop/migrate or manually clean DB records. API stopped, port 55819 closed, generated fixture files removed; no provider/production action occurred. HC-TASK-033 remains In Progress with lifecycle coverage partial and the invoice-detail assertion corrections unverified. Do not commit, push, package, deploy, begin HC-TASK-021 before HC-TASK-032/034, or start another product slice.
- A later guarded API startup for a focused invoice-detail check unexpectedly applied `20261002134931_AddEmailOutbox` to the same authorized disposable DB; this was not intended or freshly authorized. Test-user seeding was disabled and no Bruno requests were sent. The API was stopped; port 55819 and generated fixture cleanup were verified. No rollback/reset/further migration/database cleanup was attempted. Suspend DB-backed verification pending owner review/direction. No production/provider action occurred.
- After owner authorization, a clean-target startup initially failed while creating the renewal fixture. Source review found the domain copied EF-owned benefit instances from a tracked plan; it now clones those values, with a focused regression test. That test passed; subscription tests passed 147 with 3 PostgreSQL-only skips; full suite passed Architecture 1 + Unit 2,172 with 3 PostgreSQL-only skips; solution build passed 0 warnings/errors.
- With owner approval, guarded startup applied the Finance migration to the exact disposable `localhost:5432/SanadBrunoTestDb`; offline payload/ID/HMAC checks passed for all seven webhook variants. The next explicit-path Bruno lifecycle run passed 5 requests and stopped at enrollment payment intent with HTTP 400; `--bail` skipped the remaining 18 requests. API logs show the Finance query ran successfully, but no effective `platform_charge_rules` row exists. The application correctly fails closed when the required Admin-configured rule is absent; do not add a product default. The remaining likely harness gap is provisioning an effective shared Finance rule in the disposable fixture before subscription requests. Port 55819 is closed and only the exact generated `local-fixtures.bru` and `service-icon-fixture.png` artifacts were removed. The disposable DB remains migrated with partial fixture state; no reset, rollback, record cleanup, or further DB-backed run was performed. No provider/production action, commit, push, package, or deployment occurred. HC-TASK-033 remains In Progress; lifecycle coverage is incomplete. Next: design and test bounded fixture provisioning, then obtain explicit authorization before another stateful lifecycle run.
- Fixture-only correction reviewed for the preceding 400: `TestPlatformChargeRuleSeeder` is called only by the opt-in Development `TestUserDataSeeder`, and provisions an effective version using the Finance writer only when no effective rule exists. It uses 1.00% fee and 0.00% tax, preserves effective/future rules, selects the next version, tolerates a concurrent effective-rule winner, and throws clearly if provisioning still fails. Its six focused tests passed; the earlier API build passed with 0 warnings/errors and the reviewer approved the bounded change. This is not a product fallback. The seeder change has not been DB- or Bruno-runtime-verified; no further DB run is authorized or performed. The API is stopped, port 55819 is closed, and `local-fixtures.bru`/`service-icon-fixture.png` are absent. The lifecycle design remains the single Bruno CLI invocation with `bru run` followed by the 24 explicit request paths selected by contiguous `meta.seq` 0–23, `--env local-fixtures --insecure --bail --reporter-skip-body`; this excludes `meta.bru` and unrelated requests (see the exact sequence evidence in `Care_Homes_Tasks.md`). No Postman update applies because no HTTP contract changed. HC-TASK-033 remains In Progress. Next: reviewer/mastermind validate this fixture-only checkpoint and obtain fresh authorization before any stateful lifecycle run.
- Mastermind integration for the fixture-only change passed: `dotnet build Sanad.slnx --no-restore -m:1 --nologo` (0 warnings/errors); focused Finance + Families.Subscriptions tests (168 passed, 3 PostgreSQL-only skipped); full solution tests (Architecture 1 passed; Unit 2,178 passed, 3 PostgreSQL-only skipped). Offline rendering of all seven callback request bodies from their actual Bruno templates passed JSON-object and HMAC-SHA512 verification using fixture-only rates (1% / 0%) and a synthetic fixture run ID; enrollment, first renewal failure, renewal retry, and plan-change transaction/order IDs were distinct as intended, with duplicate requests retaining their scenario IDs. These are offline/static results, not Bruno-runtime coverage. No API/database/Bruno was run after the seeder change. The exact explicit-path command is recorded in the task checklist. No commit, push, package, release, or deployment is authorized by these test results.
- After fresh owner authorization, the guarded startup targeted only `localhost:5432/SanadBrunoTestDb`; all migrations were already current, the opt-in fixture seeder ran, and no provider/production endpoint was contacted. Offline rendering using actual fixture ID `365dd4c7a77d` and the expected 1%/0% test rule verified all seven actual callback templates as JSON objects with valid server-order HMAC-SHA512; enrollment 913824/813824, first renewal 923824/823824, retry 928824/828824, and plan change 933824/833824 IDs were distinct, with duplicates reusing IDs. The single explicit 24-path lifecycle run passed 24 requests and 24/24 assertions; one unrelated Wellness Tips parser skip remained. Payment/callback/idempotency, renewal failure/grace/retry, plan-change, invoice list/detail, and logout passed. API stopped; generated environment/icon removed; port 55819 closed. The DB retains disposable test records and was not reset or manually cleaned. Lifecycle coverage is now verified, but changed caregiver quote/Family checkout Bruno coverage remains unverified; HC-TASK-033 stays In Progress. No commit, push, package, or deployment occurred.

## Scope

Backend only. Include caregiver review permission correction now (SuperAdmin/SupportAdmin, not ContentAdmin), Family caregiver ratings/top-10, Admin APIs and in-app/email events; no SMS notifications. Do not expand unrelated caregiver booking work or create future slice backlogs. Priority direction only: Care homes → Chat → Notifications → application UI reconciliation.

Read [governance](governance/AGENTS.md) and preserve the worker order and gates. The owner approved this slice and answered its remaining business contracts. Continue the current slice only; do not re-ask completed intake.

## Safety and cleanup

During the earlier documentation cleanup, no product code, database/provider actions, migrations or execution-phase changes occurred; that cleanup deleted only its exact 118-file manifest. The later owner-approved caregiver-review and caregiver-rating implementation is recorded in the active task evidence. Preserve the existing dirty Elderly audit, VPS environment, root fixture helper, UI, and protected fixture images.

The earlier 103-file generated project backlog is superseded, not executable. [Cleanup proposal](operations/care-homes/Cleanup_Proposal.md) records the confirmed 118-file cleanup and retained-reference checks. Preserve current source/evidence and unrelated changes; the older legacy deletion list was not used automatically.

Owner clarified cleanup must include irrelevant legacy workflows, documentation and handoffs, not just generated drafts. The reconciled [cleanup proposal](operations/care-homes/Cleanup_Proposal.md) records the completed exact 118-file deletion and replacement mapping: 102 withdrawn generated task drafts plus 16 unchanged legacy sources. Current/modified audits, coverage evidence, the modified fixture helper, and all unrelated files were explicitly excluded. Obsolete instructions were removed rather than renamed or archived as competing active sources.

## Retained historical migration evidence

Governance and four scripts have canonical docs locations; root AGENTS.md is the discovery pointer. Earlier script relocation checks passed: 312 route-method signatures, 352 Postman requests, 993 Bruno requests, no missing/orphan mappings; fixture environment restoration verified. These are historical migration checks, not current Care homes implementation results. See retained [migration evidence](slices/documentation/Documentation_Migration_Tasks.md).

## Cleanup checkpoint

Replacement/link review and the confirmed deletion are complete. Retained deployment and mapping references use `docs/tools`; no product code or execution phase changed. This cleanup checkpoint is historical; the active Care homes next action is recorded above.
