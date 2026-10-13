# Chat Plus — Phase 7 evidence: release verification (backend gates)

Branch: `builder/chat-plus-production`. Base: `41f9c91`.
Code at Phase 6 closeout `006f510` plus the migration-correctness fix in
this turn's commit (see §2). Voice excluded.

Authorizations (owner, this turn): disposable target
`localhost:5432/SanadBrunoTestDb`; API Development `http://localhost:5235`
pinned to that DB, stopped afterwards; apply + rollback of the three Chat
Plus migrations; minimal fixtures via supported APIs/seeders; push delivery
proof owner-run with the checklist. No production, reset, broad cleanup,
credential configuration, or provider send occurred. All secrets stayed
process-local and were never printed, persisted, or committed.

## 1. Target verification (read-only, before startup)

- PostgreSQL listener reachable on 5432; port 5235 free.
- Only `IdentityDatabase` (+integration) connection configured; all module
  contexts fall back to it. The built-in `TestUserSeedTargetGuard` pins to
  `SanadBrunoTestDb` and refuses any other target; startup additionally
  derives and validates all nine module strings in-process (localhost +
  test-DB name asserted, values withheld).
- `TestUserSeed:Enabled=true` in this environment; seeder-required
  `ElderlyPassword`/`RunId` supplied process-only (random / `chatplus7`).
  `Paymob:SecretKey` overridden to empty in-process so no provider send is
  possible; Finance startup migrations off by default.

## 2. Migration application — 3/3 applied live, one real defect found+fixed

First startup applied, per context (startup log counts):
Identity `20261013104759_AddChatMessagesPreference` 1 applied; Families
`20261013005844_AddBookingAreaServiceAttribution` 1 applied; Chat
`20261013000659_AddChatPlusChanges` 1 applied; all other contexts 0 pending.

**Defect caught by the exact target (would have broken any PostgreSQL
apply, including production):** the hand-tuned Identity backfill referenced
columns `NotificationPreferences_ChatMessages` /
`NotificationPreferences_MessagesFromFamilies`, which never existed — the
owned-type convention is snake_case (`chat_messages` was missing entirely,
source is `messages_from_families`). The `has-pending-model-changes` gate is
blind to hand-tuned SQL drift (model↔snapshot agreed while `Up()` diverged),
so only the live apply caught it. Fix in this commit: explicit
`chat_messages` configuration (`HasColumnName`, default true, required) in
`UserConfiguration`, corrected `Up()`/`Down()`, identical Designer +
snapshot edits. Re-verified: model alignment clean, live apply green,
history rows present for all three migrations, `information_schema`
confirms `chat.ChatChanges`/`chat.ChatOutbox`/`chat.ChatPrivacySettings`,
`Conversations.ChangeRevision`, `Reports.Version`, `Messages.AttachmentId`,
`bookings.booking_area_id/_service_id` (+index), `users.chat_messages`.

## 3. Rollback verification — 3/3 cycles on the exact target

API stopped; per context `dotnet ef database update <previous>` →
`migrations list` shows ours `(Pending)` → `database update` →
list shows applied, history consistent:
- Chat down to `20261011145248_AddChatMessageLocation` and back.
- Families down to `20261005111105_AddMedicalAccessGrantGrantee` and back.
- Identity down to `20261012064508_AdminUserActions` and back.
No fixture data lost (chat tables were empty; seeded bookings unattributed;
`chat_messages` re-backfilled from `messages_from_families`, which no test
had yet diverged). API restarted afterwards: all contexts 0 pending.

## 4. Runtime manifest — 47/47 checks pass, disposable target only

Harness: throwaway two-client console (SignalR.Client + Npgsql, TEMP dir,
never committed) against `http://localhost:5235`; seeded users
owner/viewer/medical-caregiver (+companion for Hourly); one device session
per user; targeted logouts at the end (3×204); API stopped, port verified
free. No token material in the API log (`eyJ` hits: 0); no Chat/Hub/
SignalR/Dispatch failure lines.

- Schema/history on live DB: tables, columns, 3/3 history rows.
- Matrix live: owner→caregiver 200; caregiver→linked-family viewer 200
  (correct: seeded live bookings link the family); caregiver→unlinked
  subscription family (0 non-terminal links by direct SQL) 404
  `Chat.Conversation.NotFound`. (An early harness expectation of 404 for
  the linked case was wrong; corrected with the SQL link proof.)
- Sockets: header-auth + query-auth connect; join; typing start + server
  8 s-expiry stop; REST send → `MessageCreated` on both via the durable
  dispatcher pipeline; history fetch → `MessageReceiptUpdated` to sender;
  read → sync delta carries the read stamp.
- Privacy: defaults undisclosed (`isOnline` null); opt-in + heartbeat →
  online true; restored to false/false afterwards.
- Cursors: snapshot + change cursors; garbage/tampered → 404
  `Chat.Sync.NotFound`; a requester-foreign cursor 404s by design
  (proves requester scoping live).
- Kill-socket: B killed, 5 missed, reconnect + sync from B's own cursor →
  5/5 recovered, `hasMore` drained.
- Abuse control live: 31 rapid sends → 24×201 + 7×429 with `Retry-After`.
- Push path to inbox: 50 `Chat/NewMessage` in-app alerts for B
  (producer→outbox→dispatcher→inbox proven); provider payload keys covered
  by unit fact; Development uses the no-op sender — no provider send.
- Pricing live: attributed Hourly checkout 200 with a selected pair;
  one-sided/unknown pairs 400; pair durable on the booking row;
  insights 200 with independent nulls + `updatedOnUtc`; empty/unknown ids
  400 `Caregivers.PricingInsights.InvalidFilter`; direct-SQL eligible
  counts hourly=0, eight-hour=0 agree with API nulls.
- Transport guards: REST with `?access_token=` → 401 (hub-scoped only);
  hub CORS preflight → 204 (recorded; exact-allowlist values are a
  deployment item, §6.4).
- Restart: unclean stop → restart → inbox stable at 50 alerts (no
  redelivery duplicates); presence unknown until heartbeat, then online;
  existing conversation reopens 200.

## 5. Adjacent observation (not fixed — out of slice)

`CareHomeReviewNotificationProcessor` logs a recurring EF LINQ translation
failure (`u.Id.Value` comparison) on this target. Pre-existing, unrelated
to Chat Plus, no chat impact; left for its owning slice.

## 6. Deferred / remaining items (nothing hidden)

1. **Owner-run device proof (only Phase 7 item needing the owner):**
   Android + physical iPhone matrix in `docs/app/chat/operations.md`
   (taps, permission denied, token refresh, logout, preferences, block,
   retries, environment, no-content disclosure). Backend is ready
   (classification, ids-only payloads, preference gating proven); live
   delivery stays unverified until the owner supplies device evidence.
2. **Satisfied-pool live aggregate equality:** paid state is unreachable
   through public APIs without a provider round-trip (intent needs Paymob;
   webhook needs an existing intent), so no N≥5 attributed sample exists
   live. Unit suite proves the arithmetic; live run proves filter
   translation + null agreement. Unblock recipe: owner-authorized
   payment-path fixture (loopback-stub pay → accept → complete for
   5 Hourly + 5 EightHour attributed bookings), then API-vs-direct-SQL
   equality.
3. **24 h unbound-expiry long window:** logic unit-covered; not observable
   inside one session.
4. **Edge TLS/proxy:** local run was http loopback; TLS termination,
   WebSocket timeouts, and the exact origin allowlist at the real edge
   remain deployment-verify items.
5. **Orphan sweep worker:** still a future worker (Phase 6 record stands).
6. **Item 5 above** (other slice).

## 7. Status

Backend release verification is complete on the authorized disposable
target; the report, checklist, and handoff updates follow in this commit.
Release is **not** claimed production-ready: items 6.1 (owner devices)
and 6.2 (satisfied-pool equality) remain open by evidence, not by effort.
