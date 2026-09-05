# Caregiver Booking Actions

The caregiver `الطلبات` actions on family bookings: accept, decline, start the visit, and complete it.

All routes live under `/api/v1/caregiver/bookings...`.

## Access

- **Normal JWT** for a caregiver account (`access_type = Normal`, `account_type = MedicalCaregiver` or `CompanionCaregiver`). Policy: `CaregiverAccess`.

## Endpoints

| Method | Route | Body | Description |
|---|---|---|---|
| `POST` | `/api/v1/caregiver/bookings/{bookingId}/accept` | — | Accept a paid booking awaiting approval |
| `POST` | `/api/v1/caregiver/bookings/{bookingId}/decline` | `{ "reason": string ≤ 500 }` | Decline — the family is refunded in full |
| `POST` | `/api/v1/caregiver/bookings/{bookingId}/start` | — | Mark the visit as started |
| `POST` | `/api/v1/caregiver/bookings/{bookingId}/complete` | `{ "caregiverNotes": string? ≤ 2000 }` | Complete the visit with optional notes |

## Status rules

| Action | Required current status | Extra guard | Resulting status |
|---|---|---|---|
| accept | `PendingCaregiverApproval (2)` | Before the acceptance deadline (`min(paid + 24h, booking start)`) | `Confirmed (3)` |
| decline | `PendingCaregiverApproval (2)` | — | `DeclinedByCaregiver (7)` |
| start | `Confirmed (3)` | — | `InProgress (4)` |
| complete | `InProgress (4)` | — | `Completed (5)` |

Accepting after the deadline returns `409 Bookings.Domain.InvalidOperation` — the booking will expire instead and the family is refunded.

## Error Catalog

| Code | HTTP | When |
|---|---|---|
| `Bookings.NotFound` | 404 | Booking id unknown. |
| `Bookings.Domain.InvalidOperation` | 409 | Wrong status for the action, or acceptance window expired. |

> **Recorded follow-up:** verifying that the targeted booking actually belongs to the calling caregiver (via `caregivers.user_id`) is a planned hardening slice.
