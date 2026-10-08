# Care Homes receipts, revenue, exports, and internal notes

HC-TASK-037 adds operator and operational-Admin read models for Care Home booking charges, completed refunds, revenue movements, and dashboard counts. It reuses the booking's immutable base/fee/tax snapshot and refund completion record. It does not calculate new fees, change refund behavior, initiate payouts, or include resident wallets, visits, bank breakdowns, invoice analytics, PDF receipts, or email delivery.

## Authorization and routes

Facility-owner routes require `CareHomeOwnerAccess`. The application resolves the authenticated owner's facility and scopes every booking to it. An unknown booking and a booking belonging to another facility both return the existing safe `CareHomes.Bookings.NotFound` response.

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/v1/care-homes/booking-requests/mine/{bookingId}/receipt` | Read the booking receipt. |
| GET | `/api/v1/care-homes/dashboard/mine?from=YYYY-MM-DD&to=YYYY-MM-DD` | Read occupancy, live pending counts, and period revenue. |
| GET | `/api/v1/care-homes/revenue/mine?from=YYYY-MM-DD&to=YYYY-MM-DD` | Read revenue totals and transaction rows. |
| GET | `/api/v1/care-homes/revenue/mine/export.csv?from=YYYY-MM-DD&to=YYYY-MM-DD` | Export the same transaction rows as CSV. |
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

CSV is UTF-8 with BOM, has a header row, uses invariant two-decimal money values, and contains the same period transaction rows and scope as the JSON report. Text cells are quoted, embedded quotes are doubled, and formula-leading user references are neutralized. Exports do not contain medical snapshots or Family contact information.

## Internal notes

Notes are visible only through the protected Owner and operational-Admin routes; Family booking responses do not include them. Notes are append-only; there is no edit or delete endpoint. Each row records `authorUserId` and UTC `createdOnUtc`. Text is trimmed and limited to 4,000 characters. A correction is another note, preserving the earlier record.

## Migration and verification limits

The additive `AddCareHomeFinanceReporting` migration adds `payment_completed_on_utc` and `internal_booking_notes`; source is generated but not applied to a database. No API/database/Bruno runtime result is implied by the route examples or unit tests. Migration application requires separate exact-target authorization.
