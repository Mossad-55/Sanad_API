# FUR-06 — Eligible caregiver payout selector worker delivery

**Phase:** FUR-06 — Eligible caregiver payout selector
**Status:** Complete — awaiting Mastermind review. Not committed, pushed, or deployed.
**Base revision:** `af78095a38d8ce0e490e842e441037374d3f2c9c`.
**Approved contract:** [`contracts/fur-06-eligible-caregiver-payouts.md`](../contracts/fur-06-eligible-caregiver-payouts.md)
**Worker prompt:** [`worker-prompts/fur-06-eligible-caregiver-payouts.md`](../worker-prompts/fur-06-eligible-caregiver-payouts.md)
**Date:** 2026-10-17.

## Summary

Added the read-only eligible-booking selector
`GET /api/v1/admin/caregiver-payouts/eligible-bookings` under the existing
`PayoutOperationalAdmin` policy (SuperAdmin or FinanceAdmin). It returns the
completed-and-paid bookings an operator may record a manual payout for right now,
reusing the record command's eligibility rules and the effective Finance policy.
Nothing is written; no provider is called; no schema, migration, configuration,
or database target changed.

> **Concurrent-writer collision (disclosed).** During this phase the working tree
> briefly contained **two** conflicting FUR-06 implementations written by two
> producers into the same owned files (an `eligible-bookings` / `ArabicFullName` /
> oldest-eligible-first variant and an `eligible` / `CaregiverArabicFullName` /
> newest-completion-first variant), which left the strict build failing with
> `CS0101`/`CS0111`/`CS8863`. I kept the `eligible-bookings` variant because it is
> the one already described by the admin guide, the Postman request/example, and
> the authorization route test in this manifest, and removed the duplicate
> `eligible` variant (plus its duplicate test suite and duplicate test doubles).
> The discarded variant's distinct coverage (a not-completed booking is excluded)
> was folded back into the kept exclusion test. See "Discarded variant" below.

## Changed files

Owned manifest only:

| File | Change |
|---|---|
| `src/API/Sanad.API/Controllers/AdminCaregiverPayoutsController.cs` | Added `GetEligibleBookings` action: `[HttpGet("eligible-bookings")]`, `200` produces `PagedResult<EligibleCaregiverPayoutBookingResponse>`, dispatches `GetEligibleCaregiverPayoutBookingsQuery(caregiverId, page, pageSize)`. Class-level `PayoutOperationalAdmin` policy unchanged. |
| `src/Modules/Caregivers/Application/Sanad.Modules.Caregivers.Application/Payouts/CaregiverPayoutReadQueries.cs` | Added `EligibleCaregiverPayoutBookingResponse`, `GetEligibleCaregiverPayoutBookingsQuery`, and `GetEligibleCaregiverPayoutBookingsQueryHandler`. |
| `tests/Sanad.UnitTests/Caregivers/CaregiverPayoutHandlerTests.cs` | Added 6 focused handler tests plus a `FixedClock` and a `FixedHeaderCaregiversDbContext` double. |
| `tests/Sanad.UnitTests/API/AdminCaregiverPayoutsAuthorizationTests.cs` | Extended the route-metadata test with the exact `eligible-bookings` template. |
| `docs/app/admin/caregiver-payouts.md` | New admin caregiver-payout guide (routes, selector shape, eligibility, errors, record/mark-failed). |
| `docs/postman/admins/Sanad.Admin.Finance.postman_collection.json` | Added a "List eligible caregiver payout bookings" request with a saved `200` example; expanded the folder description. |
| `docs/internal/work/portal-api-reconciliation/reports/fur-06-worker-delivery.md` | This report. |

Pre-existing unrelated worktree edits in `.codex/config.toml`,
`docs/internal/governance/AGENTS.md`, `docs/internal/operations/codex-workflow.md`,
`docs/internal/Mastermind_Handoff.md`, and
`docs/internal/work/portal-api-reconciliation/plan.md`, plus untracked
`tools/opencode_bridge/`, the contract, and the worker prompt, were left untouched.

## Implementation summary

`GET /api/v1/admin/caregiver-payouts/eligible-bookings`

- Query parameters: `caregiverId` (optional GUID filter), `page` (default `1`,
  clamped to a minimum of `1`), `pageSize` (default `20`, clamped `1`–`100`).
- Response: the standard `PagedResult<T>` envelope. Item shape
  `EligibleCaregiverPayoutBookingResponse(BookingId, CaregiverId, ArabicFullName,
  EnglishFullName, CompletedOnUtc, EligibleOnUtc, CaregiverAmount, Currency)` —
  **no payout-account, bank, or IBAN field.**
- `CaregiverAmount`/`Currency` come from the booking price snapshot
  (`BaseCaregiverFee` + snapshot currency); `PlatformFeeAmount` is never deducted.
- `EligibleOnUtc` = `max(CompletedOnUtc, PaidOnUtc)` plus the effective payout
  delay, via the existing `PayoutEligibility.Evaluate`.
- Ordering: oldest `EligibleOnUtc` first, then booking id for a stable page order
  (matches the existing admin review-queue precedent, and the admin guide).
- Caregiver display names are resolved through the existing
  `ICaregiversDbContext.GetCaregiverUserHeaderAsync` identity header, cached per
  caregiver per page; empty strings when the identity name cannot be resolved.

Eligibility (mirrors `RecordCaregiverPayoutCommandHandler`): a booking is included
only when it is `Completed` with both `CompletedOnUtc` and `PaidOnUtc` set, the
effective delay has elapsed, the caregiver has a `Verified` payout account, and no
`Paid` payout row exists for the booking. A `Failed` payout row does not exclude
the booking (it stays available for retry). A missing effective policy **fails
closed** with `CaregiverPayoutPolicyErrors.Missing`, which the shared problem-details
mapper maps to HTTP `400` (`Finance.PayoutPolicy.Missing`).

## Verification

All commands run from `D:\Sanad_API`. Only the manifest files above were changed.

### Strict solution build (zero warnings / zero errors)

```
dotnet build Sanad.slnx --no-restore -m:1 --nologo /p:TreatWarningsAsErrors=true
```

- Result: **Build succeeded — 0 Warning(s), 0 Error(s)** (Time Elapsed ~16–18s).

### Focused tests

```
dotnet test tests\Sanad.UnitTests\Sanad.UnitTests.csproj --no-build --nologo ^
  --filter "FullyQualifiedName~CaregiverPayoutHandlerTests|FullyQualifiedName~AdminCaregiverPayoutsAuthorizationTests"
```

- Result: **Passed — 21, Failed 0, Skipped 0**. That is 19 tests in
  `CaregiverPayoutHandlerTests` (13 pre-existing + 6 new) plus 2 in
  `AdminCaregiverPayoutsAuthorizationTests`.

```
dotnet test tests\Sanad.UnitTests\Sanad.UnitTests.csproj --no-build --nologo --filter "FullyQualifiedName~Sanad.UnitTests.Caregivers"
```

- Result: **Passed — 840, Failed 0, Skipped 0**.

```
dotnet test tests\Sanad.ArchitectureTests\Sanad.ArchitectureTests.csproj --no-build --nologo
```

- Result: **Passed — 1, Failed 0, Skipped 0**.

New coverage: inclusion (identifiers, both display names, amount/currency,
timestamps, and a read-only assertion that neither DbContext tracks a change);
exclusions (pending payout account, already-`Paid` payout, delay not yet elapsed,
no payout account, and a paid-but-never-completed booking); failed-payout retry
eligibility; missing-policy fail-closed (`CaregiverPayoutPolicyErrors.Missing`);
paging/ordering/caregiver-filter/page + pageSize clamping; a reflection guard that
the response exposes no `Iban`/`Bank`-named property; and the controller route
template boundary.

### Postman JSON validation

Validated the modified collection with PowerShell `ConvertFrom-Json`:

- Parses OK; the "Caregiver payouts (SuperAdmin or FinanceAdmin)" folder now has
  5 items (including "List eligible caregiver payout bookings").
- Request URL: `{{baseUrl}}/api/v1/admin/caregiver-payouts/eligible-bookings?page=1&pageSize=20`.
- The embedded example body parses with keys `bookingId`, `caregiverId`,
  `arabicFullName`, `englishFullName`, `completedOnUtc`, `eligibleOnUtc`,
  `caregiverAmount`, `currency`; the Arabic display name is intact in the saved body.

### Route-mapping script

```
pwsh -NoProfile -File docs\internal\tools\Verify-ApiContractMapping.ps1
```

- Output: 558 controller route-method signatures, 622 Postman API requests,
  exit 0. (Down from 559 signatures before the duplicate `eligible` action was
  removed.) Independent spot-check confirms the new route is present in both the
  controller and the Postman collection.

### Whitespace

```
git diff --check
```

- Exit 0. Git printed only informational `LF will be replaced by CRLF` autocrlf
  notices, not whitespace errors.

## Authored vs executed

- Authored and executed this session: the 6 new handler tests, the extended route
  test, the strict build, the focused/Caregivers/architecture test runs, the
  Postman JSON parse, the route-mapping script, and `git diff --check`.
- The Postman request and saved `200` example are an **authored** representative
  example, not runtime response evidence; no stateful Postman/Bruno request was run.

## Assumptions

- The concrete route segment `eligible-bookings` and the property names
  `ArabicFullName`/`EnglishFullName` satisfy the contract, which fixes the base
  route and required fields but not the exact segment or DTO member names.
- Oldest-eligible-first ordering is acceptable (contract specifies paging/filter
  conventions but not an explicit sort); the admin guide documents it.
- The caregiver safe display name is the existing identity-profile name exposed by
  `GetCaregiverUserHeaderAsync`, consistent with other admin caregiver reads.
- A `Failed` payout row leaves the booking eligible, matching the record command.

## Discarded variant (duplicate-writer collision)

Because the working tree held a second, conflicting copy of this feature, the
following were removed to restore a single coherent implementation that matches
the already-authored guide, Postman request, and authorization test:

- The `[HttpGet("eligible")]` action in `AdminCaregiverPayoutsController.cs`.
- The `EligibleCaregiverPayoutBookingResponse` / `GetEligibleCaregiverPayoutBookingsQuery`
  / handler block using `CaregiverArabicFullName`/`CaregiverEnglishFullName` and
  newest-completion-first ordering in `CaregiverPayoutReadQueries.cs`.
- The `EligibleBookings_*` test suite and the duplicated `FixedClock` /
  `FixedHeaderCaregiversDbContext` test doubles in `CaregiverPayoutHandlerTests.cs`.
- A duplicate `using Sanad.BuildingBlocks.Application.Abstractions;` that the
  second writer had added to the test file.

Both variants were functionally equivalent for eligibility; the kept variant was
chosen for documentation/test/Postman consistency and the oldest-eligible-first
ordering precedent. The discarded variant's unique "not completed is excluded"
case was folded into `EligibleSelector_ShouldExcludeIneligibleBookings`. No
concurrent writer touched the owned files after 16:58:43 local time (verified by
mtime before and after the final build/test runs).

## Fixtures, targets, and state

- Tests use isolated in-memory `CaregiversDbContext`/`FamiliesDbContext`
  instances with local `FixedClock`, `FixedPolicyReader`, and
  `FixedHeaderCaregiversDbContext` doubles (the last supplies the identity name
  header because the in-memory provider cannot run the production raw-SQL join).
- No database was created, reset, migrated, seeded, or connected; no
  PostgreSQL/integration test ran; no API was started; no Bruno request ran; no
  provider (Paymob/SMTP/push) call was made; no deployment or environment change
  occurred.

## Blockers

None outstanding. The only blocker encountered was the concurrent duplicate-writer
collision above, which was resolved by deduplicating to a single implementation.

## Unfulfilled acceptance checks / limitations

- `docs/app/admin/caregiver-payouts.md` is new but is **not linked** from an index,
  because `docs/admin/admin-overview.md` and `docs/app/caregivers/payouts.md` are
  outside the FUR-06 manifest and were not edited (linking is a review/release step).
- `docs/internal/tools/Verify-ApiContractMapping.ps1` prints counts and exits 0,
  but its controller-vs-Postman missing/orphan comparison is effectively a no-op
  (the comparison variables are never populated), so it is not independent proof of
  route coverage; the route was confirmed by direct inspection instead.
- The full solution test suite was not run; the contract scopes verification to the
  focused tests, and the change is a bounded read-only query plus one route.

## Boundaries confirmed

No commit, push, merge, deployment, database/migration action, provider call, API
launch, or Bruno run occurred. No file outside the owned manifest was edited. The
pre-existing worktree edits were preserved. This phase stops here pending Mastermind
review.
