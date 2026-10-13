# Chat Plus — Phase 4 evidence: hub, typing, presence, client recovery

Base: `41f9c91`. Branch: `builder/chat-plus-production`.
Depends on Phases 1/3 (sync, privacy contract, publisher seam).

## Changed manifest (Phase 4)

- Chat Application: `Realtime/` seams extended (`PublishTypingChangedAsync`,
  `PublishPresenceChangedAsync`, `IChatHubAudience` + audience record,
  `IChatAuthorizationChangedNotifier`); `ITypingTracker.Refresh` takes the
  connection id; `IChatRateGate.TryAcquireNegotiate` + `ChatRateOptions.
  NegotiatePerMinutePerUser` (30/min/user).
- Handlers: block/hide/privacy-update accept an optional notifier and
  evict affected realtime state after commit (unblock needs none —
  clients rejoin).
- API new: `Hubs/ChatHub.cs`, `Realtime/` (registry, presence/typing
  trackers, audience, publisher, evictor), `ChatPlus/
  ChatPlusDeploymentOptions.cs`.
- API edits: `DependencyInjection` (SignalR + 16 KiB frame budget, JWT
  `OnMessageReceived` scoped to `/hubs/chat`, singleton registries,
  scoped audience/publisher/evictor, single-instance topology guard),
  `ApplicationBuilderExtensions` (`MapHub`), dispatch service (5s
  typing-expiry sweep with eligibility re-check).
- Tests: `ChatPlusRealtimeTests` (11 facts incl. integrated
  disconnect→reconnect→rejoin→sync recovery).

## Decisions and deviations

- Standard SignalR negotiation; user routing from validated JWT `sub`
  only. Browser query-token auth is scoped to the hub path; headers
  elsewhere. TLS, origin allowlist, and token redaction are deployment
  configuration (operating guide, Phase 6); code cannot enforce them.
- `CloseOnAuthenticationExpiration` has no corresponding server option —
  enforced equivalently via the `NormalAccess` hub policy, per-call
  authorization, 15-minute access tokens, and client refresh/reconnect.
- Groups are routing only: every dispatch resolves audience + joined
  connections first; hidden/blocked recipients receive nothing (REST
  history untouched). Either-direction block suppresses all realtime
  between the pair.
- Presence is opt-in (both flags default false); last-seen persists per
  user in memory across disconnects (last accepted heartbeat, never the
  timeout instant), lost only on restart; online = any-device heartbeat
  <90s. Typing 1/2s, heartbeat 1/30s, 5 connections/user,
  20 joins/connection — sanitized drops/rejections.
- Single-instance topology validated at startup; multi-instance without
  a verified backplane throws. No Redis.

## Defect found and fixed in-turn

Sync change-mode `MessageCreated` payloads were double-wrapped
(`SyncChangeItem` inside `SyncChangeItem`), found by the new reconnect
test. Fixed by splitting the message payload builder from the snapshot
envelope; the sync delta test now asserts payload types per item.

## Verification (executed)

- New: 11/11 realtime facts pass (tracker semantics, hub budgets/auth,
  audience gating, eviction, negotiate cap, integrated reconnect with
  missed-message + receipt-only convergence to quiet).
- Regression in-turn: full `ChatPlus` filter 52/52; Chat/Notifications/
  Identity preference suites 478/478 (one pre-existing fan-out theory
  pinned the superseded mapping and was updated to the approved
  `ChatMessages` contract).
- Builds: API + tests zero warnings/errors.

## Skipped / blocked (Phase 7)

- Two-client socket proof, transport-fallback observation (SSE/Long
  Polling), deployed proxy/TLS/origin verification: need exact-target
  authorization. REST fallback is frontend-owned with the tested sync
  contract; the suite is the runnable harness (no TestServer sockets).
