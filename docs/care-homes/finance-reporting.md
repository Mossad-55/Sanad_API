# Care Homes receipts, revenue, exports, and internal notes

HC-TASK-037 adds operator and operational-Admin read models for Care Home booking charges, completed refunds, revenue movements, and dashboard counts. It reuses the booking's immutable base/fee/tax snapshot and refund completion record. It does not calculate new fees, change refund behavior, initiate payouts, or include resident wallets, visits, bank breakdowns, invoice analytics, PDF receipts, or email delivery. FUR-05 adds a read-only facility-owner settlement view over the same existing payout truth; it does not calculate new fees, record transfers, or expose evidence, reason, or recording-actor fields.

## Authorization and routes

Facility-owner routes require `CareHomeOwnerAccess`. The application resolves the authenticated owner's facility and scopes every booking to it. An unknown booking and a booking belonging to another facility both return the existing safe `CareHomes.Bookings.NotFound` response.

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/v1/care-homes/booking-requests/mine/{bookingId}/receipt` | Read the booking receipt. |
| GET | `/api/v1/care-homes/dashboard/mine?from=YYYY-MM-DD&to=YYYY-MM-DD` | Read occupancy, live pending counts, and period revenue. |
| GET | `/api/v1/care-homes/revenue/mine?from=YYYY-MM-DD&to=YYYY-MM-DD` | Read revenue totals and transaction rows. |
| GET | `/api/v1/care-homes/revenue/mine/export.csv?from=YYYY-MM-DD&to=YYYY-MM-DD` | Export the same transaction rows as CSV. |
| GET | `/api/v1/care-homes/payouts/mine` | Read the facility's recorded transfers and eligible unpaid balances. |
| GET | `/api/v1/care-homes/booking-requests/mine/{bookingId}/internal-notes` | Read private booking notes. |
| POST | `/api/v1/care-homes/booking-requests/mine/{bookingId}/internal-notes` | Append a private note using `{ "text": "..." }`. |

Operational Admin routes require `CareHomesOperationalAdmin`, which allows SuperAdmin and SupportAdmin and denies ContentAdmin. Optional `facilityId` filters dashboard, revenue, and export results.

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/v1/admin/care-homes/bookings/{bookingId}/receipt` | Read any facility's booking receipt. |
| GET | `/api/v1/admin/care-homes/dashboard?facilityId=...&from=YYYY-MM-DD&to=YYYY-MM-DD` | Read dashboard metrics across facilities or for one facility. |
| GET | `/api/v1/admin/care-homes/revenue?facilityId=...&from=YYYY-MM-DD&to=YYYY-MM-DD` | Read revenue totals and transaction rows. |
| GET | `/api/v1/admin/care-homes/revenue/export.csv?facilityId=...&from=YYYY-MM-DD&to=YYYY-MM-DD` | Export the same transaction rows as CSV. |
| GET | `/api/v1/admin/care-homes/bookings/{bookingId}/internal-notes` | Read private booking notes. |
| POST | `/api/v1/admin/care-homes/bookings/{bookingId}/internal-notes` | Append a private note using `{ "text": "..." }`. |

## Receipt and revenue shapes

The receipt includes `bookingId`, `facilityId`, `currency`, `merchantReference`, `paymentTransactionId`, `paymentCompletedOnUtc`, `baseAmount`, `platformFeeAmount`, `taxAmount`, `originalTotal`, `paymentStatus`, `completedRefundAmount`, `refundReference`, `refundCompletedOnUtc`, and `netCollected`. Original charge values remain unchanged when a refund is completed. Pending or failed refunds are not counted as completed refunds. Only paid bookings are receiptable; unknown/cross-facility Owner reads return 404.

Revenue includes `from`, `to`, `totals`, and `transactions`. Totals contain `baseAmount`, `platformFeeAmount`, `taxAmount`, `grossCollected`, `completedRefunds`, `netCollected`, `paymentCount`, and `refundCount`. A transaction row includes the booking/facility, stay dates, payment timestamp/reference, payment amount in the selected period, completed-refund timestamp/reference, refund amount in the selected period, net movement, currency, and booking/payment/refund statuses.

Date filters are inclusive Cairo-local calendar dates. The query converts their start and exclusive next-day boundary to UTC. Collections are counted on `PaymentCompletedOnUtc`; completed refunds are counted on `RefundCompletedOnUtc`. Thus, a later refund appears as a negative movement in the refund period, while the original collection remains in its payment period. An invalid reversed date range returns `400 CareHomes.Revenue.InvalidRange`.

Bookings paid before this feature has a stored payment timestamp remain in unfiltered transaction lists and unfiltered totals, with a null payment timestamp. Date-filtered reports omit a payment without a timestamp rather than attributing it to booking creation. Completed refunds still use their recorded completion timestamp.

Dashboard `cairoDate` is the current Cairo-local date. `occupiedResources` counts in-use or held capacity, including unassigned room-type holds. `totalResources` counts physical room resources for private/suite inventory and bed resources for shared inventory; `occupancyPercentage` is zero when capacity is zero. `pendingPaymentCount` counts unexpired checkout holds, and `awaitingDecisionCount` counts paid bookings in the active facility-decision window. `periodRevenue` has the same totals shape as revenue.

**Draft facilities are read like any other.** The owner dashboard and revenue reads resolve the authenticated owner's facility regardless of its review status, so a `Draft` facility is not rejected and never reports fabricated zeros — zero/empty values appear only when no activity exists for the selected period, while recorded payments, refunds, and inventory are reported with their real totals exactly as for an `Approved` facility. Both behaviors are pinned relationally in `CareHomeFinancePostgresTests` (empty-Draft zero totals and Draft-with-activity real data).

CSV is UTF-8 with BOM, has a header row, uses invariant two-decimal money values, and contains the same period transaction rows and scope as the JSON report. Text cells are quoted, embedded quotes are doubled, and formula-leading user references are neutralized. Exports do not contain medical snapshots or Family contact information.

## Owner settlement view

`GET /api/v1/care-homes/payouts/mine` requires `CareHomeOwnerAccess` and returns `200` with an array of settlement rows scoped to the authenticated owner's facility. The caller cannot supply a facility ID. When the authenticated owner has no facility the route returns the same safe `404 CareHomes.Bookings.NotFound` used by the other owner finance reads; a facility without settlement rows returns `200` with an empty array.

Each row keeps the existing payout-ledger shape: `bookingId`, `facilityId`, `startDate`, `endDate`, `remainingCustomerAmount`, `remainingGross`, `feeRatePercentage`, `feeAmount`, `netPayable`, `payoutId`, `facilityDebt`, `currency`, `transferReference`, and `recordedOnUtc`. A non-null `payoutId` identifies an already-recorded transfer; every recorded transfer is returned, including one with subsequent debt reported in `facilityDebt`. A null `payoutId` identifies a currently eligible unpaid balance: the completed stay, paid/refunded payment, completed checkout, refund state, and positive remaining amount must all pass the existing eligibility checks, and the calculated `netPayable` must be positive. Ineligible or zero-payable rows without a recorded payout are excluded. Rows stay in checkout-time-descending order, and the view never creates or changes payout, debt, or booking state. Evidence, reason, and recording-actor fields are not part of the ledger read model and are not exposed.

When an eligible unpaid balance needs a Finance rule that is not effective, the route fails closed with `CareHomes.Payout.FinanceConfigurationMissing`, reusing the existing Admin ledger behavior. The Admin ledger and the payout-writing routes are unchanged; customer collections are not owner income and no calculation moves to the caller.

## Internal notes

Notes are visible only through the protected Owner and operational-Admin routes; Family booking responses do not include them. Notes are append-only; there is no edit or delete endpoint. Each row records `authorUserId` and UTC `createdOnUtc`. Text is trimmed and limited to 4,000 characters. A correction is another note, preserving the earlier record.

## Migration and verification limits

The additive `AddCareHomeFinanceReporting` migration adds `payment_completed_on_utc` and `internal_booking_notes`; source is generated but not applied to a database. No API/database/Bruno runtime result is implied by the route examples or unit tests. Migration application requires separate exact-target authorization.
