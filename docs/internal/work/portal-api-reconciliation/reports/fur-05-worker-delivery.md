# FUR-05 — Owner settlements worker delivery

**Phase:** FUR-05 — Owner settlements (S05)
**Status:** Complete — awaiting Mastermind review. Not committed, pushed, or deployed.
**Base revision:** `75eb7a412fdb177398f53d76eee20955ecbeb1c4`.
**Approved contract:** [`contracts/fur-05-owner-settlements.md`](../contracts/fur-05-owner-settlements.md)
**Worker prompt:** [`worker-prompts/fur-05-owner-settlements.md`](../worker-prompts/fur-05-owner-settlements.md)
**Date:** 2026-10-17.

## Summary

Implemented the exact owner-approved read-only route `GET /api/v1/care-homes/payouts/mine`
protected by `CareHomeOwnerAccess`. The facility is resolved server-side from the
authenticated owner; no facility ID parameter is accepted. The response reuses the
existing `IReadOnlyList<CareHomePayoutLedgerItem>` shape and the existing payout,
refund, fee, and debt truth. Admin ledger and payout-writing routes are unchanged.
No financial calculation, provider call, payout/debt mutation, migration,
configuration, or database target changed.

## Changed files

Owned manifest only:

| File | Change |
|---|---|
| `src/Modules/CareHomes/Application/Sanad.Modules.CareHomes.Application/Bookings/CareHomePayoutHandlers.cs` | Added `GetOwnerCareHomePayoutLedgerQuery` + handler; extracted the existing ledger body into a shared `CareHomePayoutLedgerProjection.Load` used by both the unchanged Admin handler (`settlementRowsOnly: false`) and the new owner handler (`settlementRowsOnly: true`). |
| `src/API/Sanad.API/Controllers/CareHomeFinanceController.cs` | Added `PayoutLedger` action: `[HttpGet("payouts/mine")]`, `200` produces `IReadOnlyList<CareHomePayoutLedgerItem>`, dispatches `GetOwnerCareHomePayoutLedgerQuery`. Class-level `CareHomeOwnerAccess` unchanged. |
| `tests/Sanad.UnitTests/CareHomes/CareHomePayoutTests.cs` | Added 7 focused handler tests (scope/isolation, payable + recorded rows, debt, ineligible rows, zero-payable exclusion, no facility + empty, fail-closed, no caller scope) plus two local fixtures. |
| `tests/Sanad.UnitTests/API/CareHomeBookingControllerTests.cs` | Extended the owner finance route-metadata test with the exact `payouts/mine` route and assertions that the action exposes only a `CancellationToken` (no `[FromQuery]`, no facility parameter). |
| `docs/care-homes/finance-reporting.md` | Added the owner settlement route to the Facility Owner table and a new "Owner settlement view" section (shape, inclusion/exclusion, ordering, 404/empty and fail-closed semantics, read-only boundary). |
| `docs/postman/care-homes/Sanad.CareHomes.Finance.postman_collection.json` | Added a "Payout ledger" Facility Owner request with a realistic saved `200` example matching the actual DTO. |
| `docs/internal/work/portal-api-reconciliation/reports/fur-05-worker-delivery.md` | This report. |

Pre-existing intake edits in `.codex/config.toml`,
`docs/internal/governance/AGENTS.md`, `docs/internal/operations/codex-workflow.md`,
`docs/internal/Mastermind_Handoff.md`, and
`docs/internal/work/portal-api-reconciliation/plan.md`, plus untracked
`tools/opencode_bridge/`, the contract and the worker prompt, were left untouched.

## Behavior implemented

- `GET /api/v1/care-homes/payouts/mine` resolves the acting owner's facility via
  `Facilities.SingleOrDefaultAsync(x => x.OwnerUserId == actor)` — the same owner
  finance read pattern used by the dashboard/revenue handlers.
- No facility → `CareHomes.Bookings.NotFound` (reuses `CareHomeRevenueProjection.NotFound`,
  the same safe `404` result the other owner finance reads return).
- Facility with no settlement rows → success with an empty array `[]` (`200`).
- Row membership:
  - Every already-recorded payout (`payoutId != null`) is included, including one
    with subsequent debt, which is reported as the summed `facilityDebt`.
  - An unpaid row (`payoutId == null`) is included only when the existing
    `CareHomePayoutCalculation.TryGetRemainingAmounts` eligibility checks pass
    (completed stay, paid/refunded payment, completed checkout, valid refund-state,
    positive remaining amount) **and** the calculated `netPayable` is positive.
  - Ineligible rows without a recorded payout (e.g. pending refunds) and
    zero-payable rows are excluded.
- Ordering is the existing `ActualCheckOutOnUtc` descending order.
- Missing effective Finance rule for an eligible unpaid balance fails closed with
  `CareHomes.Payout.FinanceConfigurationMissing`, preserving existing behavior
  (recorded-payout-only facilities do not require a Finance rule).
- The Admin `GET /api/v1/admin/care-homes/payouts/ledger` handler and both write
  routes are behaviorally unchanged; the shared projection keeps the Admin output
  identical (including all pre-existing rows, including zero-payable non-payout rows).

## Verification

All commands run from `D:\Sanad_API`. Only the two code/test files and docs above
were changed; the strict build and focused test gates below were produced in this
session on the final code, and only documentation/Postman files changed afterwards.

### Strict solution build (zero warnings/errors)

```
dotnet build Sanad.slnx --no-restore -m:1 --nologo /p:TreatWarningsAsErrors=true
```

- First run: **failed** with 2 errors `xUnit2029` (analyzer) in
  `CareHomeBookingControllerTests.cs` for `Assert.Empty` on a LINQ-filtered sequence.
  Fixed by using `Assert.DoesNotContain(..., predicate)`.
- Final run: **Build succeeded**, `0 Warning(s)`, `0 Error(s)` (Time Elapsed ~40s).

### Focused tests

```
dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj --no-build --no-restore -m:1 --nologo /p:TreatWarningsAsErrors=true --filter "FullyQualifiedName~Sanad.UnitTests.CareHomes.CareHomePayoutTests|FullyQualifiedName~Sanad.UnitTests.API.CareHomeBookingControllerTests|FullyQualifiedName~Sanad.UnitTests.API.CareHomeOwnerPortalTests"
```

- Result: **Passed — 56, Failed 0, Skipped 0**.
- Class-level confirmation:
  - `Sanad.UnitTests.CareHomes.CareHomePayoutTests` → **12 passed, 0 failed, 0 skipped**
    (5 pre-existing + 7 new).
  - `Sanad.UnitTests.API.CareHomeBookingControllerTests` → **25 passed, 0 failed, 0 skipped**.
  - The combined run additionally includes the 19 existing
    `Sanad.UnitTests.API.CareHomeOwnerPortalTests` owner-portal HTTP regressions (56 − 12 − 25).

New tests cover: owner-to-facility scoping and foreign-facility exclusion; an
eligible unpaid row and an already-recorded transfer row in one response (with
amounts, null/non-null `payoutId`, reference and timestamp); debt visibility after
a recorded payout without a Finance rule; exclusion of ineligible (pending-refund)
and pending-stay rows; exclusion of a zero-payable non-payout row; safe no-facility
`404`; empty-facility `200 []`; fail-closed missing Finance rule; and a read-only
assertion (`ChangeTracker.HasChanges()` is false and no payout/debt row is created).

### Postman JSON validation

Validated the modified collection and its embedded example body with PowerShell
`ConvertFrom-Json`:

- Collection parsed; Facility Owner folder now has 7 items (including "Payout ledger").
- Request: `GET {{baseUrl}}/api/v1/care-homes/payouts/mine`.
- Embedded example body parsed to 2 rows; row 0 is a recorded transfer
  (`payoutId` non-null, `feeRatePercentage` 5.00, `feeAmount` 50.00, `netPayable` 950.00),
  row 1 is an eligible unpaid balance (`payoutId`, `transferReference`, `recordedOnUtc` null).

### Diff whitespace

```
git diff --check
```

- Exit 0 (clean). Git printed only informational `LF will be replaced by CRLF`
  autocrlf notices for the edited files, not whitespace errors.

## Fixtures and targets

- Tests use isolated in-memory `CareHomesDbContext` instances
  (`UseInMemoryDatabase(Guid.NewGuid()...)`), one fresh store per test, plus the
  existing `FixedChargeRules` test double. No database was created, reset,
  migrated, seeded, or connected; no PostgreSQL/integration test ran.
- No API was started; no Bruno collection was run; no provider (Paymob/SMTP/push)
  call was made; no deployment or environment change occurred.

## Skipped / not run

- The full solution test suite was **not** run. The change is a bounded read-only
  handler plus one route; the prompt scopes verification to the focused tests, and
  no direct cross-cutting risk required the full suite.
- The Postman↔controller contract-mapping script and the OpenAPI document were not
  run here (Mastermind verification). The new route is represented in the Care Homes
  Finance collection so the route set stays mapped.
- `CareHomes.Payout.FinanceConfigurationMissing` has no explicit entry in
  `ResultProblemDetailsMapper`, so it maps to the default `400` — inherited existing
  behavior shared with the Admin ledger; not changed in this phase and no status was
  asserted for it.

## Boundaries confirmed

No commit, push, merge, deployment, database/migration action, provider call, file
storage access, API launch, or Bruno run occurred. No file outside the owned manifest
was edited. The pre-existing worktree edits were preserved. This phase stops here
pending Mastermind review and any bounded corrections.
