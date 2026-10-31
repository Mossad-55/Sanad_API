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
dotnet run --no-build --no-launch-profile --project src/API/Ahlena.API/Ahlena.API.csproj
```

This is a test-only configuration example: it must remain on the pinned local Development target and must not be used for production or a shared environment. There is no public time-control endpoint. `ArrivalLeadDuration` is deliberately not configurable and must remain 24 hours. Overrides without `Enabled=true`, enabling the test clock outside Development, or zero/negative durations are rejected during startup.

## Family routes

All routes require `FamilyAccess` and resolve the caller to a family owner or member. A caller without a family receives `404`; an unauthenticated caller receives `401`.

| Method | Route | Contract |
|---|---|---|
| GET | `/api/v1/family/care-home-bookings` | Lists the caller family's bookings, newest first. |
| GET | `/api/v1/family/care-home-bookings/{bookingId}` | Returns one booking only when it belongs to the caller's family. Unknown or cross-family IDs return `404 CareHomes.Bookings.NotFound`. |
| POST | `/api/v1/family/care-home-bookings/checkout` | Creates a pending payment booking and a 15-minute capacity hold. |
| POST | `/api/v1/family/care-home-bookings/{bookingId}/extensions` | Creates one explicitly requested, pending-payment extension for the current paid-through date. |
| POST | `/api/v1/family/care-home-bookings/{bookingId}/payments/intent` | Creates or replays the Paymob Card/Wallet intent for a pending booking or extension. |

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

**Platform availability.** A new Care-home checkout is refused while the
platform is in Maintenance (`503 Platform.Maintenance`) and when the platform
controls cannot be read (`503 Platform.ControlStateUnavailable`). The refusal
happens before the capacity read, the reservation, and any booking write, so a
rejected checkout leaves no booking and no hold. Local room/bed maintenance
blocks are unrelated to this control and are still applied as documented above.

**Retry safety (`Idempotency-Key`).** The header is optional; without it the
request behaves exactly as before. With it, the trimmed value must be 1–128
printable ASCII characters, otherwise `400
CareHomes.Bookings.IdempotencyKeyInvalid`. Records are scoped to the
authenticated actor, the operation and the key, and the key row commits in the
same transaction as the booking and its hold. The fingerprint covers the
request body and the route identifiers (`facilityId`, `roomTypeId`,
`elderlyId`, `startDate`); server-derived values (price, hold, status,
arrival window) are excluded. The same key with the same fingerprint replays
the stored `200` status and body byte-for-byte with no second booking and no
second 15-minute hold. The same key with a different fingerprint is `409
CareHomes.Bookings.IdempotencyConflict`. Concurrent duplicates execute exactly
once through the unique `(actor_id, operation, idempotency_key)` index; the
loser replays the winner. Because the replay lookup runs inside the
facility/room-type reservation guard, a confirmed replay answers its original
result even while the platform is in Maintenance; a new request is still
refused. Rows are retained for 7 days; no purge worker runs in this slice.

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

**Platform availability.** A *new* payment intent is refused while the platform
is in Maintenance (`503 Platform.Maintenance`), when the selected method is
disabled or not configured on the server (`400 Platform.PaymentMethodUnavailable`
— including ApplePay, which is not supported), or when the platform controls
cannot be read (`503 Platform.ControlStateUnavailable`). The refusal happens
before the payment claim, the Paymob call, and any pending-transaction write.
Replaying a request for an intent that already exists returns that same intent
and is **not** blocked, so a family that already started a payment can still
finish it while the platform is in Maintenance. Settlement of a verified
provider callback is never gated.

## Paid stay extensions (HC-036)

`POST /api/v1/family/care-home-bookings/{bookingId}/extensions` requires `FamilyAccess`; the source booking must belong to the caller's Family. The source must be the current accepted/paid segment and must not have checked out. The endpoint creates a separate linked booking segment for one calendar month beginning exactly at the source `endDate`; month-end dates use the existing `DateOnly.AddMonths` clamp. The paid-through date cannot already be in the past. `extensionOfBookingId` identifies the segment being extended. The original booking remains unchanged.

The extension uses the selected room type from the existing stay and checks the full added interval under the facility/room-type reservation guard. Shared stays require an available bed; Private/Suite stays require an available room. A facility may assign the extension to another available physical room/bed of the same room type using the normal assignment route. The extension snapshots the room type's current monthly price and effective Finance fee/tax rules at request time. It starts as `PendingPayment` with the normal 15-minute capacity hold. The Family then calls the standard `/payments/intent` route with the returned extension `id`; no automatic charge or renewal occurs.

Successful payment changes only the extension segment to `PaidAwaitingDecision`; facility acceptance is still required. Facility rejection fully refunds the extension's `totalAmount` and leaves the previous accepted stay intact. A paid-through boundary with an active extension request cannot be requested twice concurrently. Once an extension is accepted, the next extension is requested against that new segment.

If the existing stay ends before a future extension segment's start date, that future segment is cancelled when paid and the money follows the segment's own revision schedule. Under version 1 (current while the policy ships dormant) the paid extension is fully refunded. Under version 2 the extension decides from its own snapshot: a recorded check-out carries no cause evidence, so the extension never receives the provider-failure full refund on a mere checkout — a provider-caused early end must be recorded as an explicit facility cancellation (which refunds in full) or raised through the dispute/review channel. An unpaid extension is cancelled; if a provider success arrives late, the existing callback path records it and starts the full refund. The same-type capacity and assignment rules still apply. No automatic renewal or cross-room-type change is added.

## Facility-owner routes

These routes require `CareHomeOwnerAccess` and scope every read or decision to the caller's owned facility:

| Method | Route | Contract |
|---|---|---|
| GET | `/api/v1/care-homes/booking-requests/mine` | Lists paid requests for the caller's facility, newest first. |
| POST | `/api/v1/care-homes/booking-requests/mine/{bookingId}/decision` | Accepts or rejects a paid request. |

Decision JSON is `{ "accept": true, "reason": null }` for acceptance or `{ "accept": false, "reason": "Capacity unavailable" }` for rejection. A rejection reason is required (nonblank) but carries **no length cap**: the domain validates only that the reason is nonblank, and the stored `care_homes.bookings.decision_reason` column is an unbounded `text` — the OpenAPI request schema therefore publishes no `maxLength` for it. A paid booking is `PaidAwaitingDecision = 2` with a 24-hour decision hold. Acceptance moves it to `Accepted = 3`. Rejection moves it to `RefundPending = 7` and claims/initiates a full Paymob refund when a transaction exists. A decision outside the paid decision state returns `409 CareHomes.Bookings.InvalidState`; another facility or unknown booking returns `404 CareHomes.Bookings.NotFound`.

The owner list keeps its paid-only selection (`Pending` and `Failed` payments are excluded), its owned-facility scoping (an owner without a facility receives `404 CareHomes.Bookings.NotFound`; another facility's requests never appear) and its newest-first order. Its row type stays `CareHomeBookingListItem` (the same OpenAPI component ID as before this change), and each row additionally carries the stored resident identity/age, the stored responsible contact and care notes, and the stored decision deadline:

```json
[
  {
    "id": "<booking-guid>",
    "facilityId": "<facility-guid>",
    "roomTypeId": "<room-type-guid>",
    "startDate": "2026-11-10",
    "endDate": "2026-12-10",
    "status": 2,
    "paymentStatus": 2,
    "totalAmount": 1150,
    "currency": "EGP",
    "elderlyEnglishName": "Resident English",
    "assignedRoomId": "<room-guid-or-null>",
    "assignedBedId": "<bed-guid-or-null>",
    "actualCheckInOnUtc": null,
    "actualCheckOutOnUtc": null,
    "familyCheckInConfirmedOnUtc": null,
    "refundStatus": 0,
    "refundAmount": null,
    "extensionOfBookingId": null,
    "elderlyArabicName": "اسم المقيم",
    "elderlyAge": 77,
    "responsibleContactName": "Family member",
    "responsibleContactPhone": "+201000000000",
    "responsibleContactRelationship": "Daughter",
    "careNeedsNotes": "Optional note captured at checkout",
    "decisionHoldUntilUtc": "2026-11-10T12:24:00Z"
  }
]
```

`decisionHoldUntilUtc` is the stored decision hold (payment time plus the 24-hour decision window) and is returned **only** while `status` is `PaidAwaitingDecision = 2`; every other state (including an already accepted request, whose stored hold is retained on the booking) returns `null`. The list never calculates, extends or re-derives the deadline. The clinical medical snapshot is never returned, and the resident, contact and care-needs values are the snapshots stored at checkout — the list is a read of persisted state and adds no request/decision behavior.

The Family list route (`GET /api/v1/family/care-home-bookings`) keeps its own separate row type `CareHomeFamilyBookingListItem` unchanged: the same fields in the same order, the same family scoping, no added member, and the same absence of a published response schema. The owner-only additions above cannot appear on a Family row.

## Paymob callback and lifecycle states

`POST /api/v1/payments/webhooks/paymob` remains anonymous at the HTTP authorization layer but requires the configured Paymob HMAC. A valid `chb_` merchant reference dispatches to the Care-home aggregate. Amount mismatch returns `400` (`Paymob.AmountMismatch`); a different transaction for an already paid booking returns `409` (`CareHomes.Bookings.PaymentConflict`); duplicate same-transaction callbacks are acknowledged idempotently. Successful payment during the checkout hold sets `PaidAwaitingDecision = 2`, `PaymentStatus = Paid = 2`, and a 24-hour decision hold. A successful late callback enters `RefundPending` for refund initiation. A failed non-pending callback sets `Cancelled = 6` and `PaymentStatus = Failed = 3`.

The domain statuses are `PendingPayment (1)`, `PaidAwaitingDecision (2)`, `Accepted (3)`, `Rejected (4)`, `Expired (5)`, `Cancelled (6)`, `RefundPending (7)`, `RefundInitiated (8)`, and `Refunded (9)`. Payment statuses are `Pending (1)`, `Paid (2)`, `Failed (3)`, `RefundInitiated (4)`, and `Refunded (5)`. The expiry worker transitions an unpaid checkout hold to `Expired`; an expired paid decision window enters `RefundPending`, then the refund claim/provider flow may persist `RefundInitiated` after a successful refund. In the Development fixture, the hosted sweep calls the Paymob stub immediately, so the final timeout readback is `RefundInitiated (8)`, `PaymentStatus = RefundInitiated (4)`, and `refundStatus = Initiated` until a signed completion callback.

### Cancellation and refund recovery (HC-035)

Family cancellation is `POST /api/v1/family/care-home-bookings/{bookingId}/cancel` and requires `FamilyAccess`. It is scoped to the caller's Family. A pending unpaid checkout hold is cancelled without a refund, releasing its capacity. A paid booking cancelled before the recorded facility check-in receives a full refund of `totalAmount`, even if the facility already approved it. After actual check-in is recorded, Family cancellation refunds 50% of `totalAmount`, rounded to two decimals. Cancellation after checkout or in another unsupported booking state returns `409 CareHomes.Bookings.InvalidState`. This paragraph is the version-1 (and dormant) rule. Under version 2 a cancellation after a recorded check-in refunds nothing — delivered care is not refundable — and the booking instead takes the zero-award shape described below.

The facility owner may cancel an accepted stay with `POST /api/v1/care-homes/booking-requests/mine/{bookingId}/cancel`, requiring a nonblank `{ "reason": "..." }` and `CareHomeOwnerAccess`. The cancellation reason has the same shape as the rejection reason: required and nonblank, **no length cap** (unbounded `text` column, no `maxLength` in the request schema). Facility cancellation returns the full `totalAmount`, including after check-in. Existing owner rejection and decision-timeout paths also return the full amount.

#### Refund policy versions, snapshots and the scheduled-start policy

Booking responses built on `CareHomeBookingResponse` (checkout, facility decision, extension purchase, single-booking reads) carry `refundPolicyVersion`, `refundPolicyDocumentEffectiveOn`, and `cancellationTerms`. The version is stamped at creation and dispatches every later money decision, so no booking is ever repriced against a later policy text (clause 12; decisions #7, #8, #10).

- `refundPolicyVersion = 1` is the pre-policy behavior described above: full refund before recorded check-in, half of `totalAmount` after it, rounded with the original half-away-from-zero rule. Every booking created while the care-home scheduled-start policy ships dormant carries version 1 with a default `0001-01-01` document date and `cancellationTerms = null`, and its behavior is byte-for-byte unchanged.
- `refundPolicyVersion = 2` (bookings created after the policy is activated) decides from the booking's own snapshot: the facility's structured cancellation terms as displayed on its approved profile at purchase time, or the clause-2 default schedule when the snapshot carries no terms. The first satisfied tier wins; a cancellation at or after the scheduled stay start, or below every tier's deadline, awards zero. A recorded check-in makes a version-2 family cancellation refund nothing, wherever it sits on the window (delivered care is not refundable). Extension segments snapshot the policy/terms applicable at their own purchase (decision #8) — they never inherit the root stay's snapshot. An explicit facility cancellation refunds its extension in full under any revision (a provider failure is not an act the customer's timing can shrink); every other ending — a family cancellation, or a recorded check-out, which carries no cause evidence — decides the extension from its own snapshot.
- A zero-award cancellation is a valid outcome, not an error: the booking becomes `Cancelled (6)` and remains `PaymentStatus = Paid` with `refundStatus = None` and `refundAmount = null`. The captured money stays with the facility and the booking never enters the refund lifecycle. Facility payouts and the owner ledger do NOT include this shape or a refunded booking with no recorded stay: payout eligibility stays checkout-based exactly as before this lane, and how the retained money is allocated (facility payouts, debt netting, or a new payable category) is an explicit Owner decision still pending — see the lane report.

Dormancy is a single switch (`CareHomeRefundPolicySnapshotResolver.ScheduledStartPolicyActivated`); flipping it is a separate, reviewed change that simultaneously turns on version-2 stamping, snapshot display, and the scheduled-start money behavior. The effective document date is `2026-10-22`.

Refund initiation is concurrency-claimed once per booking. Paymob failure persists `refundStatus = Failed`, allowing an Admin retry at `POST /api/v1/admin/care-homes/bookings/{bookingId}/refund/retry`; a failed refund is retryable, and so is a pending refund that was never claimed or sent (the crash window between the begin-refund save and the claim save — a claim always precedes a send, so such a row is proven not-sent; the send claims atomically, and a claimed attempt is never retryable from this route). Operational Admins may record an externally completed refund at `POST /api/v1/admin/care-homes/bookings/{bookingId}/refund/record-completed` with `{ "reference": "...", "reason": "..." }`. Both Admin routes require `CareHomesOperationalAdmin` (SuperAdmin or SupportAdmin; ContentAdmin is denied). Manual completion does not call Paymob. Duplicate requests/callbacks do not issue another refund or replace a completed state. Booking readbacks include `refundStatus` and `refundAmount`; the Admin booking detail additionally exposes `refundPolicyVersion`, `refundAttemptClaimId`, `isRefundOutcomeAmbiguous`, `isRefundAttemptInFlight`, and the latest attempt's `refundAmbiguityAwardAmount`/`refundAmbiguityReason`/`refundAmbiguityRaisedOnUtc`/`refundAmbiguityClearedOnUtc`.

When Paymob cannot confirm a refund outcome (`Paymob.RefundOutcomeUnknown`, or a refund call that was cancelled or timed out before its outcome could be read — mapped as `Paymob.RefundCallNotConfirmed`), the attempt is parked under a durable ambiguity marker: the caller receives `CareHomes.Refund.OutcomeAmbiguous`, the pre-call claim and its attempt identity (`refundAttemptClaimId` on the Admin booking detail) stay persisted, no money state moves, and no retry succeeds while the marker stands. Every provider failure is classified against the gateway's actual contract before it touches the booking: `Paymob.NotConfigured` (nothing was sent) and `Paymob.RefundRejected` (the provider's own 4xx refusal) release the claim into the ordinary retryable failed state; everything else — the unknown class, any unrecognised code, and cancelled/timed-out calls — raises the ambiguity marker and is never retried blindly. On the wire, the shared mapper now answers the two refund-critical gateway codes directly: `Paymob.RefundRejected` surfaces as `502 Bad Gateway` (a definitive refusal that proved no money moved) and `Paymob.RefundOutcomeUnknown` as `409 Conflict` (an unconfirmable outcome requiring reconciliation). Both keep their stable error code in the problem body, and the HTTP status alone never grants retry permission — the booking's refund ledger and state fences (claim, ambiguity marker, refund state) remain the authoritative retry gate.

Operational Admins clear the marker at `POST /api/v1/admin/care-homes/bookings/{bookingId}/refund/reconcile` with `{ "expectedAttemptClaimId": "<the refundAttemptClaimId read from the Admin booking detail response (GET /api/v1/admin/care-homes/bookings/{bookingId})>", "refundDidMove": true|false, "verifiedProviderReference": "..." (required when refundDidMove is true), "evidence": "..." (≤ 1966 characters) }`. The `expectedAttemptClaimId` is a compare-and-swap key: it must match the booking's current in-flight attempt, so a stale operator card — or a request aimed at an attempt that has since concluded and been superseded — is refused with `409 CareHomes.RefundReconciliation.AttemptMismatch` instead of reconciling a different attempt than the one verified; an absent (empty) id is a stable `400 CareHomes.RefundReconciliation.InvalidAttemptClaim`, distinct from authentication. A verified movement completes the refund for exactly the recorded award, once, and discharges the held claim; a verified non-payment returns the booking to the retryable failed state; the **latest attempt's** record (reason, raised instant, award) is retained as the audit trail — a second ambiguous attempt overwrites it, and `isRefundOutcomeAmbiguous` remains the only authoritative open-question gate readers key on. A claimed attempt with no recorded provider outcome (a process crash between the persisted claim and the provider result) is deliberately not reconcilable from this surface: it returns `409 CareHomes.RefundReconciliation.AttemptInconclusive`, because nothing in the system can prove the provider call is no longer in flight (elapsed age is not evidence), and resetting such a claim could race a live send into a second refund — its recovery is queued behind the provider attempt-liveness/idempotency seam described in the lane report. Evidence is bounded against the composed clearance column before anything is applied: missing evidence returns `400 CareHomes.RefundReconciliation.EvidenceRequired`, and evidence that would overflow the 2000-character column returns `400 CareHomes.RefundReconciliation.EvidenceTooLong`; a concurrent writer recovering first returns `409 CareHomes.RefundReconciliation.ConcurrencyConflict`. The reconcile response is mapped before the committing save, so a mapping failure (for example an unreadable stored snapshot on the response view) is reported with nothing persisted — a committed outcome is final and the returned body is exactly the committed state.

The booking statuses also include `Refunded (9)` for completed refunds. `refundStatus` distinguishes `None`, `Pending`, `Initiated`, `Failed`, `Completed`, and `ManuallyCompleted`; a successful Paymob refund request is `Initiated`, and a signed refund callback matching the stored refund or payment reference advances it to `Completed`. Only a **settled** callback (`success=true`, not `pending`) completes the refund, and a callback that matches the stored refund reference must carry the recorded award amount; Paymob reports a refund as a **child** transaction whose own id is the stored refund reference (the refunded flag lands on the parent payment), so both the child and a refunded parent callback are handled by the same endpoint while a void or unrelated child never completes anything. Completion is matched only on the transaction id, because the routing flags that mark a child (`has_parent_transaction`, `is_refunded`) do not prove the transaction *is* a refund — `is_refund` is not part of Paymob's signed field list. A settled callback that arrives before the processor has persisted `refundReference` (it raced the refund call) therefore matches only by the signed `order.id`, which is used purely to recognise it: it is refused as not-yet-applicable rather than used to complete, so a non-refund child that happens to share the order and amount can never fabricate a completed refund, and the callback is not silently acknowledged-and-lost. When a callback's completion save instead races another writer that already committed this booking's refund (the processor's own readback, or a duplicate delivery), the callback is acknowledged only when the committed row proves *this* booking refunded — by the stored child refund reference, or, on the refunded-parent path, by the booking's recorded payment transaction; a committed row that is not this callback's refund is never credited to it.

Refund completion does not depend on callback (re)delivery. Immediately after the processor durably persists the refund reference returned by the refund call, it reads that exact transaction back from Paymob over the authenticated merchant channel (`GET /api/acceptance/transactions/{id}`, authorized by the auth token exchanged from the API key — the same contract the subscription-management namespace uses; the secret key is rejected there and no credential ever appears in a URL or log). The booking advances to `Completed` only when the provider attests a **settled refund** for that exact id (`is_refund=true`, `success=true`, `pending=false`) and the readback cross-correlates with the server's own stored state: amount equal to the recorded award, currency `EGP`, `order.id` equal to the stored intention order, and `parent_transaction` equal to the stored capture transaction. Any refusal — an unreadable or unavailable readback, a pending/unsuccessful/non-refund transaction, or any mismatch of id, amount, currency, order or original capture — leaves the booking at `Initiated` with the reference persisted, and can never cause a second refund request; the signed callback and Operator reconciliation remain the fallback. A readback is therefore a strict authenticated attestation, never inferred from unsigned callback flags, and a gateway that cannot be read back never fabricates a settled refund. A 2xx refund response carrying no usable reference skips the readback entirely. The fixture-only synthetic refund reference (`fixture-refund-…`) is never sent to the provider: the refund-outcome seam refuses to verify it locally, while a real (non-synthetic) id passes through read-only.

Stable mapped booking errors are `CareHomes.Bookings.NotFound` (404), `CareHomes.Bookings.InvalidState` (409), `CareHomes.Bookings.CapacityConflict` (409), `CareHomes.Bookings.PaymentConflict` (409), `CareHomes.Bookings.RefundNotRetryable` (409), and `CareHomes.Bookings.ChargesNotConfigured` (503). The lane's new codes — `CareHomes.Refund.OutcomeAmbiguous`, `CareHomes.RefundReconciliation.EvidenceRequired`, `CareHomes.RefundReconciliation.EvidenceTooLong`, `CareHomes.RefundReconciliation.InvalidAttemptClaim`, `CareHomes.RefundReconciliation.NoWindowToReconcile`, `CareHomes.RefundReconciliation.AttemptMismatch`, `CareHomes.RefundReconciliation.AttemptInconclusive`, `CareHomes.RefundReconciliation.ConcurrencyConflict`, and `CareHomes.Bookings.UnreadableSnapshot` (stored-snapshot integrity on booking, profile, and discovery reads) — have dedicated status mappings in the shared mapper: 409 for `OutcomeAmbiguous`, `NoWindowToReconcile`, `AttemptMismatch`, `AttemptInconclusive`, `ConcurrencyConflict`, and `UnreadableSnapshot`; 400 for `EvidenceRequired`, `EvidenceTooLong`, and `InvalidAttemptClaim`. Validation and the 24-hour arrival rule use the API's normal validation/problem mapping; the concrete domain code for the arrival rule is `CareHomes.Bookings.Invalid`.

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
| GET | `/api/v1/care-homes/booking-requests/mine/{bookingId}/operational` | Returns the booking's stored resident Arabic/English name and age snapshots, the currently effective assignment, append-only assignment history, actual check-in/out, and Family-confirmation readback fields. |

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
  "residentArabicName": "Resident Arabic",
  "residentEnglishName": "Resident English",
  "residentAge": 77,
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

Both routes require `CareHomesOperationalAdmin`, which permits `SuperAdmin` and `SupportAdmin` with normal access. `ContentAdmin` is not permitted. Resolution JSON is `{ "effectiveCheckInOnUtc": "2026-11-10T12:00:00Z", "evidence": "signed arrival log", "reason": "Facility record reconciled" }`. Evidence, reason, and a UTC effective check-in time are required. Missing cases return `404 CareHomes.CheckInDispute.NotFound`; repeated resolution or invalid case state returns `409 CareHomes.CheckInDispute.InvalidState`. Responses expose `id`, `bookingId`, `facilityId`, `checkoutOnUtc`, `status`, `openedOnUtc`, `openedBy`, `effectiveCheckInOnUtc`, `evidence`, `reason`, `resolvedOnUtc`, and `resolvedBy`; these fields provide the audit trail without replacing the original facility timestamps. The Admin **list** response additionally carries the booking's stored `residentArabicName`/`residentEnglishName` snapshots and the original booking `originalBookingTotalAmount` in `originalBookingCurrency`; the currency carries the current fixed Care Home currency convention (`EGP`) — the booking does not persist a currency property — and the amount remains the booking's original `TotalAmount`, not a refund, payout, or net amount. The Admin resolve and Family submission responses keep their pre-existing shapes without these enrichment fields.

Facility owners can review cases without changing them using `GET /api/v1/care-homes/check-in-disputes/mine` (`CareHomeOwnerAccess`). The server derives the facility from the authenticated owner; no facility ID is accepted. Optional `page` defaults to 1 (minimum 1), `pageSize` defaults to 25 (1–100), and `status` accepts `Open` or `Resolved`, case-insensitively. Invalid query values receive the standard validation problem response. The result is `{ page, pageSize, totalCount, items }`; the count is after owner/status filtering and before paging. Items are ordered by opening time descending, then case ID descending. If the owner has no facility, the endpoint deliberately returns a successful empty page (unlike existing finance reads).

Each item contains case/booking/facility IDs, status, opening and nullable checkout times, nullable Family reason and resident name snapshots, nullable original booking amount/currency, and nullable effective check-in/resolution times. A missing related booking makes resident names and original amount/currency null; otherwise the stored booking total and current fixed `EGP` convention are shown. Open cases have null outcome timestamps. The owner projection omits Admin evidence, rationale and resolver identity, and exposes no family/resident identity or payment details. Loading, empty and errors should be presented as read-only list states; Ahlena support handles disputes. An Admin's effective case outcome does not change the facility-recorded check-in or checkout timestamps. There is no owner resolve/mutation action.

Families may also submit a check-in dispute after a facility-recorded check-in:

`POST /api/v1/family/care-home-bookings/{bookingId}/check-in-dispute`

This route requires `FamilyAccess`, resolves the booking within the caller's Family, and accepts JSON `{ "reason": "The recorded arrival does not match our evidence." }`. The reason is trimmed and must contain 1–2000 characters; blank or over-limit input returns `400 CareHomes.CheckInDispute.InvalidReason`. An unauthenticated caller receives `401`; an unknown or cross-family booking returns `404 CareHomes.CheckInDispute.NotFound`; a booking without a facility-recorded check-in returns `409 CareHomes.CheckInDispute.InvalidState`.

The first valid submission opens one `Open` Admin case and returns the case representation, including `familyReason`. Repeated submissions for the same booking return the same open case and do not create a second case or replace its reason. The operation does not change `actualCheckInOnUtc` or `actualCheckOutOnUtc`; those facility timestamps remain the source operational record. If the booking was already checked out, the existing checkout timestamp remains unchanged. The case is then handled through the operational Admin list and resolve routes above. A distinct eligible Family identity and a booking owned by that Family with facility check-in recorded are required for runtime verification; the current local fixture does not provide that booking, so this guide documents the contract but does not claim a Bruno/runtime result.

Future-only maintenance mutation is documented in [inventory and availability](inventory-and-availability.md). HC-035 cancellation/refund recovery is included in this guide and is merged on `main`; HC-038 payouts/ledger remain future scope. HC-023 is Done per the owner's closeout decision. Its fresh-database migration application and live notification delivery were not verified; see the task checklist for the evidence and limits.
