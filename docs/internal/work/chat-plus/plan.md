# Chat-plus package (pricing insights + full SignalR with polling fallback) — frozen plan (v1, read-only)

Owner-ordered 2026-10-12, expanded to blocker-free depth the same day:
pricing stats as specified (N=5 completed bookings / 90 days); voice messages
DROPPED entirely; FULL SignalR realtime with REST polling as automatic backup;
Firebase live-fire deferred. Nothing here authorizes code, migration applies,
seeds, provider calls, or runtime.

## 0. Golden rules (frozen — the whole design hangs off these)

1. **REST is the system of record.** Sends, history, idempotency, ordering, and
   read-state live in the existing REST endpoints, unchanged. The socket
   accelerates screens; it never owns truth.
2. **Every event is re-derivable by polling.** If the socket is dead, slow, or
   forbidden on the network, the client gets the identical state through
   `GET /messages?beforeId=&pageSize=` + unread-count. No socket-only state,
   ever — except the deliberately ephemeral (typing spark, online dot), which
   are documented as hints, not data.
3. **Fail closed on membership.** No event, presence, or typing hint ever
   reaches a non-participant, a blocked party, or a stranger. Every join and
   every push re-verifies.
4. **No new PII surfaces.** Message content never enters logs; presence honors
   the existing block/privacy posture; tokens never ride URLs beyond the
   negotiate step (see §2).

## 1. Scope (closed — new surfaces need a new decision)

- `GET /api/v1/caregiver/pricing-insights?areaId=&serviceId=` — market hourly
  min/max + most-requested 8-hour-day min/max, EGP, `updatedOnUtc`; `null`
  fields (never zero-filled) when fewer than 5 completed bookings in 90 days
  for the pair. Computed per request, no storage.
- SignalR hub `/hubs/chat` — JWT auth, per-conversation groups, 4 events
  (§2), transport fallback chain (§3), reconnect replay (§4), presence (§5).
- Polling fallback protocol (§3) — specified, documented, and live-tested as a
  first-class path, not an afterthought.
- **No database migration in v1.** Presence/typing live in memory only; stats
  compute; everything else reuses existing tables. A restart loses only online
  dots and typing sparks (documented, acceptable).

## 2. Endpoint + protocol catalog (frozen contract)

| # | Surface | Contract | Success | Failures |
|---|---|---|---|---|
| 1 | `GET /api/v1/caregiver/pricing-insights` | Query `areaId`, `serviceId` (required GUIDs) | 200 shape per report (nulls when thin) | 400 invalid/missing ids |
| 2 | `POST /hubs/chat/negotiate` | Bearer JWT in **header** (never query string — URLs end up in logs); returns connection ticket | 200 ticket | 401 anonymous/invalid |
| 3 | WebSocket transport | Upgrade carrying the negotiate ticket (no JWT on the socket) | connected + joined groups | ticket mismatch → drop |
| 4 | `MessageCreated` push | On every created message, to conversation group only | delivered | non-participants receive nothing (drop + audit) |
| 5 | `MessageReceiptUpdated` push | On delivered/read stamp changes | delivered | same scoping |
| 6 | `TypingChanged` push | Client `typing(start|stop)`; server expires after 8s without refresh; max 1 event / 2s per user (spam guard) | delivered | expired states never re-sent; over-limit hints dropped silently |
| 7 | `PresenceChanged` push | Heartbeat 30s; timeout 90s → offline; online if ANY device connected; last-seen = last heartbeat; strangers/blocks see nothing | delivered | — |
| 8 | Reconnect replay | Client sends last-seen message id; server answers through the existing list endpoint (`beforeId`/`pageSize`) | ordered catch-up, zero gaps | unknown cursor → full recent page |

## 3. Transports and the polling fallback (the heart of your request)

SignalR negotiates transports in order and degrades automatically:

1. **WebSockets** (primary) → 2. **Server-Sent Events** → 3. **Long Polling**
   (all built into SignalR — configured, not coded).
2. If all three fail (hostile network, old client, killed socket), the client
   runs **REST polling**: `GET /messages` newest-first + `GET /unread-count`
   on the existing cadence. The realtime guide documents the exact degraded
   behavior per level (what stays live, what lags, what disappears — only the
   typing spark and the online dot disappear; everything else converges).
3. **Reconnect storm discipline**: exponential backoff with jitter (1s → 30s
   cap), resume token = last message id, replay through REST (§2.8). A
   reconnecting client MUST NOT miss messages even if the socket was down for
   an hour — this is live-tested by killing the socket mid-conversation.
4. Mobile backgrounding: on foreground, client always replays (`since`
   cursor); background push remains Firebase's job, unchanged.

## 4. Scale-out story (decided now so it never blocks later)

- v1 ships **single-server safe**: in-memory group/presence/typing registries.
- The registries sit behind an `IMessageBus` seam (in-memory implementation in
  v1, unit-tested). A Redis backplane plugs into the same seam later with zero
  hub-code changes — the seam, its contract tests, and the config flag ship in
  v1; the Redis package and ops do not.
- Documented limitation (operator guide + plan): multi-instance deployments
  before the backplane lands will split presence/typing across instances.
  Messages and receipts are unaffected (REST-backed).

## 5. Security rules (non-negotiable, all tested)

1. Group name = `conversation:{id}`; join validates participant status at join
   time AND re-validates on every push (mid-session block/kick stops delivery).
2. Preview/draft and admin report surfaces never traverse the hub.
3. No message text, no tokens, no PII in hub logs — connection metrics only
   (connects, drops, pushes per event type).
4. Typing/presence rate-guarded (§2.6–2.7); malformed client frames dropped.
5. Web clients: socket origins allow-listed in CORS exactly like the REST API.

## 6. Business rules and cases

1. Stats null-semantics: thin data → `null`, never `0`; N=5/M=90 are named
   constants, one-line change. Aggregates completed + paid bookings only.
2. Typing without a prior join is dropped; typing in a blocked/left conversation is dropped.
3. Presence multi-device OR; offline timeout 90s; last-seen frozen at last heartbeat.
4. Reconnect with unknown/empty cursor → newest page (documented, deterministic).
5. Stats equal direct aggregates, byte for byte, on the same data.

Cases: empty stats (nulls), exact-threshold boundary, anonymous negotiate 401,
non-participant join + mid-session block drops, typing expiry without close,
presence invisible to strangers/blocks, reconnect replay order after killed
socket, polling-fallback convergence (socket killed → same final state via
REST), backplane-seam contract tests, stats-vs-direct equality.

## 7. File manifest (exclusive)

- `Modules/Caregivers/Application/.../Pricing/` (insights query/handler — reads only).
- New `Sanad.API/Hubs/ChatHub.cs` (thin: auth, groups, event fan-out calls into
  the existing application layer — no business logic inside).
- New bus seam (`IMessageBus` + in-memory implementation + flag) for
  presence/typing/groups.
- `tests/.../Caregivers/PricingInsightsTests.cs` + hub tests (auth scoping,
  expiry, privacy, backplane-seam contract, reconnect replay) — TestServer +
  SignalR test client, no live sockets in unit gates.
- `docs/app/caregivers/pricing-insights.md` + `docs/app/chat/realtime.md`
  (protocol incl. fallback levels + reconnect flow) + Postman additions
  (insights request; hub documented as protocol, not Postman-runnable).
- `docs/internal/work/chat-plus/{tasks.md,handoff.md,reports/}` tracking.

## 8. Verification (incl. live API)

Per phase: zero-warning build; focused tests (InMemory + SQLite twins for the
insights query; hub scoping/expiry/privacy/replay unit tests); docs + Postman
synced; diff-check; contract-map 0/0 (hub routes excluded with this documented
reason). Joint gate: full unit + arch once. **Live gates** on the disposable
target: insights vs direct DB counts; real two-client socket test (message →
push, receipt → push, typing expiry, blocked-user silence); **socket-kill test**
(kill mid-conversation → polling fallback converges to identical state);
sessions revoked; API stopped. Mutating live checks need per-run approval.
Firebase live-fire stays deferred per owner order.

## 9. Decisions recorded (locked 2026-10-12, expanded same day)

Pricing as specified (N=5/M=90); voice dropped; full SignalR per this expanded
spec with polling fallback as a tested first-class path; single-server safe +
bus seam now, Redis backplane at scaling review; presence in-memory with
documented restart behavior; Firebase/APNs live proof deferred.
