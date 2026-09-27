# Notification inbox

The notification inbox is a durable, in-app read surface for any authenticated
account with a Normal JWT. It is recipient-isolated: the recipient is taken from
the token, not from a request parameter. Restricted-verification tokens and
unauthenticated requests are not accepted.

This slice is the inbox foundation only. It does not yet have a check-in
producer caller, push or email providers, delivery retries, physical purge, a
complete category inventory, or Admin inspection. The app maps a destination
using the stored entity kind and ID; the API does not return a client route.

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

No category list or event delivery policy should be inferred from the current
fields. The approved negative daily check-in event will create durable in-app
alerts for eligible Family recipients when its producer is delivered; push may
be attempted when a provider exists, and SMS is not used for that event.
