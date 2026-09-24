# Family subscriptions and checkout quotes

Family subscription reads require a normal Family JWT and the `FamilyAccess` policy. Only the family Owner may use these routes; Editors, Viewers, non-family accounts, users without a family, and deleted-family owners receive `403`.

## Published plan catalog

`GET /api/v1/family/subscriptions/plans` returns every published plan version. Draft versions are excluded. Published versions retired from new sales remain visible, with `isAvailableForNewSales: false`.

Each item includes its key, version, tax-exclusive price, currency, billing cycle, rollover policy, member and monthly-booking limits, publication/creation timestamps, availability, and all benefit keys with their included state. Interpret these numeric JSON enum values as follows: `cycle` is `1` Monthly or `2` Annual; a benefit's `key` is `1` Chatting, `2` Library, `3` CommunityForum, `4` FamilyActivityTimeline, `5` BasicSearch, `6` AdvancedSearchFilters, `7` MedicalSummaryExportAndSecureSharing, or `8` PremiumContent, and `isIncluded` is the boolean state for that benefit. Each limit has `kind` `1` Finite with a positive integer `value`, or `2` Unlimited with `value: null`. `rollover` is `1` None or `2` NotApplicable. The catalog read does not add VAT/tax to the returned price.

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

## Plan changes

Plan changes are Owner-only. Editors, Viewers, non-family accounts, and
unauthenticated callers cannot use these routes. Plan-change requests do not
accept coupons.

`POST /api/v1/family/subscriptions/plan-change/quote` calculates an immediate
upgrade quote without charging or changing the subscription:

```json
{ "planVersionId": "0198e2c1-1111-7777-8888-000000000001" }
```

The target must be published and available for new sales. The response contains
`planVersionId`, `planKey`, `planVersion`, `targetRemainingGross`,
`settledCredit`, `taxRatePercentage`, `targetTaxAmount`, `totalPayable`, and
`currency`. The target gross and tax are the target plan's remaining-period
amounts. `settledCredit` is time-prorated from the actually settled
current-period gross amount; unpaid time earns no credit. `totalPayable` is
rounded to two decimals and floored at zero. The server calculates all values;
clients cannot override price, tax, credit, or currency.

`POST /api/v1/family/subscriptions/plan-change/payment-intent` starts an
immediate upgrade:

```json
{
  "planVersionId": "0198e2c1-1111-7777-8888-000000000001",
  "method": 1,
  "billing": {
    "firstName": "Ahmed",
    "lastName": "Ali",
    "email": "ahmed@example.com",
    "phoneNumber": "+201012345678"
  }
}
```

`method` is numeric: `1` Card or `2` Wallet. The response uses the normal
payment-intent fields (`paymentAttemptId`, `merchantReference`, `method`,
`amount`, `currency`, `clientSecret`, `publicKey`, and
`recurringRenewalSupported`). Wallet remains one-time/manual. For Card, a
successful immediate upgrade updates the existing Paymob subscription's future
recurring gross amount; it does not create a parallel subscription or re-enroll
the card. A zero-charge upgrade performs no provider charge, still updates the
Card future amount before applying the local target snapshot, and returns
`amount: 0` with empty provider secret/key fields. Local finalization occurs
only after required payment/provider work succeeds.

`PUT /api/v1/family/subscriptions/pending-downgrade` schedules or replaces a
lower-priced plan for the next successful renewal:

```json
{ "planVersionId": "0198e2c1-1111-7777-8888-000000000002" }
```

Success is `204`. `DELETE /api/v1/family/subscriptions/pending-downgrade`
cancels it and also returns `204`. For Card, schedule/replace synchronizes the
existing provider subscription to the target recurring gross amount; cancel
restores the current recurring gross amount. Wallet has no provider update. A
pending downgrade is applied only after a successful renewal at the original
period boundary and is not prorated. A successful upgrade clears it.

The `currentSubscription` object includes `pendingDowngrade` when present,
with `planKey`, `planVersion`, `price`, `cycle`, and `currency`; it is `null`
otherwise. Plan changes are unavailable during renewal grace or after
cancel-renewal has been requested. Replacing an existing pending downgrade is
supported.

Typical outcomes are `401` unauthenticated, `403`
`Subscriptions.PlanChange.NotOwner`, `404`
`Subscriptions.PlanChange.NotFound` or `Subscriptions.PlanChange.PlanNotFound`,
and `409` for a non-upgrade/non-downgrade target, renewal grace, cancelled
renewal, unavailable tax/provider state, or another invalid lifecycle
transition. Provider failures return `502` (`Paymob.GatewayError`) and missing
provider configuration returns `503` (`Paymob.NotConfigured`). A failed Card
provider amount update leaves local pending state unchanged and is safe to
retry; do not replay payment or downgrade mutations against shared data
without an approved fixture.

## Subscription invoices

Successful initial purchases and successful renewals create one immutable
server-side invoice using the fixed Sanad Care branded PDF template. Failed,
pending, duplicate, and grace-expired callback events do not create invoices.
Invoice numbers use `INV-YYYY-MM-######`; the stored record includes the plan,
period, subtotal, discount, tax, total, currency, issue time, and private PDF
storage key. Duplicate settlement is idempotent and cannot create a second
invoice for the same payment attempt or provider event.

The invoice routes are Owner-only:

- `GET /api/v1/family/subscriptions/invoices` lists invoice number, kind,
  amount, currency, issue time, and covered period.
- `GET /api/v1/family/subscriptions/invoices/{invoiceId}` returns the immutable
  invoice metadata.
- `GET /api/v1/family/subscriptions/invoices/{invoiceId}/pdf` downloads the
  private `application/pdf` document as `{invoiceNumber}.pdf`.

These routes return `401` without authentication, `403` for a non-owner, and
`404` for an invoice outside the owner's family or a missing PDF. Invoice
generation is part of successful settlement and does not send email. A finite
monthly booking allowance is consumed only after the caregiver successfully
completes an `InProgress` booking; cancelled, declined, expired, unpaid,
duplicate, and failed completion attempts do not consume it. Consumption resets
after successful renewal, unlimited plans do not increment usage, and an
exhausted allowance rejects completion with `409 Bookings.AllowanceExceeded`.
Notifications remain a later application slice. Release packaging and deployment smoke verification are complete and documented in `docs/operations/deployment.md`.

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

When no current row exists, the route returns HTTP `200` with `currentSubscription: null`. It does not synthesize a Free plan. The stored benefit objects use the same numeric `key` mapping and boolean `isIncluded` state described for the published catalog; `cycle` and `rollover` use the same numeric mappings, and each member/monthly-booking limit is returned as `{ "kind": 1, "value": <positive integer> }` for Finite or `{ "kind": 2, "value": null }` for Unlimited.

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
