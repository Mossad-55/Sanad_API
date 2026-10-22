# Family Bookings (request lifecycle)

Completed visits can have one immutable caregiver visit report. See [Family reports](reports.md) for
the caregiver submission route, family feed, fields, and report-specific error codes.

Routes for creating a care booking (checkout), paying it through Paymob, browsing requests by tab, reading booking details, and cancelling. The family pays the caregiver's base price plus the independently calculated shared platform fee and tax; the caregiver then accepts or declines.

All routes live under `/api/v1/family/bookings...`.

## Booking review

`POST /api/v1/family/bookings/{bookingId}/review` accepts
`{ "rating": 1, "comment": "...", "isAnonymous": false }`. Only a family
owner/editor may submit, and only after the booking status is `Completed`. A
booking accepts one review; it is not editable. Rating is 1â€“5, comment is
optional and limited to 2,000 characters. Success returns `201`; not completed
returns `409 Bookings.ReviewNotAllowed`, a duplicate returns
`409 Bookings.ReviewExists`, an invalid review returns `400
Bookings.InvalidReview`; a foreign booking returns `403 Bookings.AccessDenied`,
and an unknown booking returns `404 Bookings.NotFound`.

## Access

- **Normal JWT** for a **Family** account (`access_type = Normal`, `account_type = Family`). Policy: `FamilyAccess`.
- The caller must be an active member of a family.
- **Checkout** requires an **Owner** or **Editor** role (`403 Bookings.UnauthorizedRole` for Viewers).
- **List / detail / cancel / payment intent** are available to every family member â€” but only for bookings of their **own** family (`404 Bookings.NotFound` for a foreign booking on intent, `404 Bookings.BookingNotInFamily` on cancel, `404 Bookings.FamilyNotFound` when the caller belongs to no family).

## Lifecycle, pricing & slot rules

```text
PendingPayment â”€â”€payâ”€â”€â–¶ PendingCaregiverApproval â”€â”€acceptâ”€â”€â–¶ Confirmed â”€â”€startâ”€â”€â–¶ InProgress â”€â”€completeâ”€â”€â–¶ Completed
      â”‚                         â”‚
      â”œâ”€ cancel â”€â–¶ CancelledByFamily        â”œâ”€ decline â”€â–¶ DeclinedByCaregiver
      â””â”€ expire â”€â–¶ Expired                  â”œâ”€ cancel (caregiver) â”€â–¶ CancelledByCaregiver
                                            â””â”€ expire â”€â–¶ Expired
```

- **Server-side pricing:** the checkout body carries **no price fields and no caregiver type**. The server loads the caregiver's real pricing (the same engine behind the discovery quote endpoint), rejects an inactive caregiver or an absent shared Finance rule, and stores an immutable snapshot of the base fee, independent platform fee/tax rates and amounts, charge-rule version, total, and currency. The caregiver receives the full base fee.
- **Acceptance window:** the caregiver must respond within `min(now + 24h, booking start)`; acceptance after the deadline is rejected. A payment whose success webhook arrives after the deadline is **not** honoured as a booking â€” the booking expires and the payment is refunded automatically (see refunds below).
- **Slot reservation:** a booking in `PendingPayment` or `PendingCaregiverApproval` already blocks the same caregiver/date/overlapping-time slot (`409 Bookings.ScheduleConflict`).
- **Snapshot immutability:** later caregiver price edits never change an existing booking.
- **Payments (contract as of `b556c4e`):** checkout returns no payment data. The app starts a payment with the **payment intent** endpoint (section 5), which creates a Paymob **intention** server-side and returns `clientSecret` + `publicKey` for the **Paymob mobile SDK** (embedded card / wallet UI â€” card data never touches Sanad servers). The Paymob **webhook** (`POST /api/v1/payments/webhooks/paymob`, anonymous HMAC-SHA512 from query / JSON `hmac` / `X-Paymob-Hmac`, idempotent, amount-checked) is the **single source of truth**: only the webhook can mark a booking paid. Refunds are **automatic** on caregiver decline, on family cancellation before acceptance, and when a late payment arrives after the deadline (pay â†’ expire â†’ refund).

## Endpoints

| Method | Route | Description | Role |
|---|---|---|---|
| `GET` | `/api/v1/family/bookings?tab={tab}` | Tab list (newest first) | Any member |
| `GET` | `/api/v1/family/bookings/{bookingId}` | Booking detail (own family only) | Any member |
| `POST` | `/api/v1/family/bookings/checkout` | Create booking + server-side price snapshot | Owner / Editor |
| `POST` | `/api/v1/family/bookings/{bookingId}/payments/intent` | Paymob intention + mobile-SDK handoff | Any member |
| `POST` | `/api/v1/family/bookings/{bookingId}/cancel` | Cancel an own-family booking | Any member |
| `POST` | `/api/v1/caregiver/bookings/{bookingId}/provider-outcome` | Caregiver records a provider cancellation or no-show (full refund) | Assigned caregiver |
| `POST` | `/api/v1/admin/bookings/{bookingId}/outcome` | Admin records lateness, inaccessibility, safety, or force majeure | Admin |

### Tabs (`tab`, enum int â€” the name string is also accepted)

| Value | Meaning | Statuses included |
|---|---|---|
| `1` `Upcoming` (default) | Awaiting payment / caregiver response / confirmed | `PendingPayment (1)`, `PendingCaregiverApproval (2)`, `Confirmed (3)` |
| `2` `Current` | Care in progress | `InProgress (4)` |
| `3` `Past` | Finished | `Completed (5)`, `CancelledByFamily (6)`, `DeclinedByCaregiver (7)`, `CancelledByCaregiver (8)`, `Refunded (9)`, `Expired (10)` |

## Request & Response Payloads

### 1. Create booking (checkout)

`POST /api/v1/family/bookings/checkout`

```json
{
  "elderlyId": "0198e2c1-1111-7777-8888-000000000001",
  "caregiverId": "0198e2c2-2222-7777-8888-000000000002",
  "shiftType": 1,
  "bookingDate": "2026-06-08",
  "startTime": "10:00",
  "endTime": "12:00",
  "serviceAddress": "14 Ø´Ø§Ø±Ø¹ Ø§Ù„ÙƒÙˆØ±Ù†ÙŠØ´ØŒ Ø§Ù„Ø¥Ø³ÙƒÙ†Ø¯Ø±ÙŠØ©",
  "specialInstructions": "Ø§Ù„Ù…Ø±ÙŠØ¶ ÙŠØ¹Ø§Ù†ÙŠ Ù…Ù† Ø¶ØºØ· Ø§Ù„Ø¯Ù…",
  "bookingAreaId": null,
  "bookingServiceId": null
}
```

| Field | Type | Notes |
|---|---|---|
| `elderlyId` | UUID | Must belong to the caller's family (`404 Bookings.ElderlyNotFound`). |
| `caregiverId` | UUID | Must be an **Active** caregiver; type and price are resolved server-side. |
| `shiftType` | enum (int) | `1` HomeVisit, `2` EightHourShift, `3` TwelveHourShift, `4` TwentyFourHourShift, `5` Hourly. Medical products only for Medical caregivers; `Hourly` only for Companion. |
| `bookingDate` | `YYYY-MM-DD` | Cannot be in the past. |
| `startTime` / `endTime` | `HH:mm` | Must not overlap an existing pending/active booking of that caregiver. |
| `serviceAddress` | string â‰¤ 500 | Where the care takes place. |
| `specialInstructions` | string? â‰¤ 1000 | Optional family notes shown on the caregiver's request card. |
| `bookingAreaId` + `bookingServiceId` | UUID? | Optional immutable pricing attribution (Chat Plus): both together or neither. When supplied, both must be nonempty GUIDs naming active catalogue entries selected by the booked caregiver, else `400` through the owning booking vocabulary. Omission preserves legacy checkout and contributes no pricing sample; retained after catalogue/profile changes, never backfilled. See `docs/app/chat/pricing-insights.md`. |

**`200` response**

```json
{
  "bookingId": "0198e3f0-3333-7777-8888-000000000003",
  "status": 1,
  "totalPayableAmount": 2875.00,
  "currency": "EGP"
}
```

`totalPayableAmount = base fee + platform fee amount + tax amount`; both percentages are calculated from the base fee and rounded independently. The exact rates and rule version are server-selected and preserved in the booking snapshot. Payment starts with the intent endpoint below.

**Platform availability.** A new checkout is refused while the platform is in
Maintenance (`503 Platform.Maintenance`) and when the platform controls cannot
be read (`503 Platform.ControlStateUnavailable`). The refusal happens before any
slot reservation or booking is created, so a rejected checkout leaves no
booking and no hold. Reads, cancellation, refunds and the booking lifecycle stay
available. The app can read the current state and the available payment methods
from the public status route â€” see
[platform business status](../public/platform-status.md).

### 2. Tab list

`GET /api/v1/family/bookings?tab=1` â†’ `FamilyBookingListItem[]`: booking id, caregiver id, senior Arabic/English names, date + time window, `shiftType`, `status`, `totalPayableAmount`, `currency`. The caregiver name/avatar fields are reserved and currently return `null` pending the caregiver-name join: a booking stores the **caregiver aggregate id**, and the caregiver's identity name lives behind the Caregivers/Identity boundary, which exposes no seam to the Families module today. They stay honest `null` â€” the app must not render an invented caregiver name. The caregiver's public card is available separately from `GET /api/v1/caregivers/{caregiverId}`.

The senior's date of birth, gender and photo are **not** on the list item; they are on the booking detail (below) and on the dependent route.

### 3. Booking detail

`GET /api/v1/family/bookings/{bookingId}` â†’ the exposed price fields (`baseCaregiverFee`, `platformFeePercentage`, `platformFeeAmount`, `totalPayableAmount`, `currency`), the elderly summary (fields are `null` when the dependent record is absent â€” the API never fabricates values), lifecycle timestamps (`paidOnUtc`, `confirmedOnUtc`, `startedOnUtc`, `completedOnUtc`, `cancelledOnUtc`, `refundedOnUtc`), `refundState` (`1` NotApplicable, `2` Failed Paymob refund, `3` Succeeded, `4` NoRefundDue, `5` Pending`), `cancellationReason`, and `caregiverNotes`. The persisted booking snapshot also contains the tax rate/amount and charge-rule version, but those fields are not currently exposed by this response contract.

#### The elderly summary

| Member | Type | Meaning |
|---|---|---|
| `elderlyId` | UUID | The dependent of the booking. |
| `arabicFullName` / `englishFullName` | string or `null` | The senior's stored names; `null` when the dependent row is gone. |
| `gender` / `dateOfBirth` | int or `null` | The senior's stored gender and date of birth; `null` when the dependent row is gone. |
| `healthNotes` | string or `null` | The free-text health notes the family stored on the dependent row. |
| `hasPhoto` | bool | The dependent has a stored photo. The bytes are read from `GET /api/v1/family/dependents/{elderlyId}/photo` (same family access as the booking detail); no photo URL is projected on the booking. |
| `emergencyContact` | object or `null` | The dependent's stored emergency contact â€” `{ name, relationship, phoneNumber }`. It is `null` when none is stored. The family may already read the same contact on `GET /api/v1/family/dependents/{dependentId}/emergency-contact`, so projecting it here adds no new access. **The caregiver booking detail never receives it** (`emergencyContact: null`); a caregiver reads it only through the live-window route `GET /api/v1/caregiver/bookings/{bookingId}/emergency-contact`. |

#### Where the other booking-screen values come from

- **Family names and avatar** live on the family profile only: `GET /api/v1/family` returns the family name and each member's display name and avatar reference. The booking responses do not repeat them.
- **The caregiver's name and avatar** are not on the booking (see the tab list above); use `GET /api/v1/caregivers/{caregiverId}`.

#### Caregiver-only members

`visitHistory`, `upcomingVisits` and `familyReview` belong to the caregiver
booking detail and are always `null` on the family booking detail â€” they are
read from the caregiver's own records, not from the family's.

### 4. Cancel

`POST /api/v1/family/bookings/{bookingId}/cancel`

```json
{ "reason": "Ø¸Ø±Ù Ø·Ø§Ø±Ø¦" }
```

Family Owner or Editor may cancel; Viewer receives `403 Bookings.UnauthorizedRole`. From `PendingPayment` or `PendingCaregiverApproval`, a plain optional note is allowed but `reasonCategory` is rejected. From `Confirmed`, a valid `reasonCategory` and non-blank `reason` are mandatory. Unpaid bookings never refund uncaptured money. Unsupported states return `409 Bookings.Domain.InvalidOperation`.

**Which policy applies is decided at booking time, not at cancellation time.** Every booking records the policy revision it was sold under, and the refund entitlement is always computed with that recorded revision, even if the current policy later changes. Revision 2 applies to bookings **created after the Owner's activation order**; activating it is a separate release decision, and until then new bookings continue to be sold under revision 1.

| Sold revision | Entitlement for a family cancellation of an accepted (paid) booking |
|---|---|
| `1` (legacy) | Full captured refund when the cancellation happens before **60 minutes from the server-recorded acceptance time**; at or after 60 minutes the entitlement is `NoRefundDue`. |
| `2` (current) | Full captured refund when the cancellation is at or before **24 hours before the scheduled start**; **50%** of the captured amount when it is later than 24 hours before the start but before the visit has started; `NoRefundDue` once the visit has started. |

Revision 2 also refunds per delivered session when the booking purchased a multi-session package: each session is allocated a share of the captured amount (the shares sum exactly to the captured amount, and any remainder piastre goes to the earliest sessions), and only the sessions that were **not** delivered are refunded. The award is always clamped to what is still refundable â€” the captured amount minus anything already refunded â€” so repeated or partial refunds can never exceed what was paid.

### 5. Payment intent (Paymob mobile-SDK handoff)

`POST /api/v1/family/bookings/{bookingId}/payments/intent`

```json
{
  "method": 1,
  "billing": {
    "firstName": "Ø£Ø­Ù…Ø¯",
    "lastName": "Ø¹Ù„ÙŠ",
    "email": "ahmed@example.com",
    "phoneNumber": "+201012345678"
  }
}
```

| Field | Type | Notes |
|---|---|---|
| `method` | enum (int) | `1` Card, `2` Wallet (mobile wallets, e.g. Vodafone Cash). `3` ApplePay is reserved â€” currently `409 Paymob.MethodNotAvailable` until Paymob enables it. |
| `billing` | object | Required by Paymob: first/last name, email, phone number. The app should auto-fill these from the family profile. |

Only a booking in `PendingPayment` can start a payment; the amount and currency always come from the booking's immutable price snapshot (never from the client).

**Platform availability.** A new payment intent is refused while the platform is
in Maintenance (`503 Platform.Maintenance`), when the selected method is
disabled or not configured on the server (`400 Platform.PaymentMethodUnavailable`
â€” this includes ApplePay, which E04 does not support), or when the platform
controls cannot be read (`503 Platform.ControlStateUnavailable`). The refusal
happens before any Paymob call and before any pending transaction is written, so
a rejected request leaves no partial payment state. Read the currently
available methods from the public status route â€” see
[platform business status](../public/platform-status.md).

**`200` response**

```json
{
  "bookingId": "0198e3f0-3333-7777-8888-000000000003",
  "paymobOrderId": "0198e3f0-3333-7777-8888-000000000003",
  "amount": 575.00,
  "currency": "EGP",
  "clientSecret": "egy_csk_test_REDACTED_ROTATE_IF_LIVE",
  "publicKey": "pk_test_626xxxxxxxxxxxxx"
}
```

| Field | Notes |
|---|---|
| `paymobOrderId` | Equals the `bookingId` â€” Paymob echoes it back on the webhook as `merchant_order_id`. |
| `amount` / `currency` | From the immutable price snapshot: base fee plus the independently calculated platform fee and tax. |
| `clientSecret` + `publicKey` | Pass both to the Paymob mobile SDK to present the embedded checkout (card form or wallet PIN screen). The SDK result is **UI-only**. |

**How the app completes a payment**

1. Call the intent endpoint, receive `clientSecret` + `publicKey`.
2. Initialize the Paymob mobile SDK with them; the user completes card/wallet entry inside the SDK (no WebView, no card data in the app's code).
3. Show a *processing* state and poll `GET /family/bookings/{bookingId}` (~3s, cap ~60s).
4. Paymob calls the Sanad webhook; only the webhook moves the booking `PendingPayment â†’ PendingCaregiverApproval` (paid) or records a failure. Never trust the SDK callback or redirect alone.

**Automatic refunds (full amount, via Paymob)**

| Trigger | Outcome |
|---|---|
| Caregiver declines (`PendingCaregiverApproval`) | Booking `DeclinedByCaregiver` â†’ automatic refund â†’ `Refunded` |
| Family cancels before caregiver acceptance | Booking `CancelledByFamily` â†’ full captured refund when paid |
| Family cancels an accepted booking within the sold revision's full-refund window | Booking `CancelledByFamily` â†’ full captured refund when paid |
| Family cancels an accepted booking outside that window | Booking `CancelledByFamily` â†’ the revision's prorated award (a partial refund), or `NoRefundDue`; the award â€” never the captured amount â€” is what is refunded |
| Payment webhook arrives after the acceptance deadline | Booking `Expired` â†’ automatic refund â†’ `Refunded` |

If the refund call fails at the gateway, the booking still transitions and the cancellation fact is retained; `refundState` is `2` `Failed` and the outstanding settlement is retried by ops. A policy-denied cancellation is `4` `NoRefundDue`, not `Failed`, and must not be retried. A **partial** award (`PartialRefund`) is judged by what actually settled, never by the entitlement alone: once the owed amount is recorded as refunded it reads `3` `Succeeded`, and if the provider definitively refused it reads `2` `Failed` so the remainder stays retryable. A gateway that could not confirm the outcome is different again â€” it reads `5` `Pending` and refuses every further refund until reconciled (see below).

**Provider and platform outcomes (revision 2)**

Revision-2 bookings also refund when the provider is at fault, when the recipient could not be reached, on a safety cancellation, or on a force-majeure event. These are recorded by the caregiver or by an administrator, not through the family cancel route:

| Outcome | Recorded by | Refund |
|---|---|---|
| Caregiver cancels an accepted booking | `POST /api/v1/caregiver/bookings/{bookingId}/provider-outcome` with `outcomeReason: 1` | Full captured refund (provider fault). |
| Caregiver does not show up | same route with `outcomeReason: 2` | Full captured refund. |
| Provider arrives more than **60 minutes** late | `POST /api/v1/admin/bookings/{bookingId}/outcome` with `outcomeReason: 3` and `providerLatenessMinutes` | Full captured refund. Lateness the customer accepted in advance (`customerConsentedToLateness: true`) is a service issue, not a refund trigger. |
| Recipient could not be reached / access denied | admin route with `outcomeReason: 4` | `NoRefundDue` â€” the customer's own inaccessibility is not a refund ground. |
| Safety cancellation | admin route with `outcomeReason: 5` | Full captured refund. |
| Force majeure | admin route with `outcomeReason: 6` and `forceMajeure: true` | Full captured refund, regardless of how late the decision was made. |

Every outcome recorded on an accepted booking requires the accepted-booking feedback shape â€” a valid `reasonCategory` and a non-blank `reason` â€” exactly like a family cancellation from `Confirmed`. A provider cancellation or no-show therefore never lands on an accepted booking without a recorded reason.

**Reconciling an unconfirmed refund**

Every refund attempt is **claimed and committed before the gateway is called**: the exact award, its correlation id and an in-flight flag are persisted first. That is what makes the attempt exclusive â€” a second request reading the same booking cannot commit its own claim, so it never reaches the gateway â€” and it is what makes an interrupted attempt recoverable. If the process dies between the claim and the gateway call, or between the gateway call and the result save, the claim survives: the booking is not automatically refunded again.

An interrupted attempt â€” and an attempt whose gateway outcome was not confirmed â€” leaves the booking **non-retryable** and reads **`5` `Pending`**: either the attempt's ambiguity marker stands, or its claim is still in flight, and in both cases the award is recorded but nothing is booked against the refund ceiling. **Every further refund path on that booking is refused** (family cancel re-issue, caregiver cancel/decline replay, admin retry and outcome) until an administrator reconciles it. This is deliberate â€” a blind retry could return the money a second time.

Because a claim may still be live at the gateway, it cannot be cleared as â€œdid not moveâ€ on an operator assertion alone. A process that died before making the gateway call has no provider reference to verify, so that booking stays **Pending** and must be escalated; it is never reset by age or by editing the database.

| Route | Body | Description |
|---|---|---|
| `POST /api/v1/admin/bookings/{bookingId}/refund/reconcile` | `{ "expectedAttemptClaimId": guid, "evidence": string, "refundDidMove": bool, "verifiedProviderReference": string? }` | The only command that clears the marker or an interrupted claim. `expectedAttemptClaimId` must match the booking's current attempt, so a stale operator action cannot settle a different attempt. `refundDidMove: true` requires the provider's own refund reference and records the attempt's award against the ceiling exactly once (so the money cannot be refunded again); `false` leaves the ceiling untouched so the outstanding award can be re-attempted, but it cannot clear a claim that is still in flight. The operator, reference and evidence are kept as the durable reconciliation record. |

**Retrying an incomplete settlement**

The admin retry sends only what is still owed, never a recomputed figure. For a revision-2 booking the cancellation fact stores the award the policy decided, so the retry sends that award less anything already settled (and never more than the remaining captured amount): a failed 50% award is retried at 50%, and an award that has already settled is refused as `409 Bookings.AlreadyRefunded` rather than paid twice. A revision-1 booking is amount-free by design, so its retry keeps the legacy semantics of the remaining captured amount. A revision-2 fact that does not record its award is incomplete evidence and is refused (`409 Bookings.RefundNotEligible`) instead of falling back to a full-capture refund.

Finance complaint/review case correlation for an ambiguous attempt is deferred to a later wave; this wave clears the marker with the operator's own evidence only.

## Error Catalog

| Code | HTTP | When |
|---|---|---|
| `Bookings.FamilyNotFound` | 404 | Caller belongs to no family. |
| `Bookings.UnauthorizedRole` | 403 | Viewer attempted checkout. |
| `Bookings.ElderlyNotFound` | 404 | Dependent not in the caller's family. |
| `Bookings.ScheduleConflict` | 409 | Caregiver already has a pending/active booking overlapping the slot. |
| `Bookings.NotFound` | 404 | Booking id unknown (or not in the caller's family, on intent). |
| `Bookings.BookingNotInFamily` | 404 | Booking belongs to another family (on cancel). |
| `Bookings.Domain.InvalidOperation` | 409 | Lifecycle rule violated (wrong-status transition, cancel after completion, past-deadline accept, intent on a paid booking). |
| `Bookings.AllowanceExceeded` | 409 | The current family subscription has no remaining finite booking allowance for the period; the booking remains `InProgress`. |
| `Bookings.AllowanceConcurrency` | 409 | Another completion changed the allowance concurrently; retry the completion request. |
| `Bookings.PriceUnavailable` | 409 | Reserved for pricing unavailability. |
| `Bookings.RefundExceedsCeiling` | 409 | The award would exceed the amount still refundable for the booking (captured minus already refunded). The booking is left untouched. |
| `Bookings.AlreadyRefunded` | 409 | The booking is already refunded, or the recorded policy award has already been settled in full. Nothing is sent to the gateway. |
| `Bookings.RefundNotEligible` | 409 | The booking is not in a retryable refund state, has no stored gateway transaction, or its revision-2 cancellation fact does not record the award the retry needs. |
| `Bookings.RefundSettlementIncomplete` | 503 | The refund entitlement was recorded and the booking transitioned, but the provider movement did not complete. The award is durable; the outstanding settlement can be retried. |
| `Bookings.RefundOutcomeAmbiguous` | 409 | The gateway could not confirm a refund outcome. The booking carries an ambiguity marker and refuses every further refund until an administrator reconciles it. |
| `Bookings.Outcome.InvalidReason` | 400 | `outcomeReason` is not a defined provider outcome. |
| `Bookings.Outcome.ReasonNotAllowed` | 400 | A caregiver tried to attest an outcome only the platform may assert. |
| `Bookings.RefundReconciliation.EvidenceRequired` | 400 | A reconciliation was submitted without evidence. |
| `Bookings.RefundReconciliation.EvidenceTooLong` | 400 | The evidence plus provider reference exceeds the stored reconciliation record. |
| `Bookings.RefundReconciliation.ProviderReferenceRequired` | 400 | A reconciliation reported that money moved without the provider's refund reference. |
| `Bookings.RefundReconciliation.NoAmbiguousAttempt` | 409 | A reconciliation was attempted on a booking with no unconfirmed refund attempt. |
| `Bookings.RefundReconciliation.AttemptMismatch` | 409 | The submitted attempt id is not the booking's current refund attempt; refresh and retry. |
| `Bookings.RefundReconciliation.ClaimInFlight` | 409 | A reconciliation tried to clear a claim that is still in flight without the provider's reference. |
| `Bookings.RefundReconciliation.ConcurrencyConflict` | 409 | The booking changed while the reconciliation was being saved; re-read it and retry. |
| `Bookings.RefundReconciliation.AwardUnknown` | 409 | A reconciliation reported that the money moved, but the attempted award was not recorded, so the movement cannot be booked. |
| `Bookings.NoRefundDue` | 409 | The sold policy revision grants no refund for this cancellation, or the booking was never paid. |
| `Bookings.Cancel.ReasonCategoryRequired` | 400 | A cancellation or outcome on an accepted booking omitted `reasonCategory`. |
| `Bookings.Cancel.ReasonCategoryInvalid` | 400 | The supplied `reasonCategory` is not a defined cancellation reason. |
| `Bookings.Cancel.ReasonRequired` | 400 | A cancellation or outcome on an accepted booking omitted the reason note. |
| `Bookings.Cancel.AlreadyProcessed` | 409 | This booking was already cancelled (or an outcome was already recorded) by a concurrent request. |
| `Bookings.Outcome.InvalidReason` | 400 | The supplied `outcomeReason` is not a defined provider/platform outcome. |
| `Bookings.Outcome.ReasonNotAllowed` | 400 | A caregiver tried to record an outcome only the platform may assert (lateness, inaccessibility, safety, force majeure). |
| `Caregivers.Discovery.CaregiverNotFound` | 404 | Checkout/quote against an unknown caregiver. |
| `Caregivers.Discovery.QuoteNotAvailable` | 409 | Caregiver pricing or the shared Finance charge rule is unavailable for the requested product. |
| `Bookings.ChargesNotConfigured` | 409 | No effective shared platform-fee/tax rule is configured; no booking is created. |
| `Paymob.NotConfigured` | 503 | Payment gateway keys are absent from server configuration. |
| `Paymob.MethodNotAvailable` | 409 | Method not enabled (e.g. Apple Pay before Paymob enablement). |
| `Paymob.GatewayError` | 502 | Paymob returned an unexpected response. |
| `Platform.Maintenance` | 503 | The platform is in Maintenance; a new checkout or payment intent was refused. No booking, hold, or pending payment is created. |
| `Platform.PaymentMethodUnavailable` | 400 | The selected payment method is disabled or not configured on the server (includes ApplePay, which is not supported). No Paymob call is made. |
| `Platform.ControlStateUnavailable` | 503 | The platform controls could not be read, so the request failed closed. Not a published outage â€” retry later. |
