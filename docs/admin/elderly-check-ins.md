# Admin Elderly check-ins

These are read-only operational routes. They use the existing `ElderlyMedicationOperationalRead` policy: a Normal-access `SuperAdmin` or `SupportAdmin` may read them. There are no Admin write routes.

All successful reads write an immutable payload-free operational access audit before querying the check-in result. If the audit write fails, the read does not continue. Deleted/inactive Families are excluded and an inaccessible detail is returned as not found, so the route fails closed. Invalid date ranges are rejected before the read and before the audit row is written.

## Routes

| Route | Query/path parameters | Result |
|---|---|---|
| `GET /api/v1/admin/elderly/check-ins` | `page` (default 1), `pageSize` (default 20; invalid/out-of-range values normalize to 20; max 100), optional `elderlyId`, `startDate`, `endDate`, `answer` | Paged list with `items`, `page`, `pageSize`, `totalCount` |
| `GET /api/v1/admin/elderly/check-ins/{checkInId}` | Check-in UUID | One check-in record |
| `GET /api/v1/admin/elderly/check-ins/timeline/{elderlyId}` | Required `startDate` and `endDate` | Chronological check-in records for the Elderly profile |
| `GET /api/v1/admin/elderly/check-ins/aggregate` | Optional `elderlyId`, `startDate`, `endDate` | `{ total, positive, negative }` counts |

When both dates are supplied, the range is inclusive, must not be reversed, and must span no more than 31 calendar days (a 31-day interval means `endDate - startDate < 31` because both endpoints are included). List and aggregate apply the optional Elderly/date/answer filters. Timeline requires the Elderly ID and both dates.

Each record exposes only `id`, `elderlyId`, `familyId`, `elderlyArabicName`, `elderlyEnglishName`, `answer`, `localDate`, `answeredAtLocalTime`, `answeredOnUtc`, and `actorUserId`. Timezone, phone/contact, address/location, clinical, medication, and other source-profile fields are intentionally excluded.

Error outcomes:

- `401` for an unauthenticated request; `403` for a non-operational role or non-Normal access.
- `400 Families.AdminCheckIn.InvalidDateRange` for a reversed or over-31-day range.
- `404 Families.AdminCheckIn.NotFound` for a missing check-in or a check-in whose linked Family is no longer active.

Admin check-in reads do not expose notification delivery, push/provider status, reminders, missed-check-in state, or Family current-status/history. Check-in persistence in Families and alert persistence in Notifications remain separate contexts with no atomic cross-context transaction; alert idempotency/retry is scoped to the negative-check-in fan-out boundary.
