# Caregiver earnings and medication tasks

## Family-facing availability

`GET /api/v1/caregivers/{caregiverId}/availability` requires `FamilyAccess`.
It returns the caregiver’s published availability and schedule for discovery;
the caller does not need to own the caregiver profile. Schedule times are
caregiver-local and must be displayed with the associated time-zone metadata.

All routes require `CaregiverAccess`. The authenticated caregiver must own the
`{caregiverId}` in the route; requests for another caregiver return a not-found
result rather than exposing their data.

## Earnings

- `GET /api/v1/caregivers/{caregiverId}/earnings/summary`
- `GET /api/v1/caregivers/{caregiverId}/earnings/transactions?page=1&pageSize=20`

Earnings are currently an estimate based on the booking’s `BaseCaregiverFee` for
completed bookings. This is not a payout ledger or proof that money has been
transferred. `EarningsThisPeriod` is the sum of fees for bookings completed in
the current UTC calendar month. The transactions route lists completed bookings
only; page is clamped to at least 1 and page size to 1–100.

## Medication task list

`GET /api/v1/caregivers/{caregiverId}/medication-tasks` supports `status`,
`startDate`, and `endDate`. Results include scheduled local date/time and the
elderly person’s `TimeZoneId`; do not interpret that date/time as UTC. Tasks are
limited to elderly people linked to the caregiver by a `Confirmed` or
`InProgress` booking.

## Record a dose

- `POST /api/v1/caregivers/{caregiverId}/medication-tasks/{taskId}/administer`
- `POST /api/v1/caregivers/{caregiverId}/medication-tasks/{taskId}/skip`

Administer has no request body. Skip requires `{ "reason": "..." }` with 1–500
non-whitespace characters. Both actions verify the caregiver owns the profile
and has an active booking for the dose’s elderly person, then update the tracked
Families dose log and save through the Families context. A missing task returns
`404 Caregivers.MedicationTask.NotFound`; an out-of-scope task returns
`403 Caregivers.AccessDenied`; an invalid transition returns
`409 Caregivers.MedicationTask.InvalidOperation`.

These are state-changing operations. Run them only with an approved disposable
fixture; do not replay against live medication schedules.

## Help-request transitions

The caregiver self-service routes under `/api/v1/caregiver/help-requests` are:

- `GET /help-requests` and `GET /help-requests/{requestId}` — list/detail for
  requests involving an elderly person with an active caregiver booking.
- `POST /help-requests/{requestId}/accept` — accepts a pending request.
- `POST /help-requests/{requestId}/decline` — declines it and requires
  `{ "reason": "..." }`.
- `POST /help-requests/{requestId}/start` — starts an accepted request.
- `POST /help-requests/{requestId}/resolve` — resolves an in-progress request
  and requires a reason.

The authenticated caregiver is taken from the token. Transition history is
append-only; invalid lifecycle transitions return a conflict and foreign or
out-of-scope requests are not disclosed.
