# Phase 6 report — Caregiver payout reads

- **Phase:** 6. Caregiver payout reads
- **Branch:** `opencode/opus-work`
- **Base SHA (phase start):** `8adccf3bc156206ccef592518285c15578e27625`
- **Final SHA (content commit):** `1f9e0aa337ab7972b5b6f1487a8a052a0bdbb821`
- **Effective model:** `opencode/mimo-v2.6-flash-free` (Windows, dotnet 10,
  dotnet-ef 10.0.12)

## Contract delivered (plan.md §4)

Four self-only reads under `CaregiverAccess` (caregiver resolved from the
JWT identity — no `caregiverId` parameter, no cross-caregiver read path):

- `GET /api/v1/caregiver/payouts/summary` → `{currency, unpaidAmount,
  transferredAmount, failedAmount, nextExpectedPayoutOnUtc}`.
  `unpaidAmount` = eligible-unrecorded (Completed + Paid, past the policy
  delay, no `Paid` row; a `Failed` row does **not** clear the debt).
  `nextExpectedPayoutOnUtc` = earliest eligibility time among unpaid
  bookings, only with a `Verified` payout account, else null. No effective
  policy → fail closed `400 Finance.PayoutPolicy.Missing` (see owner notes).
- `GET /api/v1/caregiver/payouts?status=&page=&pageSize=` → paged items
  `{id, amount (= gross), currency, status, bankNameArabic,
  bankNameEnglish, maskedIban, transferReference, createdOnUtc,
  transferredOnUtc (= paidOnUtc), failureReason}`; `status` accepts
  `Paid|Failed` (enum binding, other values → 400); page ≥ 1, pageSize
  clamped 1–100; bank names resolved from the snapshotted `bankCode`
  against the bank catalogue (null when unknown).
- `GET /api/v1/caregiver/payouts/{id}` → same fields + `bookingItems` as a
  one-element array `[{bookingId, caregiverAmount, currency}]`; another
  caregiver's id → `404 Caregivers.Payouts.NotFound` (no existence leak).
- `GET /api/v1/caregiver/payment-policy` → admin-authored
  `descriptionArabic/descriptionEnglish` of the effective policy (null
  before descriptions existed or when no policy exists; still `200`),
  `requiresVerifiedBankAccount: true`, `minimumPayoutAmount: null`,
  `expectedProcessingDays: null` (honest nulls).

`Transferred ≡ Paid` is documented (alias of the merged T4 label), never
renamed; no `Processing` bucket exists on any surface; `platformFeeAmount`
stays informational and `netAmount == gross` is structurally enforced by
the domain (asserted in tests).

## Changed files

- `src/Modules/Finance/Domain/.../CaregiverPayoutPolicy.cs` — optional
  `DescriptionArabic`/`DescriptionEnglish` (trimmed, ≤ 4000 chars, blank →
  null, oversize → DomainException), `Create(...)` gains optional params
  (all existing call sites compile unchanged).
- `src/Modules/Finance/Application/.../CaregiverPayoutPolicyCommands.cs` —
  `CreateCaregiverPayoutPolicyCommand`, `CaregiverPayoutPolicyRates`,
  `CaregiverPayoutPolicyHistoryItem`, `CaregiverPayoutPolicyResponse` gain
  optional description fields; `ICaregiverPayoutPolicyWriter.CreateAsync`
  gains optional description params; handler/history mapping pass-through.
- `src/Modules/Finance/Infrastructure/.../CaregiverPayoutPolicyStore.cs` —
  descriptions written on create, read back by effective/history queries.
- `src/Modules/Finance/Infrastructure/.../FinanceDbContext.cs` — nullable
  `description_arabic`/`description_english` columns.
- `src/Modules/Finance/Infrastructure/.../Persistence/Migrations/
  20261010143327_AddCaregiverPayoutPolicyDescriptions.cs` (+ Designer,
  snapshot regenerated via `dotnet ef migrations add`) — **additive only,
  authored only, never applied**.
- `src/API/Sanad.API/Controllers/AdminCaregiverPayoutPolicyController.cs` —
  create request gains optional `descriptionArabic`/`descriptionEnglish`
  (additive; existing bodies still bind; no authorization/behavior change).
- `src/Modules/Caregivers/Application/.../Payouts/
  CaregiverPayoutReadQueries.cs` (new) — response records + four queries/
  handlers (summary, list, detail, payment-policy).
- `src/API/Sanad.API/Controllers/CaregiverPayoutsController.cs` (new) —
  `api/v1/caregiver` route, four GETs, `CaregiverAccess`, caregiver
  resolved from identity (401 without profile), mirroring the
  payout-account controller pattern.
- `tests/.../Caregivers/CaregiverPayoutReadTests.cs` (new) — 10 tests:
  summary sums (Paid/Failed/eligible-unpaid, Failed-doesn't-clear-debt),
  no-verified-account null, no-policy fail-closed, not-yet-eligible
  exclusion, honest empty state, cross-caregiver isolation (summary+list),
  list filters/status mapping/bank-name resolution/page-size clamp,
  detail `bookingItems` + `net == gross`, detail ownership 404, payment
  policy descriptions + honest nulls (with and without a policy).
- `tests/.../Finance/CaregiverPayoutPolicyTests.cs` — 4 tests: persist+trim
  descriptions, blank → null, oversize rejected, store round-trip through
  effective/history/current reads.
- `tests/.../API/NewEndpointsAuthorizationContractTests.cs` — new fact
  asserting `CaregiverAccess`, controller route, and all four action
  routes on `CaregiverPayoutsController`.
- `tests/.../Finance/PlatformChargeRuleTests.cs` — **pre-existing
  date-dependent test repaired** (see below).
- `docs/app/caregivers/payouts.md` — new "Caregiver-facing payout reads"
  section (statuses/aliasing, summary semantics, list/detail shapes,
  payment policy, error table).
- `docs/postman/app/Sanad.App.Caregiver.Operations.postman_collection.json`
  — new `06. Payout reads` folder (4 GETs) + `payoutId` variable.
- `docs/postman/admins/Sanad.Admin.Finance.postman_collection.json` —
  policy-create body/description gain the bilingual descriptions (read-back
  notes on current/history reads).

## Pre-existing failure found and fixed (test-only)

`PlatformChargeRuleTests.Writer_requires_strictly_increasing_versions_and_
preserves_future_schedule` failed on first run — **not caused by this
phase** (PlatformChargeRule code untouched). Root cause: the writer derives
`isActive` from `effectiveOnUtc <= DateTime.UtcNow`
(`PlatformChargeRuleReader.cs:30`) while the test scheduled rule v2 at a
hardcoded `Utc(10)` = **2026-10-10T00:00Z**. The test passed while that
date was in the future and silently started failing the moment it became
the past (today). Fixed by scheduling at 2030-01-01 (time-independent) with
a regression comment; test intent unchanged.

## Verification (all on final content commit)

| Command | Exit | Result |
| --- | --- | --- |
| `dotnet build Sanad.slnx -warnaserror` | 0 | 0 warnings, 0 errors |
| `dotnet test ... --filter "FullyQualifiedName~Sanad.UnitTests.Caregivers" --no-build` | 0 | Passed 761 / Failed 0 / Skipped 0 |
| `dotnet test ... --filter "FullyQualifiedName~Sanad.UnitTests.Finance" --no-build` | 0 | Passed 50 / Failed 0 / Skipped 0 |
| `dotnet test ... --filter "FullyQualifiedName~Sanad.UnitTests.API" --no-build` | 0 | Passed 266 / Failed 0 / Skipped 0 (includes the new authorization-contract fact) |
| `ConvertFrom-Json` on `Sanad.App.Caregiver.Operations.postman_collection.json` and `Sanad.Admin.Finance.postman_collection.json` | 0 | valid JSON |
| `git diff --cached --check` | 0 | no whitespace errors |

(Quirk: the first build after some edits reported phantom errors/1 error;
a clean rerun of the identical command reported 0/0 — both observed runs
are reflected above only after confirmation.)

## Skipped / blocked checks

- Full unit + architecture suites: deferred to Phase 7 per tasks.md.
- Migration apply / provider calls / stateful HTTP: forbidden this package;
  migration generated by `dotnet ef` with a dummy design-time connection
  string and never applied.

## Dependencies confirmed in main

- `CaregiverAccess` policy, `ICaregiversDbContext` (`Payouts`, `PayoutAccounts`,
  `Banks`), `IFamiliesDbContext.Bookings` + price snapshots,
  `ICaregiverPayoutPolicyReader/Writer` DI (Finance.Infrastructure),
  `PayoutEligibility`, `IDateTimeProvider`, admin payout record flow
  (statuses `Paid/Failed` only), bank lookup catalogue — all present at
  base. No new project references were added in this phase.

## Remaining owner decisions / notes

1. **Shared surface: untouched this phase.** No `ResultProblemDetailsMapper`
   lines were added; `Finance.PayoutPolicy.Missing` stays unmapped and
   therefore returns the framework default `400` — exactly what admin
   payout recording already returns for that code today. If the owner
   prefers `409`/`503` for a missing policy on the summary read, that is a
   one-line mapper change (flagged, not made).
2. **Policy create extended** (optional bilingual descriptions) is the one
   contract-mandated touch inside the "no policy-admin changes" note of
   tasks.md: plan.md §4 requires descriptions "added to policy create = new
   version". It is additive/optional (old bodies still bind), no endpoint,
   authorization, or lifecycle behavior changed.
3. `docs/admin/finance-platform-charges.md` still documents the old
   3-field policy-create body; it is a doc-only file owned by Phase 7's
   doc sync and will be updated there.
