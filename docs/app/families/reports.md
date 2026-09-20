# Family reports

Visit reports are immutable, dated records attached to a completed booking. They are separate from
the elderly medical profile and ordinary family notes. Version 1 stores no photos and no structured
medical measurements.

## Access

The family report feed uses a normal Family JWT and the `FamilyAccess` policy. Owner, Editor, and
Viewer members may read reports. Access is the union of every active family membership belonging to
the caller; deleted families are excluded.

## Family feed

| Method | Route | Description |
|---|---|---|
| `GET` | `/api/v1/family/reports?type=visit` | Paged visit reports across the caller's active families |

Optional filters are `elderlyId`, `bookingId`, `page` (default `1`), and `pageSize` (default `50`,
maximum `50`). The response contains `items`, `page`, `pageSize`, and `totalCount`.

Each item includes the booking, family, elderly, and caregiver identity snapshot, caregiver type,
server-recorded arrival and departure, server report submission time, observed condition, activities,
notes, and the caregiver-recorded assessment:

| Value | Assessment |
|---|---|
| `1` | No immediate concern observed |
| `2` | Needs follow-up |
| `3` | Urgent concern |
| `4` | Not assessed |

## Caregiver submission

| Method | Route | Description |
|---|---|---|
| `POST` | `/api/v1/caregiver/bookings/{bookingId}/visit-report` | Submit one report for an assigned completed visit |

The body contains only `observedCondition`, `activities`, `notes`, and `assessment`:

```json
{
  "observedCondition": "Alert and comfortable.",
  "activities": "Assisted with the planned exercises.",
  "notes": "Family follow-up requested.",
  "assessment": 2
}
```

Each text field is optional, is trimmed, and is limited to 2,000 characters; at least one must be
non-blank. Assessment values must be `1` through `4`. The caregiver must be the caregiver assigned
to the booking, either Medical or Companion, and the booking must be `Completed` with recorded start
and completion timestamps. The report stores those attendance timestamps unchanged, so writing a
late report cannot extend the visit.

Identity and timestamps are assigned by the server. The request cannot supply a caregiver identity,
arrival/departure time, or submission time. A booking accepts only one report; the report is
append-only and cannot be edited or deleted.

## Error catalog

| Code | HTTP | When |
|---|---|---|
| `Reports.Visit.InvalidContent` | 400 | Blank/all-empty text, text over 2,000 characters, invalid assessment, or invalid timestamp data |
| `Reports.Visit.InvalidType` | 400 | The family feed was requested with a type other than `visit` |
| `Reports.Visit.AccessDenied` | 403 | The caller has no active family membership |
| `Reports.Visit.AlreadySubmitted` | 409 | A report already exists for the booking |
| `Bookings.NotFound` | 404 | The booking is unknown or is assigned to another caregiver |
| `Bookings.Domain.InvalidOperation` | 409 | The assigned booking has not completed attendance |
