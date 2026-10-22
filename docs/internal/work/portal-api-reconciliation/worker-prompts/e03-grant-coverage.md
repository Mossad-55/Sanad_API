# E03 bounded correction — restore grant decision tests

## Authority and baseline

Owner approved E03 and automatic bounded corrections, then requested NEW
sessions for corrections after each prior child stops. Prior children
`ses_eb009c918ffeLAuZIU6LQJSrn3`, `ses_eafbbc346ffeao6QPuZoA161pT`, and
`ses_eafb5454bffewf1kxf0DaTOD18` are stopped. The last ended with planning text
without adding the required tests. Owner now explicitly selected
`opencode/beam-free#xhigh`. Use that model/effort in a fresh session; do not
resume previous children or change providers.

Repository `D:\Sanad_API`, `main` HEAD
`59a6582a065bd81b51d388fd984348b277f09d03` plus existing uncommitted E03 work.
Read root AGENTS, governance, current handoff, approved
`docs/internal/work/portal-api-reconciliation/contracts/e03-medical-access-audit.md`,
and this entire brief. Do not restart implementation. Preserve all unrelated
Owner changes (listed in original E03 brief); preserve all previous source and
tests. Mastermind owns handoff/plan/contracts/prompts.

## Single task

Edit **only** `tests/Sanad.UnitTests/Families/MedicalAccessGrantHandlerTests.cs`.
Existing file has original `MedicalAccessGrantHandlerTests`, weak
`E03GrantAuditTests`, and two `E03PersistenceFailureTests`. Earlier detailed
grant tests were accidentally removed in a prior correction. Restore meaningful
grant decision coverage WITHOUT removing any current tests.

Add class `MedicalAccessGrantHandlerE03Tests` in that file, using its existing
fixture construction pattern and `MedicalAccessGrantHandlerTests.FakeGateway`.
Keep the task small: implement these eight real tests only:

1. Allowed create: invoke actual handler; single event must have matching
   actor/dependent/grantee/grant IDs, Allowed/CreateGrant, Limited grant type,
   permission flags true/false/false, UTC timestamp and nonempty correlation,
   null reason. Assert exactly one grant persisted.
2. Allowed revoke: create through handler then revoke through actual handler;
   find event by `Action == RevokeGrant`, not timing. Assert matching IDs,
   Allowed decision, permission snapshot and persisted revoked actor/time.
3. Unauthorized create: different Family actor, result Families.AccessDenied;
   Denied/CreateGrant event with attempted actor/dependent, stable reason,
   all grantee/grant/type/permission/expiry fields null; zero grants.
4. Unauthorized revoke: seeded valid grant, different actor; result
   Families.AccessDenied; Denied/RevokeGrant event with null detail fields,
   grant remains unrevoked with no revoked actor.
5. Duplicate: seed active grant directly, invoke create; MedicalAccess.GrantExists
   and zero audit rows, existing grant unchanged.
6. Inactive grantee: fake gateway false; MedicalAccess.InvalidGrant and zero
   audit rows or grants.
7. Unknown grant: owned dependent but unknown grant ID; MedicalAccess.GrantNotFound
   and zero audit rows.
8. Invalid permission shape: edit/share true while view false; gateway true;
   MedicalAccess.InvalidGrant and zero audit rows/grants.

Use EF InMemory for this decision-only correction; no SQLite/atomicity required
in this round. Do NOT edit source, Postman, other tests/docs, or delivery report.
Do NOT write placeholder assertions or remove current tests to lower counts.

## Gate and stop

Run the relevant test project with a filter that actually includes new and old
classes, for example:

`dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj -c Release --filter "FullyQualifiedName~MedicalAccessGrantHandler|FullyQualifiedName~E03GrantAudit|FullyQualifiedName~E03PersistenceFailure"`

Use a test-project build as needed, zero warnings/errors. Do not run a redundant
solution build, full suite, or external API. Return exact command, pass/fail/
skip counts and names of added tests; report COMPLETE for this bounded task only
if all eight are authored and pass. Otherwise report concrete BLOCKED reason.
No shared/persistent DB, migration application, commit/push/deploy, or E04+ work.
