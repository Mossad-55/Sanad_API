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

Each record exposes only `id`, `elderlyId`, `familyId`, `elderlyArabicName`, `elderlyEnglishName`, `answer`, `localDate`, `answeredAtLocalTime`, `answeredOnUtc`, and `actorUserId`. `localDate` and `answeredAtLocalTime` are the elderly profile's own local values, derived from that profile's stored IANA time zone; `answeredOnUtc` is the same instant in UTC. Timezone, phone/contact, address/location, clinical, medication, and other source-profile fields are intentionally excluded.

Error outcomes:

- `401` for an unauthenticated request; `403` for a non-operational role or non-Normal access.
- `400 Families.AdminCheckIn.InvalidDateRange` for a reversed or over-31-day range.
- `404 Families.AdminCheckIn.NotFound` for a missing check-in or a check-in whose linked Family is no longer active.

Recorded-answer reads do not expose notification delivery, push/provider status, reminders, or Family current-status/history. The derived missed-check-in feed below is separate from those recorded-answer reads. Check-in persistence in Families and alert persistence in Notifications remain separate contexts with no atomic cross-context transaction; alert idempotency/retry is scoped to the negative-check-in fan-out boundary.

## Missed daily wellness check-ins (derived)

`GET /api/v1/admin/elderly/check-ins/missed` derives missing expected daily wellness answers. It uses the same `ElderlyMedicationOperationalRead` policy and the same payload-free audit (`ListMissedCheckIns`). Required `startDate` and `endDate` (inclusive, max 31 days, not reversed); optional `elderlyId`; `page` (default 1), `pageSize` (default 20, max 100). Response: `{ evaluationOnUtc, items, page, pageSize, totalCount }`; items contain only `elderlyId`, `familyId`, `localDate`, `dueOnUtc`. `localDate` is the elderly profile's own local calendar date, derived from that profile's stored IANA time zone; `dueOnUtc` is that local day's next-midnight deadline converted to UTC. No names, contact, clinical, medication or payment data.

Error outcomes:
- `400 Families.AdminCheckIn.InvalidDateRange` for reversed/over-31-day ranges or an end date of `9999-12-31` (its next-midnight deadline is not representable). Missing dates return standard model-validation `400` before audit.
- `409 Families.AdminCheckIn.IndeterminateEvaluation` when cohort/timezone/identity facts are unusable.
- `503 Families.AdminCheckIn.EvaluationUnavailable` when source evaluation is unavailable.

## OpenAPI contract

The list and aggregate reads publish their actual success schemas (`200`
paged `{ items, page, pageSize, totalCount }` of check-in records, and `200`
`{ total, positive, negative }`). Shared enum/ID conventions are in
[api-contract-conventions](api-contract-conventions.md).
