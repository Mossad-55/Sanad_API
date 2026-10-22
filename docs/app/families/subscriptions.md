# Family subscriptions and checkout quotes

Family subscription reads require a normal Family JWT and the `FamilyAccess` policy. Only the family Owner may use these routes; Editors, Viewers, non-family accounts, users without a family, and deleted-family owners receive `403`.

## Published plan catalog

`GET /api/v1/family/subscriptions/plans` returns every published plan version. Draft versions are excluded. Published versions retired from new sales remain visible, with `isAvailableForNewSales: false`.

Each item includes its key, version, tax-exclusive price, currency, billing cycle, rollover policy, member and monthly-booking limits, publication/creation timestamps, availability, and all benefit keys with their included state. It also includes nullable `paymobSubscriptionPlanId`: a positive value enables initial Card enrollment when Paymob Card 3DS is configured; `null` means no initial enrollment and one-time/manual billing. Interpret these numeric JSON enum values as follows: `cycle` is `1` Monthly or `2` Annual; a benefit's `key` is `1` Chatting, `2` Library, `3` CommunityForum, `4` FamilyActivityTimeline, `5` BasicSearch, `6` AdvancedSearchFilters, `7` MedicalSummaryExportAndSecureSharing, or `8` PremiumContent, and `isIncluded` is the boolean state for that benefit. Each limit has `kind` `1` Finite with a positive integer `value`, or `2` Unlimited with `value: null`. `rollover` is `1` None or `2` NotApplicable. The catalog read does not add platform fee or tax to the returned price.

### Bilingual plan copy

Each catalog item also carries the optional bilingual copy authored for **that plan version**: `arabicName`, `englishName`, `arabicDescription`, and `englishDescription`. Each benefit carries the optional `arabicLabel` and `englishLabel` authored for that benefit on that same version. Every one of these fields is nullable and holds the authored value with surrounding whitespace trimmed; a whitespace-only value is stored as absent (`null`). The API never generates, translates, or back-fills copy. A plan version created before the copy existed, or one whose author supplied no copy, keeps `null` for all of these fields; treat `null` as "no authored copy for this version" and fall back to your own product copy for that plan. The copy belongs to the version, so two versions of the same plan key may return different names, descriptions, and benefit labels.

```json
{
  "key": "premium",
  "version": 2,
  "arabicName": "الباقة المميزة",
  "englishName": "Premium plan",
  "arabicDescription": "كل ما تتضمنه الباقة المميزة.",
  "englishDescription": "Everything the premium plan includes.",
  "benefits": [
    { "key": 1, "isIncluded": true, "arabicLabel": "محادثات", "englishLabel": "Chatting" },
    { "key": 2, "isIncluded": true, "arabicLabel": null, "englishLabel": null }
  ]
}
```

The copy is display-only: it never changes price, cycle, currency, member limit, monthly-booking limit, rollover, or benefit inclusion.

The approved AR/EN names, descriptions, and feature lists are an **authored Content deliverable**, entered through the admin plan-version fields; the API never generates the copy — or the prices, discounts, and benefits.

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
`taxRatePercentage`, `taxAmount`, `platformFeeRatePercentage`,
`platformFeeAmount`, `platformChargeRuleVersion`, `totalPayable`, `currency`,
`cycle`, and the selected plan identity. Prices are tax-exclusive in the catalog;
the quote applies the effective shared Finance rule only when its
`effectiveOnUtc` is at or before the quote time. Fee and tax are each calculated
on the discounted taxable amount and rounded independently to two decimals using
the server's banker's-rounding convention. Coupons must belong to the selected plan and be
unexpired. The quote response's `recurringRenewalSupported` value is not a
payment-method capability guarantee. This endpoint does not charge a payment method, activate a subscription, create
an invoice, consume an allowance, or mutate data.

Errors include `401` unauthenticated, `403` non-owner, `404`
`Subscriptions.Quote.PlanNotFound`, `400` `Subscriptions.Quote.CouponInvalid`,
and `409` `Subscriptions.Quote.TaxNotConfigured` when no effective shared Finance rule exists.

## Initial payment intent and settlement

`POST /api/v1/family/subscriptions/payment-intent` is Owner-only and starts the
initial checkout boundary for a published, available plan. The server reruns the
quote from the plan version, coupon, and effective shared Finance rule; clients
cannot provide or override the amount, fee, tax, discount, currency, or renewal
dates.

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

### Gateway rejection reasons

When Paymob rejects an intention, the `502 Paymob.GatewayError` error keeps its
existing message and appends a sanitized reason from the provider response:
`Paymob intention request failed with status <code>.; upstream: <reason>`. The
reason is the upstream `detail`, `message`, or `status` field, trimmed to a
single bounded line (≤300 characters). It reflects only those fields; a body
that is not JSON, contains no recognized reason field, or carries credential
material (`secret`/`token`- or `key`-shaped values, bearer headers, Paymob
secret prefixes) contributes nothing, and the description reverts to the
status-only text. Neither the request payload, the billing email/phone, nor the
Authorization token ever appears in the surfaced text.

The reason is intended for support diagnosis of integration, currency, and
plan-mapping rejections — for example a currency the selected integration does
not accept. It is the provider's own wording and is not a stable, localized, or
machine-parseable contract: show it as diagnostic detail only, never parse it
for control flow. Surfacing the reason does not change the status code, the
error code, or any other behavior; the upstream `4xx` root cause still has to be
diagnosed from the reason plus gateway configuration.

**Platform availability.** The initial payment intent is a new payment start,
so it is refused while the platform is in Maintenance (`503
Platform.Maintenance`), when the selected method is disabled or not configured
(`400 Platform.PaymentMethodUnavailable`), or when the platform controls cannot
be read (`503 Platform.ControlStateUnavailable`). The refusal happens before
any provider call and before the payment attempt is written. An initial Card
payment whose selected plan carries a provider subscription plan id also
requires the Paymob Card 3DS enrollment integration to be configured; without
it the request fails closed with `400 Platform.PaymentMethodUnavailable` rather
than a gateway error. Quotes, plan reads, renewals that already exist, and
settlement of an existing payment are unaffected. The app can read the current
state and the available methods from the public status route — see
[platform business status](../public/platform-status.md).

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

A renewal is a new payment start, so it is refused while the platform is in
Maintenance (`503 Platform.Maintenance`), when the selected method is disabled
or not configured (`400 Platform.PaymentMethodUnavailable`), or when the
platform controls cannot be read (`503 Platform.ControlStateUnavailable`).
The refusal happens before the renewal attempt is written. Renewal of an
already-created payment and settlement of an existing renewal callback are
unaffected.

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
`409 Subscriptions.PlanChange.NotUpgrade` when an immediate-payment target is
not higher priced, `409 Subscriptions.PendingDowngrade.NotDowngrade` when a
scheduled target is not lower priced, and `409` for renewal grace, cancelled
renewal, unavailable shared Finance/provider state, or another invalid lifecycle
transition. A missing effective shared Finance rule is `409
Subscriptions.Quote.TaxNotConfigured`. Provider failures return `502`
(`Paymob.GatewayError`) and missing
provider configuration returns `503` (`Paymob.NotConfigured`). A failed Card
provider amount update leaves local pending state unchanged and is safe to
retry; do not replay payment or downgrade mutations against shared data
without an approved fixture.

The plan-change payment intent goes through the same Paymob intention boundary,
so its `502 Paymob.GatewayError` carries the same sanitized upstream reason
described under [Gateway rejection reasons](#gateway-rejection-reasons).

An immediate plan-change payment intent is a new payment start, so it is refused
while the platform is in Maintenance (`503 Platform.Maintenance`), when the
selected method is disabled or not configured (`400
Platform.PaymentMethodUnavailable`), or when the platform controls cannot be
read (`503 Platform.ControlStateUnavailable`). The plan-change **quote** and
the pending-downgrade replace/cancel lifecycle are reads and existing state
changes, not new payment starts, so they remain available during Maintenance.

### Local Development no-provider downgrade fixture

When the opt-in `App:TestUserSeed` runs in Development, it also seeds a
dedicated family owner with a current Premium v1 subscription that has no
Paymob subscription ID. This fixture exercises scheduling a lower-priced Free
plan, reading the pending downgrade, cancelling it, and confirming the current
Premium snapshot remains unchanged. Because the fixture has no provider
subscription association, those operations do not call Paymob. It is test-only
state and must never be enabled with a live Paymob key.

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
Push and email notifications are complete. Release packaging and deployment smoke verification are documented in `docs/internal/operations/deployment.md`.

## Card enrollment boundary

Initial Card enrollment is available only when the selected local plan has a
positive `paymobSubscriptionPlanId` and `Paymob__Card3dsIntegrationId` is
configured. The server sends that local mapping as Paymob's
`subscription_plan_id` and selects the Card 3DS integration. Wallet remains
one-time/manual. Provider subscription identity persistence and renewal-event
settlement are implemented through the callback contract above; wallet renewal
continues through the explicit manual payment-intent route because wallet
recurrence is not claimed.

Clients must combine this with the anonymous `GET /api/v1/platform/status`
response to decide Card usability per plan: see the
[platform status consumption contract](../public/platform-status.md#subscription-card-consumption-contract).
A new intent is still refused with `400 Platform.PaymentMethodUnavailable`
when the selected method is unavailable.

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

## Subscription cancellation, refunds, and shutdown

Clause 9 defines three economically different subscription money outcomes, and they must stay
distinguishable. All three are recorded as movements in the shared, append-only Finance ledger
(the movement, never a resulting balance); the `reason` in each response is part of the answer,
not decoration.

| Outcome | Trigger | Amount |
| --- | --- | --- |
| Cancellation (`CancelRenewal`, `1`) | Owner disables auto-renew | Nothing for the current period; access continues to the period end |
| Duplicate charge (`DuplicateCharge`, `2`) | An erroneous or duplicate charge | The whole captured amount, refunded in full — never prorated |
| Service shutdown (`ServiceShutdown`, `3`) | Ahlena service shutdown | The unused portion of the current paid period |

These rules apply to a family premium subscription and to a care-home listing subscription
alike; the two scopes carry the same clause-9 money rules.

### Cancel-renewal

`POST /api/v1/family/subscriptions/cancel-renewal` is Owner-only and returns `204`. The request
body is optional:

```json
{ "idempotencyKey": "cancel-0198e2c1-1111-7777-8888-000000000001" }
```

When an `idempotencyKey` is supplied, the cancel-renewal outcome is recorded through shared
Finance so the cancellation is attributable and distinct from the other two reasons. A repeat
cancellation **without** a key still returns `409 Subscriptions.CancelRenewal.AlreadyRequested`.
With a key, however, an already-cancelled renewal is not short-circuited: the recorder is
idempotent and conflict-detecting, so replaying the same key with the same content returns the
recorded outcome and reusing the same key with **different** content returns `409
Subscriptions.Refund.IdempotencyConflict`. Omitting the key keeps the lifecycle exactly as before
and records no movement.

### Duplicate charge

`POST /api/v1/family/subscriptions/refunds/duplicate-charge` is Owner-only and refunds the full
captured amount of one succeeded subscription payment attempt:

```json
{
  "paymentAttemptId": "0198e2c1-2222-7777-8888-000000000001",
  "idempotencyKey": "dup-0198e2c1-2222-7777-8888-000000000001",
  "evidenceReference": "Paymob double capture 000123"
}
```

The response is the recorded outcome:

```json
{
  "refundCaseId": "0198e2c1-3333-7777-8888-000000000001",
  "awardedMinorUnits": 15000,
  "currency": "EGP",
  "outcome": 2,
  "isReplay": false
}
```

`PaymentAttemptId` must be a succeeded attempt belonging to the caller's family. `recordedOnUtc` is
an optional audit instant (defaults to now) and must be UTC. Errors are `403
Subscriptions.Refund.NotOwner`, `404 Subscriptions.Refund.NothingToRefund` (a missing or
not-succeeded attempt), `400 Subscriptions.Refund.MalformedInput` (a non-UTC `recordedOnUtc`, or an
attempt with no server-side subscription link and therefore no refundable capture to claim against),
and `409 Subscriptions.Refund.IdempotencyConflict` when the same key is reused with different content.
`Subscriptions.Refund.ExceedsCaptured` is returned when the full award no longer fits what that
attempt captured.

### Service shutdown

An Ahlena service shutdown is a platform decision, not a family self-service action, so it is
exposed to a SuperAdmin or SupportAdmin (the refund-workflow policy) and not on the family
routes:

`POST /api/v1/admin/finance/subscription-refunds/service-shutdown`

```json
{
  "subscriptionId": "0198e2c1-1111-7777-8888-000000000001",
  "shutdownOnUtc": "2026-10-15T00:00:00Z",
  "idempotencyKey": "shutdown-0198e2c1-1111-7777-8888-000000000001",
  "evidenceReference": "Ahlena shutdown notice 2026-10-15"
}
```

The prorated award is the unused portion of the current paid period — the time remaining from
`shutdownOnUtc` to the stored period end, as a ratio of the whole period, applied to the
**consideration settled for that period**: the subscription's own current-period charge snapshot,
which the initial capture and every renewal maintain and which a paid plan change updates to the
target period's gross with the prorated credit already folded in. Neither an all-time sum of renewals
nor the plan's list price is used, and a paid plan change is never added on top of the period it was
credited against. The caller supplies no money figures. The award is banker's-rounded (ToEven) and
floored at zero. A shutdown at or after the period end may legitimately compute exactly zero, and
the zero award is recorded as computed rather than rounded up. The `outcome` is always `3`
(`ServiceShutdown`). A non-UTC `shutdownOnUtc` is
`400 Subscriptions.Refund.MalformedInput`; a shutdown for a subscription that is not current returns
`409 Subscriptions.Refund.NotCurrent`; a missing subscription returns
`Subscriptions.Refund.SubscriptionNotFound`; a paid period that settled nothing returns
`404 Subscriptions.Refund.NothingToRefund`; and a current period whose paying attempt cannot be
resolved returns `Subscriptions.Refund.CaptureNotResolved` rather than falling back to a lifetime
total or an arbitrary latest attempt.

### Refundable budget per capture

All clause-9 cases that draw on one paid period share a single refundable budget, owned by Finance and
keyed by the **capture** — the succeeded payment attempt that paid for that period. A
duplicate-charge case is anchored on the very attempt it corrects; a shutdown case is anchored on the
attempt that settled inside the current paid period. Both claim against that one row, so the same
captured money cannot be refunded twice — in either order, and under different idempotency keys.
Same-key uniqueness alone would not serialize them, which is why the budget exists.

The scope is the capture because the ceiling is the money paid for *that* period. A budget keyed by
the subscription and seeded with lifetime captures would let money already consumed in an older
period supply headroom for the current one: refunding a current-period duplicate charge and then half
of the same current period would "fit" under a lifetime total while refunding the current period
twice. `subscription_id` is kept as an indexed audit column, so all of a subscription's budgets
remain findable for reconciliation without widening any one ceiling.

An approval is claimed once, as in-flight exposure; completing the transfer is the same money moving,
so it is never claimed a second time. The cumulative claimed total is checked at the Finance
persistence boundary in the same unit of work as the movement, against a single capture row carrying
an optimistic concurrency token: concurrent claims resolve to one winner and the loser retries. An
award that would exceed what the capture paid is refused with
`Subscriptions.Refund.ExceedsCaptured` rather than silently trimmed, because a trimmed duplicate charge
would no longer be a full refund. The caller never supplies a prior-refund offset or the captured
amount.

Cancel-renewal records a zero-piastre movement, so it claims no budget and names no capture.

Out of scope for that budget: a quality-complaint award. It is scoped to its own complaint case, a
case accepts at most one funded award, and it is recorded through the complaint-review workflow.
Whether complaint compensation outside a specific capture should be capped remains an Owner decision.

### Idempotency

Callers pass an idempotency key (at most 200 characters). Shared Finance enforces uniqueness:
replaying the same key **with the same content** returns the original recorded outcome with
`isReplay: true`; reusing the same key with **different content** returns `409
Subscriptions.Refund.IdempotencyConflict`. The refund case is derived deterministically from the
corrected source — the payment attempt for a duplicate charge, the subscription for a shutdown or
a cancel-renewal — so a retry can never open a second refund case for one charge.

Unauthenticated requests receive `401`; authenticated requests failing the owner read rule receive `403`.

Published plans may remain visible after an administrator retires them from new sales. Retirement changes only `isAvailableForNewSales`; a family’s stored subscription snapshot keeps its original plan terms and benefits.
