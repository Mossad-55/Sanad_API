# Caregiver bookings

Medical Reports V1 are submitted by medical caregivers at
`POST /api/v1/caregiver/bookings/{bookingId}/medical-report` using multipart form data with a JSON
`report` part and optional `photo` part. Blood-pressure values are paired; measurement time is UTC
and required for any supplied measurement, and future timestamps are rejected. No clinical ranges
are imposed. Companion submission across caregiver ownership boundaries returns `404 Bookings.NotFound`. Photo consent is
a caregiver UI checkbox/attestation; unavailable or false consent submits without a photo.
The submitting caregiver reads an authorized photo inline at
`GET /api/v1/caregiver/bookings/medical-reports/{reportId}/photo`.

Caregiver `الطلبات`: list/detail (including cancellations) plus accept, decline, start, and complete.

All routes live under `/api/v1/caregiver/bookings...`.

## Access

- **Normal JWT** for a caregiver account (`access_type = Normal`, `account_type = MedicalCaregiver` or `CompanionCaregiver`). Policy: `CaregiverAccess`.
- Every query/command is scoped to the caller’s caregiver profile (`caregivers.user_id`). Another caregiver’s booking is `404 Bookings.NotFound`. A JWT without a caregiver profile is `401`.

## Local Development decision fixtures

When `App:TestUserSeed` is explicitly enabled in Development, the test seeder creates two isolated, paid `PendingCaregiverApproval` bookings for the seeded medical caregiver. One is reserved for accept; the other has captured test evidence without a Paymob transaction reference and is reserved for decline, so the decline handler does not issue an external refund request. Bruno locates the fixtures from the caregiver Upcoming list and reads each result back. These are local test fixtures only; they do not represent a provider-confirmed payment and must never be enabled with a live Paymob key.

## List & detail

| Method | Route | Description |
|---|---|---|
| `GET` | `/api/v1/caregiver/bookings?tab={tab}` | Tab list, newest slot first |
| `GET` | `/api/v1/caregiver/bookings/{bookingId}` | Detail (own bookings only) |

### Tabs (`tab`, enum int)

| Value | Statuses |
|---|---|
| `1` `Upcoming` (default) | `PendingCaregiverApproval (2)`, `Confirmed (3)` |
| `2` `Current` | `InProgress (4)` |
| `3` `Past` | `Completed (5)`, **`CancelledByFamily (6)`**, `DeclinedByCaregiver (7)`, **`CancelledByCaregiver (8)`**, `Refunded (9)`, `Expired (10)` |

Past includes cancellations **by the family and by the caregiver**. `PendingPayment` family cancels still appear if they share this caregiver id (unpaid).

List item: booking id, family/elderly ids, senior names, slot, `shiftType`, `status`, amount, `cancellationReason`, `cancelledOnUtc`, `refundState` (`1` NotApplicable, `2` Failed, `3` Succeeded, `4` NoRefundDue).

### Detail

`GET /api/v1/caregiver/bookings/{bookingId}` matches family booking detail plus `refundedOnUtc` and `refundState`.

The caregiver detail also carries the senior's summary and the caregiver-only
record members:

| Member | Type | Meaning |
|---|---|---|
| `elderly.arabicFullName` / `elderly.englishFullName` | string or `null` | The senior's stored names; `null` when the dependent row is gone. |
| `elderly.gender` / `elderly.dateOfBirth` | int or `null` | The senior's stored gender and date of birth; `null` when the dependent row is gone. |
| `elderly.healthNotes` | string or `null` | The free-text health notes the family stored on the dependent row. This is the **only** medical context a caregiver can read today (see the medical-context mapping below). |
| `elderly.hasPhoto` | bool | The dependent has a stored photo. A caregiver has **no** route to those bytes, so no photo URL is published. |
| `elderly.emergencyContact` | object or `null` | Always `null` for a caregiver. No emergency-contact access or status model exists, so none is invented and no caregiver access is added. |
| `visitHistory` | array or `null` | The real reports recorded for **this** booking (the visit report and/or the medical report), newest first. `null` — not an empty array — when no report was recorded yet. |
| `upcomingVisits` | array or `null` | **Always `null`.** The meaning of "upcoming" is undecided (the mobile team's MOB-09 question: the same booking, or the caregiver's other bookings), so no other or future booking is mapped as a visit and no synthetic schedule is produced. |
| `familyReview` | object or `null` | The family's stored booking review: `id`, `bookingId`, `rating` (1–5), `comment`, `isAnonymous`, `createdOnUtc`. `null` when the family has not reviewed the booking. The review author is never published, so an anonymous review stays anonymous. |

Each `visitHistory` item carries the stored report facts: `reportId`,
`reportType` (`visit` or `medical`), `caregiverType`, `observedCondition`,
`activities`, `systolic`, `diastolic`, `pulse`, `temperature`, `notes`,
`assessment`, `startedOnUtc`, `completedOnUtc`, `measurementTakenOnUtc`,
`submittedOnUtc` and `photoAvailable`. The fields of the other report kind stay
`null`. `photoAvailable` says the medical report carries a photo; the photo
bytes are read through
`GET /api/v1/caregiver/bookings/medical-reports/{reportId}/photo`.

The booking's own amounts (`baseCaregiverFee`, `platformFeePercentage`,
`platformFeeAmount`, `totalPayableAmount`, `currency`) are the immutable
price snapshot of that booking — the caregiver's entitlement is
`baseCaregiverFee` in full and the family-paid platform fee is never deducted
from it (see [payout amounts](payouts.md)).

### Medical context a caregiver can read (route → field → authorization)

| Route | Fields | Authorization |
|---|---|---|
| `GET /api/v1/caregiver/bookings/{bookingId}` | `elderly.healthNotes` (free text stored by the family on the dependent row) | `CaregiverAccess`; the booking must belong to the calling caregiver, otherwise `404 Bookings.NotFound`. |
| `GET /api/v1/caregiver/bookings/medical-reports/{reportId}/photo` | the medical report photo bytes | `CaregiverAccess`; only the **submitting** caregiver (`403 Reports.Medical.AccessDenied` otherwise), and only when the report has a photo (`404 Reports.Medical.PhotoNotFound`). |

The dependent's **structured** medical profile (blood type, height, weight,
chronic conditions, allergies, medical history) and **medications** are
family-scoped only: `GET`/`PUT /api/v1/family/dependents/{dependentId}/medical-profile`
and `/api/v1/family/dependents/{dependentId}/medications` require the
`FamilyAccess` policy and family membership. The medical-access grant model
exists (`/api/v1/family/dependents/{dependentId}/medical-access-grants`), but
**no caregiver route reads those records through a grant today** — so the
caregiver booking detail publishes no medical profile and no medication. See
[medical profile](../families/medical-profile.md) and
[medical access](../families/medical-access.md) for the exact mapping. A
caregiver-facing medical-record read is a separate, authorized decision, not
part of this contract.

When a cancellation fact was recorded for the booking, the caregiver detail read also fills five additive members from that persisted fact; without a fact they stay `null`:

| Field | Values |
|---|---|
| `cancellationActorSide` | `1` Family, `2` Caregiver |
| `cancellationAction` | `1` Cancel, `2` Reject |
| `cancellationReasonCategory` | Recorded category name as a string: `Emergency`, `MedicalIssues`, `TransportationIssues`, `AccountDeletion`, `Other`; `null` when none was recorded |
| `refundEntitlement` | `1` NoRefundDue, `2` FullCapturedRefund |
| `refundDecisionReason` | `1` NothingCaptured, `2` FamilyCancellationBeforeAcceptance, `3` CaregiverRejectionBeforeAcceptance, `4` CaregiverCancellationAfterAcceptance, `5` FamilyCancellationWithinGraceWindow, `6` FamilyCancellationOutsideGraceWindow |

With a fact present, `refundState` is resolved from the fact, so a policy denial reads `4` `NoRefundDue` rather than a failed refund. The booking's own note (`ReasonNote`), the internal `isCaregiverIncident` flag, a status ledger, an event history and any derived timeline are never exposed; the added values are read straight from the stored row, and no amount, eligibility rule or existing field changes. The caregiver-only boundary is unchanged: another caregiver's booking is still `404 Bookings.NotFound`.

## Actions

| Method | Route | Body | Description |
|---|---|---|---|
| `POST` | `/api/v1/caregiver/bookings/{bookingId}/accept` | — | Accept a paid booking awaiting approval; `204` |
| `POST` | `/api/v1/caregiver/bookings/{bookingId}/decline` | `{ "reason": string ≤ 500 }` | Decline; `204`, with a full refund attempted when a provider transaction reference exists |
| `POST` | `/api/v1/caregiver/bookings/{bookingId}/cancel` | `{ "reason": string?, "reasonCategory": int? }` | Cancel an accepted booking before the visit starts |
| `POST` | `/api/v1/caregiver/bookings/{bookingId}/start` | — | Mark the visit as started |
| `POST` | `/api/v1/caregiver/bookings/{bookingId}/complete` | `{ "notes": string? ≤ 2000 }` | Complete the visit with optional notes |
| `POST` | `/api/v1/caregiver/bookings/{bookingId}/visit-report` | Visit report fields | Submit one immutable report after completion; see [family reports](../families/reports.md) |
| `POST` | `/api/v1/caregiver/bookings/{bookingId}/medical-report` | multipart `report` JSON + optional `photo` | Medical caregiver only; submit one immutable report after completion |
| `GET` | `/api/v1/caregiver/bookings/medical-reports/{reportId}/photo` | — | Inline private photo read by the submitting caregiver |

For a `Confirmed` booking, caregiver cancellation requires a valid reason category and a non-blank note. The policy is evaluated before mutation; a full captured refund is attempted when entitled, while `NoRefundDue` makes no provider call. The cancellation fact is recorded atomically. A cancellation outside the allowed state returns `409 Bookings.Domain.InvalidOperation`.

## Status rules

| Action | Required current status | Extra guard | Resulting status |
|---|---|---|---|
| accept | `PendingCaregiverApproval (2)` | Before the acceptance deadline (`min(paid + 24h, booking start)`) | `Confirmed (3)`; HTTP `204` |
| decline | `PendingCaregiverApproval (2)` | — | `DeclinedByCaregiver (7)` (or `Refunded (9)` if Paymob refund succeeds); HTTP `204` |
| cancel | `Confirmed (3)` | Before visit start; category + non-blank note required | `CancelledByCaregiver (8)` (or `Refunded (9)` if a full captured refund succeeds) |
| start | `Confirmed (3)` | `utcNow >= ConfirmedOnUtc` | `InProgress (4)` |
| complete | `InProgress (4)` | `utcNow >= StartedOnUtc` | `Completed (5)` |

Successful start, complete, and other command actions return `204 No Content`. Invalid state or timestamp ordering returns `409 Bookings.Domain.InvalidOperation`; an exhausted family subscription booking allowance returns `409 Bookings.AllowanceExceeded` and leaves the booking `InProgress`; an unknown or foreign booking returns `404 Bookings.NotFound`.

The visit-report action returns `201` and accepts only `observedCondition`, `activities`, `notes`, and
`assessment`. It uses server attendance and submission timestamps and rejects a second report for the
same booking with `409 Reports.Visit.AlreadySubmitted`.

Visit-report submission is JSON and returns `201`; it is a one-shot lifecycle
mutation and should be run only with an approved completed-booking fixture.
The medical-report action is also a one-shot lifecycle mutation and returns
`201`. Its `report` multipart field contains the JSON report, while `photo` is
an optional original image sent only when `photoConsentConfirmed` is true.
Photo reads return the original image inline (`200`), with `404` for a missing
photo/report and `403` for an unauthorized reader.

Medical Reports are a separate action for Medical caregivers. The mobile app must show the
optional photo-consent step before capturing or selecting a medical photo. A confirmed consent
attestation is submitted with the photo; if consent is unavailable, the caregiver submits the
Medical Report without a photo. Visit Reports never accept photos. Medical photo storage is private,
the API stores only the original image, and the app is responsible for its own thumbnail rendering.

Accepting after the deadline returns `409 Bookings.Domain.InvalidOperation` — the booking will expire instead and the family is refunded when the gateway allows it.

## Error Catalog

| Code | HTTP | When |
|---|---|---|
| `Bookings.NotFound` | 404 | Booking id unknown **or not this caregiver**. |
| `Bookings.Domain.InvalidOperation` | 409 | Wrong status for the action, or acceptance window expired. |
| `Bookings.Cancel.ReasonCategoryRequired` | 400 | Confirmed cancellation omitted the reason category. |
| `Bookings.Cancel.ReasonCategoryInvalid` | 400 | Confirmed cancellation supplied an undefined category. |
| `Bookings.Cancel.ReasonRequired` | 400 | Confirmed cancellation omitted a non-blank note. |

## Help Requests

Caregivers can view help requests submitted by elderly people they have active bookings with. These endpoints require policy `CaregiverAccess`.

### Get caregiver help requests (paginated)

Returns paginated help requests for the caregiver with optional filtering by status and elderly person. Results are ordered by request time descending (newest first).

```http
GET /api/v1/caregiver/help-requests?page=1&pageSize=20&status=null&elderlyId=null
Authorization: Bearer {{caregiverToken}}
```

#### Query Parameters

- `page` *(optional)*: Page number (defaults to `1`).
- `pageSize` *(optional)*: Page size (defaults to `20`, clamped to `1–100`).
- `status` *(optional)*: Filter by help request status (`Pending`, `Accepted`, `InProgress`, `Resolved`, `Rejected`, `Cancelled`, `Reopened`).
- `elderlyId` *(optional)*: Filter by specific elderly person ID.

- `200` — `PagedCaregiverHelpRequests` containing help request items and pagination metadata.
- `403` — Access denied (non-caregiver account).
- `404` — Caregiver profile not found.

### Get specific caregiver help request

Returns a specific help request for the caregiver. The caregiver can only access help requests for elderly people they have active bookings with.

```http
GET /api/v1/caregiver/help-requests/{requestId}
Authorization: Bearer {{caregiverToken}}
```

- `200` — `CaregiverHelpRequestResponse` for the specified help request.
- `403` — Access denied (non-caregiver account or no booking with the elderly person).
- `404` — Caregiver profile not found or help request not found/accessible.

### Caregiver help-request actions

`POST /api/v1/caregiver/help-requests/{requestId}/accept` and
`POST /api/v1/caregiver/help-requests/{requestId}/start` do not require a body.
Decline and resolve require `{ "reason": "..." }`; the reason is trimmed and must
contain 1–500 characters. Missing or invalid reasons return
`400 Caregivers.HelpRequest.InvalidReason`. The request must belong to an elderly
person with a `Confirmed` or `InProgress` booking for the authenticated caregiver.
Transitions and their history rows are persisted by the Families module in the
same Families unit of work. Unknown, expired, foreign, or unbooked requests return
`404 Caregivers.HelpRequest.NotFound`.

### Medication tasks and earnings

See [caregiver earnings and medication tasks](earnings-and-medication-tasks.md).
These endpoints are caregiver-self-only and verify the route caregiver ID
against the authenticated caregiver profile.
