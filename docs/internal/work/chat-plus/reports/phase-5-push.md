# Chat Plus — Phase 5 evidence: push integration (code)

Base: `41f9c91`. Branch: `builder/chat-plus-production`.
Status: code + focused tests complete. Operating instructions and the
secret-free Firebase/APNs checklist ride Phase 6; live delivery stays a
Phase 7 release gate.

## Changed manifest (Phase 5, code)

- Identity domain: `NotificationPreferences.ChatMessages` (default
  enabled; legacy rows adopt `MessagesFromFamilies`); response DTO,
  get/update handlers with the documented compatibility rule (explicit
  wins → legacy-change mirror → omission preserves); API request +
  controller passthrough; migration `AddChatMessagesPreference`
  authored-only (Up hand-tuned: nullable add → backfill from
  `MessagesFromFamilies` → non-null default true; model alignment
  verified clean).
- Notifications: `PushFanoutPolicy` maps canonical `Chat/NewMessage`
  and legacy `MessagesFromFamilies/*` rows to `ChatMessages`;
  `PushOutboxProcessor.IsEnabled` consults `ChatMessages`.
- Provider classification (`FcmPushSender`): `Unregistered` retires the
  token (proven token-specific); `InvalidArgument` no longer retires —
  bounded retry as `Push.PayloadRejected`; sender/auth mismatches retry
  as configuration failures for operator action; cancellation
  propagates; unconfigured stays `NotConfigured`. Sanitized codes ride
  the outbox row; no tokens or content in logs.
- Push payloads stay ids-only (`BuildData` + conversation/message ids,
  inbox deep link); no text/location in notifications, data, metrics.
- Tests: `ChatPlusPushTests` (6 facts) + one updated pre-existing
  fan-out theory (superseded mapping → approved contract).

## Verification (executed)

- New: 6/6 push facts pass (canonical + legacy routing, explicit/
  mirror/preserve compat, defaults, actual producer→outbox enqueue
  with a registered device incl. `ChatMessages` preference key and
  ids-only data).
- Model alignment: Identity `has-pending-model-changes` clean.
- Builds: API + tests zero warnings/errors.

## Skipped / blocked (Phase 7, unchanged)

- Backend Firebase service-account injection, Firebase Apple app +
  APNs key, iOS capabilities/registration, and Android/physical-iPhone
  delivery proof (foreground/background taps, denied permission, token
  refresh, logout/block/preferences, retries, environment check,
  no-content disclosure): owner-operated until explicitly authorized.
- A local PostgreSQL listener was observed earlier (auth refused, no
  credentials); untouched and unprobed, as before.
