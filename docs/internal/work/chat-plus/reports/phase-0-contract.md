# Chat Plus — Phase 0 execution contract

Base SHA: `41f9c91` (local `main` = `origin/main`, verified 2026-10-13).
Branch: `builder/chat-plus-production` (created from `41f9c91`; planning
documents `plan.md` / `contracts.md` / `tasks.md` / `handoff.md` /
`frontend-report.md` preserved untouched from the mastermind revision 2).
Voice remains excluded. No migration applied; no API/DB/provider/production
operation in this package — all checks below are code + InMemory/SQLite
automated gates unless a Phase 7 authorization names an exact target.

Owner acceptance recorded by the task instruction: revision 2 business rules
and additive contracts are approved. Phase 0 therefore pins mechanics, not
product choices.

## 1. Exact file manifest

### 1a. Existing integration points (read-only reuse, behavior changes noted)

- `src/API/Sanad.API/Controllers/ChatController.cs` — add
  `NormalAccess` policy (currently bare `[Authorize]`, accepts restricted
  tokens), 429 mapping, sync/privacy/presence endpoints. Wire shapes of
  endpoints 1–11 unchanged.
- `src/API/Sanad.API/Controllers/AdminChatReportsController.cs` — add
  `GET reports/{id}/messages/page`; keep array endpoint with the
  >1,000-message 409 compat (`Chat.Report.HistoryRequiresPagination`).
- `src/API/Sanad.API/Controllers/FamilyBookingsController.cs` +
  `CreateBookingCheckoutRequest` — additive optional
  `bookingAreaId`/`bookingServiceId` (both-together-or-neither).
- `src/API/Sanad.API/Controllers/CaregiverController.cs` (new file
  `CaregiverPricingInsightsController.cs` instead) — new route
  `GET /api/v1/caregiver/pricing-insights`, `CaregiverAccess` policy.
- `src/API/Sanad.API/ChatIntegration/` — rewrite
  `ApiChatEligibilityChecker` (approved actor matrix), extend
  `ApiChatBookingGateway` (reopen resolution + elderly opener), batch
  overrides on `ApiChatUserLookup`, new `CaregiverCatalogLookup`
  (Families attribution seam), new `DataProtectionChatSyncCursorProtector`,
  new `ChatRealtimePublisher` (`IHubContext` fan-out).
- `src/API/Sanad.API/Hubs/ChatHub.cs` (new) + `Realtime/`
  (`ChatConnectionRegistry`, `PresenceTracker`, `TypingTracker`) +
  `ChatPlus/ChatPlusDispatcher.cs` + `ChatAttachmentCleanupService.cs`.
- `src/API/Sanad.API/DependencyInjection.cs` — JWT `OnMessageReceived`
  scoped to `/hubs/chat`, `AddSignalR`, singleton registries, hosted
  services, `ChatPlusOptions` binding + single-instance topology guard.
- `src/API/Sanad.API/ApplicationBuilderExtensions.cs` —
  `MapHub<ChatHub>("/hubs/chat")`.
- `src/API/Sanad.API/Controllers/Requests/UpdateNotificationPreferencesRequest.cs`
  + preferences handlers — additive optional `chatMessages` with
  legacy-mirror compat.

### 1b. Chat module (domain + application + infrastructure)

Domain edits: `Conversation` (+`ChangeRevision` concurrency token,
monotonic `UpdateLastActivity`), `Report` (+`Version` concurrency token).
Domain new: `Changes/ChatChange.cs` (+`ChatChangeType.cs`),
`Outbox/ChatOutboxItem.cs`, `Privacy/ChatPrivacySettings.cs`.
Application edits: `IChatDbContext` (+3 DbSets), open/send/read/delivery/
report/list/unread/upload handlers listed in §4, `IChatBookingGateway`
(+`ResolveForReopenAsync`), batch default methods on `IChatUserLookup` /
`IChatFacilityLookup`.
Application new: `Sync/` (query, DTOs, errors, cursor-protector seam),
`Privacy/` (query, command, DTOs, errors), `Presence/` (snapshot query),
`RateLimiting/` (`IChatRateGate`, options, errors), `Events/`
(`IChatEventRecorder` + default writer), `Realtime/` (tracker interfaces).
Infrastructure: 3 configurations + 3 configuration edits, 1 additive
migration `AddChatPlusChanges`, snapshot update.

### 1c. Families / Caregivers / Identity / Notifications

- Families domain `Booking` (+nullable `BookingAreaId`/`BookingServiceId`,
  immutable setter at creation); application checkout (+optional attribution
  + `ICaregiverCatalogLookup` seam); infrastructure `BookingConfiguration` +
  1 additive migration.
- Caregivers application `Discovery/GetPricingInsightsQuery.cs` (new;
  reads Families bookings through `IFamiliesDbContext`, catalogue through
  `ICaregiversDbContext`, clock via `IDateTimeProvider`).
- Identity domain `NotificationPreferences` (+`ChatMessages`, default true,
  optional ctor tail); 1 additive migration (owned-column).
- Notifications application `PushFanoutPolicy` (`Chat/NewMessage` →
  `ChatMessages`; legacy `MessagesFromFamilies/*` rows also map to
  `ChatMessages`); API `PushOutboxProcessor.IsEnabled` (+`ChatMessages`).

### 1d. Tests, docs, collections, reports

- `tests/Sanad.UnitTests/ChatPlus/` — new focused facts per phase +
  `ChatPlusClientHarness` (runnable recovery harness: kill socket, >100
  missed + receipt-only convergence, reconnect rejoin).
- `docs/app/chat/` — update conversations/messages/reports guides; new
  `sync.md`, `realtime.md`, `pricing-insights.md`, `privacy.md`,
  `operations.md`.
- Postman — extend `Sanad.App.Chat` (sync/privacy/presence), new
  caregiver pricing examples, admin paged-messages examples; parse-gated.
- `docs/internal/work/chat-plus/reports/phase-N-*.md` per phase.

## 2. Supported booking creation paths (only one creates)

1. `POST /api/v1/family/bookings/checkout` → `CreateBookingCheckoutCommand`
   — THE supported attribution write path. Optional `bookingAreaId` +
   `bookingServiceId` persisted atomically on `Booking` at creation;
   both-together-or-neither, nonempty GUIDs, active catalogue rows selected
   by the booked caregiver (via `ICaregiverCatalogLookup`), else 400
   through the owning booking vocabulary. Legacy omissions keep current
   checkout behavior and contribute no pricing sample.
2. `POST .../payments/intent` + `POST /api/v1/payments/webhooks/paymob` —
   payment only, never attribution.
3. Caregiver accept/decline/start/complete/report routes — lifecycle only.
4. Care-home bookings (`ICareHomesDbContext.Bookings`) — NOT attributable
   (no fee snapshot, no area/service write path); excluded from pools.

No alternate creation path exists; grep-verified single `Booking.Create`
call site (checkout handler).

## 3. Migration order (all authored-only, never applied here)

1. Identity `AddChatMessagesPreference` (owned column, default true;
   legacy rows migrate from `MessagesFromFamilies`).
2. Families `AddBookingAreaServiceAttribution` (nullable GUIDs + index).
3. Chat `AddChatPlusChanges` (`chat_changes`, `chat_outbox`,
   `chat_privacy_settings`, `conversations.change_revision`,
   `reports.version`, unique `messages.attachment_id`).
Rollback: each migration is additive/nullable; downgrade drops new objects
only. Application order at startup unchanged (Identity → … → Chat);
`docs/operations/publish.md` `requiredMigrations` must list the three new
migration IDs before any Phase 7 target run.

## 4. DTOs pinned (wire shapes; see contracts.md for semantics)

- Sync: `{items:[{revision,type,payload}],nextCursor,hasMore,resetRequired}`;
  revision decimal-string; MessageCreated payload = full
  `MessageItemResponse`; receipt payload =
  `{conversationId,messageId,deliveredOnUtc,readOnUtc}`.
- Privacy: `{showOnlineStatus,shareLastSeen}` full-replacement PUT.
- Presence snapshot: `{userId,isOnline|null,lastSeenOnUtc|null}`.
- Pricing: frontend shape verbatim; null-range independence; `updatedOnUtc`
  = calculation instant.
- Hub: envelopes verbatim from frontend-report; join/leave =
  `{joined:true/false}`; hub errors sanitized `Chat.Conversation.NotFound`.
- Push data: `{conversationId,messageId,notificationId,entityId,entityKind,
  deepLink}` (existing `BuildData` + ids); no text/location.

## 5. Numeric limits (config `ChatPlus:*`, defaults = contracts.md table)

Sends 60/min/user + 30/min/user/conversation; opens 30/min/user; uploads
10/min/user + 2 concurrent/user; reports 5/hour/user; sync 60/min/conv +
300/min/user; negotiate 30/min/user; connections 5/user; 20 joins/conn;
frame 16 KiB; typing 1/2s; heartbeat 1/30s; unbound 100 MiB/user, 24 h
expiry, hourly sweep max 100/pass; multipart 52 MiB total / 50 MiB file;
outbox 8 attempts, 5 s→60 min jittered backoff; dispatch batch 100, stale
claim 5 min; support history page 30 (1–100); report array compat bound
1,000. Sliding windows, `Retry-After` on 429, no reset bursts.

## 6. Verification plan (gates)

Zero-warning build; focused `ChatPlus*` + affected `Chat*`/`Families*`
facts; full unit + architecture once; contract-mapping, Postman parse,
`git diff --check`. Concurrency/provider translation evidence needs
exact-target PostgreSQL authorization → recorded blocked, Phase 7.
Skips/limitations recorded per report; no invented results.

## 7. Phase 7 release gates left open (no authorization this turn)

Exact-target migration apply + rollback check; disposable-target runtime
(API startup, fixtures, two-client socket incl. kill/REST-fallback);
deployed proxy/TLS/origin/redaction/limit verification; Android + physical
iPhone Firebase/APNs proof (taps, token refresh, logout, preferences,
block, retries, no-content disclosure); Firebase/APNs credentials remain
owner-operated secret injection.
