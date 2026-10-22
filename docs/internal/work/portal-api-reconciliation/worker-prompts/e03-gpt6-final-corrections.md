# E03 final corrections — fresh OpenAI GPT-6 Worker

## Owner direction / session boundary

The Owner explicitly requires a **new Worker session after the prior session
finishes** and selected OpenAI `openai/gpt-6-luna#medium`. The old Beam sessions
are terminal and the first GPT-6 session also ended; do not resume any previous
child. This is a fresh, non-overlapping correction session.

## Baseline and reading

- Repository `D:\Sanad_API`, branch `main`; pinned `HEAD` and `origin/main` are
  `59a6582a065bd81b51d388fd984348b277f09d03`, plus existing uncommitted E03 work.
- Read root `AGENTS.md`, governance, current Mastermind handoff, the complete
  approved `contracts/e03-medical-access-audit.md`, original
  `worker-prompts/e03-medical-access-audit.md`, previous completion brief
  `worker-prompts/e03-openai-gpt6-completion.md`, and this entire brief.
- Preserve all existing E03 implementation, migrations, tests, docs, Postman
  changes, ignored release artifacts, and unrelated Owner changes. In
  particular, the Admin Postman Arabic repair was independently verified by
  parsing HEAD/current JSON and removing the single E03 item; the remaining
  object trees are identical. Do not reserialize that collection.
- E03 migration is authored but unapplied. Do not apply it or start the API
  against any shared/Production database.

## Current verified gaps

- `MedicalAccessGrantHandlerTests.cs` has the pre-E03 tests, basic E03 create
  success/empty-dependent tests, and create-only persistence/cancellation tests.
  More detailed grant tests added in a former Worker round were later removed.
- `AdminCaregiverMedicalAccessAuditTests.cs` covers direct controller filters,
  time bounds, expiry, paging and registered policy outcomes; OpenAPI currently
  checks route/200/query names but does not resolve/assert the 200 response page
  and audit-item schemas or sensitive-property exclusions.
- Retention service uses bounded `ExecuteDeleteAsync`; tests call
  `CleanupOnceAsync`, but only cover one batch and tracked update, not tracked
  delete.
- The Admin Postman item is now a minimal one-item E03 addition with original
  Arabic content preserved; the Family collection has only its two intended
  grant/revoke description edits. Revalidate structurally; do not reserialize.
- E03 delivery report is stale/incomplete and must be rewritten after all gates.

## Owned correction files

For this correction round edit only:

- `tests/Sanad.UnitTests/Families/MedicalAccessGrantHandlerTests.cs`
- `tests/Sanad.UnitTests/API/AdminCaregiverMedicalAccessAuditTests.cs`
- `tests/Sanad.UnitTests/Families/CaregiverMedicalAccessAuditRetentionTests.cs`
- `docs/internal/work/portal-api-reconciliation/reports/e03-medical-access-audit-delivery.md`

Postman collections should remain unchanged unless validation proves the E03
item/Family descriptions are absent or inaccurate; if so, apply only a minimal
UTF-8-preserving patch and prove the rest of the Admin collection equals HEAD.
Do not alter the production source in this pass unless a focused test proves a
contract defect; if that happens, stop and report exact file/failure for a
Mastermind correction instruction. Do not edit the contract, prior prompts,
`plan.md`, `Mastermind_Handoff.md`, or any file outside the correction manifest.

## Required grant audit tests

Use real handlers with isolated in-memory data for domain/application cases and
test-local SQLite for transaction cases. Preserve the original pre-E03 tests and
remove only Worker-created placeholders. Required assertions:

1. Allowed create: one grant and one Allowed/CreateGrant event with matching
   actor, dependent, grantee, grant ID, permission flags, UTC timestamp and
   nonempty correlation; no denial reason.
2. Allowed revoke: one existing grant is revoked, matching revoker/time are
   persisted, and an Allowed/RevokeGrant event records the grant permission
   snapshot.
3. Denied create and revoke from a foreign actor: Families.AccessDenied, a
   Denied event with stable reason and null attempted grant details, and no
   grant mutation.
4. Duplicate, inactive/nonmedical recipient, unknown grant, invalid permission
   shape, and empty dependent preserve existing result codes and add no audit
   event. Empty dependent must not throw while constructing an audit.
5. For each successful create/revoke path, use SQLite `DataSource=:memory:` and
   a test-only trigger/interceptor that fails the audit INSERT. Invoke the real
   handler, then query with a fresh context and prove grant creation/revocation
   and audit insertion all rolled back. InMemory or a wrapper throwing before
   SaveChanges is not transaction proof.
6. Audit persistence failures return Families.AuditWriteFailed and the Family
   controller produces safe HTTP 503 without DB details; existing normal grant
   errors/statuses remain unchanged. Cancellation propagates and is never
   converted to AuditWriteFailed.

## Required Admin/API and retention tests

- Add generated OpenAPI assertions that resolve the actual GET 200 response
  `$ref`, `CaregiverMedicalAccessAuditPage.items`, and the audit item schema;
  assert the listed fields and absence of notes/medical/PII properties.
- Exercise the registered `MedicalAccessAuditAdmin` policy for Normal
  SuperAdmin/SupportAdmin (allow) and ContentAdmin/Family/FinanceAdmin or
  Restricted SuperAdmin (deny). Assert controller policy metadata too.
- Seed matching and nonmatching rows to assert actor/dependent/grantee/action/
  decision/date filters, exact IDs in stable order (including timestamp tie),
  paging contents/counts, invalid filters, UTC range and overflow behavior.
  Include a test-local SQLite query path to catch EF typed-ID translation issues.
- Call the actual `CleanupOnceAsync` through a local DI scope/SQLite context;
  include more than 500 expired rows, a row exactly at the retention boundary,
  and a fresh row. Assert expired rows are purged in batches, boundary/fresh
  rows survive, and tracked audit update **and delete** are rejected by the
  ordinary Families context guard.

## Required Postman / report work

- Admin E03 request: Normal JWT, SuperAdmin/SupportAdmin, exact route, filters,
  paging defaults/max, response envelope, and 400/401/403 notes.
- Family create/revoke descriptions: successful grant/revoke and
  Families.AccessDenied decisions audited; invalid/inactive grantee, duplicate,
  unknown grant, malformed requests, GET grant reads, and framework auth
  failures excluded.
- Parse both collections as UTF-8 JSON. Compare original Admin collection from
  HEAD to current collection after removing only the E03 item; assert structural
  equality and one E03 route occurrence. Run route validation.
- Rewrite the delivery report with complete actual file paths and exact final
  commands, exit codes, pass/fail/skip counts, build warnings, Postman/diff
  results, migration authored/not applied, and remaining limitations. Do not
  retain stale “placeholder” or “Postman deferred” statements after completion.

## Gates and stop

After final changes run one strict `dotnet build Sanad.slnx -c Release` (zero
warnings/errors), focused grant/Admin/retention/OpenAPI tests, both Postman
validations, and `git diff --check`. Full suite and Bruno are not required. Use
only isolated synthetic/in-memory/SQLite data; no API startup, shared DB,
migration application, provider calls, production requests, commit/push/package/
deployment, or E04+. Report `COMPLETE` only if actual gates cover the requirements;
otherwise report `BLOCKED` with the precise unresolved acceptance items. Preserve
all unrelated Owner changes and stop.
