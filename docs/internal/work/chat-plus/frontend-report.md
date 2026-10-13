# Chat Plus frontend report — received 2026-10-13

This is frontend input, not evidence of implemented or deployed behavior.
The production plan in [plan.md](plan.md) resolves implementation requirements;
[tasks.md](tasks.md) owns status. The owner will implement, commit, and push;
the mastermind updates the plan/handoff and reviews the resulting revision.

## Pricing insights

`GET /api/v1/caregiver/pricing-insights?areaId={areaId}&serviceId={serviceId}`

```json
{
  "currency": "EGP",
  "marketHourlyPrice": { "min": 40, "max": 80 },
  "mostRequestedEightHourDayPrice": { "min": 200, "max": 400 },
  "updatedOnUtc": "2026-10-11T08:00:00Z"
}
```

Insufficient data returns the relevant range object as `null`, not invented
numbers or a zero range. Example values are illustrative, not defaults.

## Voice — conditional frontend request

The frontend proposes reusing attachment upload with `recording.m4a`, returning
`attachmentId`, `contentType: audio/mp4`, `sizeBytes`, and server-validated
`durationSeconds`; sending would use `type: Voice` and `attachmentId`.
The message attachment would include duration, and download would use the
existing participant-authorized file endpoint. Backend would set format,
size, and duration limits.

The report says voice is optional for the first release. The existing owner
decision excludes voice; this report does not reverse that decision. Voice
remains excluded from this package unless the owner explicitly includes it.
Do not add `Voice`, accept audio files, or publish duration fields in v1.

## SignalR

Hub `/hubs/chat`; authenticate using the existing access token. REST sends
remain authoritative; realtime updates screens. Events reach eligible
conversation participants only.

```json
{
  "conversationId": "uuid",
  "message": {
    "id": "uuid",
    "clientMessageId": "unique-client-id",
    "senderUserId": "uuid",
    "type": "Text",
    "text": "مساء الخير",
    "createdOnUtc": "2026-10-11T08:00:00Z"
  }
}
```

The preceding payload is `MessageCreated`. The minimum frontend example is
extended by the existing full REST message item for files/locations/receipts.

```json
{
  "conversationId": "uuid",
  "messageId": "uuid",
  "deliveredOnUtc": "2026-10-11T08:00:02Z",
  "readOnUtc": "2026-10-11T08:00:05Z"
}
```

The preceding payload is `MessageReceiptUpdated`.

```json
{
  "conversationId": "uuid",
  "userId": "uuid",
  "isTyping": true,
  "expiresOnUtc": "2026-10-11T08:00:10Z"
}
```

The preceding payload is `TypingChanged`.

```json
{
  "userId": "uuid",
  "isOnline": false,
  "lastSeenOnUtc": "2026-10-11T08:00:00Z"
}
```

The preceding payload is `PresenceChanged`. Presence and last seen must honor
privacy settings; absence of a known privacy policy is not consent to disclose.

## Operations

Enable backend Firebase and configure APNs for iPhone delivery. This requires
backend credentials, Firebase-project APNs configuration, and mobile app
capabilities/device registration. A backend code gate alone cannot verify it.
The earlier deferred live-provider check remains deferred until an exact
environment, test devices, and test sends are authorized.
