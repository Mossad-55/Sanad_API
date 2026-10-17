# Admin caregiver payouts

The Admin caregiver payout surface records manual bank transfers to caregivers.
Transfers are executed outside the API by a human operator; these routes only
select eligible work and record the result. All routes below require the
`PayoutOperationalAdmin` policy (**SuperAdmin or FinanceAdmin**); SupportAdmin and
ContentAdmin are denied (`403`), and an unauthenticated caller receives `401`.

```text
GET  /api/v1/admin/caregiver-payouts/eligible-bookings?caregiverId=&page=1&pageSize=20
GET  /api/v1/admin/caregiver-payouts?status=&caregiverId=&page=1&pageSize=20
GET  /api/v1/admin/caregiver-payouts/{payoutId}
POST /api/v1/admin/caregiver-payouts/record
POST /api/v1/admin/caregiver-payouts/{payoutId}/mark-failed
```

The caregiver-facing payout account, self-only payout reads and payment policy
are documented separately in `docs/app/caregivers/payouts.md`. The payout timing
policy that drives eligibility is an Admin Finance configuration
(`docs/admin/finance-platform-charges.md`).

## Eligible-booking selector

```text
GET /api/v1/admin/caregiver-payouts/eligible-bookings?caregiverId=&page=1&pageSize=20
```

Read-only. It returns the completed-and-paid bookings the operator can record a
payout for **right now**. It never writes a payout, booking, or account row, and
never calls a payment provider.

Query parameters:

- `caregiverId` (optional GUID) — limit the selector to one caregiver. Omitting it
  returns every caregiver's eligible bookings.
- `page` (default `1`) — clamped to a minimum of `1`.
- `pageSize` (default `20`) — clamped to `1`–`100`.

Response (`200`, standard `PagedResult` envelope, oldest eligible first):

```json
{
  "items": [
    {
      "bookingId": "0198e2c2-2222-7777-8888-000000000003",
      "caregiverId": "0198e2c2-2222-7777-8888-000000000001",
      "arabicFullName": "محمد أحمد",
      "englishFullName": "Mohamed Ahmed",
      "completedOnUtc": "2026-10-05T00:00:00Z",
      "eligibleOnUtc": "2026-10-08T00:00:00Z",
      "caregiverAmount": 150.00,
      "currency": "EGP"
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 1,
  "totalPages": 1
}
```

- `bookingId` / `caregiverId` — the identifiers to pass to
  `POST /api/v1/admin/caregiver-payouts/record`.
- `arabicFullName` / `englishFullName` — the existing safe caregiver display
  name from the identity profile. Empty strings when the identity name cannot be
  resolved. No phone number, payout-account, bank code, or IBAN is exposed by this
  read.
- `completedOnUtc` — the booking completion timestamp.
- `eligibleOnUtc` — `max(completedOnUtc, paidOnUtc)` plus the effective payout
  delay. This is the moment the booking became payable; the value may be in the
  past (already due, still unrecorded).
- `caregiverAmount` / `currency` — the booking price-snapshot `BaseCaregiverFee`
  in the snapshot currency. This is the amount the recorded payout will carry; the
  snapshot `PlatformFeeAmount` is charged to the family and is never deducted.
- Rows are ordered oldest eligible first, then by booking ID for a stable order.

### Eligibility (reuses the record-payout rules)

A booking appears only when **all** of the following hold:

- it is `Completed` with both `completedOnUtc` and `paidOnUtc` set;
- the effective payout policy delay has elapsed since `max(completedOnUtc,
  paidOnUtc)`;
- the caregiver has a `Verified` payout account; and
- no `Paid` payout already exists for the booking.

A `Failed` payout row does **not** exclude the booking, so it reappears here for a
retry. A caregiver with a `Pending`, `Rejected`, `Revoked`, or missing payout
account is omitted until the account is `Verified`.

Missing effective policy fails closed: when no effective caregiver payout policy
exists the selector returns `400 Finance.PayoutPolicy.Missing`, exactly like
`POST .../record` (eligibility is undefined without the configured delay). The
shared problem-details mapper does not special-case this code.

### Errors

- `401` — missing or invalid token.
- `403` — valid token without the `PayoutOperationalAdmin` role.
- `400 Finance.PayoutPolicy.Missing` — no effective caregiver payout policy.
- `400` — a non-GUID `caregiverId` fails model binding.

## Recorded payouts, detail and failures

`GET /api/v1/admin/caregiver-payouts` lists recorded payouts (both `Paid` and
`Failed`) ordered by recording time descending, with optional `status` and
`caregiverId` filters and the same `PagedResult` paging (`page` ≥ 1, `pageSize`
clamped `1`–`100`). `GET /api/v1/admin/caregiver-payouts/{payoutId}` returns one
payout or `404 Caregivers.Payouts.NotFound`.

```json
{
  "bookingId": "0198e2c2-2222-7777-8888-000000000003",
  "transferReference": "BANK-2026-000123",
  "evidence": "Transfer advice 000123",
  "reason": "October weekly payout"
}
```

Recording re-validates, in order: the booking exists and is `Completed` **and**
`Paid` with timestamps; an effective payout policy exists
(`400 Finance.PayoutPolicy.Missing`); the delay has passed; the caregiver has a
`Verified` account (`409 Caregivers.Payouts.AccountNotVerified`); and no `Paid`
payout exists (`409 Caregivers.Payouts.Conflict`, backed by a partial unique
index). Success returns `201` with the payout in `Paid` status. Resource-specific
length rules apply: `reason` and `evidence` at most 2000 characters each, and
`transferReference` at most 200.

A `Paid` payout recorded in error is corrected with `POST
.../{payoutId}/mark-failed` and a `{"reason": "..."}` body, moving it to `Failed`.
`Failed` is terminal for that row; the booking then becomes eligible again in the
selector so a fresh payout row can be recorded. Marking a non-`Paid` payout
returns `409 Caregivers.Payouts.InvalidState`.
