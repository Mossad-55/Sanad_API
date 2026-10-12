# Chat slice — frozen contract (owner-approved)

Backend-only slice. No implementation has started. This document freezes the
product contract; `tasks.md` owns execution order and `handoff.md` owns live
status. Changing anything below needs a new explicit owner decision.

## Participants (locked)

- Family member → any caregiver, booked caregivers, any approved care home.
- Elderly → own family members, and caregivers linked by a booking.
- Support admin → everyone, including care homes.
- Caregiver initiation is limited to booking families plus replies
  (approved option A). Families, elderly (within their rules), support, and
  care homes keep full initiation.
- All family roles participate fully, including Viewer (approved option A).
- Conversations are strictly 1:1. No group chats.

## Entry and duplication rules (locked, Opus delta applied)

- Exactly one open target is required: `bookingId` alone opens a
  booking-scoped chat (the backend resolves the counterpart — the booking
  must exist, have the caller as a party, and not be cancelled/expired/
  refunded), or `recipientUserId` alone opens a direct (booking-less) chat.
  Both or neither is `400 Chat.Conversation.OpenTargetInvalid`; every
  resource-shaped denial stays the privacy `404`.
- Booking-scoped chat requires both users on a non-cancelled, non-expired,
  non-refunded booking. Direct (booking-less) chat is allowed for
  family↔caregiver, family↔care home, and support↔anyone; elderly↔caregiver
  requires a linking booking.
- Eligibility is checked at open time. Cancellation never retro-closes a chat.
- Same pair + same booking = one conversation; same pair without booking =
  one conversation. Reopening returns the existing id (`200`, never a
  duplicate).

## Messaging rules (locked, Opus delta applied)

- Send-only: messages are immutable once sent (no edit/delete).
- Text ≤ 2000 characters; `Location` carries
  `{latitude (-90…90), longitude (-180…180), label?}` with the same
  participant-only privacy as text. Server-set UTC stamp, exposed on the
  wire as `createdOnUtc` (Opus delta renames the former `sentOnUtc` on
  responses; server storage is unchanged).
- `deliveredOnUtc` is set on the recipient's first fetch containing the
  message; `readOnUtc` is set by explicit read marking.
- `clientMessageId` is unique per conversation: exact replay returns the
  original message with `200`; same id with different content is `409`.
- Retention is indefinite. Ticks and timestamps are displayed
  (sent time, delivered grey, read blue).

## Attachments (locked)

Images ≤ 5 MB (JPG/PNG/WebP), video ≤ 50 MB (MP4), PDF/documents ≤ 10 MB.
Magic-byte verification, generated safe keys, private storage with authorized
controller serving (`GET …/attachments/{id}/file`, participant-only,
no-store). No signed URLs in V1. No voice notes. Each upload is
conversation-scoped, uploader-bound, and binds to a single message.

## Blocking, hiding, reporting (locked)

- Block is per-user; the blocked sender receives a generic failure that never
  reveals the block. Unblock restores sending.
- Delete hides the conversation for the deleter only; the other party keeps
  history. Nothing is erased.
- Reports carry a required reason (1–500). Support sees reported
  conversations only — no general chat access. Uphold records the violation
  and applies a mutual chat-block; dismiss closes the report with history.

## Transport and notifications (locked)

Polling + in-app new-message alerts through the existing notification seam
(conversation id + message id only, never message text). No SignalR, no typing
indicators, no online status, no push provider in V1.

## Roles and policies (locked)

- App routes: `[Authorize]` with participant resolution from the JWT; unknown
  or foreign conversations, messages, and attachments are privacy-preserving
  `404`s.
- Support routes: new `ChatSupportAdmin` policy (SuperAdmin + SupportAdmin),
  mirroring the elderly-ops policies. No other role receives chat-admin
  rights.

## App endpoints (frozen)

1. `GET /api/v1/chat/conversations?page=1&pageSize=20` → `PagedResult` of
   `{id, participant{userId, displayName, avatarUrl, accountType}, lastMessage?{id, type,
   text, createdOnUtc}, unreadCount}`, last-activity first. `accountType` is
   the Identity `AccountType` integer; care-home owners present as the
   facility (name/cover) over the owner's user id.
2. `POST /api/v1/chat/conversations` `{recipientUserId?, bookingId?}` →
   exactly one required → `200 {conversationId, created}` (open-or-get;
   `created` tells new history). Both/neither → `400
   Chat.Conversation.OpenTargetInvalid`. Unknown/ineligible recipient or
   booking → `404`.
3. `GET …/{id}/messages?beforeId=&pageSize=30` → cursor page desc by
   (`createdOnUtc`, `id`); pageSize default 30, clamp 1–100;
   `{items[{id, conversationId, clientMessageId, senderUserId, type, text, attachment?, location?,
   createdOnUtc, deliveredOnUtc?, readOnUtc?}], nextCursor, hasMore}`.
4. `POST …/{id}/messages` `{clientMessageId, type, text?, attachmentId?, location?}` →
   `201` on create (`200` on exact replay); `type` is `Text`, `File`, or
   `Location`.
5. `POST …/{id}/attachments` (multipart `file`) →
   `201 {attachmentId, fileName, contentType, sizeBytes}`.
6. `GET /api/v1/chat/attachments/{id}/file` → participant-only bytes
   (participant check derives from the attachment's own conversation),
   no-store.
7. `POST …/{id}/read` `{lastReadMessageId}` → `204`, idempotent.
8. `GET /api/v1/chat/unread-count` →
   `{unreadMessagesCount, unreadConversationsCount}`.
9. `POST /api/v1/chat/blocks` `{blockedUserId}` → `204`;
   `DELETE …/blocks/{userId}` → `204`; `GET …/blocks` → blocked ids.
10. `DELETE …/{id}` → `204` (hide for deleter only, documented as hide).
11. `POST /api/v1/chat/reports` `{conversationId, messageId?, reason}` →
    `201 {reportId}`.

## Support endpoints (frozen)

12. `GET /api/v1/admin/chat/reports?status=&page=` → report queue.
13. `GET …/reports/{id}` and `GET …/reports/{id}/messages` → read-only
    reported-conversation view (the only non-participant read path).
14. `POST …/reports/{id}/dismiss` → `204`.
15. `POST …/reports/{id}/uphold` → `204` (records violation + mutual block).

## Domain and persistence (frozen shape)

New `Chat` module (Community-shaped): `Conversation` (ordered participant
pair, scope kind, optional booking ref, per-user hidden flags),
`Message` (sender, type, text/location payload, attachment ref,
per-conversation-unique `clientMessageId`, server timestamps,
delivered/read stamps), `Attachment` (conversation-scoped, uploader-bound,
single-use binding), `Block` (unique ordered pair), `Report` (status
Open/Dismissed/Upheld with actor/time audit). Location columns arrive via
one more additive migration, authored only until authorized.

Booking-less care-home chats resolve their recipient through facility
detail: `GET /api/v1/care-homes/discovery/{careHomeId}` carries the additive
`contactUserId` (the facility owner's user id) for authenticated callers
only; anonymous visitors receive `null`.

## Documentation commitment

`docs/app/chat/` specifies every workflow (open, send, attach, read, block,
hide, report, uphold/dismiss, unread) and every condition (eligibility
matrix, validation limits, all error codes with triggers, state transitions,
timestamp semantics, retention, privacy boundaries) as the frontend
reference, with matching Postman examples for each path.
