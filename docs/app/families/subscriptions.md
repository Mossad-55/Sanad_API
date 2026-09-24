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
unexpired. The quote response's `recurringRenewalSupported` value is not a
payment-method capability guarantee. This endpoint does not charge a payment method, activate a subscription, create
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
`recurringRenewalSupported: true` only when `method` is Card, the selected
local plan has a positive `paymobSubscriptionPlanId`, and the Paymob Card 3DS
integration is configured. Otherwise it is `false`. Wallet remains
one-time/manual; no recurring wallet capability is implied. The response
contains no provider subscription identity yet. The subsequent Paymob
subscription callback establishes that identity in the shared provider
identity registry and links it to the payment attempt and family snapshot.

The Paymob subscription callback is `POST /api/v1/payments/webhooks/paymob`. It
is anonymous but requires a valid body `hmac`: HMAC-SHA512 over
`{trigger_type}for{subscription_data.id}` using `Paymob__HmacSecret`. The
callback body must contain `subscription_data.id`, `trigger_type`, and `hmac`;
`subscription_data.initial_transaction`, `amount_cents`, `state`, and
`next_billing` are optional fields. Accepted trigger spellings are `CREATED`,
`Subscription Created`, `Successful Transaction`, `Failed Transaction`, and
`Failed Overdue Transaction`, case-insensitively. Renewal callbacks
(`Successful Transaction`, `Failed Transaction`, and `Failed Overdue
Transaction`) must also contain top-level `paymob_request_id`.

Initial creation records the provider subscription identity in the shared
registry and links it to the initial payment attempt and family snapshot.
Renewal callbacks use that registry, verify amount against the stored snapshot,
and append a durable callback ledger row keyed by the provider event/request
identity. Repeated or out-of-order callbacks are idempotently acknowledged;
they do not apply a second renewal or extend access. `Failed Transaction` starts
the existing seven-day grace without extending the period; `Failed Overdue
Transaction` does not start or extend grace. A successful renewal advances the
original period-end anchor. If grace has expired, the callback is still
ledgered, returns controller HTTP `200`, and leaves the successful-renewal state
unchanged. Unknown provider identities are acknowledged without mutation.

Payment-intent errors include `400` invalid billing/coupon or zero payable
amount, `403` non-owner, `404` unavailable plan, `409` current subscription or
unavailable payment method, `502` provider failure, and `503` missing provider
configuration.

## Renewal payment and seven-day grace

`POST /api/v1/family/subscriptions/renewal/payment-intent` is Owner-only and
starts a renewal payment at the current period boundary or while the
subscription is inside its seven-day renewal grace window.

```json
{
  "method": 1,
  "billing": {
    "firstName": "Ahmed",
    "lastName": "Ali",
    "email": "ahmed@example.com",
    "phoneNumber": "+201012345678"
  }
}
```

The server uses the stored subscription snapshot terms and never accepts a
client-supplied price, plan, tax, discount, or renewal date. A failed renewal
settlement keeps access in grace until `currentPeriodEndsOnUtc + 7 days`; a
successful retry advances the period from the original period-end anchor and
clears grace. Settlement is idempotent. A renewal before the boundary, after
grace expiry, or while another renewal attempt is pending returns `409`.
This endpoint always uses the manual payment-intent boundary and reports
`recurringRenewalSupported: false`; renewal enrollment is not started here.
Wallet renewal remains one-time/manual.

This slice does not implement upgrades/downgrades or proration, invoices/PDFs,
allowance consumption, notifications, or deployment automation.

## Card enrollment boundary

Initial Card enrollment is available only when the selected local plan has a
positive `paymobSubscriptionPlanId` and `Paymob__Card3dsIntegrationId` is
configured. The server sends that local mapping as Paymob's
`subscription_plan_id` and selects the Card 3DS integration. Wallet remains
one-time/manual. Provider subscription identity persistence and renewal-event
settlement are implemented through the callback contract above; wallet renewal
continues through the explicit manual payment-intent route because wallet
recurrence is not claimed.

## Current subscription snapshot

`GET /api/v1/family/subscriptions/current` returns `{ "currentSubscription": ... }`. The value is selected only from the caller's family `FamilySubscriptions` rows where `isCurrent` is true. It includes the stored plan terms and owned benefits; it does not join the mutable catalog.

When no current row exists, the route returns HTTP `200` with `currentSubscription: null`. It does not synthesize a Free plan.

The current snapshot also exposes `autoRenewEnabled`, `cancellationRequestedOnUtc`, and the stored
`currentPeriodEndsOnUtc` boundary, plus `renewalGraceEndsOnUtc` and
`lastRenewalFailedOnUtc` when a renewal has entered recovery. The Owner
can call `POST /api/v1/family/subscriptions/cancel-renewal` to disable renewal while preserving
current access, `isCurrent`, and all stored snapshot terms. A repeat cancellation returns `409`.
Before the current period ends, the Owner can call
`POST /api/v1/family/subscriptions/reenable-auto-renew` to restore auto-renew.
Lifecycle mutations use optimistic concurrency; a stale concurrent mutation returns the same
`409` conflict as an already-requested cancellation.

Unauthenticated requests receive `401`; authenticated requests failing the owner read rule receive `403`.

Published plans may remain visible after an administrator retires them from new sales. Retirement changes only `isAvailableForNewSales`; a family’s stored subscription snapshot keeps its original plan terms and benefits.
