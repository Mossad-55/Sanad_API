# Platform controls (Admin — SuperAdmin)

Small, auditable platform switchboard: the platform is either **Operational** or
**Maintenance**, and the Card/Wallet payment methods can be turned on or off.
There is no general settings registry, no gateway-secret editing, and no Admin
frontend here — only the API that the Admin portal calls.

The control record is persisted in the existing `finance` schema alongside the
shared platform-charge rules, so no new database, context, or module is
introduced.

## Access and routes

Both routes require a normal authenticated JWT and the `PlatformControlsAdmin`
policy: **SuperAdmin only**. SupportAdmin, FinanceAdmin, ContentAdmin, Family,
Caregiver, and anonymous callers are denied (`401` unauthenticated, `403` any
other role).

```text
GET /api/v1/admin/platform-controls
PUT /api/v1/admin/platform-controls
```

Gateway secrets and Paymob integration ids are never read, returned, or written
by these routes. The routes expose only the state, the public message, the two
method switches, and the concurrency version.

## Read the current controls

```http
GET /api/v1/admin/platform-controls
```

**`200` response**

```json
{
  "version": 1,
  "state": 1,
  "maintenanceMessageArabic": null,
  "maintenanceMessageEnglish": null,
  "cardEnabled": true,
  "walletEnabled": true,
  "updatedOnUtc": "2026-10-19T06:16:22Z"
}
```

| Field | Type | Notes |
|---|---|---|
| `version` | int | Optimistic concurrency token. Send it back as `expectedVersion`. |
| `state` | enum (int) | `1` Operational, `2` Maintenance. |
| `maintenanceMessageArabic` | string \| null | Bilingual public message shown while in Maintenance. Blank input is stored as `null`. |
| `maintenanceMessageEnglish` | string \| null | Same, English side. |
| `cardEnabled` | bool | Card switch. A disabled method is never advertised and never creates a new payment intent. |
| `walletEnabled` | bool | Wallet switch, same rules as Card. |
| `updatedOnUtc` | string | UTC timestamp of the last accepted change. |

The read fails closed: if the control record is missing or unreadable the route
returns `503` with code `Platform.ControlStateUnavailable` rather than reporting
a default Operational state. There is no "assume Operational" fallback.

## Update the controls

`PUT` is a **full replacement** of the editable values — every field is supplied
explicitly. A body that omits `cardEnabled`/`walletEnabled` disables those
methods; it does not keep them silently enabled.

```http
PUT /api/v1/admin/platform-controls
Content-Type: application/json
```

```json
{
  "expectedVersion": 1,
  "state": 2,
  "cardEnabled": false,
  "walletEnabled": true,
  "maintenanceMessageArabic": "الصيانة الدورية",
  "maintenanceMessageEnglish": "Scheduled maintenance",
  "internalReason": "Paymob maintenance window"
}
```

| Field | Type | Required | Notes |
|---|---|---|---|
| `expectedVersion` | int | yes | The `version` read from the current controls. |
| `state` | enum (int) | yes | `1` Operational, `2` Maintenance. |
| `cardEnabled` | bool | yes (default `false` when omitted) | Card switch. |
| `walletEnabled` | bool | yes (default `false` when omitted) | Wallet switch. |
| `maintenanceMessageArabic` | string \| null | no | Maximum 500 characters after trimming. |
| `maintenanceMessageEnglish` | string \| null | no | Maximum 500 characters after trimming. |
| `internalReason` | string \| null | no | Internal note stored in the audit history only. Maximum 500 characters. It is **never** returned by the public status API. |

**`200` response** — the same shape as the read, with the new `version` and
`updatedOnUtc`.

| Status | Code | When |
|---|---|---|
| `400` | `Platform.Control.Invalid` | Invalid state value, a message longer than 500 characters, or a non-UTC internal timestamp. Nothing is changed and no audit entry is written. |
| `409` | `Platform.ControlVersionConflict` | `expectedVersion` is stale — another administrator changed the controls first. The current state is preserved; re-read and retry. |
| `503` | `Platform.ControlStateUnavailable` | The control record cannot be read. Nothing is changed. |

A successful update appends exactly one audit entry recording the actor, the UTC
time, the before/after values (state, both maintenance messages, and the two
method switches), and the optional internal reason. Audit history is
append-only: it has no update or delete path, and there is no audit read
endpoint in this phase.

## What the controls actually change

Only the operations named by the approved E04 contract are gated:

- new caregiver booking checkout;
- new Care Home booking checkout;
- new payment-intent creation for caregiver bookings, Care Home bookings,
  subscription initial payments, subscription renewals, and plan changes.

Everything else keeps working during Maintenance: authentication, the public
status route, these Admin routes, verified provider callbacks, retrieval or
replay of an already-created payment intent, booking and subscription reads,
quotes, cancellations, refunds, the care-delivery lifecycle, and all Admin
operations. Local Care Home room/bed maintenance blocks are unrelated to this
control and unchanged.

Enforcement happens in the backend business path, not only in the status
response, so a client that ignores the status route still cannot start a new
booking or payment while the platform is in Maintenance.

## Method availability

A method is advertised and accepted only when it is **both** enabled in the
control record **and** configured on the server:

- Card needs the Paymob gateway secret and the Card integration id.
- Wallet needs the Paymob gateway secret and the Wallet integration id.

A disabled or unconfigured method is rejected with `400
Platform.PaymentMethodUnavailable` before any provider call. ApplePay is not a
supported E04 method and is rejected the same way; it is never advertised.

One technical detail differs for subscriptions: an initial Card subscription
payment whose selected plan carries a provider subscription plan id is sent
through Paymob's dedicated Card 3DS enrollment integration, so that path
additionally requires the Card 3DS integration id to be configured. Without it
the request fails closed with `400 Platform.PaymentMethodUnavailable` instead of
a gateway-level error.

## Migration and startup safety

The control tables and the single seeded `Operational` row are created by
migration `20261019061622_AddE04PlatformControls` in the `finance` schema. That
migration is **authored only** in this phase and is not applied; it is applied
only on an explicitly authorized database. No seed policy or second row exists:
the migration inserts exactly one deterministic row (Card and Wallet enabled,
version 1), and a missing row means the control plane is unreadable, so gated
operations fail closed.

## OpenAPI contract

The read publishes `200` with the control schema and `503` for the fail-closed
read; the update publishes `200`, `400`, `409` and `503`. Both publish `401` and
`403` for authentication and role denial. Shared conventions are in
[api-contract-conventions](api-contract-conventions.md). The public, anonymous
read of the same state is documented in
[the app public status guide](../app/public/platform-status.md).
