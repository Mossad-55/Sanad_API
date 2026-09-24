# Admin subscriptions

Subscription plan authoring, publication, retirement, and administrative reads are available only to a Super Admin with a normal JWT. Administrative reads expose both the configured plan catalog and the family subscription records created from immutable plan snapshots.

## Administrative reads

List every family subscription, including inactive historical snapshots:

```text
GET /api/v1/admin/subscriptions?page=1&pageSize=10
```

The paged response includes the subscription ID, family ID/name, owner user ID, stored plan terms and benefits, current-state flag, auto-renew state, cancellation request timestamp, period end, renewal grace/failure timestamps, and creation timestamp. Retrieve one record with:

```text
GET /api/v1/admin/subscriptions/{subscriptionId}
```

List every plan version, including drafts, published versions, and retired-for-new-sales versions:

```text
GET /api/v1/admin/subscriptions/plans?page=1&pageSize=10
GET /api/v1/admin/subscriptions/plans/{planVersionId}
```

Plan reads include publication and availability state, immutable terms, benefits, and timestamps. These reads are safe and do not change configuration or family subscriptions.

Create a draft plan version:

```text
POST /api/v1/admin/subscriptions/plans
```

The request supplies `key`, positive `version`, `price`, `cycle`, `currency` (`EGP`), every existing benefit key with its `isIncluded` value, `memberLimit`, `monthlyBookingLimit`, `rollover`, and optional `paymobSubscriptionPlanId`. The `(key, version)` pair is unique. The response is `201` with the new plan-version ID; invalid terms return `400` and a duplicate pair returns `409`.

`paymobSubscriptionPlanId` is the local plan's Paymob subscription-plan mapping. A positive value enables the initial Card enrollment path when the Paymob Card 3DS integration is configured; omit it for plans that must use one-time/manual billing. Plan list/detail reads return the nullable field so administrators can verify the effective mapping. This field is not a persisted provider subscription ID.

Publish a draft exactly once:

```text
POST /api/v1/admin/subscriptions/plans/{planVersionId}/publish
```

Publishing sets `isPublished` and `publishedOnUtc` once and returns `204`. Missing plans return `404`; already-published plans and publication races return `409`. Published terms and benefits cannot be edited.

```text
POST /api/v1/admin/subscriptions/plans/{planVersionId}/retire
```

The plan must be published and available for new sales. Retirement sets `isAvailableForNewSales` to `false`; it does not change the published plan terms or existing family subscription snapshots. Missing plans return `404`. Draft/unpublished and already-retired plans return `409`.

Each successful retirement appends an immutable audit record containing the plan version ID, plan key/version snapshot, actor user ID, actor role (`SuperAdmin`), old/new availability, and the UTC retirement timestamp. The plan mutation and audit insert are committed by one save operation.

## Coupon configuration

Coupon configuration uses the same `SubscriptionPlanAdmin` policy as plan authoring: a normal JWT
whose `account_type` is `SuperAdmin`. Content Admin, Support Admin, family, and caregiver tokens
receive `403`; unauthenticated requests receive `401`. These are configuration endpoints only; the create and delete requests below are
manual/destructive examples and should not be replayed against shared data without an owner-approved
plan/coupon fixture.

```text
POST   /api/v1/admin/subscriptions/coupons
GET    /api/v1/admin/subscriptions/coupons
GET    /api/v1/admin/subscriptions/coupons/{couponId}
DELETE /api/v1/admin/subscriptions/coupons/{couponId}
```

Create body:

```json
{
  "code": "WELCOME10",
  "subscriptionPlanVersionId": "0198e2c1-1111-7777-8888-000000000001",
  "discountPercentage": 10.00,
  "expiresOnUtc": "2026-12-31T23:59:59Z"
}
```

The target ID must refer to a published plan version that is still available for new sales. The
response is `201` with the created coupon UUID. Codes are trimmed, normalized to uppercase, and
unique case-insensitively. Percentages must be from `1.00` through `100.00`; expiry must be a UTC
timestamp after creation. `GET` list returns `200` with coupons ordered by code; `GET` detail returns
`200` with `id`, `code`, `planVersionId`, `discountPercentage`, `expiresOnUtc`, and `createdOnUtc`.
`DELETE` returns `204` and is a hard delete; an unknown coupon returns `404`.

Create errors: `400 Subscriptions.Coupon.Invalid` for invalid terms, `404
Subscriptions.Coupon.PlanNotFound` for a missing, unpublished, or retired target plan, and `409
Subscriptions.Coupon.DuplicateCode` for a duplicate code. Detail/delete use `404
Subscriptions.Coupon.NotFound` for an unknown coupon.

Each coupon is one-time and globally non-stackable; redemption and consumption are a later slice.
Coupons are configuration records, so deleting one does not rewrite existing transactions.

## VAT / tax-rule configuration

Subscription plan prices are stored and exposed as tax-exclusive EGP amounts. VAT/tax
configuration is now available as a Super Admin-only configuration surface. These routes
require a normal JWT with the `SubscriptionPlanAdmin` policy; Content Admin, Support Admin,
family, and caregiver tokens receive `403`, while unauthenticated requests receive `401`.

```text
POST /api/v1/admin/subscriptions/tax-rules
GET  /api/v1/admin/subscriptions/tax-rules/current
GET  /api/v1/admin/subscriptions/tax-rules/history
```

Create body:

```json
{
  "ratePercentage": 14.00,
  "version": 1,
  "effectiveOnUtc": "2026-10-01T00:00:00Z"
}
```

The create response is `201` with the new rule UUID. `ratePercentage` is inclusive from
`0` through `100` and is rounded to two decimals; `version` must be positive and
`effectiveOnUtc` must be UTC. A version is unique (`409
Subscriptions.Tax.DuplicateVersion`). Creating a new rule deactivates the prior active
rule while retaining it in history. A concurrent active-rule change returns `409
Subscriptions.Tax.ActiveConflict`; invalid values return `400 Subscriptions.Tax.Invalid`.

The current route returns `200` with the active rule, or JSON `null` when no rule exists.
The history route returns `200` with all rules ordered by descending `version`. Rule
objects contain `id`, `ratePercentage`, `version`, `effectiveOnUtc`, `createdOnUtc`, and
`isActive`.

Tax-rule configuration is consumed by the family-owner subscription quote and
initial payment-intent routes when a rule is active and effective. The payment
intent reruns the quote server-side and returns a provider client secret; the
Paymob callback settles the subscription-specific `sub_` reference and
activates the plan only after a matching successful payment. Subscription
callbacks use `subscription_data.id`, `trigger_type`, body `hmac`, and optional
`initial_transaction`, `amount_cents`, `state`, and `next_billing`; renewal
callbacks require top-level `paymob_request_id`. HMAC-SHA512 covers
`{trigger_type}for{subscription_data.id}`. The accepted trigger spellings are
`CREATED`, `Subscription Created`, `Successful Transaction`, `Failed
Transaction`, and `Failed Overdue Transaction`.

Provider subscription identities are durably stored in the shared registry and
callback event identities are append-only ledger records. Renewal settlement
preserves the seven-day grace and original period-end anchor. A successful
callback after grace expiry is durably acknowledged with HTTP `200` and does
not mutate successful-renewal state. Unknown identities and duplicate events
are acknowledged without a second mutation. This callback contract does not
create an invoice or PDF. Recurring wallet billing, trials, redemption,
upgrades/downgrades, proration, allowance consumption, notifications, and
deployment automation remain separate slices. Initial Card enrollment is
available only when the local plan has `paymobSubscriptionPlanId` and Paymob
Card 3DS is configured; Wallet remains one-time/manual. The current family
renewal endpoint uses the manual payment-intent boundary and preserves the
original renewal anchor after a successful retry.
