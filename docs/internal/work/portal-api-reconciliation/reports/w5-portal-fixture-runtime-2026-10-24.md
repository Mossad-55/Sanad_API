# W5 portal fixture pack — local runtime acceptance receipt

Date: 2026-10-24 (Africa/Cairo). Status: **W5 accepted — runtime-verified on the local target.**
Package revision reported by the host: `eb218a002b598e66d3e62013d2c1d198d66c3768` (Wave A base).
No Production resource was touched by this receipt.

## Target proof (before any stateful startup)

- Host guard: all nine module contexts (Identity, Cms, Caregivers, Families, Notifications,
  CareHomes, Finance, Community, Chat) resolved to exactly `localhost:5432/SanadDb`;
  `target.passed=true`, `failedChecks=[]`, zero pending migrations.
- The target started **15 migrations behind** (`Identity` 2, `Cms` 3, `Families` 3,
  `CareHomes` 2, `Finance` 3). With explicit owner approval the existing local schema was
  updated through the repository's guarded `--apply-local-fixtures-migrations --expect` path
  (exact pending id list passed on the command line), additive only — no reset, no drop, no
  seed, no delete. Result: `"passed":true,"appliedAnyMigrations":true`, 15 ids applied.
- Pre-migration backup: custom-format dump of `SanadDb`, validated by `pg_restore -l`
  (803 TOC lines). Path (never committed): `C:\Users\mosad\AppData\Local\Temp\opencode\sanad-portal-fixture\before-w5-migrations.dump`.
- Provider isolation proof (resolved from DI before the listener started): `IPaymobClient` →
  `ScriptedPaymobClient`, `ISubscriptionRefundGateway` → `ScriptedSubscriptionRefundGateway`,
  `ISmsSender`/`IEmailSender` → `PortalFixtureInert*Sender`, `IPushSender` →
  `PortalFixtureInertPushSender`, `IDateTimeProvider` → `PortalFixtureClock`;
  `applicationHostedServices=[]`. A live Paymob key observed in configuration did not change
  any seam (`paymobLiveKeyObserved=false` after isolation).

## Provisioning and idempotence

- `--mode start` provisioned **12/12 requested groups**, `passed=true`, `unresolvedGroups=[]`,
  each with a persisted readback: caregiver refund ambiguity (Pending, ambiguous), caregiver
  refund failure (CancelledByCaregiver / Failed / retryable), completed paid booking with a
  Paid synthetic payout, two paid `PaidAwaitingDecision` slots (accept and reject) plus a
  persisted rejected example in `RefundPending`, care-home refund ambiguity, care-home refund
  failure (Failed), evidenced external refund (Completed), booking extension off an accepted
  paid segment, checkout recorded with an open dispute, current paid premium subscription with
  a submitted duplicate-charge claim plus a separate service-shutdown refund
  (`ServiceShutdown`, ledger `Approved 20255` minor units), complaints in `Received` and
  `Investigating`.
- **Repeat runs:** the same command was executed three times against the already-provisioned
  target. Every run reported `passed=true` with the identical record ids (for example
  subscription `01a16edc-afb0-...`, claim `01a16edc-bae0-...`, complaints `01a16edc-bd85-...`
  and `01a16edc-bddc-...`), so reuse is idempotent: no duplicate users, subscriptions,
  claims, complaints or refund budget rows.
- Fixture volume stays tiny and run-owned: one to three synthetic rows per group, every
  narrative value carrying the `sanad-portal-fixture`/`w5p` marker, every synthetic provider
  reference carrying the `sanad-portal-fixture-synthetic` prefix.

## Browsable fixtures and private family login

The host serves the real `Sanad.API` pipeline on loopback `http://127.0.0.1:5299`, so a local
web/portal client can log in and browse the same fixtures without any product-code test flag.
Credentials live only in the private, current-user-ACL credential file outside the repository
(`...\sanad-portal-fixture\credentials-w5p.json`); contents were never printed, logged or
committed.

Readbacks over HTTP against the live listener (all authenticated with fixture-issued tokens):

| Principal | Endpoint | Result |
|---|---|---|
| Family (private login) | `POST /api/v1/auth/login` | **200**, token issued, `accessType=1` |
| Family | `GET /api/v1/family/subscriptions/current` | **200**, `planKey=premium`, 299.00 EGP, plan version 1 |
| Family | `GET /api/v1/family/subscriptions/payments` | **200**, the succeeded duplicate-claim capture |
| Family | `GET /api/v1/family/subscriptions/refunds/duplicate-charge` | **200**, the submitted claim |
| Family | `GET /api/v1/family/bookings` | **200** |
| Family | `GET /api/v1/family/care-home-bookings` | **200**, fixture bookings visible |
| Super admin | `GET /api/v1/admin/finance/complaint-reviews` | **200**, two reviews (Received, Investigating) |
| Super admin | `GET /api/v1/admin/finance/subscription-refunds/duplicate-charge` | **200**, `state=1` (Submitted), `awardMinorUnits=29900` |
| Super admin | `GET /api/v1/admin/care-homes/bookings` | **200**, 15 rows incl. refund states Pending/Failed/Completed |
| Super admin | `GET /api/v1/admin/care-homes/check-in-disputes` | **200**, the open checkout dispute |
| Super admin | `GET /api/v1/admin/bookings` (+ `finance=1`, `status=2`) | **200** after the fix below |

## Defect found and fixed by this runtime pass

`GET /api/v1/admin/bookings` returned **500** on the real relational provider: the Wave A
enrichment ordered by `b.Id.Value`, filtered families by `f.Id.Value` and caregivers by
`caregiver.Id.Value` inside EF lambdas. Unwrapping a value-converted id is not translatable for
Npgsql. Fixed in the same slice:

- `BookingId` implements `IComparable<BookingId>` and the query orders by the mapped id
  (`ThenByDescending(b => b.Id)`), keeping the deterministic tiebreaker for both relational and
  CLR-side ordering.
- Family and caregiver lookups filter on the mapped id (`Contains(f.Id)` /
  `Contains(caregiver.Id)`) and unwrap after materialization.
- Verified live: the plain page and both filters return **200** with family display names
  resolved.

## Cleanup

`-Mode Cleanup -RunManifestPath <manifest>` printed a report-only classification of all 23
records (`retained-operator-decision` / `report-only-money-free`) and ended with
"report only: nothing was deleted. No delete statement exists in this tool." No delete SQL
surface exists in the wrapper.

## Limitations

- Evidence is HTTP/API level through the real pipeline (no separate web client was exercised in
  this environment); the portal can browse the same listener.
- Email/SMS/push delivery is inert by design and Paymob is scripted, so no provider call and no
  real money movement occurred.
- Local `SanadDb` was brought to the current schema head with owner approval; Production
  migrations are handled separately by the deploy runbook.
- One previously created checkout-dispute row (from an intermediate run before the checkout
  ordering fix) keeps `checkoutOnUtc=null`, which is a valid domain state (dispute opened at
  check-in); no row was deleted or rewritten, preserving the no-delete policy and any manual
  progress.
