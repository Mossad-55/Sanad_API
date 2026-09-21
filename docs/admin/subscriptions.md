# Admin subscriptions

Subscription plan retirement is a non-billing catalog operation available only to a Super Admin with a normal JWT.

```text
POST /api/v1/admin/subscriptions/plans/{planVersionId}/retire
```

The plan must be published and available for new sales. Retirement sets `isAvailableForNewSales` to `false`; it does not change the published plan terms or existing family subscription snapshots. Missing plans return `404`. Draft/unpublished and already-retired plans return `409`.

Each successful retirement appends an immutable audit record containing the plan version ID, plan key/version snapshot, actor user ID, actor role (`SuperAdmin`), old/new availability, and the UTC retirement timestamp. The plan mutation and audit insert are committed by one save operation.
