# Refunds verification pack

Status: **AUTHORED, NOT RUN.** Defined by lane **W2-V0** (verification authoring)
for stage **S7** of the [refund policy execution brief](../../../docs/internal/work/portal-api-reconciliation/worker-prompts/team-reports-refund-policy-2026-10-22.md).
Every asset here is a **definition**. No script in this folder has been executed
against any database, gateway or host, and none may be executed until the
Mastermind grants the sole runtime slot for the exact run.

Authority for behavior: the [refund contract's numbered decision lock](../../../docs/internal/work/portal-api-reconciliation/contracts/refund-policy-owner-contract-2026-10-22.md)
(decisions **#1-#37**, all Owner-approved). Where a scenario depends on an
Owner-only input, it is marked `NEEDS-OWNER-EVIDENCE` with the specific item
number from the W0 D1 hypothesis list (see "Owner-evidence markers" below).

## 1. What this pack verifies

1. **Refund lifecycle states** — every B7 state defined by the money core:
   `requested`, `investigating`, `approved`, `rejected`, `transfer-initiated`,
   `transfer-confirmed`, `failed`, `timed-out`, `credit-balance-posted`.
   The append-only `RefundLedger` (`src/Modules/Finance/Domain/Ahlena.Modules.Finance.Domain/RefundLedger.cs`)
   is the source of truth for the state machine; `investigating` is the
   SupportAdmin review phase that precedes SuperAdmin approval (decisions #11,
   #12) and is verified as an observable phase, not a stored enum member.
2. **Timeout reconciliation** (decision #17) — an unknown provider outcome
   already recorded as `TimedOut` must be reconciled **before** retry or
   confirmation; `RefundLedger.HasUnresolvedTimeout` blocks both. The dry-run
   sweep selects those rows and prints the decision it *would* take.
3. **Credit balance** (decisions #10, #15) — app credit is a real, recorded,
   non-withdrawable balance; a posted credit is only valid when its
   `CreditBalancePosted` ledger entry is written in the same unit of work.
4. **Money correctness** — decision #4 (minor units, piastres), decision #5
   (discounted package allocation summing exactly to the paid total), decision
   #6 (subscription shutdown proration), decision **#31** (proration floor 0,
   never round up), and the **rounding contradiction** from W0 D2: historic
   care-home bookings use `MidpointRounding.AwayFromZero`
   (`CareHomeBooking.cs:273`) while the new money core uses
   `MidpointRounding.ToEven` (`Money.cs`). New-policy amounts must be `ToEven`;
   legacy rows must keep their historic value.
5. **Idempotency and replay** — M7 sequential replay, conflicting replay and
   raced replay must not duplicate a refund, a credit or a cancellation fact
   (decision #17 and the "repeated/concurrent cancellation" check).
6. **Fixture eligibility** — M1 qualified caregiver discovery and family access
   under the approved fixtures, so refund scenarios run against accounts that
   satisfy the discovery predicate instead of an empty 200.

## 2. What this pack NEVER does

- **No real charges or refunds.** No live Paymob call, no card charge, no real
  transfer. Every provider interaction is a deterministic **gateway double**.
- **No resets.** No database drop, truncate, wipe or destructive repair. Seed
  data is INSERT-only and idempotent, matching the L1 library convention.
- **No destructive repair.** A stuck or inconsistent row is *reported*, never
  edited or deleted by these scripts.
- **No shared-host TestClock changes.** The clock-control seam (if used at all)
  is local-only; the shared production-configured host is never driven.
- **No shared-host writes** without a separately Owner-approved exact fixture /
  write list (decision #29).
- **No migrations** and no schema change. These assets read and INSERT only.
- **No secrets.** Connection strings, tokens, provider keys and payloads are
  never printed; failures report status, safe business code, field names and a
  redacted log line only.

## 3. Prerequisites

1. **Verified disposable local PostgreSQL.** A local PostgreSQL instance
   (`localhost` / `127.0.0.1` only) holding a database whose name matches the
   disposable pattern `^ahlena_(refund|verify|disposable)[a-z0-9_]*$`. The
   Mastermind must have **verified** the target identity (effective database
   name + host) before any run.
2. **Deterministic gateway doubles.** The graph must resolve
   `DevelopmentPaymobClient`, whose `RefundPaymentAsync` returns a fake
   `dev-refund-{guid}` reference and never contacts Paymob. See §4.
3. **Frozen revision.** Source and migration heads pinned; the run records the
   revision like every other gate.
4. **Sole runtime slot.** The Mastermind owns the only build/test/runtime slot;
   a dry-run sweep run is a runtime action and requires explicit authorization.

## 4. Mandatory client-type assertion (highest verification risk)

W0 D4 established that `DevelopmentPaymobClient` selection is
**key-presence-gated, not environment-gated**. `AddFamiliesInfrastructure`
(`src/Modules/Families/Infrastructure/Ahlena.Modules.Families.Infrastructure/DependencyInjection.cs:53-62`)
registers the client from the **presence of `Paymob:SecretKey` alone**:

```
paymobSecretKey = configuration["Paymob:SecretKey"]
if (IsNullOrWhiteSpace(paymobSecretKey)) -> DevelopmentPaymobClient
else                                       -> PaymobClient
```

The `DOTNET_ENVIRONMENT` value is **not** consulted. A Development host with a
real `Paymob:SecretKey` value present would therefore bind the **real**
`PaymobClient`. That is the highest verification risk in this pack.

**Rule enforced by every script in this folder:** before any read, write, sweep
or HTTP call, a script must

1. resolve which `IPaymobClient` implementation the effective configuration
   would produce (key absent → `DevelopmentPaymobClient`; key present →
   `PaymobClient`), and
2. **abort** unless the resolved type is the expected deterministic double
   (`DevelopmentPaymobClient`).

`scripts/gateway-double-contract-check.ps1` performs this as **static
inspection of config + DI source only** — it never executes the payment path.
The dry-run sweep script refuses to run at all if the key is present in the
environment it would inherit.

Fixtures for the double are the W0 D4 seam; they are deliberately not
duplicated here. `DevelopmentPaymobClient.RefundPaymentAsync` returns
`dev-refund-{guid}` (never a real provider reference), which is what the
"transfer-confirmed" and "failed" scenarios pin.

## 5. Layout

| Path | Purpose |
|---|---|
| `README.md` | This file. Scope, prohibitions, prerequisites, client-type rule. |
| `fixtures/refund-scenarios.json` | Declarative, deterministic scenario definitions (JSON, not SQL). |
| `scripts/refund-reconciliation-dryrun.ps1` | Authored **not run**. Timeout-sweep dry-run against the disposable target only. |
| `scripts/gateway-double-contract-check.ps1` | Static config + DI source inspection of the resolved client type. |
| `checklist/acceptance-checklist.md` | The ordered gate the Mastermind follows before refund features are accepted. |

## 6. Owner-evidence markers

Scenarios and checklist items that cannot be fully exercised without an
Owner-only input carry the literal token `NEEDS-OWNER-EVIDENCE` and name the W0
D1 hypothesis item. The W0 D1 lane (`session ses_e9bd7466dffeJf1Sk3zuXbyfSP`)
recorded **7 ranked hypotheses needing sanitized config/DB evidence** and one
confirmed defect (the Paymob upstream error body is discarded at
`PaymobClient.cs:223-228`; no attempt row persists on 502). The numbered list
below is the stable addressing scheme this pack uses for those items; each
marker names one.

| Item | Hypothesis / evidence need | Affects |
|---|---|---|
| `D1-H1` | Sanitized Paymob upstream **400 response body** for the failing subscription intention (M2, trace `0HNPG5KPDG28A:00000001`) is not retrievable without Owner access. | reconciliation "failed" evidence, S2 mapping |
| `D1-H2` | Whether the failing `Paymob:SecretKey` was a **live vs test** key at the time of the 502 — config evidence. | client-type assertion provenance |
| `D1-H3` | Whether a **merchant/integration** mismatch (intention `payment_methods` vs the configured integration) caused the 400 — merchant settings evidence. | subscription rejection scenarios |
| `D1-H4` | Whether a **subscription/plan** or recurring flag mismatch on the Paymob side caused the 400 — provider account evidence. | subscription rejection scenarios |
| `D1-H5` | Whether an **attempt row** should persist on a 502 (currently none is written) — DB-schema/decision evidence. | reconciliation provenance |
| `D1-H6` | The **production-configured host's** actual config booleans (`Paymob:SecretKey`, `FinanceMigrations:ApplyOnStartup`) — never read by these scripts, Owner-reported only. | every shared-host boundary |
| `D1-H7` | ~~Whether an **approved holiday calendar** exists for the business-day rule (#1).~~ **RESOLVED mid-W2**: Owner decision **#36** approved the full calendar and it is now implemented in `BusinessDayCalendar.cs` — fixed Gregorian dates, Coptic Easter + Sham El-Nessim via the Julian computus, Islamic feasts via `UmAlQuraCalendar`, plus an `AnnouncedHolidays` override seam for moon-sighting corrections. **Remaining Owner input**: only the annual announced-holiday feed (when the government moves an Islamic feast ±1 day). Deadline scenarios now run against the implemented calendar. | business-day deadline scenarios (`DEADLINE-CLOCK-01`) |

New markers may only be added by the Mastermind; a worker does not invent an
item number.

## 7. Execution protocol (Mastermind)

1. Confirm the disposable target identity and the resolved client type
   (`gateway-double-contract-check.ps1`, static, may run in a read-only slot).
2. Run the reconciliation dry-run **only** under explicit authorization, with
   `-IUnderstandThisRuns`, and confirm it prints `DRY-RUN` and writes nothing
   unless `-Apply` was separately approved.
3. Follow `checklist/acceptance-checklist.md` top to bottom for a refund
   feature before it is declared accepted.
4. Record receipts (revision, target alias, counts, decisions exercised) in the
   brief's ledger. Never record connection strings or payloads.
