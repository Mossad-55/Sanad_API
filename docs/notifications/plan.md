# Notifications slice — frozen contract (owner-approved)

Backend-only slice. No implementation has started. This document freezes the
product contract; `tasks.md` owns execution order and `handoff.md` owns live
status. Changing anything below needs a new explicit owner decision.

## Channel stack (locked)

| Channel | Role | Mechanism |
|---|---|---|
| In-app | Source of truth | Unchanged: every notify-worthy event writes a `Notification` row first (idempotent, auditable) |
| Push (new) | Instant alert | Firebase Admin SDK **3.7.0** (pinned), FCM HTTP v1, token-based 1:1 sends |
| Email (overhauled) | Rich follow-up | Same SMTP/MailKit pipeline, upgraded to branded bilingual HTML + plain-text twin |
| SMS | Unchanged | OTP and critical alerts only (SmsMisr) |

Fan-out rule: event → in-app row (existing seam, topic preferences checked
server-side) → push (registered tokens only) → email (templated, via the
existing `EmailOutboxMessage` table rendered at enqueue) → SMS untouched.

## Per-event matrix (locked)

- Identity: OTP → email code + SMS code (never pushed); invitation → templated email; support ticket → templated email to support.
- Caregiver bookings: paid/accepted/declined/started/completed/cancelled → app + push; cancellations with refunds also get a receipt email.
- Subscriptions/money: renewal upcoming/renewed/failed, plan changed/retired, payout recorded/failed → app + receipt-style email (money gets a paper trail, not just a ping).
- Care homes: keep current in-app + email behavior; add push alongside (same seam).
- Health/safety (check-in, help-request, medication-late, SOS alerts): keep in-app rows; add push (urgent by nature). No email/SMS.
- Community: own post published/rejected → app + push. Likes/favorites/comments on own posts → silent in V1. Followed-author posts → deferred (no follow graph exists).
- Chat: new message → app row + push (ids only). No email/SMS for chat.

## Push rules (locked)

- Token registry: `DeviceToken` (user, platform Android/iOS, token unique, app version, registered/lastSeen). Register upserts on (user, token); unregister deletes.
- Payload: title + body + data ids + deep link (`sanad://…`, inbox fallback; prefix confirmed by mobile team or swapped in one place). Push content carries ids only, never message text beyond the notification's own title/body.
- `NotRegistered`/`InvalidArgument` retires the token immediately, never retried; transient errors retry with capped backoff.
- Service-account JSON arrives via host config only (`/etc/sanad/sanad.env`, e.g. `Push__Firebase__ServiceAccountJson`); missing/invalid key fails closed (`Push.NotConfigured`-style error, mirroring Paymob). Never in repo, DB, or logs.
- Push enabled ⇔ ≥1 registered token. No quiet hours, no extra master switches in V1.

## Email rules (locked)

- Shared bilingual layout (Arabic-first RTL + English below), text wordmark header (no logo — dropped by owner decision), system fonts, sender "Sanad Care".
- 12 templates: OTP code, invitation, support acknowledgment, booking paid/accepted/declined/started/completed/cancelled(+refund), subscription renewed/failed/changed, payout recorded/failed, post published/rejected. Every HTML email ships a plain-text twin.
- Bodies rendered at enqueue and stored in the outbox (immutable snapshots); existing `MessageId` stability kept.

## Preferences (locked)

The 8 existing topic switches cover everything (`bookingUpdates`, `medicationReminders`, `checkInAlerts`, `communityNotifications`, `messagesFromFamilies`, `systemNotifications`, …): push and email both check them server-side before sending. No new settings API.

## App endpoints (frozen)

1. `POST /api/v1/devices` `{platform, token, appVersion?}` → `200 {deviceId, pushEnabled:true}` (upsert; token format validated).
2. `GET /api/v1/devices` → own tokens (masked except last 6) for the settings screen.
3. `DELETE /api/v1/devices/{deviceId}` → `204`.
4. Preferences endpoint: unchanged.

## Domain and persistence (frozen shape)

- `DeviceToken` + `PushOutboxMessage` (recipient, title/body/data, idempotency key unique, status/attempts, FCM message id). One additive migration, authored only until authorized.
- `IPushSender` abstraction (`DevelopmentNoOpPushSender` local / `FcmPushSender` real) + `PushOutboxProcessor` (`BackgroundService`, capped backoff — mirrors `EmailOutboxProcessor`).

## Documentation commitment

`docs/app/notifications.md` gains devices, deep-link table, and preference rules; `docs/admin/notifications.md` gains a delivery-state note. Postman requests for device register/list/unregister go where notification routes live (`Sanad.Auth.postman_collection.json`). Every template and event documents its triggers, channels, and error codes as the frontend reference.
