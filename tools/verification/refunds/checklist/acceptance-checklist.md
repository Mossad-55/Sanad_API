# Refund acceptance checklist

Audience: the **Mastermind**, before any refund feature is declared *accepted*.
This is an ordered gate. Work top to bottom; do not skip a step because a later
one looks green.

Authority: the [refund contract's numbered decision lock](../../../../docs/internal/work/portal-api-reconciliation/contracts/refund-policy-owner-contract-2026-10-22.md)
(Owner-approved #1–#37) and the [execution brief](../../../../docs/internal/work/portal-api-reconciliation/worker-prompts/team-reports-refund-policy-2026-10-22.md).

> **Acceptance is not release.** Passing this gate does not authorize commit,
> push, migration application, deployment or any external mutation. Those need a
> separate Owner order per [`docs/internal/operations/deploy-runbook.md`](../../../../docs/internal/operations/deploy-runbook.md).

## 0. Preconditions before the gate opens

- [ ] **0.1 Frozen tree.** Source and migration heads pinned; the revision is
      recorded like every other gate. Writes affecting compiled source are frozen.
- [ ] **0.2 Sole runtime slot.** The Mastermind owns the only build/test/runtime
      slot. No worker runs a gate concurrently.
- [ ] **0.3 Client-type assertion.** Run `tools/verification/refunds/scripts/gateway-double-contract-check.ps1 -Expect Double`
      (static inspection only). It must print `RESULT: PASS` and
      `resolved client : DevelopmentPaymobClient`. This is the highest
      verification risk (W0 D4) — refund runs never proceed on the real client.
- [ ] **0.4 Disposable target identity verified.** If any step below needs
      PostgreSQL, the target is a **verified disposable local** database:
      host `localhost`/`127.0.0.1`, name matching `^sanad_(refund|verify|disposable)[a-z0-9_]*$`.
      Never `SanadDb` on the shared host, never the production-configured remote.

## 1. Build — 0 / 0

- [ ] **1.1** Strict Release build of the solution: **0 warnings, 0 errors**
      (the repository builds with warnings-as-errors).
- [ ] **1.2** No new analyzer suppressions; no placeholder test
      (`Assert.True(true)` or equivalent) introduced.

## 2. Focused money-core tests (FQN filters)

Run each filter and record the passed/total. These prove the shared Finance
money core (F0) that every refund lane depends on.

| Filter | Class | Proves |
|---|---|---|
| `FullyQualifiedName~Sanad.UnitTests.Finance.MoneyTests` | `MoneyTests` | minor-unit arithmetic (decision #4), ToEven rounding, proration floor 0 (#31) |
| `FullyQualifiedName~Sanad.UnitTests.Finance.RefundLedgerTests` | `RefundLedgerTests` | the B7 state machine, transfer evidence (#16), unresolved-timeout block and reconciliation (#17) |
| `FullyQualifiedName~Sanad.UnitTests.Finance.CreditBalanceTests` | `CreditBalanceTests` | non-withdrawable credit, never-negative balance, zero/negative rejection (#15) |
| `FullyQualifiedName~Sanad.UnitTests.Finance.BusinessDayCalendarTests` | `BusinessDayCalendarTests` | Cairo Sunday–Thursday business days, DST-safe add (#1, #2) |
| `FullyQualifiedName~Sanad.UnitTests.Finance.FinanceMoneyCorePersistenceModelTests` | `FinanceMoneyCorePersistenceModelTests` | the Finance schema/model for the ledger, credit and their mappings |
| `FullyQualifiedName~Sanad.UnitTests.Finance.PayoutEligibilityTests` | `PayoutEligibilityTests` | payout eligibility unaffected by refund changes |
| `FullyQualifiedName~Sanad.UnitTests.Finance.CaregiverPayoutPolicyTests` | `CaregiverPayoutPolicyTests` | payout policy consistent with ledger/payout effects |

- [ ] **2.1** All filters green; counts recorded exactly (not "all passed").
- [ ] **2.2** Families refund-path filters green:
      `FullyQualifiedName~BookingCancellationPolicyTests`,
      `~BookingCancellationFeedbackAndFactTests`,
      `~BookingPaymentTests`, `~AdminRefundRetryGuardTests`,
      `~BookingRefundStateResolutionTests`,
      `~FamiliesInfrastructureDependencyInjectionTests` (the key-presence client gate).
- [ ] **2.3** The single known pre-existing failure
      `PortalRemainingResponseContractTests.NestedResponseRefs_ResolveToActualShapes`
      (OpenAPI nullable-positional-record gap, proven pre-existing at `4a066b3`)
      is accounted for, not silently accepted as caused by this work.

## 3. Declarative scenario coverage

- [ ] **3.1** Every scenario in `tools/verification/refunds/fixtures/refund-scenarios.json`
      maps to at least one executable check (unit or relational).
- [ ] **3.2** Every B7 state is covered: requested, investigating, approved,
      rejected, transfer-initiated, transfer-confirmed, failed, timed-out,
      credit-balance-posted.
- [ ] **3.3** M1 qualified-caregiver, family-access, M7 replay (sequential /
      conflicting / raced), rounding legacy-vs-new, proration floor-0 and
      timeout-sweep categories each have green checks.

## 4. Relational proof on the disposable PostgreSQL — REQUIRED

**Rule: in-memory tests alone never prove PostgreSQL refund behavior.** The
following items only count as proven when run against the verified disposable
local PostgreSQL (§0.4). An InMemory/SQLite result is recorded as *unproven*.

- [ ] **4.1 Refund idempotency.** M7 raced replay
      (`M7-REPLAY-03-raced-replay`): two concurrent same-key calls leave exactly
      one ledger entry — needs a real unique constraint and transaction.
- [ ] **4.2 Timeout sweep.** `finance.refund_ledger_entries` selection of the
      `TimedOut` latest-entry set returns the expected candidates on real SQL,
      and a reconciled case then completes without double-refunding
      (`TIMEOUT-SWEEP-02`, `-03`).
- [ ] **4.3 Credit balance persistence.** A posted credit and its
      `CreditBalancePosted` ledger entry are written in the same unit of work
      and survive a fresh read (`B7-STATE-09-credit-balance-posted`).
- [ ] **4.4 Concurrency / double-refund.** `M7-REPLAY-04` concurrent
      cancellation vs provider callback cannot double-refund on real
      transaction isolation.
- [ ] **4.5 city/governorate discovery EXISTS clause** (`M1-CAREGIVER-02`)
      translates to SQL on Npgsql (parked W1 V0 item) — not an in-memory no-op.
- [ ] **4.6 NetRecorded movement accounting** (`NET-RECORDED-01`): on real rows,
      the Requested/TransferInitiated entries are excluded from `NetRecorded`
      while Approved/Failed (signed) count — decision #35 (#4 formula: the
      completion check uses `TotalCompleted()`; `NetRecorded` is the risk
      ceiling).
- [ ] **4.7 Holiday-crossing deadline** (`DEADLINE-CLOCK-01`): the
      seven-business-day clock from an approved 2026-04-09 case lands on
      Cairo-local end-of-day 2026-04-22 after excluding Coptic Easter (Sun
      2026-04-12) and Sham El-Nessim (Mon 2026-04-13) — decisions #2, #36.

## 5. Rounding and proration boundary checks

- [ ] **5.1** Exactly 24h / immediately below 24h / exactly start; exactly 60 /
      greater than 60 minutes (`FAMILY-ACCESS-02`, `-04`).
- [ ] **5.2** Legacy care-home half amounts keep `AwayFromZero`
      (`CareHomeBooking.cs:273`); new-policy amounts use `ToEven`
      (`Money.FromMajorUnits`), and the two differ at an exact .5 midpoint
      (`ROUNDING-01`, `-02`).
- [ ] **5.3** Subscription shutdown proration returns 0 piastres where the
      unused duration is under one piastre, and is never rounded up; duplicate
      charges refund in full, distinct from shutdown proration
      (`PRORATION-FLOOR-01`, `-03`).
- [ ] **5.4** Cumulative refunds never exceed captured funds less previous
      refunds (`FAMILY-ACCESS-03`, `PRORATION-FLOOR-02`).
- [ ] **5.5** A previously settled refund is unchanged by activation
      (`ROUNDING-03`).

## 6. Owner-evidence closures

Every scenario marked `NEEDS-OWNER-EVIDENCE` must be either closed by the
Owner-supplied input or explicitly recorded as a limitation — never silently
counted as passed.

- [ ] **6.1 `D1-H1` `D1-H3` `D1-H4`** — real Paymob upstream 400 body,
      merchant/integration and subscription/plan evidence
      (`TIMEOUT-SWEEP-05`). Until supplied, timed-out reconciliation is proven
      against the deterministic double only, and that must be stated.
- [x] **6.2 `D1-H7`** — ~~whether an approved Egyptian public-holiday calendar
      exists for the business-day rule (#1).~~ **CLOSED mid-W2 by Owner decision
      #36**: the calendar is now implemented (`BusinessDayCalendar` — fixed
      Gregorian dates, Coptic computus Easter + Sham El-Nessim, UmAlQura Islamic
      feasts, `AnnouncedHolidays` override seam). The deadline clock is verified
      by scenario `DEADLINE-CLOCK-01` against the implemented calendar.
      Remaining Owner input: only the annual announced-holiday feed (moon-
      sighting shifts), which the override seam absorbs.
- [ ] **6.3 `D1-H2` `D1-H5` `D1-H6`** — key live-vs-test provenance, the
      502 attempt-row decision, and the shared host's config booleans. These are
      **never** read by these scripts; they remain Owner-reported-only and must
      be recorded as such.

## 7. Safety prohibitions re-checked

- [ ] **7.1** No real charge or refund occurred; only `DevelopmentPaymobClient`
      was bound (§0.3 / §4).
- [ ] **7.2** No reset, truncate, drop or destructive repair ran; seed data was
      INSERT-only and idempotent.
- [ ] **7.3** No shared-host TestClock change; no shared-host write without a
      separately approved exact fixture/write list (#29).
- [ ] **7.4** No migration applied and no schema change made during the gate.
- [ ] **7.5** No secret, token, connection string or payload was printed or
      recorded; receipts carry revision, target alias, counts and decisions only.

## 8. Acceptance statement

- [ ] **8.1** All applicable boxes above are checked, or each unchecked box has
      a recorded, specific reason and an explicit limitation.
- [ ] **8.2** The receipt names: revision, gate date, target alias (never a
      connection string), test counts, relational-proof items (§4) and the
      `NEEDS-OWNER-EVIDENCE` limitations (§6).
- [ ] **8.3** The Mastermind records acceptance in the brief's ledger. Release
      and deployment remain separately authorized.
