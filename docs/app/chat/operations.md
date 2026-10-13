# Chat Plus operations

Read with the [Chat Plus plan](../../internal/work/chat-plus/plan.md)
(sections 8–9), [additive contract details](../../internal/work/chat-plus/contracts.md),
[realtime.md](realtime.md), and [sync.md](sync.md). This page is for operators
and release owners. It contains no credentials, tokens, keys, connection
strings, or device identifiers — secret values are injected through host
configuration only and must never appear in source, docs, Postman, database
fixtures, or logs. Builder disposable-target runtime and migration apply/rollback evidence is
recorded in the Phase 7 report. Production rollout, actual edge verification,
and provider/device delivery proof remain open; see the integration handoff.

## Topology

- Supported release topology is **one API instance** (`ChatPlus:Deployment`,
  `InstanceCount = 1`, `BackplaneConfigured = false`). Connection, typing, and
  presence registries are in-memory behind seams; durable change/outbox state
  is in the Chat database behind a separate transport seam.
- Declaring anything other than one instance, or setting BackplaneConfigured
  to true, **fails startup validation**: realtime (messages, receipts, typing, presence) would
  split across instances while REST persistence stays safe. Multi-instance is
  unsupported until a backplane is implemented and verified — do not conflate
  the local registry with a Redis backplane (none exists).
- Graceful shutdown stops the dispatch loop via its cancellation token;
  unprocessed durable rows are redelivered after restart (claim/retry
  semantics below).

## Configuration reference

All sections bind with the documented defaults; every value is configurable
without a code change.

| Section | Selected keys (defaults) |
|---|---|
| `ChatPlus:RateLimits` | Sends 60/min/user + 30/min/user/conversation; opens 30/min/user; uploads 10/min/user + 2 concurrent/user; reports 5/hour/user; sync 60/min/conversation + 300/min/user; negotiate 30/min/user; unbound quota 100 MiB/user; unbound lifetime 24 h |
| `ChatPlus:Dispatch` | Batch 100; 8 attempts; base delay 5 s, max 60 min, jittered; stale claim 5 min |
| `ChatPlus:Deployment` | `InstanceCount = 1`, `BackplaneConfigured = false` |
| SignalR | Inbound frame budget 16 KiB; JWT `?access_token=` accepted on `/hubs/chat` only |
| Uploads | Multipart total 52 MiB; per-type file caps 5 MiB image / 50 MiB MP4 / 10 MiB PDF |

Windows are sliding (no reset burst at clock-minute boundaries). `429`
responses carry `Retry-After` = earliest token/window release and never reveal
account/resource details.

## Background services

- `ChatPlusDispatchService` runs every **5 s** in a fresh scope: publishes
  unpublished change rows (bounded 100, ordered) to the realtime publisher and
  delivers due notification intents as idempotent
  `CreateChatMessageNotificationCommand`s (ids only). One stream's failure
  never blocks the other; hub outage converges through REST sync.
- The same cycle sweeps server-expired typing hints (8 s without refresh) and
  broadcasts stops while the pair is still eligible.
- Outbox backoff runs 5 s → 60 min with jitter across 8 attempts. Exhausted
  rows park **unpublished with their error code** for operator retry: reset
  `NextAttemptOnUtc` to requeue. Stale claims (> 5 min) are reclaimed after a
  crash — no manual cleanup is needed for dispatcher recovery.
- There is **no attachment orphan sweep worker** in this package. Expiry is
  enforced lazily: expired unbound uploads are excluded from the 100 MiB quota
  and a send binding an expired unbound upload reads as `404`. Bound messages,
  history, and moderation evidence never expire. Physical deletion of expired
  blobs needs a future worker (open item — see plan section 4); until it
  exists, monitor unbound storage growth per user.

## Health and monitoring (no PII / no message payloads)

Watch, at minimum: dispatch backlog age, unpublished/parked outbox counts and
their error codes, retry/failure rates, sync latency and query cost, active
SignalR connections, rejected joins, rate-limit hit counters (sanitized),
unbound-upload bytes per user, and provider delivery/configuration health
exposed by the push outbox. Application, proxy, and APM logs must redact
`access_token`, connection tokens, `Authorization` headers, and message
payloads. Push data, metrics, and logs never carry message text or location.

## Migrations (disposable-target verified; production unapplied)

The builder applied and rollback-tested these migrations only on the authorized
SanadBrunoTestDb target; this does not authorize production application.
Three additive migrations ship with this package and must be applied in order
(application startup order is unchanged: Identity → … → Chat):

1. Identity `AddChatMessagesPreference` — nullable `chatMessages` add →
   backfill from `messagesFromFamilies` → non-null, default enabled.
2. Families `AddBookingAreaServiceAttribution` — nullable
   `bookingAreaId`/`bookingServiceId` + pair index.
3. Chat `AddChatPlusChanges` — Chat-schema `ChatChanges`, `ChatOutbox`, `ChatPrivacySettings`,
   `Conversations.ChangeRevision`, `Reports.Version`, unique `Messages.AttachmentId`.

Each is additive/nullable; rollback drops only the new objects. The publish script's requiredMigrations now lists the three new IDs.
See [publish instructions](../../internal/operations/publish.md). Rollback
was verified on the disposable target with empty Chat tables; it does not prove
lossless rollback after real Chat Plus data exists. Never run rollback against
production from this evidence.

## Reverse proxy and client-facing requirements (Phase 7 verify)

- WebSocket upgrade support with timeouts that tolerate long-lived hub
  connections; TLS required end to end.
- Keep AllowAnyOrigin/AllowAnyMethod/AllowAnyHeader unchanged by owner confirmation;
  query-token redaction in proxy logs remains required.
- Request/upload limits aligned with the table above (52 MiB multipart total).
- Health endpoint, secret injection (no secrets in config files/images), and
  the single-instance placement rule above.

## Firebase / APNs checklist (secret-free; live proof is a Phase 7 gate)

No Apple or Firebase credential belongs in API source, Postman, database
fixtures, or this document. The owner performs the steps below; each needs
its exact-target authorization before any test send.

### Backend (operator)

- [ ] Inject the Firebase service-account/project **via host secret
      configuration only** (same pattern as `Push__Firebase__ServiceAccountJson`).
- [ ] Confirm the sender distinguishes: unconfigured (`NotConfigured`),
      invalid token (`Unregistered` retires that token only), payload/
      configuration failure (bounded retry as `Push.PayloadRejected`, operator
      action), transient provider failure (capped backoff, 8 attempts),
      cancellation (propagates). `InvalidArgument` never retires a token.
- [ ] Confirm chat pushes stay ids-only (conversation/message ids, notification
      id, inbox deep link) — no text/location in notification, data, metrics,
      or logs — and consult `chatMessages` with session/device ownership,
      account status, chat visibility, directional block policy, and
      preference re-checked immediately before each provider send; ineligible
      work is suppressed durably (never retried as a provider failure).

### Firebase console (owner)

- [ ] Firebase Apple app registered with the **exact production bundle ID** in
      the same Firebase project the backend points at.
- [ ] APNs authentication key configured in that Firebase project (correct
      team/key, production vs sandbox environment matched to the build).

### iOS app (mobile team)

- [ ] Push Notifications capability + required background modes enabled.
- [ ] FCM/APNs integration initialized, permission requested, FCM token
      registered through the existing devices API; token refresh and
      logout revocation handled.

### Delivery proof matrix (authorized devices only)

- [ ] Android **and physical iPhone**, foreground and background taps.
- [ ] Permission denied, token refresh, logout, disabled `chatMessages`,
      blocked pair — no push, no leak.
- [ ] Provider retries observed within the 8-attempt/backoff budget; correct
      environment/project confirmed; no content disclosure in any payload.

Until every box above is evidenced on the exact target, push is
implementation-verified only — never release-verified.
