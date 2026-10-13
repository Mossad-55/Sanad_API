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

The event delivery policy is listed in the Push fan-out section below. Eligible check-in, help-request, medication-late, SOS, and care-home booking notifications enqueue push when a device is registered. The worker checks the existing topic preference before delivery. Email and SMS rules remain as described by their owning slices.

Medication late/missed evaluation uses the `MedicationReminders` preference for
eligible active linked Family recipients. It also targets the assigned caregiver
only during an active `Confirmed` or `InProgress` booking for the Elderly, plus
active `SupportAdmin` users. Unassigned or unbooked caregivers are excluded.
The current event is `category = MedicationReminders` and
`type = MedicationDoseMissed`, with a typed `Medication` destination. Rows are durable and idempotent per recipient and scheduled dose. Eligible recipients with a registered device receive push through the outbox after the `medicationReminders` preference check. Email and scheduling remain outside Lane A.

SOS creation uses the same durable inbox foundation. It creates
`category = ElderlySos`, `type = SosCreated`, and destination kind
`ElderlySos`, idempotent per SOS and recipient. Current recipients are active
linked Family members eligible under the current help-request preference
lookup, the assigned caregiver only during an active `Confirmed` or
`InProgress` booking (and subject to the applicable active-account rule), and
active `SupportAdmin` users. Unrelated caregivers are excluded. The current
implementation reuses the help-request preference lookup; a dedicated SOS
preference and exact preference semantics
are **Needs Owner Verification**. Push delivery uses the outbox and checks `helpRequestAlerts` server-side, matching the current recipient lookup. Email, scheduler, physical purge, device-dialer behavior, and export remain **Needs Owner Verification**. SMS and server-side dialing are excluded.

## Push devices

Push registration is available to any authenticated account with a Normal JWT.
The authenticated user owns every listed or removed token; requests do not accept
an owner ID.

| Method and route | Request | Result |
|---|---|---|
| `POST /api/v1/devices` | ``{ "platform": "android", "token": "<FCM token>", "appVersion": "<optional>" }` (platform is `android` or `ios`)` | `200 { "deviceId": "<uuid>", "pushEnabled": true }`; registering the same token refreshes its metadata and owner |
| `GET /api/v1/devices` | none | Own devices only; token is masked except for its last six characters |
| `DELETE /api/v1/devices/{deviceId}` | none | `204`; another user's ID is not deleted |

Registration rejects unknown platforms and malformed tokens with `400`. When
Firebase configuration is missing or invalid outside Development, registration
returns `503 { "code": "Push.NotConfigured" }` and stores no token. Development
uses a no-op sender. Configure the service-account JSON only through host
configuration, for example `Push__Firebase__ServiceAccountJson`; never place it
in source, database rows, request examples, or logs.

## Push fan-out and deep links

Eligible in-app notification rows atomically enqueue a push outbox item when the
recipient has a registered device. The processor checks the existing preference
switch immediately before sending. The current mapping is:

| Event category/type | Push topic | Payload navigation |
|---|---|---|
| Care-home booking events (`CareHomes` with `Booking*` types) | `bookingUpdates` | `sanad://inbox?notificationId=<id>` |
| Check-in (`ElderlyCheckIn`) | `checkInAlerts` | inbox fallback |
| Medication late (`MedicationReminders`) | `medicationReminders` | inbox fallback |
| Help request (`ElderlyHelpRequest`) | `helpRequestAlerts` | inbox fallback |
| SOS (`ElderlySos`) | `helpRequestAlerts` (current recipient lookup) | inbox fallback |
| Own community post published/rejected (`CommunityNotifications`) | `communityNotifications` | inbox fallback |
| New chat message (canonical `Chat` / `NewMessage`, plus legacy `MessagesFromFamilies` / `NewMessage` / `ChatMessageReceived` rows) | `chatMessages` (legacy `messagesFromFamilies` opt-out preserved via migration/mirror; field retained for existing consumers) | inbox fallback |

Payload data contains notification/entity IDs, entity kind, and the deep link; it
does not include message text. Push title/body use the in-app notification's
own title/body. Likes, favorites, comments, money/payout events, and OTP are not
pushed by this policy. Chat produces its in-app row through the durable Chat
Plus outbox/dispatcher (staged in the Chat transaction, delivered after commit
with bounded retries) — the mapping above applies when that seam creates the row.
The `sanad://inbox` scheme is centralized in `PushFanoutPolicy.BuildData` so the
mobile team can confirm or change it in one place.

Transient provider errors use capped exponential backoff (maximum 60 minutes,
up to eight attempts). FCM `Unregistered` retires that token only (proven
token-specific failure); `InvalidArgument` no longer retires — it retries
bounded as `Push.PayloadRejected` for operator action, as do sender/auth
mismatches (configuration failures). Cancellation propagates. A user with no device or a disabled topic preference is
suppressed. No live Firebase verification was run for this implementation.

## Email notifications

Email uses the existing SMTP transport and the durable `notifications.email_outbox`.
The shared template layout places Arabic first with RTL direction, English second
with LTR direction, a text wordmark, and system fonts. Outbox-backed notification
messages are rendered and stored as immutable plain-text and HTML snapshots when
they are enqueued. SMTP sends both parts as `multipart/alternative`; legacy
outbox rows without an HTML snapshot remain plain-text messages. The correlation
key continues to determine the stable SMTP `MessageId`. OTP and family-invitation
emails continue through the existing direct Identity send path and render both
parts immediately before SMTP delivery.

| Trigger | Template kind | Delivery |
|---|---|---|
| OTP code | `OtpCode` | Email + SMS; never push |
| Family invitation | `Invitation` | Email |
| Support request | `SupportAcknowledgment` | Email to support |
| Booking paid, accepted, declined, started, completed | Matching `Booking*` kind | App + push + email |
| Booking cancelled with refund | `BookingCancelledWithRefund` | App + push + email |
| Subscription renewed or renewal upcoming | `SubscriptionRenewed`; upcoming uses upcoming-specific copy | App + email |
| Subscription failed | `SubscriptionFailed` | App + email |
| Plan changed or retired | `SubscriptionChanged`; retirement uses event-specific copy | App + email |
| Payout recorded or failed | `PayoutRecorded` / `PayoutFailed` | App + email |
| Own post published or rejected | `PostPublished` / `PostRejected` | App + push + email |
| Existing care-home email notices | Shared bilingual layout with existing Arabic and English event copy | Existing app + email; push is added where eligible |

The table is the approved event-to-channel contract. The current repository has
no event producers for caregiver bookings, subscription changes, payouts, or
community-post moderation; their templates are ready, but those messages will
not be enqueued until those event seams exist. Chat remains without a producer
as noted above.

Likes, favorites, and comments on a user's own post remain silent. Followed-author
posts remain deferred because there is no follow graph. Chat and OTP are not
emailed as notification events. Email delivery is asynchronous: outbox rows move
through `Pending`, `Processing`, `Sent`, or `Failed`; failed sends retry with
backoff. There is no synchronous email error code returned to the app. Push
configuration errors remain `Push.NotConfigured`.

The Postman collection contains authenticated device and inbox endpoints. Email
templates are server-rendered behavior and do not add a client API route.
