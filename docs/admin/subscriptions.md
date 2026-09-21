# Admin subscriptions

Subscription plan authoring, publication, and retirement are non-billing catalog operations available only to a Super Admin with a normal JWT.

Create a draft plan version:

```text
POST /api/v1/admin/subscriptions/plans
```

The request supplies `key`, positive `version`, `price`, `cycle`, `currency` (`EGP`), every existing benefit key with its `isIncluded` value, `memberLimit`, `monthlyBookingLimit`, and `rollover`. The `(key, version)` pair is unique. The response is `201` with the new plan-version ID; invalid terms return `400` and a duplicate pair returns `409`.

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

This slice does not call Paymob and does not implement checkout, card or wallet processing, recurring billing, trials, coupons, VAT/tax, invoices, payment methods, proration, retries, grace periods, or allowance consumption. Existing family subscription snapshots remain immutable.
