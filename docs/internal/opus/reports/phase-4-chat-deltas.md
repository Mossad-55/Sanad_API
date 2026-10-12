# Phase 4 report — chat deltas

- **Phase:** 4. Chat deltas (unblocked this session)
- **Branch:** `opencode/opus-work`
- **Base SHA (phase start = origin/main merge tip):** `21081a8a73bcc65ec0daf9dce4a6718b48edda78`
  (merge of `origin/main` @ `9c2e9ca`, Chat Lane A+B core, into the work
  branch; the phase-4 unblock condition — chat core on `main` — was met and
  re-verified with `git ls-files src/Modules/Chat` = 71 files)
- **Final SHA (content commit):** `a9d8819be64c9f1ad9785e8dda579243a39a8fae`
- **Effective model:** `opencode/muse-spark-1.3-contributor-free` (Windows, dotnet 10)

## What changed

- **Open XOR (delta 1):** `OpenConversationCommand(Opener, UserId? Recipient,
  BookingId?)` — exactly one target required; both/neither is the new 400
  `Chat.Conversation.OpenTargetInvalid` (surfaced by the shared mapper's
  unmapped-code default — no shared mapper change, same philosophy as the
  chat lane's controller-local forcing). Booking-only opens resolve the
  counterpart through the new `IChatBookingGateway` port (Chat.Application →
  API-layer `ApiChatBookingGateway` over `IFamiliesDbContext` /
  `ICaregiversDbContext` / `ICareHomesDbContext`, mirroring the existing
  `CaregiverBookingRatingEligibilityGateway` precedent); every denial is the
  privacy 404. Families bookings rejected when `CancelledByFamily` /
  `CancelledByCaregiver` / `Refunded` / `Expired`; CareHomes bookings when
  `Expired` / `Cancelled` / `Refunded`. Family-side involvement = owner or
  member of a non-deleted family; caregiver-side counterpart = the booking
  creator; CareHomes sides = `FamilyUserId` ↔ facility `OwnerUserId`.
- **Message list (delta 2):** `beforeMessageId`→`beforeId`,
  `limit`→`pageSize`; items gain `conversationId`; `createdOnUtc` replaces
  `sentOnUtc` on the wire (responses, cursor docs, admin report view). The
  domain property `Message.SentOnUtc` and the `sent_on_utc` column are
  deliberately unchanged — no destructive migration (flagged).
- **Location type (delta 3):** `MessageType.Location = 2` (additive);
  payload `{latitude (-90…90), longitude (-180…180), label? (≤200, trimmed,
  blank→null)}` on request/response; domain + handler validation, replay
  comparison includes the payload; participant-only like text. Storage is the
  additive migration `20261011145248_AddChatMessageLocation` (three nullable
  columns, authored only via `dotnet ef` with a dummy connection string,
  never applied).
- **Read POST (delta 4):** controller verb PUT→POST; handler semantics
  untouched (idempotent 204).
- **Attachment route (delta 5):** `GET /api/v1/chat/attachments/{id}/file`;
  `GetAttachmentFileQuery` drops the conversation parameter — the
  participant check now loads the attachment first and gates on *its*
  conversation.
- **Participants (delta 6):** `ParticipantResponse` + `accountType`
  (Identity `AccountType` integer; `UserProfile` port + `UserProfileById`
  carry it — see flags). Care-home owners present as the facility:
  `IChatFacilityLookup` port (API impl over CareHomes) overrides
  name/cover (approved revision, English preferred, discovery-formatted
  cover URL; no approved revision → owner profile fallback). Additive
  `contactUserId` on `CareHomeDiscoveryDetail`, populated only for
  authenticated callers (route stays `[AllowAnonymous]`); the anonymous
  discovery contract is unchanged.
- **Realtime (delta 7):** verify-only — zero SignalR matches repo-wide;
  polling + the ids-only new-message alert seam unchanged.

## Tests

- Fixed all Chat/CareHomes tests that pinned the old contract (command
  shapes, PUT route, two-segment download route, `SentOnUtc` asserts,
  cursor/query property names, fake ports).
- 23 new tests, all green: `ChatBookingGatewayTests` (7, against real
  InMemory Families/Caregivers/CareHomes stores — family owner + member,
  caregiver side, foreign/unknown, cancelled/expired/declined, CareHomes
  both sides + cancelled); open XOR/privacy/resolution/400-mapping/facility
  override/accountType (ConversationTests); Location domain + send/replay/
  conflict + list item shape (MessageTests); `contactUserId`
  authed/anonymous (CareHomeDiscoveryTests).

## Verification (all on the phase-4 content commit)

| Command | Exit | Result |
| --- | --- | --- |
| `dotnet build Sanad.slnx -warnaserror` | 0 | 0 warnings, 0 errors |
| `dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj --no-build` (full suite, run once) | 0 | Passed 2903 / Failed 0 / Skipped 3 / Total 2906 |
| `dotnet test tests/Sanad.ArchitectureTests/Sanad.ArchitectureTests.csproj --no-build` (full suite, run once) | 0 | Passed 1 / Failed 0 |
| `git diff --cached --check` (content + docs commits) | 0 | no whitespace errors |
| `ConvertFrom-Json` over every file under `docs/postman/` | 0 | 20/20 valid |

The 3 unit-suite skips are the pre-existing static `[Fact(Skip=...)]`
legacy Families/Finance tax fixtures. No provider calls, no migration
applies, no seeds. Nothing pushed, merged, or deployed.

## Flags for the owner / Integrator

1. `createdOnUtc` is wire-only; storage keeps `sent_on_utc` (see above).
2. Terminal-status sets follow the contract literally: a Families
   `DeclinedByCaregiver` booking and CareHomes `Rejected` /
   `RefundPending` / `RefundInitiated` bookings stay open-eligible —
   documented in `docs/app/chat/conversations.md`; tightening them is an
   owner decision, not a bug fix.
3. Multi-account users report their **first-registered** account as
   `accountType` (documented); the facility display does not depend on it.
4. The direct-chat **role/participant matrix** (family→anyone, elderly
   limits, caregiver initiation) remains an unenforced, documented gap —
   outside the frozen delta.
5. `UserProfileById` gained `AccountType` (Identity.Application, additive);
   `GetUserProfilesByIdsQueryHandler` now `.Include(user => user.Accounts)`.
6. `ApiChatBookingGateway` / `ApiChatFacilityLookup` live in
   `src/API/Sanad.API/ChatIntegration/` — the established chat-seam area
   alongside the existing two ports — plus DI registrations; the
   controller/request changes are in `Sanad.API/Controllers/Chat*` per the
   owns list. `CareHomeDiscovery*` changes are the contract-mandated
   `contactUserId` surface.
7. Merge note: `PlatformChargeRuleTests` takes main's clock-relative fix
   (`c820aae`); the phase-6 hardcoded-2030 repair in this branch was dropped
   in favor of it during the `21081a8` merge.
8. Postman: new "Open or get conversation (booking)" + "Send location
   message" requests, `bookingId` collection variable, read verb, download
   route, `beforeId`/`pageSize`, `createdOnUtc` + `conversationId` +
   `location` shapes, `accountType` + `contactUserId` notes.
