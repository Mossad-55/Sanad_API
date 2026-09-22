# Family reports

Visit reports are immutable, dated records attached to a completed booking. They are separate from
the elderly medical profile and ordinary family notes. Visit reports store no photos. Medical Reports
are a separate Medical-caregiver-only report type.

## Access

The family report feed uses a normal Family JWT and the `FamilyAccess` policy. Owner, Editor, and
Viewer members may read reports. Access is the union of every active family membership belonging to
the caller; deleted families are excluded.

## Family feed

| Method | Route | Description |
|---|---|---|
| `GET` | `/api/v1/family/reports?type=visit` | Paged visit reports across the caller's active families |
| `GET` | `/api/v1/family/reports?type=medical` | Paged medical reports |
| `GET` | `/api/v1/family/reports?type=all` | Common envelope containing both report types |

Optional filters are `elderlyId`, `bookingId`, `page` (default `1`), and `pageSize` (default `50`,
maximum `50`). The response contains `items`, `page`, `pageSize`, and `totalCount`.

This is a safe authenticated read for Family Owner, Editor, or Viewer members;
it returns `200` for an empty page. The private photo route is also a safe read
and streams the original image inline with `200`; it returns `403` without
family access and `404` when the report has no photo.

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

## Medical Reports V1 — mobile photo-consent contract

Medical Reports are submitted only by an assigned Medical caregiver for a completed booking. A
Medical caregiver may submit both a Visit Report and a Medical Report for the same booking. A
Companion caregiver cannot submit a Medical Report.

The Medical Report request supports structured measurements (blood pressure, pulse, and
temperature), measurement time, assessment, notes, and one optional original photo. Missing
measurements remain missing; the API must not convert them to zero. The photo is optional, and the
report remains valid when no photo is supplied.

The mobile application must show a caregiver-only attestation checkbox immediately before capturing
or uploading an optional medical photo. The elderly person and family do not participate in V1:

1. The caregiver opens the Medical Report and taps **Add medical photo**.
2. Before opening the camera or selecting an existing image, the caregiver checks the attestation.
3. If the checkbox is confirmed, the app sends the photo with the Medical Report submission and sets
   `photoConsentConfirmed` to `true`.
4. If consent is refused or unavailable, the app submits the Medical Report without a photo. It
   must not upload the image.

Consent confirmation records the caregiver's attestation, timestamp, and user identity; it is not
presented as a digital signature.

The API stores one private original image only. It does not store or return a thumbnail, and it never
exposes the storage key. The mobile app may create thumbnails locally from the protected image
endpoint. The caregiver who submitted the report and authorized family members may view the image
inline in the app through authenticated endpoints; the app should not require a manual file download.

The caregiver endpoint is `POST /api/v1/caregiver/bookings/{bookingId}/medical-report` with a
`multipart/form-data` JSON `report` part and optional `photo` part. Photo reads stream inline from
`GET /api/v1/family/reports/{reportId}/photo` after family authorization.

## Error catalog

| Code | HTTP | When |
|---|---|---|
| `Reports.Visit.InvalidContent` | 400 | Blank/all-empty text, text over 2,000 characters, invalid assessment, or invalid timestamp data |
| `Reports.Visit.InvalidType` | 400 | The family feed was requested with a type other than `visit` |
| `Reports.Visit.AccessDenied` | 403 | The caller has no active family membership |
| `Reports.Visit.AlreadySubmitted` | 409 | A report already exists for the booking |
| `Bookings.NotFound` | 404 | The booking is unknown or is assigned to another caregiver |
| `Bookings.Domain.InvalidOperation` | 409 | The assigned booking has not completed attendance |
| `Reports.Medical.AlreadySubmitted` | 409 | A medical report already exists for the booking |
| `Reports.Medical.CaregiverForbidden` | 403 | A caregiver is identified as companion for a booking visible to that caregiver |
| `Reports.Medical.InvalidContent` | 400 | Structural measurement, timestamp, assessment, or consent/photo validation failed |
| `Reports.Medical.PhotoNotFound` | 404 | The report has no photo or the private photo is unavailable |
