# Caregiver bookings

Caregiver `الطلبات`: list/detail (including cancellations) plus accept, decline, start, and complete.

All routes live under `/api/v1/caregiver/bookings...`.

## Access

- **Normal JWT** for a caregiver account (`access_type = Normal`, `account_type = MedicalCaregiver` or `CompanionCaregiver`). Policy: `CaregiverAccess`.
- Every query/command is scoped to the caller’s caregiver profile (`caregivers.user_id`). Another caregiver’s booking is `404 Bookings.NotFound`. A JWT without a caregiver profile is `401`.

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

Detail matches family booking detail plus `refundedOnUtc` and `refundState`.

## Actions

| Method | Route | Body | Description |
|---|---|---|---|
| `POST` | `/api/v1/caregiver/bookings/{bookingId}/accept` | — | Accept a paid booking awaiting approval |
| `POST` | `/api/v1/caregiver/bookings/{bookingId}/decline` | `{ "reason": string ≤ 500 }` | Decline — refund attempted in full |
| `POST` | `/api/v1/caregiver/bookings/{bookingId}/cancel` | `{ "reason": string?, "reasonCategory": int? }` | Cancel an accepted booking before the visit starts |
| `POST` | `/api/v1/caregiver/bookings/{bookingId}/start` | — | Mark the visit as started |
| `POST` | `/api/v1/caregiver/bookings/{bookingId}/complete` | `{ "caregiverNotes": string? ≤ 2000 }` | Complete the visit with optional notes |

For a `Confirmed` booking, caregiver cancellation requires a valid reason category and a non-blank note. The policy is evaluated before mutation; a full captured refund is attempted when entitled, while `NoRefundDue` makes no provider call. The cancellation fact is recorded atomically. A cancellation outside the allowed state returns `409 Bookings.Domain.InvalidOperation`.

## Status rules

| Action | Required current status | Extra guard | Resulting status |
|---|---|---|---|
| accept | `PendingCaregiverApproval (2)` | Before the acceptance deadline (`min(paid + 24h, booking start)`) | `Confirmed (3)` |
| decline | `PendingCaregiverApproval (2)` | — | `DeclinedByCaregiver (7)` (or `Refunded (9)` if Paymob refund succeeds) |
| cancel | `Confirmed (3)` | Before visit start; category + non-blank note required | `CancelledByCaregiver (8)` (or `Refunded (9)` if a full captured refund succeeds) |
| start | `Confirmed (3)` | — | `InProgress (4)` |
| complete | `InProgress (4)` | — | `Completed (5)` |

Accepting after the deadline returns `409 Bookings.Domain.InvalidOperation` — the booking will expire instead and the family is refunded when the gateway allows it.

## Error Catalog

| Code | HTTP | When |
|---|---|---|
| `Bookings.NotFound` | 404 | Booking id unknown **or not this caregiver**. |
| `Bookings.Domain.InvalidOperation` | 409 | Wrong status for the action, or acceptance window expired. |
| `Bookings.Cancel.ReasonCategoryRequired` | 400 | Confirmed cancellation omitted the reason category. |
| `Bookings.Cancel.ReasonCategoryInvalid` | 400 | Confirmed cancellation supplied an undefined category. |
| `Bookings.Cancel.ReasonRequired` | 400 | Confirmed cancellation omitted a non-blank note. |
