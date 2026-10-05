# Finance platform charge rules

This is the shared Admin configuration surface for the percentage platform fee
and tax used by caregiver quotes/bookings and Family subscription quotes and
payments. Both percentages are calculated independently on the transaction's
base amount (or discounted subscription taxable amount); tax is not calculated
on the platform fee. There is no default when an effective rule is absent.

## Access and routes

These routes require a normal authenticated JWT and the
`FinanceOperationalAdmin` policy: **SuperAdmin or SupportAdmin**. ContentAdmin,
Family, Caregiver, and anonymous callers are denied.

```text
POST /api/v1/admin/finance/platform-charge-rules
GET  /api/v1/admin/finance/platform-charge-rules/current
GET  /api/v1/admin/finance/platform-charge-rules/history
```

Create a rule with:

```json
{
  "platformFeeRatePercentage": 15.00,
  "taxRatePercentage": 14.00,
  "version": 1,
  "effectiveOnUtc": "2026-10-01T00:00:00Z"
}
```

`version` must be positive and greater than the latest stored version;
percentages are inclusive from `0` through `100`, rounded to two decimals;
and `effectiveOnUtc` must be a UTC timestamp. The create route returns `201`
with the new rule UUID. Invalid input returns `409 Finance.Charges.Invalid`;
duplicate/out-of-order versions return a conflict; concurrent version/active
changes return `409 Finance.Charges.Conflict`.

The current route returns the active effective rule, or JSON `null` when no
effective rule exists. History returns all stored rules ordered by descending
version. Rule responses contain `id`, `platformFeeRatePercentage`,
`taxRatePercentage`, `version`, `effectiveOnUtc`, `createdOnUtc`, and
`isActive`. A future `effectiveOnUtc` is retained for scheduling, but current
consumers do not use it until it is effective.

## Consumers and immutable snapshots

- `GET /api/v1/caregivers/{caregiverId}/quote` returns the base caregiver fee,
  independent platform-fee and tax rates/amounts, `platformChargeRuleVersion`,
  total, and currency. An absent effective rule makes the quote unavailable;
  it is never silently treated as zero.
- Family caregiver checkout resolves the same effective rule server-side and
  stores base, fee rate/amount, tax rate/amount, rule version, total, and
  currency in the booking price snapshot. Later rule changes do not reprice the
  booking. The booking list/detail contract currently exposes the base fee,
  platform fee, total, and currency; the stored tax fields are not currently
  returned by the booking detail response.
- Family subscription quote/payment, renewal, plan-change, and invoice flows
  use the shared rule. Subscription payment attempts, current-period renewal
  state, and invoices preserve the base/tax/fee/rule-version/total snapshot;
  historical rows are not rewritten when a new rule is configured.
- Paymob recurring callbacks continue to use the existing anonymous HMAC
  webhook contract and the `sub_` subscription reference. This feature does
  not authorize provider calls or invent new callback payloads.

When no shared rule is effective, subscription quote returns `409
Subscriptions.Quote.TaxNotConfigured`; caregiver quote and caregiver checkout
return their existing quote/charges-not-configured errors. Clients cannot
override server-calculated amounts or rates.

## Legacy subscription-tax adapter

The existing routes remain available for compatibility:

```text
POST /api/v1/admin/subscriptions/tax-rules
GET  /api/v1/admin/subscriptions/tax-rules/current
GET  /api/v1/admin/subscriptions/tax-rules/history
```

They retain their unchanged `SubscriptionPlanAdmin` policy: **SuperAdmin only**
(normal access); SupportAdmin and ContentAdmin remain denied. The create adapter
requires shared Finance configuration and changes the shared rule's tax rate
while carrying forward its effective platform-fee rate. Use the Finance routes
for new configuration. Existing subscription-tax records and charge/invoice
records are preserved; this adapter does not retroactively rewrite snapshots.

## Migration and startup safety

The Finance schema is introduced by `20261002210606_AddPlatformChargeRulesEfMetadata`.
The related Family snapshot migration is:

```text
20261002210659_AddPlatformChargeSnapshotMetadata
```

Finance startup migration is explicitly opt-in:
`FinanceMigrations:ApplyOnStartup` defaults to `false`. Do not enable automatic
Finance migration in production; apply and verify migrations through the
approved release procedure against the exact target.

The current implementation was authorized for disposable local migration and
test work only. Provider-backed payment success and production deployment are
separate owner-controlled actions.
