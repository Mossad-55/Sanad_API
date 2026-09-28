# Notification inbox

The notification inbox is a durable, in-app read surface for any authenticated
account with a Normal JWT. It is recipient-isolated: the recipient is taken from
the token, not from a request parameter. Restricted-verification tokens and
unauthenticated requests are not accepted.

This slice is the durable in-app foundation. Help-request creation now creates
idempotent `ElderlyHelpRequest` / `HelpRequestCreated` notifications for active
linked Family recipients whose `helpRequestAlerts` preference is enabled, the
assigned caregiver only while an active `Confirmed`/`InProgress` booking exists
and the caregiver is active with `helpRequestAlerts` enabled, and active
`SupportAdmin` users. A missing/mismatched identity-bound Elderly profile or a
failed required Identity recipient lookup yields no recipients. The app maps a
destination using the stored entity kind and ID; the API does not return a
client route.

For the verified elderly wellness-event recipient flow, eligible linked Family
members are selected according to the event preference. The assigned caregiver
is eligible only when an active `Confirmed` or `InProgress` booking exists;
caregiver account activity and event preference rules also apply where that
event defines them. Active `SupportAdmin` accounts are eligible. Unassigned or
otherwise unrelated caregivers are excluded. Check-in alerts target eligible
Family members with `checkInAlerts` enabled; the SOS implementation currently
reuses the help-request preference lookup, so dedicated SOS preference
semantics remain **Needs Owner Verification**.

The bounded check-in/SOS recipient-flow Bruno run is
`tests/Bruno/collections/Sanad/elderly-wellness-events/` and passed **20/20
requests and assertions** against local fixtures. This verifies the scoped
current behavior; it does not establish the historical check-in 500 root
exception, which was not captured.
Admin operational inspection is separate from this recipient-owned inbox and
is documented in [`docs/admin/notifications.md`](../admin/notifications.md).

Preferences are managed separately at
`GET/PUT /api/v1/account/notification-preferences`. The full-replacement
request includes `helpRequestAlerts`; it defaults to `true`, and omitted legacy
stored data is read backward-compatibly as enabled. Preferences do not cause
push, email, or SMS delivery by themselves.

## List notifications

`GET /api/v1/notifications?cursor={cursor}&pageSize={pageSize}`

`cursor` is optional. `pageSize` defaults to `20` and must be between `1` and
`100`. Results are newest first and use an opaque cursor for the next page.
Only notifications created within the last year at read time are available;
there is no physical purge contract in this slice.

```json
{
  "items": [{
    "id": "018f0000-0000-7000-8000-000000000001",
    "category": "check-in",
    "type": "elderly-not-okay",
    "title": "Check-in needs attention",
    "body": "An elderly check-in was marked not okay.",
    "destinationEntityKind": "Elderly",
    "destinationEntityId": "018f0000-0000-7000-8000-000000000002",
    "createdOnUtc": "2026-09-27T08:00:00Z",
    "readOnUtc": null
  }],
  "nextCursor": null,
  "unreadCount": 1
}
```

Each item contains `category`, `type`, `title`, `body`, UTC `createdOnUtc`,
nullable UTC `readOnUtc`, and the typed destination pair
`destinationEntityKind` + `destinationEntityId`.

## Unread count

`GET /api/v1/notifications/unread-count`

Returns a JSON integer containing the unread count among the caller's
notifications that are still within the one-year read-time availability
window.

## Mark one read

`PUT /api/v1/notifications/{notificationId}/read`

Marks one current, caller-owned notification read and returns `204 No Content`.
An unknown, foreign, or older-than-one-year notification returns `404` with
`Notifications.NotFound`; ownership is not disclosed.

## Mark all read

`PUT /api/v1/notifications/read-all`

Marks all current unread notifications belonging to the caller read and returns
`204 No Content`. It does not affect another user's inbox.

## Common outcomes

- `401` when no valid Normal JWT is supplied.
- `403` for a token that does not satisfy `NormalAccess`, including a
  restricted-verification token.
- `400` for an invalid `pageSize` or malformed `cursor`; malformed cursors use
  `Notifications.InvalidCursor`.
- `404 Notifications.NotFound` for a missing, foreign, or expired notification
  in the single-read route.

No complete category list or general event delivery policy should be inferred
from the current fields. The delivered help-request and negative check-in
producers create durable in-app alerts only; push/email providers and retries
remain deferred to Notifications/Events, and SMS is excluded.

Medication late/missed evaluation uses the `MedicationReminders` preference for
eligible active linked Family recipients. It also targets the assigned caregiver
only during an active `Confirmed` or `InProgress` booking for the Elderly, plus
active `SupportAdmin` users. Unassigned or unbooked caregivers are excluded.
The current event is `category = MedicationReminders` and
`type = MedicationDoseMissed`, with a typed `Medication` destination. Rows are
durable and idempotent per recipient and scheduled dose. Push/email delivery,
outbox, and scheduler behavior are deferred and remain Needs Owner Verification.

SOS creation uses the same durable inbox foundation. It creates
`category = ElderlySos`, `type = SosCreated`, and destination kind
`ElderlySos`, idempotent per SOS and recipient. Current recipients are active
linked Family members eligible under the current help-request preference
lookup, the assigned caregiver only during an active `Confirmed` or
`InProgress` booking (and subject to the applicable active-account rule), and
active `SupportAdmin` users. Unrelated caregivers are excluded. The current
implementation reuses the help-request preference lookup; a dedicated SOS
preference and exact preference semantics
are **Needs Owner Verification**. Push/email delivery, outbox, scheduler,
physical purge, device-dialer behavior, and export remain **Needs Owner
Verification**. SMS and server-side dialing are excluded.
