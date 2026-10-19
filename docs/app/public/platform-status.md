# Platform business status (app / public)

One anonymous read that tells the app whether the platform is accepting new
bookings and payments right now, and which payment methods are available. It is
the client-facing half of the Admin platform controls.

**Route:** `GET /api/v1/platform/status`
**Auth:** none (`AllowAnonymous`)
**Success:** `200`

```http
GET /api/v1/platform/status
```

```json
{
  "state": 1,
  "maintenanceMessageArabic": null,
  "maintenanceMessageEnglish": null,
  "availablePaymentMethods": [1, 2]
}
```

| Field | Type | Notes |
|---|---|---|
| `state` | enum (int) | `1` Operational, `2` Maintenance. |
| `maintenanceMessageArabic` | string \| null | The published maintenance message, Arabic. `null` when none is published. |
| `maintenanceMessageEnglish` | string \| null | The published maintenance message, English. `null` when none is published. |
| `availablePaymentMethods` | array of enum (int) | Methods that are both enabled and configured: `1` Card, `2` Wallet. An empty array means no method is currently available. |

`availablePaymentMethods` never contains ApplePay: E04 does not support it, so
it is never advertised and never accepted.

## What the app can and cannot do with it

The status route is **advisory for the UI only**. The server enforces the same
rules in the business path, so the app must still handle the rejection codes
returned by the booking and payment routes:

- `503` with code `Platform.Maintenance` — the platform is in Maintenance and
  this new checkout or payment-intent request was refused. Show the published
  maintenance message and stop the flow.
- `400` with code `Platform.PaymentMethodUnavailable` — the selected payment
  method is disabled or not configured. Offer only the methods listed in
  `availablePaymentMethods`.
- `503` with code `Platform.ControlStateUnavailable` — the platform controls
  could not be read, so the app must fail closed and not start a new booking or
  payment. This is distinct from Maintenance: it is a server-side condition,
  not a published outage.

## What is **not** affected by Maintenance

Only new operations are refused. The app can keep using everything else while
the platform is in Maintenance:

- authentication and session handling;
- reads, lists and quotes (bookings, Care Home bookings, subscriptions, plans);
- cancellation, refunds and the care-delivery lifecycle;
- retrieving or replaying a payment intent that was already created — the
  server returns the existing intent instead of creating a new one;
- verified provider callbacks and settlement of an existing payment.

## Failure mode

If the control record cannot be read, the route fails closed with `503` and code
`Platform.ControlStateUnavailable` instead of reporting a default Operational
state. The app must treat that as "new bookings and payments are unavailable",
not as "the platform is fine".

## Related documentation

- Admin control surface: [platform controls](../../admin/platform-controls.md).
- Family booking and payment flows:
  [family bookings](../families/bookings.md),
  [family subscriptions](../families/subscriptions.md).
- Care Home booking flow: [booking lifecycle](../../care-homes/booking-lifecycle.md).
