# OpenCode worker brief — FUR-06 eligible caregiver payout selector

## Pinned state

- Repository: `D:\Sanad_API`
- Base revision: `af78095a38d8ce0e490e842e441037374d3f2c9c`
- Preserve the existing unrelated local changes. Do not revert, stage, or edit
  them.
- Read and follow `AGENTS.md`, `docs/internal/governance/AGENTS.md`,
  `docs/internal/Mastermind_Handoff.md`,
  `docs/internal/Project_Main_Goal.md`, the FUR plan, the FUR-06 contract below,
  and `docs/internal/operations/codex-workflow.md`.

## Approved contract

`docs/internal/work/portal-api-reconciliation/contracts/fur-06-eligible-caregiver-payouts.md`

Implement this whole phase only. The contract is locked. Do not expand scope or
invent policy. If an implementation detail cannot satisfy the contract using
current data, report a blocker instead of changing business rules.

## Owned files

- `src/API/Sanad.API/Controllers/AdminCaregiverPayoutsController.cs`
- `src/Modules/Caregivers/Application/Sanad.Modules.Caregivers.Application/Payouts/CaregiverPayoutReadQueries.cs`
- `tests/Sanad.UnitTests/Caregivers/CaregiverPayoutHandlerTests.cs`
- `tests/Sanad.UnitTests/API/AdminCaregiverPayoutsAuthorizationTests.cs`
- `docs/app/admin/caregiver-payouts.md` (create if absent)
- `docs/postman/admins/Sanad.Admin.Finance.postman_collection.json`
- `docs/internal/work/portal-api-reconciliation/reports/fur-06-worker-delivery.md`

Do not edit migrations, database snapshots, provider/configuration files,
FUR-07 files/collections/tests, the plan, or the handoff. If more files are
necessary, stop and report the exact need before editing outside this manifest.

## Work and acceptance criteria

1. Add a paged, read-only eligible-booking selector under
   `/api/v1/admin/caregiver-payouts` using the existing
   `PayoutOperationalAdmin` policy.
2. Return only booking ID, caregiver ID and safe display name, completion time,
   eligibility time, and existing caregiver fee amount/currency. No bank or
   IBAN fields.
3. Reuse the effective policy and payout eligibility rules from the current
   record command: completed and paid booking with completion timestamp,
   elapsed payout delay, verified payout account, and no `Paid` payout. A
   `Failed` payout remains eligible for a later retry. Missing effective policy
   fails closed using existing behavior.
4. Follow adjacent admin payout paging/filter conventions. No writes, provider
   calls, schema changes, migrations, fixture mutations, or resets.
5. Add focused tests for the changed behavior and authorization boundary.
   Update the admin guide and admin Finance Postman collection with the verified
   route and example. Do not run stateful Postman/Bruno requests.
6. Run the relevant focused automated checks and applicable build/static
   validation required by repository instructions. Report exact commands,
   outcomes, warnings, skips, and limitations; distinguish authored from run.

## Coordination

You are not alone in the repository. Preserve all unrelated edits and any
changes made by others. Do not commit, push, deploy, publish, reset databases,
or contact the owner. Do not scout FUR-07 or start another phase.

Complete the whole FUR-06 phase, save the delivery report at the path above,
then stop and return the report or a specific blocker through this OpenCode
job. Do not send follow-up messages or continue into review/release work.
