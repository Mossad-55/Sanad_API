# Weekly schedule and availability

All routes require the `CaregiverAccess` policy. Bulk schedule replaces are full-set (the screen posts the complete weekly plan); the domain validates every rule.

## Medical schedule

Medical caregiver accounts only. A week consists of **shifts** (one template per day) and/or **home-visit windows**; a single day is either a shift day or a home-visit day, never both.

```http
PUT /api/v1/caregiver/schedule/medical
Content-Type: application/json
Authorization: Bearer {{caregiverToken}}

{
  "shifts": [
    { "dayOfWeek": 0, "shiftType": 1 },
    { "dayOfWeek": 2, "shiftType": 4 }
  ],
  "homeVisitWindows": [
    { "dayOfWeek": 1, "startTime": "09:00", "endTime": "12:00" }
  ]
}
```

- `shiftType`: `1` EightHourMorning (08:00–16:00), `2` EightHourEvening (16:00–00:00), `3` EightHourNight (00:00–08:00), `4` TwelveHourDay (08:00–20:00), `5` TwelveHourNight (20:00–08:00), `6` TwentyFourHourLiveIn (08:00, 24h).
- `dayOfWeek`: `0` Sunday … `6` Saturday.
- Home-visit windows must end after they start and never cross midnight; windows/shifts on different days are checked for cyclic (overnight) overlap.
- Two shifts on the same day, or a shift plus a home-visit window on the same day, is rejected.
- Empty schedule is allowed while onboarding; an **Active** caregiver cannot clear his entire schedule.

Errors:

- `409 Caregivers.Onboarding.InvalidSchedule` — overlap, duplicate-day shift, shift/window mix on a day, or an Active caregiver removing all availability.
- `409 Caregivers.Onboarding.WrongCaregiverType` — caller is a Companion account.
- `400 Api.Validation.Failed` — bad enum values or a window whose end ≤ start.
- `401` — unauthenticated; `403` — authenticated account does not satisfy `CaregiverAccess`; `404 Caregivers.Onboarding.NotFound` — no caregiver profile.

## Companion schedule

Companion caregiver accounts only. Availability windows are typed by booking mode.

```http
PUT /api/v1/caregiver/schedule/companion
Content-Type: application/json

{
  "windows": [
    { "bookingType": 1, "dayOfWeek": 0, "startTime": "10:00", "endTime": "14:00" },
    { "bookingType": 3, "dayOfWeek": 4, "startTime": "20:00", "endTime": "08:00" }
  ]
}
```

- `bookingType`: `1` Hourly, `2` EightHourDay (must be exactly 8 hours, no overnight), `3` Overnight (fixed 20:00–08:00).
- Overnight is the one cross-midnight exception to the request-level end-after-start check; the domain accepts only the fixed 20:00–08:00 window. Equal endpoints remain invalid.
- Overlapping windows (including the Saturday↔Sunday cyclic overnight case) are rejected.
- Empty schedule allowed while onboarding; an Active caregiver cannot remove all windows.

Errors:

- `400 Api.Validation.Failed` — invalid enum values, equal endpoints, or an end time not after the start for Hourly/EightHourDay windows.
- `409 Caregivers.Onboarding.InvalidSchedule` — invalid booking-type duration/window, overlap, or an Active caregiver removing all availability.
- `409 Caregivers.Onboarding.WrongCaregiverType` — caller is a Medical account.
- `401` — unauthenticated; `403` — authenticated account does not satisfy `CaregiverAccess`; `404 Caregivers.Onboarding.NotFound` — no caregiver profile.

## Availability toggle

Only an **Active** caregiver can set himself available. Becoming available for a Medical caregiver additionally requires the mandatory certificates to be Verified and unexpired.

```http
POST /api/v1/caregiver/availability/available
Authorization: Bearer {{caregiverToken}}
```

- `200` — `availability` becomes `Available`.
- `409 Caregivers.Onboarding.NotActive` — caregiver is not Active (e.g. still Onboarding/PendingReview/Suspended), or mandatory certificates are not compliant.
- `401` — unauthenticated; `403` — authenticated account does not satisfy `CaregiverAccess`; `404 Caregivers.Onboarding.NotFound` — no caregiver profile.

```http
POST /api/v1/caregiver/availability/unavailable
```

Always allowed for an existing profile; sets `availability` to `Unavailable`.
Returns `200` with the full caregiver profile; unauthenticated requests return `401`, callers outside `CaregiverAccess` receive `403`, and a missing profile returns `404 Caregivers.Onboarding.NotFound`.

Note: editing the professional profile, replacing a mandatory certificate file, or a certificate rejection/revocation while Active also forces `Unavailable` (and profile/certificate changes re-enter review).

## Response excerpt

```json
{
  "medicalSchedule": {
    "shifts": [ { "dayOfWeek": 0, "shiftType": 1 } ],
    "homeVisitWindows": [ { "dayOfWeek": 1, "startTime": "09:00", "endTime": "12:00" } ]
  },
  "companionSchedule": null,
  "availability": 2
}
```

Schedule availability (`hasAvailability`) is part of the submit readiness checklist.

## Date-specific availability (family read)

Additive family-facing read; the weekly routes and their DTOs above are
unchanged. The family app uses it to show a caregiver's real offer for one day.

```http
GET /api/v1/caregivers/{caregiverId}/availability/day?date=2026-06-08&shiftType=EightHourShift
Authorization: Bearer {{familyToken}}
```

Policy `FamilyAccess`. `shiftType` accepts `HomeVisit`, `EightHourShift`,
`TwelveHourShift`, `TwentyFourHourShift` or `Hourly` (enum name, case-insensitive).

```json
{
  "caregiverId": "0198e2c2-2222-7777-8888-000000000002",
  "date": "2026-06-08",
  "timeZoneId": "Africa/Cairo",
  "shiftType": 2,
  "intervals": [
    { "startsAt": "2026-06-08T05:00:00+00:00", "endsAt": "2026-06-08T13:00:00+00:00" }
  ]
}
```

- `timeZoneId` is the documented display zone `Africa/Cairo`; the caregiver's
  stored weekly windows are read in that zone. `startsAt`/`endsAt` are UTC
  instants (`+00:00`); stored UTC timestamps are never relabeled.
- Mapping: `HomeVisit` → medical home-visit windows; `EightHourShift` → 8-hour
  medical shifts or companion `EightHourDay`; `TwelveHourShift` → 12-hour
  medical shifts or companion `Overnight` (20:00→08:00); `TwentyFourHourShift` →
  `TwentyFourHourLiveIn`; `Hourly` → companion `Hourly`. A day cannot mix a
  shift with home-visit windows.
- Active booking overlaps (`PendingPayment`, `PendingCaregiverApproval`,
  `Confirmed`, `InProgress`) are removed with the same predicate checkout uses:
  `start < busy.endTime && end > busy.startTime` over the raw stored day times.
  Fixed-duration shifts are offered whole or dropped entirely; `Hourly` and
  `HomeVisit` windows subtract only the overlapping portion.
- **Cross-midnight gap:** because that predicate compares raw day times, a busy
  booking inside a window that crosses midnight is not detected. The read
  mirrors the existing checkout behaviour rather than silently diverging; the
  gap is not fixed in this slice.
- DST: an interval whose boundary falls in a spring-forward gap is omitted; an
  ambiguous fall-back boundary resolves deterministically to the standard offset.
- An inactive caregiver or one whose availability is manually `Unavailable`
  answers `200` with `intervals: []`. No date-specific leave entity exists.
- The response is advisory, not a reservation. Caregiver bookings carry an
  acceptance deadline, not an availability hold, and no hold/expiry rule is
  invented here.
- Errors: `400 Caregivers.Availability.InvalidQuery` (missing/invalid `date` or
  `shiftType`), `404 Caregivers.NotFound`, `401` unauthenticated, `403` outside
  `FamilyAccess`.
- Checkout reuses this rule set inside its serialized reservation check. A slot
  outside the returned intervals is `409 Bookings.AvailabilityConflict`; an
  overlapping booking stays `409 Bookings.ScheduleConflict`.
