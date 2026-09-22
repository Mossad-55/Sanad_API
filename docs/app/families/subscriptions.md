# Family subscriptions and checkout quotes

Family subscription reads require a normal Family JWT and the `FamilyAccess` policy. Only the family Owner may use these routes; Editors, Viewers, non-family accounts, users without a family, and deleted-family owners receive `403`.

## Published plan catalog

`GET /api/v1/family/subscriptions/plans` returns every published plan version. Draft versions are excluded. Published versions retired from new sales remain visible, with `isAvailableForNewSales: false`.

Each item includes its key, version, tax-exclusive price, currency, billing cycle, rollover policy, member and monthly-booking limits, publication/creation timestamps, availability, and all benefit keys with their included state. The catalog read does not add VAT/tax to the returned price.

## Purchase quote

`POST /api/v1/family/subscriptions/quote` returns a server-calculated, read-only
quote for a published plan version that is available for new sales. The request
requires the family Owner and accepts only the plan-version ID and an optional
coupon code; the client cannot provide or override price, tax, discount, or
currency values.

```json
{
  "planVersionId": "0198e2c1-1111-7777-8888-000000000001",
  "couponCode": "WELCOME10"
}
```

The response contains `basePrice`, `discountAmount`, `taxableAmount`,
`taxRatePercentage`, `taxAmount`, `totalPayable`, `currency`, `cycle`, and the
selected plan identity. Prices are tax-exclusive in the catalog; the quote
applies the active tax rule only when its `effectiveOnUtc` is at or before the
quote time, and rounds monetary values to two decimals using the server's
banker's-rounding convention. Coupons must belong to the selected plan and be
unexpired. The current response reports `recurringRenewalSupported: false`;
this endpoint does not charge a payment method, activate a subscription, create
an invoice, consume an allowance, or mutate data.

Errors include `401` unauthenticated, `403` non-owner, `404`
`Subscriptions.Quote.PlanNotFound`, `400` `Subscriptions.Quote.CouponInvalid`,
and `409` `Subscriptions.Quote.TaxNotConfigured`.

## Initial payment intent and settlement

`POST /api/v1/family/subscriptions/payment-intent` is Owner-only and starts the
initial checkout boundary for a published, available plan. The server reruns the
quote from the plan version, coupon, and effective tax rule; clients cannot
provide or override the amount, tax, discount, currency, or renewal dates.

```json
{
  "planVersionId": "0198e2c1-1111-7777-8888-000000000001",
  "couponCode": null,
  "method": 1,
  "billing": {
    "firstName": "Ahmed",
    "lastName": "Ali",
    "email": "ahmed@example.com",
    "phoneNumber": "+201012345678"
  }
}
```

`method` is the numeric JSON enum: `1` is Card and `2` is Wallet. The response contains the server amount/currency, a `sub_` merchant
reference, the provider client secret/public key, and
`recurringRenewalSupported: false`. Wallet is therefore a one-time/manual
renewal boundary in this slice; recurring wallet capability is not assumed.

The Paymob callback is `POST /api/v1/payments/webhooks/paymob`. It is anonymous
but requires a valid Paymob HMAC. Subscription references are settled through
the subscription attempt table, with amount and currency checked against the
recorded server quote. Pending callbacks do not activate a subscription; failed
callbacks close the attempt; a successful callback activates the plan once.
Repeated callbacks are idempotent. A valid callback for an unknown or already
processed reference is acknowledged without activating another subscription.

Payment-intent errors include `400` invalid billing/coupon or zero payable
amount, `403` non-owner, `404` unavailable plan, `409` current subscription or
unavailable payment method, `502` provider failure, and `503` missing provider
configuration. This bounded slice does not implement recurring renewals,
seven-day grace, upgrades/downgrades or proration, invoices/PDFs, allowance
consumption, notifications, or deployment automation.

## Current subscription snapshot

`GET /api/v1/family/subscriptions/current` returns `{ "currentSubscription": ... }`. The value is selected only from the caller's family `FamilySubscriptions` rows where `isCurrent` is true. It includes the stored plan terms and owned benefits; it does not join the mutable catalog.

When no current row exists, the route returns HTTP `200` with `currentSubscription: null`. It does not synthesize a Free plan.

The current snapshot also exposes `autoRenewEnabled`, `cancellationRequestedOnUtc`, and the stored
`currentPeriodEndsOnUtc` boundary. The Owner
can call `POST /api/v1/family/subscriptions/cancel-renewal` to disable renewal while preserving
current access, `isCurrent`, and all stored snapshot terms. A repeat cancellation returns `409`.
Before the current period ends, the Owner can call
`POST /api/v1/family/subscriptions/reenable-auto-renew` to restore auto-renew.
Lifecycle mutations use optimistic concurrency; a stale concurrent mutation returns the same
`409` conflict as an already-requested cancellation.

Unauthenticated requests receive `401`; authenticated requests failing the owner read rule receive `403`.

Published plans may remain visible after an administrator retires them from new sales. Retirement changes only `isAvailableForNewSales`; a family’s stored subscription snapshot keeps its original plan terms and benefits.
