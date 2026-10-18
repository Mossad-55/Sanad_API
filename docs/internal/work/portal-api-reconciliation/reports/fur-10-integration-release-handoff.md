# FUR-10 — Cross-phase integration and release handoff

Date: 2026-10-18 (Africa/Cairo). **Status: complete (documentation-only).**
**FUR-10 deployment: N/A** — no runtime, API, configuration, migration,
provider, database, build, test, commit or push action is included or claimed.

Pinned baseline: `main` at `ef50f690338b3a2ae68b697fc80fc1e2488e560c`
(FUR-09 local API evidence commit) plus pre-existing local delta; all unrelated
modified/untracked worktree files were preserved untouched.

Scope: reconcile FUR-00–FUR-09 status, release revisions, migration and
configuration evidence from the existing canonical phase plan, approved
contracts, worker/review reports and release receipts; publish the dated
audience handoffs for FUR-05–FUR-09; correct the audience index and current
status summaries. No Production, database, provider or live check was repeated.

## Phase ledger — FUR-00 through FUR-10

| Phase | Status | Deployed revision | Migrations at deploy | Configuration | Key evidence |
| --- | --- | --- | --- | --- | --- |
| FUR-00 — Alignment | Done | N/A (documentation setup) | None | None | [Plan acceptance record](../plan.md#frontend-follow-up-sequence--2026-10-16); 17/17 relative links, 46/46 intake references; Admin/owner historical reports linked from [handoffs index](../../../../frontend-handoffs/README.md). |
| FUR-01 — Contract accuracy | Done | `f9231fb` | 0 applied; 9/9 heads matched | Unchanged (config hashes verified) | [Contract](../contracts/fur-01-contract-accuracy.md); [receipt](fur-01-deployment.md): 29/29 metadata operations, public direct-path limitation disclosed. |
| FUR-02 — Verification continuity | Done | `5cd3526` | 1 applied (Identity); other eight 0; 9/9 heads matched | Unchanged | [Contract](../contracts/fur-02-verification-continuity.md); [receipt](fur-02-deployment.md). |
| FUR-03 — Finance verification | Done (verification-only) | N/A — deployment N/A; phase commit `858da09` pushed | None | None | [Contract](../contracts/fur-03-finance-verification.md); [receipt](fur-03-release.md): no defect reproduced; 12/12 worker-reported PostgreSQL tests on disposable `SanadFur03VerifyDb`. |
| FUR-04 — Care Home readback | Done | `31b13cc` | 0 applied; 9/9 heads matched | Unchanged (host fingerprints verified) | [Contract](../contracts/fur-04-care-home-readback.md); [receipt](fur-04-release.md): guarded full Unit 3475/0/31, architecture 1/1; unauthorized local database setup deviation disclosed, no further DB action. |
| FUR-05 — Owner settlements | Done | `ca2ef2f` | 0 applied; 9/9 heads matched | Unchanged (service/systemd/appsettings fingerprints verified) | [Contract](../contracts/fur-05-owner-settlements.md); [review](fur-05-mastermind-review.md); [receipt](fur-05-release.md): focused 56/56; guarded full Unit 3,482/0/31; post-deploy OpenAPI confirms `GET /api/v1/care-homes/payouts/mine` (200). No live authenticated owner-ledger run. |
| FUR-06 — Eligible caregiver payouts | Done | `7339034` | 0 applied (no migration added); 9/9 heads matched | Unchanged | [Contract](../contracts/fur-06-eligible-caregiver-payouts.md); [receipt](fur-06-release.md): focused 21/21 pre-commit; guarded full Unit/Architecture attempts failed `testhost` connection, not repeated during publish/deploy; brief startup HTTP 502 recovered. |
| FUR-07 — Community moderation | Done | `303a131` | 1 applied (Community `20261017154400_AddCommunityModeration`); other eight 0; 9/9 heads matched | Unchanged | [Contract](../contracts/fur-07-community-moderation.md); [receipt](fur-07-release.md): strict build 0 warnings/errors; 153 focused tests; PostgreSQL advisory-lock path not exercised against live PostgreSQL. |
| FUR-08 — Owner disputes | Done | `14757cc` | 0 applied; 9/9 heads matched | Unchanged (protected host fingerprints verified) | [Worker brief](../worker-prompts/fur-08-owner-disputes.md); [delivery](fur-08-worker-delivery.md); [receipt](fur-08-release.md): focused 20/20; no live authenticated feature request or Bruno run. |
| FUR-09 — Caregiver integrity disposition | Done for approved local synthetic API test/report scope only | **N/A — deployment N/A** | None | None | [Contract](../contracts/fur-09-caregiver-integrity-disposition.md); [disposition report](fur-09-caregiver-integrity-disposition.md): strict build 0 warnings/errors; focused 24/24 on synthetic SQLite attached schemas. **Six reported Production rows and their cause remain unverified; no repair/quarantine recommended or authorized.** |
| FUR-10 — Integration and release handoff (this phase) | In progress → complete as documentation-only | **N/A** | None | None | This report; [contract](../contracts/fur-10-cross-phase-integration-handoff.md). |

### Release / configuration / migration reconciliation

- **Runtime deltas deployed:** FUR-01, FUR-02, FUR-04, FUR-05, FUR-06, FUR-07,
  FUR-08 (7 deployed phases). Verification-only/docs-only/local-only phases:
  FUR-00, FUR-03, FUR-09, FUR-10 (deployment N/A for each).
- **Migration additions:** exactly two new applied migrations across the sequence —
  Identity (applied with FUR-02 at `5cd3526`) and Community
  `20261017154400_AddCommunityModeration` (applied with FUR-07 at `303a131`).
  FUR-01, FUR-04, FUR-05, FUR-06 and FUR-08 added no migration and applied zero
  at deploy; FUR-03, FUR-09 and FUR-10 have no runtime delta.
- **Configuration:** every deployed phase's receipt records unchanged host
  configuration fingerprints (service environment, systemd unit, existing
  `appsettings.json`/`appsettings.Development.json` as applicable). No
  configuration delta exists anywhere in the sequence.
- **Heads:** every deployed release records all 9/9 required/applied migration
  heads matched at verification time.
- **Deployment target:** all deployments used the previously owner-designated
  Production VPS with owner-enabled SSH tunnel verification; direct public HTTP
  from the build machine is unverified by design and disclosed per receipt.

## Audience handoffs published (2026-10-18)

All six reports distinguish deployed behavior from client adoption; none claims
frontend repository inspection or client approval. Indexed from
[frontend-handoffs README](../../../../frontend-handoffs/README.md):

| Phase | Audience | Report | Deployment truth stated |
| --- | --- | --- | --- |
| FUR-05 | Admin — Care Home Owner portal | [Owner settlements](../../../../frontend-handoffs/admin/care-home-owner/phase-05-owner-settlements/2026-10-18-owner-settlements.md) | Deployed `ca2ef2f`; zero migrations; no live authenticated owner-ledger run. |
| FUR-06 | Admin — Admin portal | [Caregiver eligible payouts](../../../../frontend-handoffs/admin/admin-portal/phase-06-caregiver-eligible-payouts/2026-10-18-caregiver-eligible-payouts.md) | Deployed `7339034`; no migration; no IBAN/payout-account fields. |
| FUR-07 | Admin — Admin portal | [Community moderation](../../../../frontend-handoffs/admin/admin-portal/phase-07-community-moderation/2026-10-18-community-moderation.md) | Deployed `303a131`; Community migration applied; advisory-lock not exercised live. |
| FUR-07 | App — shared Family/Caregiver/Elderly | [Community report intake](../../../../frontend-handoffs/app/shared/phase-07-community-report-intake/2026-10-18-community-report-intake.md) | Deployed `303a131`; posts-only reporting; reported posts remain visible. |
| FUR-08 | Admin — Care Home Owner portal | [Owner disputes](../../../../frontend-handoffs/admin/care-home-owner/phase-08-owner-disputes/2026-10-18-owner-disputes.md) | Deployed `14757cc`; zero migrations; no live authenticated request/Bruno. |
| FUR-09 | Admin — Admin portal | [Caregiver integrity disposition](../../../../frontend-handoffs/admin/admin-portal/phase-09-caregiver-integrity-disposition/2026-10-18-caregiver-integrity-disposition.md) | Local synthetic reproduction only; **deployment N/A**; Production rows unverified. |

The dated FUR-00–FUR-04 reports remain intact as historical snapshots; the index
corrects the stale FUR-04 current-status cells (accepted/deployed `31b13cc`) and
the FUR-04-and-later phase rows now point to the newer handoffs where applicable.

## Verification limitations carried forward (no new checks run)

- FUR-05: authenticated owner ledger not queried live (no authorized
  owner fixture/token at release); covered by isolated tests only.
- FUR-06: guarded full Unit/Architecture reruns failed at `testhost` connection;
  worker-focused 21/21 plus strict build remain the valid evidence; the
  concurrent duplicate-writer collision and its disclosed resolution remain
  part of phase history.
- FUR-07: PostgreSQL advisory-lock path not exercised against a live
  PostgreSQL server.
- FUR-08: no live authenticated FUR-08 endpoint request or Bruno run; focused
  tests are the evidence.
- FUR-09: SQLite attached-schema execution is not PostgreSQL proof; the six
  reported Production rows and their cause remain **unverified** pending
  exact-ID, sanitized operator evidence via the unchanged read-only
  `diagnostics/caregiver-identity.sql` and
  `diagnostics/caregiver-identity-runbook.md`. No repair, quarantine, INNER JOIN
  change or data action is recommended or authorized.
- FUR-04: the worker's unauthorized local database setup deviation (three
  unintended lowercase database instances created/dropped during target setup)
  remains disclosed; no further database action occurred.
- FUR-02: the exact original frontend OTP exception was not reproduced
  (historically fixed in `5edd3419`).
- All phases: no frontend repository inspection and no external report sending;
  client adoption/exercise/approval is unknown and unclaimed. Direct public HTTP
  from the build machine remains unproven; SSH-tunneled verifier evidence is the
  documented path.

## Roadmap state and boundaries

- **E01–E08: Not started and individually uncontracted.** FUR-10 does not approve,
  begin or schedule them; each needs its own owner-approved contract.
- This handout does **not** claim complete production readiness or full 19-phase
  roadmap completion: it closes the selected FUR-00–FUR-09 sequence with
  accurate internal status, release/config/migration accounting and
  owner-forwardable audience handoffs.
- No source/API/test/Postman changes, production access, database/provider call,
  migration, package, deployment, commit or push were performed by this phase.
