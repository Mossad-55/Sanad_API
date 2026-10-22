# E03 correction handoff — new Worker session

Owner direction: after the prior Worker stopped, use a **new** Worker session
with the same `opencode/beam-free#high`. Do not resume the old child. The prior
child `ses_eb009c918ffeLAuZIU6LQJSrn3` is terminal; this is replacement ownership,
not overlapping work. Scope is still the existing Owner-approved E03 contract.

## Baseline and required context

- Repository `D:\Sanad_API`, branch `main`, HEAD
  `59a6582a065bd81b51d388fd984348b277f09d03`.
- Read root `AGENTS.md`, governance, current Mastermind handoff, the full
  `contracts/e03-medical-access-audit.md` and original
  `worker-prompts/e03-medical-access-audit.md` under
  `docs/internal/work/portal-api-reconciliation/`, then this whole correction.
- E03 source/migration/tests/docs are **uncommitted**. Preserve and finish them;
  do not restart from HEAD or delete existing implementation/tests.
- Unrelated Owner changes remain in `.codex/`, `.opencode/`,
  `.opencode-audit-tmp/`, README.md, Project_Main_Goal.md, governance,
  operations/codex-workflow.md, and tools/opencode_bridge/opencode_bridge.py.
  Do not edit/stage any of those. Mastermind also owns handoff/plan/contracts/
  prompts; do not edit those.
- Only edit files already allowed by E03's amended Worker-owned manifest.
  No new features, roles, caregiver medical-data reads, or later phase.
- No commit, push, publish, deploy, persistent/shared database, production request,
  provider call, Bruno, or migration application. Migration authoring only.

## Current verified state — do not trust old COMPLETE labels

- Implementation records grant/revoke decisions, adds a restricted Admin read
  and retention hosted service. Review corrections addressed blocked purge,
  cancellation handling, safe audit errors, and offset overflow.
- Postman encoding is repaired. Mastermind independently parsed UTF-8 HEAD and
  the Admin collection: after removing the single E03 item the objects are
  identical, including Arabic. Preserve this. The E03 item currently uses
  `{{adminToken}}` despite existing requests using `{{accessToken}}`; fix only
  the new item and improve its paging/response description. Never reserialize
  the entire collection. Family changes are two intended descriptions.
- Admin tests use registered authorization services and generated OpenAPI route/
  query assertions. They still do not resolve/assert the 200 response/item
  schemas. Several list tests assert only counts, not that filters/order work.
- The **last** old-Worker edit removed the previously authored
  `E03GrantAuditRealTests` class while adding persistence tests. Current
  `MedicalAccessGrantHandlerTests.cs` has original tests, weak create-success +
  empty-dependent tests, and two create-only persistence/cancellation tests.
  Restore meaningful missing coverage; do not rely on historical 13-pass claims.
- Retention tests invoke the actual `CleanupOnceAsync`; inspect final file to
  verify update and delete immutability, not just trust reports.
- Delivery report is stale: it still claims Postman/tests are uncompleted and
  lacks exact commands. Rewrite it accurately only when final evidence exists.

## Required completion

### 1. Grant behavior, failure, and transaction proof

In `tests/Sanad.UnitTests/Families/MedicalAccessGrantHandlerTests.cs`, retain
original tests and add/consolidate meaningful tests for:

- Allowed create/revoke with matching actor/dependent/grantee/grant IDs, action,
  decision, permissions, safe reason and timestamps; persisted grant mutation.
- Denied create/revoke (`Families.AccessDenied`) with no grant mutations and no
  attempted grant details in the audit; excluded duplicate/inactive/unknown/
  invalid/empty inputs add no audit row.
- `DbUpdateException` in create/revoke/denial saves yields
  `Families.AuditWriteFailed`, not success or a seemingly audited denial.
- Cancellation at create/revoke saves propagates `OperationCanceledException`.
- **Actual relational atomicity**: use SQLite `DataSource=:memory:` (never an
  existing DB), invoke real handlers, inject failure on the audit INSERT using a
  SQLite test-only trigger or command interceptor, then read from a fresh
  context to prove grant insert/revocation and audit event both rolled back.
  InMemory or an adapter that throws before save is NOT transaction proof.
- Verify actual Family controller create/revoke maps audit-write failure to 503
  without DB details; existing normal errors/statuses remain unchanged. These
  controller assertions can live in the approved Admin API test file.
- No placeholders, trivial assertions, duplicate test classes, or loss of
  previously passing source tests. Remove only Worker-created placeholders.

### 2. Admin query and generated schema

In `tests/Sanad.UnitTests/API/AdminCaregiverMedicalAccessAuditTests.cs`:

- Preserve registered authorization outcome tests; add FinanceAdmin deny and
  assert actual controller Authorize references the dedicated policy.
- Resolve the actual generated 200 response `$ref`, page `items`, and audit-item
  schema. Assert fields and numeric/string shape match actual DTO; explicitly
  reject notes, medical payloads, names, phone/email, and raw request content.
- Seed multiple matching and nonmatching rows for actor/dependent/grantee filters
  so a missing filter would fail. Check exact IDs for paging/order, including
  equal timestamps with ID tie-break; do not assert counts only.
- Validate invalid ranges/UTC/overflow with the contract's standard 400
  `Api.Validation.Failed` response, not ad-hoc `{code,message}` bodies. Current
  Admin controller returns ad-hoc errors; correct it within owned files.
- Prevent `lower.AddMonths(12)` throwing on a valid high-year input (e.g. 9999).
  Make the calendar bound overflow-safe and add regression coverage.
- Verify relational query execution for the typed-ID filters using test-only
  SQLite, not only EF InMemory. Current controller uses `.ActorUserId.Value`/
  `.DependentId.Value` in EF predicates; use typed-ID comparisons if translation
  fails. Test one real relational filter path before claiming compatibility.

### 3. Retention and implementation review fixes

- Confirm service purge removes only expired E03 events and normal tracked
  update/delete remain blocked. Cover a multi-batch case and calendar cutoff
  without destructive/shared targets. Use a controllable existing clock pattern
  if needed within approved files; do not introduce unrelated framework work.
- Validate domain enum/time invariants already promised in report: current
  `CaregiverMedicalAccessAudit.Create` checks UTC but not enum membership. Add
  defined-enum checks and focused tests within owned test files.
- Keep changes bounded. If anything beyond E03 manifest is necessary, stop and
  report exact path/reason.

### 4. Accurate docs and final verification

- Correct only the E03 Admin Postman item auth variable, paging/time defaults,
  successful envelope, and 400/401/403 notes; keep all pre-existing content
  structurally unchanged from HEAD. Preserve Arabic UTF-8.
- Align `docs/admin/caregiver-medical-access-audit.md` and Family guide with
  verified route/permissions/errors/privacy and migration-not-applied status.
- Finish implementation/tests first; then run one strict Release solution build
  (zero warnings/errors) and focused grant/audit/Admin/retention/OpenAPI tests.
  No full suite by default. Stop failed gates with exact evidence; do not keep
  deferring tests or claiming partial acceptance.
- Parse both Postman collections and compare all pre-existing Admin JSON content
  with UTF-8 HEAD after removing the single E03 item; route must occur once.
  Run scoped `git diff --check` including new files.
- Rewrite `reports/e03-medical-access-audit-delivery.md` with full actual paths,
  commands, exit/counts/warnings/skips, reviewed fixes, limitations, and authored
  but unapplied migration. Distinguish Worker evidence from Mastermind evidence.
- Return COMPLETE only when the approved acceptance gates really pass, otherwise
  BLOCKED with a concrete cause. End with changed files and remaining risks.
