# Family Bookings (request lifecycle)

Routes for creating a care booking (checkout), paying it through Paymob, browsing requests by tab, reading booking details, and cancelling. The family pays the caregiver's price plus a platform fee; the caregiver then accepts or declines.

All routes live under `/api/v1/family/bookings...`.

## Access

- **Normal JWT** for a **Family** account (`access_type = Normal`, `account_type = Family`). Policy: `FamilyAccess`.
- The caller must be an active member of a family.
- **Checkout** requires an **Owner** or **Editor** role (`403 Bookings.UnauthorizedRole` for Viewers).
- **List / detail / cancel / payment intent** are available to every family member — but only for bookings of their **own** family (`404 Bookings.NotFound` for a foreign booking on intent, `404 Bookings.BookingNotInFamily` on cancel, `404 Bookings.FamilyNotFound` when the caller belongs to no family).

## Lifecycle, pricing & slot rules

```text
PendingPayment ──pay──▶ PendingCaregiverApproval ──accept──▶ Confirmed ──start──▶ InProgress ──complete──▶ Completed
      │                         │
      ├─ cancel ─▶ CancelledByFamily        ├─ decline ─▶ DeclinedByCaregiver
      └─ expire ─▶ Expired                  ├─ cancel (caregiver) ─▶ CancelledByCaregiver
                                            └─ expire ─▶ Expired
```

- **Server-side pricing (contract as of `9ae68cc`):** the checkout body carries **no price fields and no caregiver type**. The server loads the caregiver's real pricing (the same engine behind the discovery quote endpoint), rejects an inactive caregiver or an unconfigured product, and stores an immutable snapshot: `totalPayableAmount = caregiver base fee + 15% platform fee`. The caregiver receives the full base fee.
- **Acceptance window:** the caregiver must respond within `min(now + 24h, booking start)`; acceptance after the deadline is rejected. A payment whose success webhook arrives after the deadline is **not** honoured as a booking — the booking expires and the payment is refunded automatically (see refunds below).
- **Slot reservation:** a booking in `PendingPayment` or `PendingCaregiverApproval` already blocks the same caregiver/date/overlapping-time slot (`409 Bookings.ScheduleConflict`).
- **Snapshot immutability:** later caregiver price edits never change an existing booking.
- **Payments (contract as of `b556c4e`):** checkout returns no payment data. The app starts a payment with the **payment intent** endpoint (section 5), which creates a Paymob **intention** server-side and returns `clientSecret` + `publicKey` for the **Paymob mobile SDK** (embedded card / wallet UI — card data never touches Sanad servers). The Paymob **webhook** (`POST /api/v1/payments/webhooks/paymob`, HMAC-SHA512-verified, idempotent, amount-checked) is the **single source of truth**: only the webhook can mark a booking paid. Refunds are **automatic** on caregiver decline, on family cancellation before acceptance, and when a late payment arrives after the deadline (pay → expire → refund).

## Endpoints

| Method | Route | Description | Role |
|---|---|---|---|
| `GET` | `/api/v1/family/bookings?tab={tab}` | Tab list (newest first) | Any member |
| `GET` | `/api/v1/family/bookings/{bookingId}` | Booking detail (own family only) | Any member |
| `POST` | `/api/v1/family/bookings/checkout` | Create booking + server-side price snapshot | Owner / Editor |
| `POST` | `/api/v1/family/bookings/{bookingId}/payments/intent` | Paymob intention + mobile-SDK handoff | Any member |
| `POST` | `/api/v1/family/bookings/{bookingId}/cancel` | Cancel an own-family booking | Any member |

### Tabs (`tab`, enum int — the name string is also accepted)

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
  "serviceAddress": "14 شارع الكورنيش، الإسكندرية",
  "specialInstructions": "المريض يعاني من ضغط الدم"
}
```

| Field | Type | Notes |
|---|---|---|
| `elderlyId` | UUID | Must belong to the caller's family (`404 Bookings.ElderlyNotFound`). |
| `caregiverId` | UUID | Must be an **Active** caregiver; type and price are resolved server-side. |
| `shiftType` | enum (int) | `1` HomeVisit, `2` EightHourShift, `3` TwelveHourShift, `4` TwentyFourHourShift, `5` Hourly. Medical products only for Medical caregivers; `Hourly` only for Companion. |
| `bookingDate` | `YYYY-MM-DD` | Cannot be in the past. |
| `startTime` / `endTime` | `HH:mm` | Must not overlap an existing pending/active booking of that caregiver. |
| `serviceAddress` | string ≤ 500 | Where the care takes place. |
| `specialInstructions` | string? ≤ 1000 | Optional family notes shown on the caregiver's request card. |

**`200` response**

```json
{
  "bookingId": "0198e3f0-3333-7777-8888-000000000003",
  "status": 1,
  "totalPayableAmount": 2875.00,
  "currency": "EGP"
}
```

`totalPayableAmount = caregiver base fee + 15% platform fee` (e.g. base `2,500.00` → total `2,875.00`). Payment starts with the intent endpoint below.

### 2. Tab list

`GET /api/v1/family/bookings?tab=1` → `FamilyBookingListItem[]`: booking id, caregiver id, senior Arabic/English names, date + time window, `shiftType`, `status`, `totalPayableAmount`, `currency`. The caregiver name/avatar fields are reserved and currently return `null` pending the caregiver-name join.

### 3. Booking detail

`GET /api/v1/family/bookings/{bookingId}` → full price breakdown (`baseCaregiverFee`, `platformFeePercentage`, `platformFeeAmount`, `totalPayableAmount`, `currency`), the elderly summary (fields are `null` when the dependent record is absent — the API never fabricates values), lifecycle timestamps (`paidOnUtc`, `confirmedOnUtc`, `startedOnUtc`, `completedOnUtc`, `cancelledOnUtc`), `cancellationReason`, and `caregiverNotes`.

### 4. Cancel

`POST /api/v1/family/bookings/{bookingId}/cancel`

```json
{ "reason": "ظرف طارئ" }
```

Allowed from `PendingPayment`, `PendingCaregiverApproval`, and `Confirmed` (`409 Bookings.Domain.InvalidOperation` otherwise). Reason ≤ 500 chars. Cancellation **fee tiers** are not enforced yet (planned slice); today no fee is deducted.

### 5. Payment intent (Paymob mobile-SDK handoff)

`POST /api/v1/family/bookings/{bookingId}/payments/intent`

```json
{
  "method": 1,
  "billing": {
    "firstName": "أحمد",
    "lastName": "علي",
    "email": "ahmed@example.com",
    "phoneNumber": "+201012345678"
  }
}
```

| Field | Type | Notes |
|---|---|---|
| `method` | enum (int) | `1` Card, `2` Wallet (mobile wallets, e.g. Vodafone Cash). `3` ApplePay is reserved — currently `409 Paymob.MethodNotAvailable` until Paymob enables it. |
| `billing` | object | Required by Paymob: first/last name, email, phone number. The app should auto-fill these from the family profile. |

Only a booking in `PendingPayment` can start a payment; the amount and currency always come from the booking's immutable price snapshot (never from the client).

**`200` response**

```json
{
  "bookingId": "0198e3f0-3333-7777-8888-000000000003",
  "paymobOrderId": "0198e3f0-3333-7777-8888-000000000003",
  "amount": 575.00,
  "currency": "EGP",
  "clientSecret": "egy_csk_test_94042f793419c5a0f14a4cadfda9d626",
  "publicKey": "pk_test_626xxxxxxxxxxxxx"
}
```

| Field | Notes |
|---|---|
| `paymobOrderId` | Equals the `bookingId` — Paymob echoes it back on the webhook as `merchant_order_id`. |
| `amount` / `currency` | From the price snapshot (`500` base + `15%` → `575.00 EGP`). |
| `clientSecret` + `publicKey` | Pass both to the Paymob mobile SDK to present the embedded checkout (card form or wallet PIN screen). The SDK result is **UI-only**. |

**How the app completes a payment**

1. Call the intent endpoint, receive `clientSecret` + `publicKey`.
2. Initialize the Paymob mobile SDK with them; the user completes card/wallet entry inside the SDK (no WebView, no card data in the app's code).
3. Show a *processing* state and poll `GET /family/bookings/{bookingId}` (~3s, cap ~60s).
4. Paymob calls the Sanad webhook; only the webhook moves the booking `PendingPayment → PendingCaregiverApproval` (paid) or records a failure. Never trust the SDK callback or redirect alone.

**Automatic refunds (full amount, via Paymob)**

| Trigger | Outcome |
|---|---|
| Caregiver declines (`PendingCaregiverApproval`) | Booking `DeclinedByCaregiver` → automatic refund → `Refunded` |
| Family cancels before caregiver acceptance | Booking `CancelledByFamily` → automatic refund → `Refunded` (fee tiers come in a later slice) |
| Payment webhook arrives after the acceptance deadline | Booking `Expired` → automatic refund → `Refunded` |

If the refund call fails at the gateway, the booking still transitions (the cancellation/decline is always recorded) and the refund is retried by ops from the Paymob dashboard; `refundedOnUtc` stays `null` until it succeeds.

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
| `Bookings.PriceUnavailable` | 409 | Reserved for pricing unavailability. |
| `Caregivers.Discovery.CaregiverNotFound` | 404 | Checkout/quote against an unknown caregiver. |
| `Caregivers.Discovery.QuoteNotAvailable` | 409 | Caregiver pricing missing for the requested product, or product/caregiver-type mismatch. |
| `Paymob.NotConfigured` | 503 | Payment gateway keys are absent from server configuration. |
| `Paymob.MethodNotAvailable` | 409 | Method not enabled (e.g. Apple Pay before Paymob enablement). |
| `Paymob.GatewayError` | 502 | Paymob returned an unexpected response. |
