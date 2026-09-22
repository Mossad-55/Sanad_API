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
| `4` `NoRefundDue` | A cancellation fact records that no refund is owed by policy. This state is not a failed refund and is never retryable. |

Automatic Paymob refund can fail silently; **Failed** is the ops queue. A policy-denied refund is **NoRefundDue**, not **Failed**, and must not be retried. Admin retries with `POST /api/v1/admin/bookings/{id}/refund` only for **Failed** items (Paymob dashboard remains a fallback if the gateway still rejects).

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
| `1` Cancelled | `CancelledByFamily (6)`, `DeclinedByCaregiver (7)`, `CancelledByCaregiver (8)` — includes unpaid, **failed**, and **NoRefundDue** cancellations (successful refunds have status `9` and drop out of this filter) |
| `2` FailedRefund | Paid + not refunded + status 6/7/8/10, excluding fact-backed `NoRefundDue` items |
| `3` Refunded | Status `Refunded (9)` |

Empty database: `200` with `totalCount: 0`.

List item fields: booking/family/caregiver/elderly ids, slot, `status`, amounts, `refundState`, `paidOnUtc`, `cancelledOnUtc`, `refundedOnUtc`, `cancellationReason`.

## Detail

```http
GET /api/v1/admin/bookings/{bookingId}
```

Same `BookingDetailResponse` as family/caregiver detail, including `refundedOnUtc` and `refundState`. Fact-backed cancellation details render `NoRefundDue` instead of the legacy failed-refund derivation. `404 Bookings.NotFound` when unknown.

## Cancellation history

```http
GET /api/v1/admin/bookings/cancellations?page=1&pageSize=10&actor=1
Authorization: Bearer {{accessToken}}
```

Returns durable cancellation facts, newest first, with optional `actor` filtering (`1` Family, `2` Caregiver). The response preserves actor, action, status-at-cancellation, reason, refund entitlement, and current booking state.

This is a safe read for Super Admin or Content Admin normal JWTs. It returns
`200` with an empty page when there are no facts; `401` is unauthenticated and
`403` is any non-admin or restricted token.

## Retry refund

```http
POST /api/v1/admin/bookings/{bookingId}/refund
Authorization: Bearer {{accessToken}}
```

No body. Allowed only when `refundState` is **Failed** (paid + cancelled/declined/expired + not yet `Refunded`).

| Result | HTTP | Meaning |
|---|---|---|
| Success | `200` | Same detail payload; `status` `Refunded (9)`, `refundState` `3` |
| `Bookings.NotFound` | 404 | Unknown id |
| `Bookings.AlreadyRefunded` | 409 | Already `Refunded` |
| `Bookings.RefundNotEligible` | 409 | Never paid, or still an open booking |
| `Bookings.NoRefundDue` | 409 | Policy recorded that no refund is owed; no Paymob call is made |
| `Paymob.NotConfigured` | 503 | Gateway keys missing |
| `Paymob.GatewayError` | 502 | Paymob rejected the refund; booking stays failed — retry again or use the dashboard |

Postman: `docs/postman/admins/Sanad.Admin.postman_collection.json`.
