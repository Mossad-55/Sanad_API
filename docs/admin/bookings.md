# Admin bookings (cancellations & refunds)

Ops routes under `/api/v1/admin/bookings/...`. Policy **`CaregiversAdmin`**: Normal JWT, `account_type` SuperAdmin or ContentAdmin.

This surface is for **closed** bookings: family/caregiver cancel, caregiver decline, expiry, and Paymob refund outcome. Live upcoming visits are not listed here.

## Refund state

`refundState` on every list/detail item:

| Value | Meaning |
|---|---|
| `1` `NotApplicable` | Never paid (e.g. family cancelled `PendingPayment`) or not in a refundable end state |
| `2` `Failed` | Paid, then cancelled / declined / expired, and `MarkRefunded` never ran (gateway error). Status stays `CancelledByFamily` / `DeclinedByCaregiver` / `CancelledByCaregiver` / `Expired` |
| `3` `Succeeded` | Status `Refunded (9)` and `refundedOnUtc` set |

Automatic Paymob refund can fail silently; **Failed** is the queue for dashboard retry.

## List

```http
GET /api/v1/admin/bookings?page=1&pageSize=10&finance=0
Authorization: Bearer {{accessToken}}
```

| Query | Default | Meaning |
|---|---|---|
| `page` / `pageSize` | `1` / `10` (max 100) | Paging |
| `finance` | `0` All | Filter |

`finance`:

| Value | Included |
|---|---|
| `0` All | Cancelled by family/caregiver, declined, expired, refunded |
| `1` Cancelled | `CancelledByFamily (6)`, `DeclinedByCaregiver (7)`, `CancelledByCaregiver (8)` — includes unpaid and **failed** refunds (successful refunds have status `9` and drop out of this filter) |
| `2` FailedRefund | Paid + not refunded + status 6/7/8/10 |
| `3` Refunded | Status `Refunded (9)` |

Empty database: `200` with `totalCount: 0`.

List item fields: booking/family/caregiver/elderly ids, slot, `status`, amounts, `refundState`, `paidOnUtc`, `cancelledOnUtc`, `refundedOnUtc`, `cancellationReason`.

## Detail

```http
GET /api/v1/admin/bookings/{bookingId}
```

Same `BookingDetailResponse` as family/caregiver detail, including `refundedOnUtc` and `refundState`. `404 Bookings.NotFound` when unknown.

Postman: `docs/postman/admins/Sanad.Admin.postman_collection.json`.
