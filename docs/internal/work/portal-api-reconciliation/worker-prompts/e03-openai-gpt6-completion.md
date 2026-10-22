# E03 completion correction — new OpenAI Worker session

## Owner direction and session boundary

The Owner explicitly selected OpenAI `openai/gpt-6-luna#medium` after the
previous `opencode/beam-free#xhigh` route rate-limited. The Worker profile was
reloaded and the effective model verified. This must be a **new Worker session**;
all prior E03 Worker children are terminal. Do not resume or overlap them.

## Baseline and required reading

- Repository `D:\Sanad_API`, branch `main`, HEAD and `origin/main`:
  `59a6582a065bd81b51d388fd984348b277f09d03`.
- Existing E03 code, migration, docs, Postman, and tests are uncommitted local
  phase work. Preserve and finish them; do not restart or overwrite them.
- Read root `AGENTS.md`, `docs/internal/governance/AGENTS.md`, current
  `docs/internal/Mastermind_Handoff.md`, the approved contract
  `docs/internal/work/portal-api-reconciliation/contracts/e03-medical-access-audit.md`,
  the original phase brief
  `docs/internal/work/portal-api-reconciliation/worker-prompts/e03-medical-access-audit.md`,
  and this complete correction prompt.
- Preserve unrelated Owner changes: modified `.codex/agents/*`, `.codex/config.toml`,
  `README.md`, `docs/internal/Project_Main_Goal.md`, governance and workflow docs,
  `tools/opencode_bridge/opencode_bridge.py`, and untracked `.opencode/` and
  `.opencode-audit-tmp/`. Do not stage/alter them. Mastermind owns plan/handoff/
  contracts/prompts.
- The previously discovered Arabic Postman corruption was repaired and verified:
  removing the one Admin E03 item leaves its parsed JSON identical to HEAD.
  Preserve that minimal change and Family description changes; never reserialize
  either collection.

## Objective and boundaries

Complete **only** the outstanding E03 contract acceptance evidence and correction
tests. Do not expand E03: no caregiver medical-read path, permission-consumption
behavior, new roles, user-facing frontend, or later phase. The Families migration
is authored but must not be applied. No API startup, shared/Production DB, real
provider, Bruno, commit, push, package, publish, or deployment.

## Required code/test correction set

1. In `tests/Sanad.UnitTests/Families/MedicalAccessGrantHandlerTests.cs`, preserve
   every original test and implement real E03 tests for:
   - Allowed create/revoke audit fields and persisted grant/revocation facts.
   - Denied create/revoke (`Families.AccessDenied`) audit fields and no grant
     mutation; denied details remain null.
   - No audit for duplicate, invalid/inactive caregiver, unknown grant, invalid
     permission shape, or empty dependent.
   - Audit write failures return `Families.AuditWriteFailed`/503 through the
     actual controllers, without exposing persistence details; cancellation
     propagates.
   - Actual relational atomicity using **test-local SQLite** and real handlers:
     use a test-only SQLite trigger/interceptor to fail the audit INSERT, then
     read using a fresh context and prove create/revoke and audit row both rolled
     back. InMemory or a wrapper throwing before SaveChanges is not transaction
     proof. Also cover denied-audit persistence failure if practical.
   - Remove only Worker-created placeholder tests; no trivial `Assert.True(true)`.

2. In `tests/Sanad.UnitTests/API/AdminCaregiverMedicalAccessAuditTests.cs`:
   - Retain registered policy checks: Normal SuperAdmin and SupportAdmin allowed;
     ContentAdmin, Family, FinanceAdmin and non-Normal tokens denied. Assert the
     controller actually uses `MedicalAccessAuditAdmin`.
   - Open the isolated generated `/openapi/v1.json` and assert GET route,
     application/json 200 schema, resolve page and item DTO references, required
     audit fields, query parameters, and absence of notes/medical/PII fields.
   - Use multiple matching/nonmatching records to assert actor/dependent/grantee
     filters, action/decision filters, exact stable ordering (including ID
     tiebreak), page contents/counts, cutoff boundary, invalid UTC/range/filter,
     and overflow errors. Add a test-local SQLite relational filter so EF
     translation of strongly typed IDs is verified; do not rely only on InMemory.

3. In `tests/Sanad.UnitTests/Families/CaregiverMedicalAccessAuditRetentionTests.cs`,
   retain actual hosted `CleanupOnceAsync` DI-scope coverage, add >500 expired
   rows to prove batched cleanup, assert fresh/in-boundary rows survive, and
   assert tracked audit update **and delete** remain blocked.

4. Preserve existing production-source behavior fixes. Review the current
   `Families.AuditWriteFailed` mapping and create/revoke controller handling; only
   that code maps to safe 503, all pre-existing normal statuses/errors remain.
   Audit query filters require strict UTC, exact `AddMonths(12)` range and
   overflow rejection. Do not weaken append-only guard; purge only expired E03
   rows via system cleanup.

## Postman and final evidence

- Admin collection: keep the verified one-item E03 addition minimal and
  structurally identical to HEAD otherwise. Family collection: keep only the
  intended E03 grant/revoke descriptions. Admin request must demonstrate auth,
  filters, page/pageSize, response envelope and errors. Use UTF-8-preserving edits.
- Validate both collection JSONs; programmatically parse HEAD Admin JSON and
  current JSON, remove only the E03 item, and assert the remaining JSON trees are
  equal. Verify the route appears once and route mapping passes.
- Update the existing delivery report with all actual changed files, exact
  commands/exits/counts/warnings/skips, migration authored/not-applied state,
  limitations, and no unsupported COMPLETE claims.
- Run one strict Release solution build after final source changes (zero
  warnings/errors), focused grant/Admin/retention/OpenAPI tests, both Postman
  validations, and `git diff --check`. No full suite unless material risk
  warrants it.
- Return COMPLETE only if acceptance gates really pass; otherwise BLOCKED with
  the concrete gap. Report and stop. No release operations or E04+ work.
