# FUR-05 — Owner settlements implementation

You are implementing the complete owner-approved FUR-05 phase in `D:\Sanad_API`.
Read and follow `AGENTS.md`, `docs/internal/governance/AGENTS.md`, the current
Mastermind handoff, the FUR sequence in `plan.md`, this approved contract, and
`docs/internal/operations/codex-workflow.md` before editing.

## Pinned context

- Base revision: `75eb7a412fdb177398f53d76eee20955ecbeb1c4`.
- Approved contract: `docs/internal/work/portal-api-reconciliation/contracts/fur-05-owner-settlements.md`.
- The worktree had pre-existing changes at intake in `.codex/config.toml`,
  `docs/internal/governance/AGENTS.md`, and
  `docs/internal/operations/codex-workflow.md`, plus untracked
  `tools/opencode_bridge/`. These are not yours; preserve them.
- You are not alone in the codebase. Preserve others' edits, do not revert or
  reformat unrelated files, and stay inside the owned-file manifest below.

## Objective

Implement the exact approved read-only route and synchronization described in
the contract. The Owner view includes both eligible unpaid balances and
recorded transfers, scopes solely to the authenticated owner's facility, and
reuses existing payout/refund/fee/debt truth. Do not broaden behavior or
financial policy.

## Owned files

- `src/Modules/CareHomes/Application/Sanad.Modules.CareHomes.Application/Bookings/CareHomePayoutHandlers.cs`
- `src/API/Sanad.API/Controllers/CareHomeFinanceController.cs`
- `tests/Sanad.UnitTests/CareHomes/CareHomePayoutTests.cs`
- `tests/Sanad.UnitTests/API/CareHomeBookingControllerTests.cs`
- `docs/care-homes/finance-reporting.md`
- `docs/postman/care-homes/Sanad.CareHomes.Finance.postman_collection.json`
- `docs/internal/work/portal-api-reconciliation/reports/fur-05-worker-delivery.md`

If a necessary additional file is outside this list, stop and report the exact
path and reason before editing it. Do not edit the contract, plan, central
handoff, unrelated docs or code.

## Required behavior

- Add `GET /api/v1/care-homes/payouts/mine` protected by
  `CareHomeOwnerAccess`, with the authenticated owner resolved server-side and
  no facility ID query parameter.
- Return the existing `CareHomePayoutLedgerItem` list shape and follow the
  contract's precise row inclusion, status-by-nullable-payout-ID, missing
  facility, empty list, ordering and missing-Finance-rule semantics.
- Keep Admin ledger and write routes unchanged. Do not change financial
  calculations, issue provider calls, or mutate payout/debt data.
- Add focused tests for scope/isolation, payable and recorded rows, debt,
  ineligible states, no facility, empty results, authorization/route metadata,
  and read-only behavior. Use isolated in-memory/local fixtures only; do not
  create, reset, migrate, seed, or connect to a database target.
- Update only the specified Care Homes finance guide and Care Homes Finance Postman
  collection. Include a realistic response example consistent with the actual
  existing DTO; validate the JSON.

## Verification and output

- Run a strict solution build with zero warnings/errors and the focused tests
  needed for this phase. Do not run a full suite unless a direct risk requires
  it; explain if so. Do not run Bruno or launch the API.
- Run `git diff --check` and validate the modified Postman JSON.
- Save `docs/internal/work/portal-api-reconciliation/reports/fur-05-worker-delivery.md`
  with changed files, exact commands and results, tests/counts, any skipped or
  blocked check, fixture/target details, and confirmation that no migration,
  database, provider, deployment, commit or push occurred.
- Stop after the complete phase report. Do not commit, push, deploy, change a
  database, start FUR-06, or perform further phases. The owner returns your
  report to the Mastermind for review and bounded corrections.
