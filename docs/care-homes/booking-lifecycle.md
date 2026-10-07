# Care-home booking lifecycle

This guide documents the implemented Care-home booking and stay lifecycle (HC-TASK-032 and the HC-034 stay-operation delta). Examples are authored from the current API contract; this guide does not claim a disposable database, Bruno, or provider runtime result.

## Timing defaults and Development test clock

The production contract is fixed at a 15-minute unpaid checkout hold, a 24-hour paid decision hold, a 24-hour earliest-arrival lead time, and a one-minute expiry sweep interval. The first three values are part of the booking behavior; the sweep interval controls how quickly the background worker observes an expired hold.

For disposable local verification only, the Development environment may opt into a short test clock with `CareHomes:TestClock:Enabled=true` and positive overrides for `CareHomes:TestClock:CheckoutHoldDuration`, `CareHomes:TestClock:DecisionHoldDuration`, and `CareHomes:TestClock:ExpirySweepInterval`. For example, a pinned local API can use `00:00:30`, `00:01:00`, and `00:00:05` respectively after the normal guarded fixture setup:

```powershell
$env:DOTNET_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'http://localhost:55819'
$env:CareHomes__TestClock__Enabled = 'true'
$env:CareHomes__TestClock__CheckoutHoldDuration = '00:00:30'
$env:CareHomes__TestClock__DecisionHoldDuration = '00:01:00'
$env:CareHomes__TestClock__ExpirySweepInterval = '00:00:05'
dotnet run --no-build --no-launch-profile --project src/API/Sanad.API/Sanad.API.csproj
```

This is a test-only configuration example: it must remain on the pinned local Development target and must not be used for production or a shared environment. There is no public time-control endpoint. `ArrivalLeadDuration` is deliberately not configurable and must remain 24 hours. Overrides without `Enabled=true`, enabling the test clock outside Development, or zero/negative durations are rejected during startup.

## Family routes

All routes require `FamilyAccess` and resolve the caller to a family owner or member. A caller without a family receives `404`; an unauthenticated caller receives `401`.

| Method | Route | Contract |
|---|---|---|
| GET | `/api/v1/family/care-home-bookings` | Lists the caller family's bookings, newest first. |
| GET | `/api/v1/family/care-home-bookings/{bookingId}` | Returns one booking only when it belongs to the caller's family. Unknown or cross-family IDs return `404 CareHomes.Bookings.NotFound`. |
| POST | `/api/v1/family/care-home-bookings/checkout` | Creates a pending payment booking and a 15-minute capacity hold. |
| POST | `/api/v1/family/care-home-bookings/{bookingId}/payments/intent` | Creates or replays the Paymob Card/Wallet intent for a pending booking. |

Checkout JSON:

```json
{
  "elderlyId": "<family-elderly-guid>",
  "facilityId": "<approved-facility-guid>",
  "roomTypeId": "<active-room-type-guid>",
  "requestedStartDate": "2026-11-10",
  "careNeedsNotes": "Optional notes",
  "responsibleContactName": "Optional override",
  "responsibleContactPhone": "+201000000000",
  "responsibleContactRelationship": "Daughter"
}
```

The elderly/contact resolver rechecks Family access and stores the server-derived elderly name, age, medical snapshot, and responsible contact. The requested start must be at least 24 hours after checkout in the Egypt/Cairo calendar. The end date is one calendar month later, clamped by `DateOnly.AddMonths`. Pricing snapshots the effective Finance platform fee and tax on the monthly base price; absent configuration fails closed with `503 CareHomes.Bookings.ChargesNotConfigured`. Capacity is checked under a facility/room-type reservation guard: Shared uses available beds, while Private/Suite uses available rooms.

Checkout returns `200` with the booking response. Initial status values are `PendingPayment = 1` and `PaymentStatus = Pending = 1`; `holdUntilUtc` is the 15-minute checkout deadline. The response includes booking/facility/elderly/room IDs, start/end dates, earliest arrival, amounts, `EGP`, status, payment status, hold, and decision reason.

Payment-intent JSON follows the existing Family payment shape:

```json
{
  "method": 1,
  "billing": {
    "firstName": "Fixture",
    "lastName": "Family",
    "email": "family@example.test",
    "phoneNumber": "+201000000000"
  }
}
```

The response contains `bookingId`, the `chb_` merchant reference, Paymob order/intention IDs, client secret, and public key. Repeating the request after an intent exists returns the same intent. An expired or otherwise non-payable booking returns `409 CareHomes.Bookings.InvalidState`.

## Facility-owner routes

These routes require `CareHomeOwnerAccess` and scope every read or decision to the caller's owned facility:

| Method | Route | Contract |
|---|---|---|
| GET | `/api/v1/care-homes/booking-requests/mine` | Lists paid requests for the caller's facility, newest first. |
| POST | `/api/v1/care-homes/booking-requests/mine/{bookingId}/decision` | Accepts or rejects a paid request. |

Decision JSON is `{ "accept": true, "reason": null }` for acceptance or `{ "accept": false, "reason": "Capacity unavailable" }` for rejection. A rejection reason is required. A paid booking is `PaidAwaitingDecision = 2` with a 24-hour decision hold. Acceptance moves it to `Accepted = 3`. Rejection moves it to `RefundPending = 7` and claims/initiates a full Paymob refund when a transaction exists. A decision outside the paid decision state returns `409 CareHomes.Bookings.InvalidState`; another facility or unknown booking returns `404 CareHomes.Bookings.NotFound`.

## Paymob callback and lifecycle states

`POST /api/v1/payments/webhooks/paymob` remains anonymous at the HTTP authorization layer but requires the configured Paymob HMAC. A valid `chb_` merchant reference dispatches to the Care-home aggregate. Amount mismatch returns `400` (`Paymob.AmountMismatch`); a different transaction for an already paid booking returns `409` (`CareHomes.Bookings.PaymentConflict`); duplicate same-transaction callbacks are acknowledged idempotently. Successful payment during the checkout hold sets `PaidAwaitingDecision = 2`, `PaymentStatus = Paid = 2`, and a 24-hour decision hold. A successful late callback enters `RefundPending` for refund initiation. A failed non-pending callback sets `Cancelled = 6` and `PaymentStatus = Failed = 3`.

The domain statuses are `PendingPayment (1)`, `PaidAwaitingDecision (2)`, `Accepted (3)`, `Rejected (4)`, `Expired (5)`, `Cancelled (6)`, `RefundPending (7)`, `RefundInitiated (8)`, and `Refunded (9)`. Payment statuses are `Pending (1)`, `Paid (2)`, `Failed (3)`, `RefundInitiated (4)`, and `Refunded (5)`. The expiry worker transitions an unpaid checkout hold to `Expired`; an expired paid decision window enters `RefundPending`, then the refund claim/provider flow may persist `RefundInitiated` after a successful refund. In the Development fixture, the hosted sweep calls the Paymob stub immediately, so the final timeout readback is `RefundInitiated (8)`, `PaymentStatus = RefundInitiated (4)`, and `refundStatus = Initiated` until a signed completion callback.

### Cancellation and refund recovery (HC-035)

Family cancellation is `POST /api/v1/family/care-home-bookings/{bookingId}/cancel` and requires `FamilyAccess`. It is scoped to the caller's Family. A pending unpaid checkout hold is cancelled without a refund, releasing its capacity. A paid booking cancelled before the recorded facility check-in receives a full refund of `totalAmount`, even if the facility already approved it. After actual check-in is recorded, Family cancellation refunds 50% of `totalAmount`, rounded to two decimals. Cancellation after checkout or in another unsupported booking state returns `409 CareHomes.Bookings.InvalidState`.

The facility owner may cancel an accepted stay with `POST /api/v1/care-homes/booking-requests/mine/{bookingId}/cancel`, requiring a nonblank `{ "reason": "..." }` and `CareHomeOwnerAccess`. Facility cancellation returns the full `totalAmount`, including after check-in. Existing owner rejection and decision-timeout paths also return the full amount.

Refund initiation is concurrency-claimed once per booking. Paymob failure persists `refundStatus = Failed`, allowing an Admin retry at `POST /api/v1/admin/care-homes/bookings/{bookingId}/refund/retry`; only a failed refund is retryable. Operational Admins may record an externally completed refund at `POST /api/v1/admin/care-homes/bookings/{bookingId}/refund/record-completed` with `{ "reference": "...", "reason": "..." }`. Both Admin routes require `CareHomesOperationalAdmin` (SuperAdmin or SupportAdmin; ContentAdmin is denied). Manual completion does not call Paymob. Duplicate requests/callbacks do not issue another refund or replace a completed state. Booking readbacks include `refundStatus` and `refundAmount`.

The booking statuses also include `Refunded (9)` for completed refunds. `refundStatus` distinguishes `None`, `Pending`, `Initiated`, `Failed`, `Completed`, and `ManuallyCompleted`; a successful Paymob refund request is `Initiated`, and a signed refund callback matching the stored refund or payment reference advances it to `Completed`.

Stable mapped booking errors are `CareHomes.Bookings.NotFound` (404), `CareHomes.Bookings.InvalidState` (409), `CareHomes.Bookings.CapacityConflict` (409), `CareHomes.Bookings.PaymentConflict` (409), `CareHomes.Bookings.RefundNotRetryable` (409), and `CareHomes.Bookings.ChargesNotConfigured` (503). Validation and the 24-hour arrival rule use the API's normal validation/problem mapping; the concrete domain code for the arrival rule is `CareHomes.Bookings.Invalid`.

HC-034 owns room/bed assignment and check-in/out. HC-035 cancellation/refund behavior is documented above. HC-038 owns facility payout/ledger behavior. Runtime evidence for HC-035 is recorded in the active checklist and execution handoff.

## Stay operations (HC-034)

Stay operations apply after a paid booking has been accepted. The facility owner can assign the physical resource, record actual arrival and departure, and read back the operational timestamps. The Family can confirm that the elderly person checked in. These actions do not change the booking's payment or acceptance state.

### Facility-owner routes

All routes below require `CareHomeOwnerAccess` and are scoped to a facility owned by the caller. Unknown bookings, bookings in another facility, and resources outside the booking's facility are hidden as `404 CareHomes.Bookings.NotFound`.

| Method | Route | Contract |
|---|---|---|
| POST | `/api/v1/care-homes/booking-requests/mine/{bookingId}/assignment` | Assigns a room and, for a Shared stay, a bed. |
| POST | `/api/v1/care-homes/booking-requests/mine/{bookingId}/assignment/transfer` | Transfers an accepted stay to another physical resource of the same original room type. |
| POST | `/api/v1/care-homes/booking-requests/mine/{bookingId}/check-in` | Records the actual UTC check-in time using the server clock. |
| POST | `/api/v1/care-homes/booking-requests/mine/{bookingId}/check-out` | Records the actual UTC check-out time using the server clock. |
| GET | `/api/v1/care-homes/booking-requests/mine/{bookingId}/operational` | Returns the currently effective assignment, append-only assignment history, actual check-in/out, and Family-confirmation readback fields. |

Assignment JSON is `{ "roomId": "<room-guid>", "bedId": "<bed-guid-or-null>" }`. A Shared room type requires `bedId`; a Private or Suite room type requires `bedId: null` and reserves the whole room. The room must be active, belong to the booking's facility and room type, and the bed (when supplied) must belong to that room. An occupied or otherwise conflicting resource returns `409 CareHomes.Bookings.AssignmentConflict`; allocation-shape or invalid-resource input returns `400 CareHomes.Bookings.InvalidAssignment` where mapped by the host. Assignment also enforces the accepted booking state and returns `409 CareHomes.Bookings.InvalidState` when the booking cannot be assigned.

Check-in requires an accepted booking with a physical assignment and can be recorded only once. Check-out requires an active recorded check-in and can be recorded only once. Invalid lifecycle transitions return `409 CareHomes.Bookings.InvalidState`. Successful booking responses include `assignedRoomId`, `assignedBedId`, `actualCheckInOnUtc`, `actualCheckOutOnUtc`, and (when confirmed) `familyCheckInConfirmedOnUtc` alongside the normal booking fields. Operational readback returns:

### Same-room-type physical transfer (HC-023)

The facility owner may transfer an accepted, assigned stay before or after check-in. The JSON shape is `{ "roomId": "<active-room-guid>", "bedId": "<bed-guid-or-null>", "effectiveDate": "YYYY-MM-DD" }`. Shared transfers require a bed in the selected room; Private and Suite transfers require `bedId: null`. The destination must be in the same facility and the booking's original room type. Cross-type accommodation changes are a separate Family-approved flow and are not supported here.

`effectiveDate` uses the Cairo-local calendar and must fall within the booking's start-inclusive/end-exclusive stay, be today or later, and allow no more than one transfer per booking on that local date. The transfer and append-only assignment history commit together. Operational readback includes each entry's prior/new room and bed IDs, effective date, actor, and UTC occurrence time; `assignedRoomId`/`assignedBedId` reflect the resource effective on the current Cairo date. Availability is projected from assignment history on both sides of each effective date, including legacy assignments with no history before their first transfer. Room type, base amount, platform fee, tax, total, and charge-rule version are unchanged.

The transfer queues idempotent Family in-app notification and email-outbox records. The existing email worker retries delivery; a delay or outage does not roll back the committed room transfer. Notification materialization is independently retried from a Care Homes outbox. Messages include prior/new room or bed labels, the effective date and booking ID, in the Family user's selected Arabic/English UI language. No live email is sent by the transfer handler.

Invalid bed shape/resource returns `400 CareHomes.Bookings.InvalidAssignment`; invalid/backdated/out-of-stay dates return `400 CareHomes.Bookings.InvalidTransferDate`; occupied destination, maintenance overlap, duplicate-date transfer, or concurrency conflict returns `409 CareHomes.Bookings.AssignmentConflict` or `409 CareHomes.Bookings.TransferConflict`. Unsupported booking state returns `409 CareHomes.Bookings.InvalidState`; unknown or other-owner bookings are hidden with `404 CareHomes.Bookings.NotFound`. All transfer paths retain `CareHomeOwnerAccess`.

```json
{
  "id": "<booking-guid>",
  "facilityId": "<facility-guid>",
  "roomTypeId": "<room-type-guid>",
  "assignedRoomId": "<room-guid>",
  "assignedBedId": "<bed-guid-or-null>",
  "actualCheckInOnUtc": "2026-11-10T12:00:00Z",
  "actualCheckInRecordedBy": "<owner-user-guid>",
  "actualCheckOutOnUtc": null,
  "actualCheckOutRecordedBy": null,
  "familyCheckInConfirmedOnUtc": null,
  "familyCheckInConfirmedBy": null
}
```

### Family confirmation

`POST /api/v1/family/care-home-bookings/{bookingId}/check-in-confirmation` requires `FamilyAccess`. It succeeds only for a booking belonging to the caller's Family and after facility check-in has been recorded. A cross-family/unknown booking is `404 CareHomes.Bookings.NotFound`; a confirmation before check-in or a repeated/invalid confirmation is `409 CareHomes.Bookings.InvalidState`. The server records `familyCheckInConfirmedOnUtc` and `familyCheckInConfirmedBy`; the original facility check-in remains intact.

### Check-in disputes and Admin cases

`POST /api/v1/care-homes/booking-requests/mine/{bookingId}/check-out` opens one `Open` check-in case when the facility records check-out without a Family confirmation. This trigger is immediate and has no timeout. A case is not opened when Family confirmation already exists, and an already-open case is not duplicated. The case stores the booking/facility, checkout time, opening actor, and opening time.

Operational admins use the following routes:

| Method | Route | Contract |
|---|---|---|
| GET | `/api/v1/admin/care-homes/check-in-disputes` | Lists cases newest first. |
| POST | `/api/v1/admin/care-homes/check-in-disputes/{caseId}/resolve` | Resolves an open case with evidence, reason, and an effective UTC check-in time. |

Both routes require `CareHomesOperationalAdmin`, which permits `SuperAdmin` and `SupportAdmin` with normal access. `ContentAdmin` is not permitted. Resolution JSON is `{ "effectiveCheckInOnUtc": "2026-11-10T12:00:00Z", "evidence": "signed arrival log", "reason": "Facility record reconciled" }`. Evidence, reason, and a UTC effective check-in time are required. Missing cases return `404 CareHomes.CheckInDispute.NotFound`; repeated resolution or invalid case state returns `409 CareHomes.CheckInDispute.InvalidState`. Responses expose `id`, `bookingId`, `facilityId`, `checkoutOnUtc`, `status`, `openedOnUtc`, `openedBy`, `effectiveCheckInOnUtc`, `evidence`, `reason`, `resolvedOnUtc`, and `resolvedBy`; these fields provide the audit trail without replacing the original facility timestamps.

Families may also submit a check-in dispute after a facility-recorded check-in:

`POST /api/v1/family/care-home-bookings/{bookingId}/check-in-dispute`

This route requires `FamilyAccess`, resolves the booking within the caller's Family, and accepts JSON `{ "reason": "The recorded arrival does not match our evidence." }`. The reason is trimmed and must contain 1–2000 characters; blank or over-limit input returns `400 CareHomes.CheckInDispute.InvalidReason`. An unauthenticated caller receives `401`; an unknown or cross-family booking returns `404 CareHomes.CheckInDispute.NotFound`; a booking without a facility-recorded check-in returns `409 CareHomes.CheckInDispute.InvalidState`.

The first valid submission opens one `Open` Admin case and returns the case representation, including `familyReason`. Repeated submissions for the same booking return the same open case and do not create a second case or replace its reason. The operation does not change `actualCheckInOnUtc` or `actualCheckOutOnUtc`; those facility timestamps remain the source operational record. If the booking was already checked out, the existing checkout timestamp remains unchanged. The case is then handled through the operational Admin list and resolve routes above. A distinct eligible Family identity and a booking owned by that Family with facility check-in recorded are required for runtime verification; the current local fixture does not provide that booking, so this guide documents the contract but does not claim a Bruno/runtime result.

Future-only maintenance mutation is documented in [inventory and availability](inventory-and-availability.md). HC-035 cancellation/refund recovery is included in this guide and is merged on `main`; HC-038 payouts/ledger remain future scope. HC-023 is Done per the owner's closeout decision. Its fresh-database migration application and live notification delivery were not verified; see the task checklist for the evidence and limits.
