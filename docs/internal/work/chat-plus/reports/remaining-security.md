# Remaining security — session-bound access and final-send push privacy (Prompt 1)

Branch: `worker/chat-plus-session-security`.
Base: `436973f2678e028f564dbe970e98f7b2142ee004` (clean `main`, synced with `origin/main` at intake).
Final SHA: `4a97485e77a38bfb854f5c83372e6eac22f3d515` (this branch tip; gates below ran on this tree; `main` untouched, not merged/pushed).

Owner direction: Bruno ignored entirely; no Bruno authored/run/read. No provider send occurred in tests (fake senders only). No migration applied; no VPS/DB/provider/deploy action. No live credentials altered. Unrelated auth requirements unchanged.

## Exact manifest (owned files only)

Identity token/session:
- `src/Modules/Identity/Application/Sanad.Modules.Identity.Application/Authentication/Tokens/AuthClaimNames.cs` (+`sid`)
- `src/Modules/Identity/Application/Sanad.Modules.Identity.Application/Abstractions/Security/IAuthTokenService.cs` (optional `DeviceSessionId?`)
- `src/Modules/Identity/Infrastructure/Sanad.Modules.Identity.Infrastructure/Security/JwtAuthTokenService.cs` (emit `sid` on Normal only)
- `src/Modules/Identity/Application/Sanad.Modules.Identity.Application/Authentication/Login/LoginCommandHandler.cs` (session-first bound issuance)
- `src/Modules/Identity/Application/Sanad.Modules.Identity.Application/Authentication/ElderlyLogin/VerifyElderlyLoginOtpCommandHandler.cs` (session-first bound issuance)
- `src/Modules/Identity/Application/Sanad.Modules.Identity.Application/Authentication/Refresh/RefreshTokenCommandHandler.cs` (bound rotation, same session)

API auth/session:
- `src/API/Sanad.API/Authorization/AuthorizationPolicies.cs` (+`SessionBoundAccess`)
- `src/API/Sanad.API/DependencyInjection.cs` (JWT `OnTokenValidated` shared session check, `SessionBoundAccess` policy, validator + evictor registration)
- `src/API/Sanad.API/Security/IdentitySessionValidator.cs` (new shared ownership/revocation check)
- `src/API/Sanad.API/Controllers/ApiControllerBase.cs` (verified `sid` helpers; header kept only as logout-current fallback)
- `src/API/Sanad.API/Controllers/ChatController.cs` (`SessionBoundAccess`)
- `src/API/Sanad.API/Hubs/ChatHub.cs` (`SessionBoundAccess`, verified session metadata, per-call revalidation)
- `src/API/Sanad.API/Controllers/AuthController.cs` (evict one/all device joins on logout-current/all/revoke-one)

Chat realtime:
- `src/Modules/Chat/Application/Sanad.Modules.Chat.Application/Realtime/ChatRealtimeSeams.cs` (session per connection, evict APIs)
- `src/API/Sanad.API/Realtime/ChatConnectionRegistry.cs` (session map + eviction)
- `src/API/Sanad.API/Realtime/ChatRealtimePublisher.cs` (per-dispatch revoked-session filter)
- `src/API/Sanad.API/Realtime/ChatConnectionEvictor.cs` (session/user eviction + typing stops)

Push final-send:
- `src/API/Sanad.API/NotificationsIntegration/PushOutboxProcessor.cs` (chat gate + durable suppression, fake-safe)

Tests (updated + new):
- `tests/Sanad.UnitTests/Identity/Infrastructure/JwtAuthTokenServiceTests.cs` (unchanged, still passes via optional param)
- `tests/Sanad.UnitTests/Identity/Login/LoginCommandHandlerTests.cs`, `Refresh/...`, `ElderlyLogin/...` (fake signature update only)
- `tests/Sanad.UnitTests/ChatPlus/ChatPlusPhase1Tests.cs` + 5 `tests/Sanad.UnitTests/Chat/*Tests.cs` (Chat now expects `SessionBoundAccess`)
- `tests/Sanad.UnitTests/ChatPlus/ChatPlusSessionSecurityTests.cs` (new, 8 facts)

Docs/Postman:
- `docs/auth/claims-and-policies.md`, `docs/auth/refresh-and-sessions.md`
- `docs/app/chat/conversations.md`, `docs/app/chat/realtime.md`, `docs/app/chat/operations.md`
- `docs/postman/app/Sanad.App.Chat.postman_collection.json` (description only; wire shapes unchanged)

No migration authored: no schema change (sid is a JWT claim; eviction is in-memory + existing `DeviceSessions` rows). Nothing applied.

## Compatibility policy (explicit rollout, not silent)

- New Normal access tokens carry `sid` (issuing session id). Restricted-verification tokens never carry `sid` (behavior preserved). Response shapes unchanged (`LoginResponse`/`RefreshTokenResponse` already carried `DeviceSessionId`).
- JWT `OnTokenValidated`: Normal+`sid` must map to a live session row (owner match, not revoked, not expired, user Active) else `401`. Normal without `sid` (legacy) still passes `NormalAccess` until its natural 15-minute expiry.
- Sensitive Chat access (`ChatController`, `ChatHub` via `SessionBoundAccess`) rejects legacy unbound tokens immediately with `401/403`; clients refresh after login. This bounds the break to a 15-minute Chat-only window and never permanently accepts unbound tokens for Chat.
- Multi-device isolation: distinct `sid` per device session; refresh preserves `sid`.
- Expired-token handling preserved (`ValidateLifetime`, `ClockSkew=0`, `CloseOnAuthenticationExpiration` kept).

## Invalidation timing (proven bound, not immediate socket recall)

- Hub calls: immediate. Every `OnConnected/Join/SetTyping/Heartbeat/Leave` revalidates user Active + session live; revoked/legacy aborts, evicts joins/hints.
- Realtime dispatch: within the 5s `ChatPlusDispatchService` cycle. `ChatRealtimePublisher` re-resolves audience + filters revoked sessions per message/receipt/typing/presence send; no new dispatch after a committed invalidation is observed. Already-sent frames cannot be recalled (documented).
- REST Chat: immediate via `SessionBoundAccess` + per-request JWT validation.
- Push final-send: immediate per item before any provider `SendAsync`; ineligible work `MarkSuppressed` durably (no retry as provider failure).
- Sockets: abort on next hub call; close on access-token expiry (max 15 min). Proactive join/hint eviction runs synchronously on logout-current/all/revoke-one; password-reset/reuse/disable rely on the validator bound above (no cross-process socket kill claimed).
- Typing: 8s server expiry + sweep; presence: 90s OR + 30s heartbeat throttle (unchanged).

## Actual tests (all with fake providers; zero provider sends)

- Zero-warning build: `dotnet build Sanad.slnx --no-restore -m:1 --nologo` → exit 0, 0 warnings/errors.
- New `ChatPlusSessionSecurity` filter: 8/8 passed — sid emission/omission, login session-first + two-device isolation, refresh same-session rotation, validator ownership/revocation/expiry/disabled, hub verified-session store + revoked/legacy reject + revoke-one/all eviction, publisher revoked-drop, push block/hide/logout suppress + directional sender-block allow + crash/retry durability (fake sender 0 sends on suppress).
- Focused: `ChatPlus|Identity.Login|Identity.Refresh|Identity.ElderlyLogin|JwtAuthTokenService|Notifications` → 227/227 passed. `Chat|AuthApiHost` → 234/234 passed after auth-posture updates.
- Full unit: 3101 passed / 0 failed / 3 existing PG skips (total 3104). Architecture: 1/1. Contract mapping script: 556 signatures / 620 Postman / 0 missing / 0 orphans. Postman collections parse; `git diff --check` clean.

Covered: two devices, current/all logout, revoke-one, refresh, reset/reuse (handler revocation + validator), expired tokens (lifetime + expired session), disabled accounts, queued push followed by block/hide/preferences/logout, crash/retry (outbox claim/retry + suppressed durability). Expired access-token closure kept; reuse detection revokes all sessions (existing handler, validator enforces).

## Limitations (not claimed)

- No live API/DB/provider/socket run; all evidence is automated InMemory/relational-unit, not disposable-target runtime. `HasAnyActiveSessionAsync` uses `DateTime.UtcNow` (not injected clock) — unit-covered but not clock-pinned.
- No proactive socket kill on password-reset/reuse/admin-disable from Identity handlers (API eviction covers logout paths only); those rely on the documented call/dispatch bound above.
- Push gate suppresses when the Chat store is unreachable (fail-closed); non-chat notifications preserve prior behavior.
- No migration, no package, no deployment, no device proof.

## Unresolved product/compatibility decision for the planning mastermind

Legacy Normal tokens remain valid on non-Chat routes for up to 15 minutes after this ships, while Chat rejects them immediately. Decide one: (A) keep this Chat-first rollout (recommended — minimal break, no permanent unbound Chat), forcing Chat clients to refresh once; or (B) require `sid` globally on a flag day (breaks all active sessions at once, needs client coordination + logout-all). No code change ships until the mastermind picks A or B and sets the client refresh notice.
