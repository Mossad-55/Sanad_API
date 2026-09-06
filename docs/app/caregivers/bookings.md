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

List item: booking id, family/elderly ids, senior names, slot, `shiftType`, `status`, amount, `cancellationReason`, `cancelledOnUtc`, `refundState` (`1` NotApplicable, `2` Failed, `3` Succeeded).

Detail matches family booking detail plus `refundedOnUtc` and `refundState`.

## Actions

| Method | Route | Body | Description |
|---|---|---|---|
| `POST` | `/api/v1/caregiver/bookings/{bookingId}/accept` | — | Accept a paid booking awaiting approval |
| `POST` | `/api/v1/caregiver/bookings/{bookingId}/decline` | `{ "reason": string ≤ 500 }` | Decline — refund attempted in full |
| `POST` | `/api/v1/caregiver/bookings/{bookingId}/start` | — | Mark the visit as started |
| `POST` | `/api/v1/caregiver/bookings/{bookingId}/complete` | `{ "caregiverNotes": string? ≤ 2000 }` | Complete the visit with optional notes |

There is **no** caregiver cancel HTTP for a confirmed visit yet (`CancelByCaregiver` exists on the domain and is used by seed/admin-visible data).

## Status rules

| Action | Required current status | Extra guard | Resulting status |
|---|---|---|---|
| accept | `PendingCaregiverApproval (2)` | Before the acceptance deadline (`min(paid + 24h, booking start)`) | `Confirmed (3)` |
| decline | `PendingCaregiverApproval (2)` | — | `DeclinedByCaregiver (7)` (or `Refunded (9)` if Paymob refund succeeds) |
| start | `Confirmed (3)` | — | `InProgress (4)` |
| complete | `InProgress (4)` | — | `Completed (5)` |

Accepting after the deadline returns `409 Bookings.Domain.InvalidOperation` — the booking will expire instead and the family is refunded when the gateway allows it.

## Error Catalog

| Code | HTTP | When |
|---|---|---|
| `Bookings.NotFound` | 404 | Booking id unknown **or not this caregiver**. |
| `Bookings.Domain.InvalidOperation` | 409 | Wrong status for the action, or acceptance window expired. |
