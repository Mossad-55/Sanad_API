# W4 Finance/payments — implementation contract

Scope/status authority: [execution brief](../worker-prompts/media-care-records-payments-2026-10-23.md).
This document is the W4 feature contract and amendment list; it is not a second
progress ledger. All authored checks are **NOT RUN** until the Mastermind records
execution evidence in the brief. No build, test, HTTP, database, storage or
provider operation was performed by this lane.

- Lane: W4 Finance/payments, session `ses_e95ea8e6effeWTNtoeqagEmM6b`
- Model: `opencode/deepseek-v4.1-flash-free#max`
- Base revision: `9433c1df117f85131c3871f850659cc78981d298` plus the preserved
  uncommitted P1/W1–W3 worktree (not committed or reverted by this lane)
- Release: not requested. Deployed revision is not assumed equal to the worktree.

## 1. Payment history reads (R4)

Owner-only reads over the existing `SubscriptionPaymentAttempt` / `SubscriptionInvoice`
schema. No schema, snapshot or migration change is required or made.

| Method | Route | Success | Errors |
|---|---|---|---|
| GET | `/api/v1/family/subscriptions/payments` | 200 `SubscriptionPaymentPageResponse` | 401 anonymous; 403 non-owner; 400 invalid query |
| GET | `/api/v1/family/subscriptions/payments/{paymentAttemptId:guid}` | 200 `SubscriptionPaymentResponse` | 401 anonymous; 403 non-owner; 404 missing/foreign/`Guid.Empty` |

Both routes stay under the controller's existing `FamilyAccess` policy and are
owner-only inside the handler, mirroring the invoice routes. A foreign attempt id
and a missing id return the same 404 so the detail route cannot be used to probe
another family's attempt ids.

### Item contract

```json
{
  "paymentAttemptId": "0199...",
  "amount": 340.86,
  "currency": "EGP",
  "status": "Succeeded",
  "paidOnUtc": "2026-10-23T16:17:47Z",
  "invoiceId": "0199...",
  "method": "Wallet",
  "createdOnUtc": "2026-10-23T16:10:00Z",
  "planKey": "premium",
  "planVersion": 1,
  "isRenewal": false,
  "isPlanChange": false
}
```

- `amount` is **EGP major units** (`340.86` = 34,086 piastres), the same scale as
  invoice amounts. Refund request bodies stay in piastres; the unit is named on
  both surfaces and must be documented in consumer/Postman text.
- `paidOnUtc` is the provider settlement instant (`SettledOnUtc`) **only**. A
  pending or failed attempt has `null`, even though it has a creation instant.
  `createdOnUtc` is always present.
- `invoiceId` is the stored invoice whose `PaymentAttemptId` equals the attempt.
  It is `null` when no invoice was issued; a plan-change attempt normally has no
  invoice.
- `status` serializes the enum numerically under the API's existing JSON
  conventions (`1` Pending, `2` Succeeded, `3` Failed) unless the shared host
  configures string enums; consumers must follow the existing enum convention
  rather than a new one.
- Raw gateway ids, billing data, secrets and provider payloads are never returned.

### Ordering, paging, filters

- Stable order: `paidOnUtc DESC NULLS LAST, createdOnUtc DESC, paymentAttemptId DESC`.
  NULLS LAST is written as a provider-portable conditional sort key
  (`SettledOnUtc == null ? 0 : 1` first) because `NullsLast()` is Npgsql-only and
  unused in the repository.
- Envelope: `{ items, page, pageSize, totalCount, totalPages }`. Default
  `page=1`, `pageSize=20`, maximum `pageSize=100`; an empty result keeps the same
  envelope with `totalCount=0, totalPages=0`.
- Optional filters: `status` (defined enum only), `method` (defined enum only),
  inclusive UTC `paidFromUtc`/`paidToUtc` over the settlement instant only.
- Invalid `page`/`pageSize`, undefined enum values, a local/unspecified date kind
  or a reversed range return `400 Subscriptions.Payment.InvalidQuery`; the bounds
  are never silently reinterpreted to local time.
- Pending/failed attempts are never in a paid window because they have no
  settlement instant.

## 2. Invoice linkage

`SubscriptionInvoiceListItemResponse` and `SubscriptionInvoiceResponse` append a
nullable `paymentAttemptId` (last positional parameter, so existing constructors
and callers keep compiling). Both invoice queries map the stored value. Invoice
money remains major-unit EGP; the id is the same attempt identity accepted by the
existing duplicate-charge POST, but a succeeded charge is not proof of a
duplicate-charge entitlement.

## 3. Paymob diagnostics and wallet path (R5/R6)

Implemented in `PaymobClient` for both booking and subscription intention paths:

- The configured integration id is validated as a **positive invariant integer
  before any HTTP call**. A malformed, non-numeric, zero, negative or whitespace
  value returns the existing safe `Paymob.MethodNotAvailable` and the configured
  value is never echoed. Previously a non-numeric id reached `long.Parse` inside
  the HTTP `try` and surfaced as a generic gateway error, and could differ between
  the card/wallet configuration paths.
- Selection is unchanged and separately asserted: wallet uses
  `WalletIntegrationId` and never sends `subscription_plan_id` or opts into
  recurring renewal (even if a plan id is supplied); card without a plan uses
  `CardIntegrationId`; card with a plan uses `Card3dsIntegrationId`, sends
  `subscription_plan_id` and reports `RecurringRenewalSupported = true`.
- A transport timeout (`OperationCanceledException`/`TaskCanceledException` not
  caused by the caller's token) returns the safe
  `Paymob.GatewayError` "The payment gateway did not respond in time." instead of
  escaping as a 500. Caller cancellation is rethrown.
- A truncated `200` body (valid JSON missing a required field) now maps to the
  same bounded `Paymob.GatewayError` as malformed JSON, so an unexpected upstream
  shape cannot escape as a 500.
- Existing sanitized decline-reason behavior is preserved: only `detail`,
  `message`, `status` scalars, credential/PII-screened and length-bounded.

### R5/R6 status: BLOCKED, not diagnosed

No provider call was made and no sanitized original upstream response exists in
this lane. The 502 trace `0HNPG5KPDG28A:00000001` (R5) and the wallet 400 trace
`0HNPGJ3AQGFPS:00000001` with plan version
`01a155d8-7994-7154-b71e-55d3814c2f28` (R6) remain **BLOCKED**. The rejected
field and the actual provider result are unknown; a gateway-double or
`DevelopmentPaymobClient` success is not evidence of real wallet success.
Advertised availability remains control flag + configuration presence, not a
provider capability probe. Closure requires either retained sanitized original
responses or an approved non-charging test-wallet intention in the approved test
environment (D8), with the sanitized result recorded. No credential or billing
PII may be requested or echoed.

## 4. Complaint award cap (D10)

- Default remains the Owner-approved provisionally enabled **EGP 500.00 =
  50,000 piastres** (`Finance:ComplaintReviews:MaxAwardMinorUnits`).
- Missing key → default; explicit positive value → override; explicit `0` →
  disabled (`null`); negative/malformed raw value → **startup failure** via
  `ParseConfiguredValue` (it never silently restores the default or disables the
  cap).
- Awards below/equal to the cap are forwarded; an award above the cap is refused
  with `Finance.Complaint.AwardExceedsCap` and records no verdict and no ledger
  entry. A zero award is valid.
- The admin controller's local error mapping now returns **409** for
  `Finance.Complaint.AwardExceedsCap`, matching the shared
  `ResultProblemDetailsMapper`. `Finance.Complaint.AmountInvalid` remains 400.
- The direct `FromConfiguredValue(-1) → null` factory behavior is retained for
  pre-parsed callers and remains pinned by `ComplaintAwardCapTests`; negative
  configuration still cannot reach it in DI.
- Corrected stale comments that said "0/negative disables" so documentation now
  matches the startup-rejection semantics.

## 5. F3 double-refund correction (D9)

**Invariant.** A paid plan change carries a prorated credit from the source
period. That credit is refundable only once, against the source capture's budget.
The plan-change capture may claim only the **new money** it charged.

**Implementation (Families attribution layer only).** In
`ResolveCurrentPeriodCaptureAsync`, a capture with `IsPlanChange` measures its
consideration as the attempt's own `TotalPayable` (the incremental charge). Every
other capture keeps the existing `CurrentPeriodGross`-then-`TotalPayable`
fallback, so discounts on settled charges survive and older-period money still
supplies no headroom.

Fixture: initial capture 342.00; target gross 400.00; prorated credit 100.00;
incremental charge 300.00. At the period midpoint:

| Capture | Basis before F3 | Basis after F3 | Midpoint award |
|---|---|---|---|
| Source (initial) | 342.00 | 342.00 | up to the unused share of 342.00 |
| Plan change | 400.00 (target gross) | **300.00** (new money) | **150.00** (was 200.00) |

The 100.00 credit is therefore refundable once (source budget) and never a second
time through the plan-change budget. Audit/accounting is unchanged: the budget row
is still keyed by the capture, the ledger movement still names the settled
attempt, and the final Finance claim still refuses anything above the capture.

**Deliberate consequence (documented, not accepted residual).** The credited share
is not refunded on the shutdown path at all: a shutdown of a plan-change capture
refunds only the unused portion of the new money. Free/zero plan-change periods
therefore refund zero on that path. This is a deliberate under-refund, never a
double refund; it is the D9 direction and must be stated in consumer/admin
guidance.

**Out of lane scope.** `SubscriptionRefundBudget.TryClaim` still lets capacity
grow to a larger supplied `capturedMinorUnits`, and its growth test pins that
behavior. F3 is fixed at the Families attribution layer, so the recorder now
always receives the incremental basis for a plan-change capture. The
`TryClaim` XML documentation still describes the plan-change basis as the target
gross (`BasePrice`); that sentence is now stale and is a required shared/domain
amendment (below). No Finance domain file was edited by this lane.

## 6. Authored checks — all NOT RUN

| Check | File | Covers |
|---|---|---|
| Payment history ordering, units, settlement-only `paidOnUtc`, invoice link, flags, filters, paging, invalid query, owner scoping, detail 404/403, invoice linkage | `tests/Sanad.UnitTests/Families/Subscriptions/SubscriptionPaymentQueriesTests.cs` | InMemory handler behavior |
| Payments route metadata and 403/400/404/200 mapping | `tests/Sanad.UnitTests/API/FamilySubscriptionsControllerTests.cs` | Controller contract (extended) |
| Wallet/card-enrollment integration selection, plan-field omission, malformed id before HTTP, no value echo, timeout, caller cancellation, decline, malformed/truncated upstream | `tests/Sanad.UnitTests/Families/PaymobSubscriptionWalletPathTests.cs` | Paymob client with a stub handler |
| Cap resolution through real Finance DI, malformed/negative startup failure | `tests/Sanad.UnitTests/Finance/ComplaintAwardConfigurationTests.cs` | Configuration semantics |
| Above-cap 409 and amount-invalid 400 mapping | `tests/Sanad.UnitTests/API/AdminFinanceComplaintsControllerTests.cs` | Controller contract (extended) |
| F3 plan-change 15,000/30,000 and source-budget independence, both orders, plan-change over-claim refusal | `tests/Sanad.UnitTests/Finance/SubscriptionRefundPostgresBudgetTests.cs` | Disposable PostgreSQL (maps/schema/real recorder) |

Amended existing test:
`tests/Sanad.UnitTests/Families/Subscriptions/SubscriptionRefundCommandsTests.cs`
— `Shutdown_base_uses_only_the_plan_change_incremental_charge_to_avoid_refunding_credit_twice`
now expects **15,000 award / 30,000 capture** (previously 20,000 / 40,000) for the
same 400/342/100/300 fixture.

### PostgreSQL target and safety

`SubscriptionRefundPostgresBudgetTests` is skip-gated by
`ConnectionStrings__IdentityIntegrationDatabase` (an explicit opt-in; absent
variable → test skipped, not passed). When present it requires:

1. loopback host (`localhost`/`127.0.0.1`/`::1`) on port 5432;
2. the database name replaced with a unique nonce-bearing
   `SanadW4F3Verify_<nonce>` and verified absent in `pg_database` before any
   write;
3. Families and Finance migrations applied with per-schema migration history
   (`families.__EFMigrationsHistory`, `finance.__EFMigrationsHistory`);
4. delete of only that unique database (`EnsureDeletedAsync`) at disposal.

It never connects to the source database itself, to `SanadDb`, or to a persistent
target. It performs no provider call and no real charge/refund.

`docs/postman` and Admin/consumer guides remain Mastermind-owned.

## 7. Changed paths (this lane)

Production:

- `src/API/Sanad.API/Controllers/FamilySubscriptionsController.cs`
- `src/API/Sanad.API/Controllers/AdminFinanceComplaintsController.cs`
- `src/Modules/Families/Application/Sanad.Modules.Families.Application/Subscriptions/SubscriptionQueries.cs`
- `src/Modules/Families/Application/Sanad.Modules.Families.Application/Subscriptions/SubscriptionInvoices.cs`
- `src/Modules/Families/Application/Sanad.Modules.Families.Application/Subscriptions/SubscriptionRefundCommands.cs`
- `src/Modules/Families/Infrastructure/Sanad.Modules.Families.Infrastructure/Payments/PaymobClient.cs`
- `src/Modules/Finance/Application/Sanad.Modules.Finance.Application/ComplaintReviewCommands.cs`
- `src/Modules/Finance/Infrastructure/Sanad.Modules.Finance.Infrastructure/DependencyInjection.cs`

Tests:

- `tests/Sanad.UnitTests/Families/Subscriptions/SubscriptionRefundCommandsTests.cs` (amend)
- `tests/Sanad.UnitTests/Families/Subscriptions/SubscriptionPaymentQueriesTests.cs` (new)
- `tests/Sanad.UnitTests/Families/PaymobSubscriptionWalletPathTests.cs` (new)
- `tests/Sanad.UnitTests/API/FamilySubscriptionsControllerTests.cs` (extend)
- `tests/Sanad.UnitTests/API/AdminFinanceComplaintsControllerTests.cs` (extend)
- `tests/Sanad.UnitTests/Finance/ComplaintAwardConfigurationTests.cs` (new)
- `tests/Sanad.UnitTests/Finance/SubscriptionRefundPostgresBudgetTests.cs` (new)

Preserved preserved-but-untouched in this lane: the pre-existing uncommitted cap
files `ComplaintAwardOptions.cs` and `ComplaintAwardCapTests.cs` (created by the
paused work; only comments were corrected, and both were left untracked as
found). No Families/Finance snapshot, migration, DbContext, API DI/Program,
policies, mapper, Postman or deployment file was edited. `PaymobWebhookController.cs`
is uncommitted from the earlier P1 work and was not changed by W4.

## 8. Shared integration status (Mastermind-owned)

- **Family Postman — integrated:**
  `docs/postman/app/Sanad.App.Family.postman_collection.json` now has payment
  list/detail requests in the subscriptions folder, a
  `subscriptionPaymentAttemptId` collection variable, and invoice/payment-linkage
  notes. The collection also documents EGP major-unit amounts, settlement-only
  `paidOnUtc`, nullable `invoiceId`, and the deliberate plan-change shutdown
  under-refund (new money only; credited share is not refunded twice).
- **Finance `SubscriptionRefundBudget.TryClaim` XML — integrated:** its summary
  now documents that the purchase owner must supply the basis attributable to the
  same attempt and that a plan-change basis is incremental `TotalPayable` only.
  Capacity-growth behavior and its existing test were not changed.
- The Admin Finance collection already documents
  `409 Finance.Complaint.AwardExceedsCap`; no additional Admin Postman change was
  required. The contract keeps R5/R6 explicitly BLOCKED until approved provider
  evidence exists.
- **Validation status:** Family Postman JSON parsing and route/Postman parity remain
  part of the coordinated final gate; no live HTTP result is claimed here.

## 9. Limitations

- All checks above are authored and **NOT RUN**; no evidence of pass exists.
- Invoice linkage inside the payment projection relies on a correlated subquery;
  the InMemory test exercises it, and the PostgreSQL test exercises the real
  recorder but not the payment-history SQL. A relational read test for the payment
  list may be added by the Mastermind if the InMemory translation differs.
- R5/R6 provider diagnosis and real wallet intention success remain BLOCKED.
- The deliberate plan-change under-refund of the credited share is documented but
  not financially accepted beyond D9's direction; any change to it is a policy
  decision, not a test fix.
