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
- Overlapping windows (including the Saturday↔Sunday cyclic overnight case) are rejected.
- Empty schedule allowed while onboarding; an Active caregiver cannot remove all windows.

## Availability toggle

Only an **Active** caregiver can set himself available. Becoming available for a Medical caregiver additionally requires the mandatory certificates to be Verified and unexpired.

```http
POST /api/v1/caregiver/availability/available
Authorization: Bearer {{caregiverToken}}
```

- `200` — `availability` becomes `Available`.
- `409 Caregivers.Onboarding.NotActive` — caregiver is not Active (e.g. still Onboarding/PendingReview/Suspended), or mandatory certificates are not compliant.

```http
POST /api/v1/caregiver/availability/unavailable
```

Always allowed for an existing profile; sets `availability` to `Unavailable`.

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
