# Family subscriptions (read contract)

Family subscription reads require a normal Family JWT and the `FamilyAccess` policy. Only the family Owner may use these routes; Editors, Viewers, non-family accounts, users without a family, and deleted-family owners receive `403`.

## Published plan catalog

`GET /api/v1/family/subscriptions/plans` returns every published plan version. Draft versions are excluded. Published versions retired from new sales remain visible, with `isAvailableForNewSales: false`.

Each item includes its key, version, price, currency, billing cycle, rollover policy, member and monthly-booking limits, publication/creation timestamps, availability, and all benefit keys with their included state.

## Current subscription snapshot

`GET /api/v1/family/subscriptions/current` returns `{ "currentSubscription": ... }`. The value is selected only from the caller's family `FamilySubscriptions` rows where `isCurrent` is true. It includes the stored plan terms and owned benefits; it does not join the mutable catalog.

When no current row exists, the route returns HTTP `200` with `currentSubscription: null`. It does not synthesize a Free plan.

Unauthenticated requests receive `401`; authenticated requests failing the owner read rule receive `403`.

Published plans may remain visible after an administrator retires them from new sales. Retirement changes only `isAvailableForNewSales`; a family’s stored subscription snapshot keeps its original plan terms and benefits.
