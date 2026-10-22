# E03 grant audit tests — bounded fresh-session correction

## Session and baseline

The Owner authorized same-provider/model corrections and asked that a finished
Worker session not be reused. Use a **fresh** `sanad-worker` child on OpenAI
`openai/gpt-6-luna#medium`; do not resume any previous E03 child.

- Repository: `D:\Sanad_API`, branch `main`, baseline HEAD and origin/main
  `59a6582a065bd81b51d388fd984348b277f09d03` plus existing uncommitted E03 work.
- Read root `AGENTS.md`, governance, current Mastermind handoff, approved E03
  contract `contracts/e03-medical-access-audit.md`, original Worker brief
  `worker-prompts/e03-medical-access-audit.md`, and this entire correction.
- Preserve all existing tests and unrelated Owner changes. Do not stage, revert,
  or overwrite any other file.

## Single owned file

Edit **only** `tests/Sanad.UnitTests/Families/MedicalAccessGrantHandlerTests.cs`.
Do not edit source, Postman, any report, or another test file in this round.
The delivery report says some audit tests passed, but actual source currently
contains only a basic create-success count test, empty-dependent denial, and
create-only write-failure/cancellation tests. Restore the missing behaviors with
real assertions; no placeholder facts or `Assert.True(true)`.

## Required cases

Use the existing in-memory fixture for decision semantics:

1. Create success: assert one grant and one Allowed/CreateGrant audit with
   matching actor/dependent/grantee/grant IDs, `Limited` type, view/edit/share
   booleans, expiry, UTC occurred time, nonempty correlation ID, and null reason.
2. Revoke success: create through the handler, revoke through the real revoke
   handler, assert Allowed/RevokeGrant audit fields and persisted revoker/time.
3. Foreign actor create denial: `Families.AccessDenied`, Denied/CreateGrant audit
   with stable reason, null grant details, and no grant mutation.
4. Foreign actor revoke denial: `Families.AccessDenied`, Denied/RevokeGrant audit,
   null grant details, and existing grant remains unrevoked.
5. Duplicate grant, inactive/nonmedical caregiver, unknown grant, invalid
   permission shape, and empty dependent preserve existing result codes and add
   no denied audit rows (a duplicate scenario may retain only its original
   Allowed/CreateGrant row).

Do not implement SQLite atomicity in this single-file task; it remains a separate
E03 acceptance gap for the next correction round. Do not claim that in-memory
tests prove relational rollback.

## Verification and stop

Run:

```powershell
dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj -c Release --filter "FullyQualifiedName~MedicalAccessGrantHandlerTests|FullyQualifiedName~E03GrantAuditTests|FullyQualifiedName~E03PersistenceFailureTests"
```

Report exact result count, including the actual fully-qualified classes selected,
and a concise list of test cases added. If any test fails, fix only within the
owned file and rerun the filter. Do not update the delivery report or claim the
whole phase complete. No API startup, database, migration application, Bruno,
commit, push, deploy, or E04+ work. Stop after returning this bounded report.
