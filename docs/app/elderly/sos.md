# Elderly SOS

SOS is an identity-bound emergency event for the authenticated Elderly
account. It is not an unauthenticated emergency endpoint and it does not
place a server-side call or send SMS.

## Create an SOS

`POST /api/v1/elderly/sos`

Requires an Elderly `Normal` JWT under `ElderlyAccess`. The server binds the
event to the Elderly identity in the token; there is no `elderlyId` request
field.

Required header:

```text
Idempotency-Key: elderly-sos-2026-09-27-001
```

Request body:

```json
{
  "locationConsentGranted": true,
  "latitude": 30.0444209,
  "longitude": 31.2357112
}
```

Latitude and longitude must be supplied together. Location is accepted only
when consent is `true`, latitude is between `-90` and `90`, and longitude is
between `-180` and `180`. Stored coordinates are rounded to three decimal
places. A successful event starts in `Open`.

The same idempotency key for the same Elderly identity and identical payload
returns the existing event. Reusing it with different consent or coordinates
returns `409 Families.Sos.IdempotencyConflict`. A missing/blank key or invalid
location returns `400 Families.Sos.InvalidOperation` or
`Families.Sos.InvalidLocation`, respectively.

## Read and cancel

- `GET /api/v1/elderly/sos` lists the caller's events, newest first.
- `GET /api/v1/elderly/sos/{sosId}` returns one caller-owned event object (not
  a list).
- `POST /api/v1/elderly/sos/{sosId}/cancel` cancels an `Open` or
  `Acknowledged` event.

Successful cancellation returns the updated `Cancelled` event. Repeating
cancellation after the event is terminal returns `409
Families.Sos.InvalidOperation`; an unavailable event returns `404
Families.Sos.NotFound`.

The Elderly routes reject foreign identities and records belonging to a
deleted Family with `404 Families.Sos.NotFound`. Location is returned only
for events created within the last 30 days; older events retain their event
metadata but return null coordinates.

## Lifecycle

`Open` → `Acknowledged` → `Resolved` is supported, as is cancellation from
`Open` or `Acknowledged`. `Resolved` and `Cancelled` are terminal. The Admin
status route records the transition in append-only SOS history.

## Alerts and unresolved delivery policy

Creating an SOS writes durable in-app notifications with category
`ElderlySos`, type `SosCreated`, and destination kind `ElderlySos`. Notification
rows are idempotent per SOS and recipient. Current recipient resolution is:

- active linked Family members eligible for the current alert preference;
- the assigned caregiver while the Elderly has an active `Confirmed` or
  `InProgress` booking; and
- active `SupportAdmin` users.

The current implementation reuses the existing help-request preference lookup;
a dedicated SOS preference and the exact preference UI/semantics are **Needs
Owner Verification**. Push, email, outbox, scheduler, physical purge, device
dialer, and export behavior are **Needs Owner Verification**. SMS and a server
dialer are not part of this contract.

Successful requests return the SOS representation with `id`, `elderlyId`,
`status`, consent, visible coordinates, and UTC created/updated timestamps.
