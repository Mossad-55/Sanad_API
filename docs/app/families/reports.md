# Family reports

Visit reports are immutable, dated records attached to a completed booking. They are separate from
the elderly medical profile and ordinary family notes. Visit reports store no photos. Medical Reports
are a separate Medical-caregiver-only report type; their planned V1 contract is documented below.

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

## Medical Reports V1 — mobile photo-consent contract

Medical Reports are submitted only by an assigned Medical caregiver for a completed booking. A
Medical caregiver may submit both a Visit Report and a Medical Report for the same booking. A
Companion caregiver cannot submit a Medical Report.

The planned Medical Report request supports structured measurements (blood pressure, pulse, and
temperature), measurement time, assessment, notes, and one optional original photo. Missing
measurements remain missing; the API must not convert them to zero. The photo is optional, and the
report remains valid when no photo is supplied.

The mobile application must request consent immediately before capturing or uploading an optional
medical photo. The consent step belongs in the caregiver app, not in a separate download flow:

1. The caregiver opens the Medical Report and taps **Add medical photo**.
2. Before opening the camera or selecting an existing image, the app asks the elderly person or
   authorized representative for permission to capture and store the image for care documentation.
3. The app displays a confirmation such as: **“I confirm that the elderly person or authorized
   representative gave permission for this photo to be captured and stored for care documentation.”**
4. If consent is confirmed, the app sends the photo with the Medical Report submission and sets
   `photoConsentConfirmed` to `true`.
5. If consent is refused or unavailable, the app submits the Medical Report without a photo. It
   must not upload the image.

Consent confirmation records the caregiver's attestation, timestamp, and user identity; it is not
presented as a digital signature. If the elderly person cannot consent, the app must use an
authorized representative only where Sanad's consent policy recognizes that representative. Until
that policy is available, submit the report without a photo.

The API stores one private original image only. It does not store or return a thumbnail, and it never
exposes the storage key. The mobile app may create thumbnails locally from the protected image
endpoint. The caregiver who submitted the report and authorized family members may view the image
inline in the app through authenticated endpoints; the app should not require a manual file download.

This section defines the mobile/API contract for the planned Medical Reports slice. It does not add
the Medical Report endpoint to the current Visit Report API.

## Error catalog

| Code | HTTP | When |
|---|---|---|
| `Reports.Visit.InvalidContent` | 400 | Blank/all-empty text, text over 2,000 characters, invalid assessment, or invalid timestamp data |
| `Reports.Visit.InvalidType` | 400 | The family feed was requested with a type other than `visit` |
| `Reports.Visit.AccessDenied` | 403 | The caller has no active family membership |
| `Reports.Visit.AlreadySubmitted` | 409 | A report already exists for the booking |
| `Bookings.NotFound` | 404 | The booking is unknown or is assigned to another caregiver |
| `Bookings.Domain.InvalidOperation` | 409 | The assigned booking has not completed attendance |
