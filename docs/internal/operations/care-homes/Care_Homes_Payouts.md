# Care Homes payout ledger

HC-TASK-038 adds a record-only Admin workflow. A bank transfer must be performed outside Sanad; these endpoints record and reconcile the completed external action and never initiate a payout.

## Access

All endpoints require `PayoutOperationalAdmin` (SuperAdmin or FinanceAdmin). SupportAdmin, Owners, and ContentAdmins cannot view or record settlements.

## Read ledger

`GET /api/v1/admin/care-homes/payouts/ledger` optionally accepts `facilityId`. It returns checked-out paid/refunded bookings with the remaining customer amount, facility gross payable, effective Finance percentage fee, net amount, recorded settlement reference/time, and outstanding facility debt. Rows without an eligible completed stay report zero payable. If any eligible unpaid payable exists and no effective Finance rule is configured, the request fails closed with `CareHomes.Payout.FinanceConfigurationMissing`.

Eligibility requires an actual checkout, an accepted/refunded booking, paid/refunded payment, and no pending refund. Completed refunds reduce the base proportionally to the customer refund and the fee is calculated against that remaining base. The applied Finance rate and rule version are snapshotted when the Admin records the transfer.

## Record completed bank transfer

`POST /api/v1/admin/care-homes/payouts/bookings/{bookingId}/record`

```json
{
  "transferReference": "BANK-TRANSFER-REFERENCE",
  "evidence": "private evidence reference or approved storage key",
  "reason": "Monthly facility settlement"
}
```

Only one payout may be recorded for a booking. The immutable ledger stores facility gross, Finance fee/rate/version, net amount, remaining customer amount, currency, evidence, reference, reason, recording Admin, and UTC timestamp. The endpoint does not upload evidence or move files; callers must supply a private evidence reference from the existing file system workflow.

## Record post-payout refund/reversal

`POST /api/v1/admin/care-homes/payouts/bookings/{bookingId}/record-reversal`

```json
{
  "customerRefundAmount": 575.00,
  "reference": "REFUND-OR-REVERSAL-REFERENCE",
  "reason": "Confirmed customer refund after settlement"
}
```

This records a confirmed external refund/reversal for reconciliation; it does not initiate or verify a provider refund. The facility debt is its proportional share of the recorded net payout, calculated cumulatively so multiple partial refunds reconcile without rounding drift. `(payout, reference)` is unique; duplicate references conflict. The settlement remains visible and the debt is added to the facility balance.

## Verification limits

Focused unit tests cover completed-stay eligibility, pending/partial/full refunds, Finance rule fee calculation, missing-rule fail-closed behavior, one-settlement protection, proportional debt/idempotency, and Admin authorization metadata. Ten focused payout/Admin-finance tests passed. The payout-ledger migration and preceding profile-media migration are applied in the authorized disposable `SanadBrunoTestDb`. Development API lifecycle/payment-webhook Bruno passed 9 requests/12 assertions; the ledger read returned 401 anonymously and 200 for SupportAdmin. No checked-out completed stay was available, so live payout-record/reversal mutation was not exercised; provider transfer and production operations were not performed.
