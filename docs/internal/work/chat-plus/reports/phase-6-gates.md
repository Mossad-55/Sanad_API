# Chat Plus — Phase 6 evidence: documentation, review, joint gates

Branch: `builder/chat-plus-production`. Base: `41f9c91`.
Prior commits on the branch: `86712c2` (Phases 0–3 + Phase 2 code),
`cef95c1` (Phases 4–5), `92889e1` (Phase 2 evidence report).
Phase 6 code/docs commit: `896c46b`. This report + checklist/handoff sync
follow in the closeout commit (final SHA recorded in the handoff).

Status: Phase 6 complete on evidence below. Voice excluded. No API startup,
migration application, fixture write, credential configuration, provider
send, push, deploy, or main-branch operation was performed — all checks are
static/automated (InMemory/SQLite) unless a Phase 7 authorization names an
exact target.

## 1. Operating instructions + Firebase/APNs checklist (Phase 5 remainder)

New `docs/app/chat/operations.md`: single-instance topology and startup
guard, `ChatPlus:RateLimits` / `ChatPlus:Dispatch` / `ChatPlus:Deployment`
reference with defaults, 5 s dispatch loop semantics (bounded batch,
8-attempt jittered backoff, stale-claim reclaim, operator requeue via
`NextAttemptOnUtc`), typing-expiry sweep, monitoring list (no PII/payloads),
log-redaction rule, migration order + rollback, proxy/TLS/origin
requirements, and a secret-free Firebase/APNs checklist (backend injection,
console bundle/APNs key, iOS capabilities/registration, authorized
Android + physical-iPhone delivery matrix). Honestly records that **no
attachment orphan sweep worker ships**: expiry is lazy (bind/quota time);
physical deletion needs a future worker (plan section 4 deferred item).

## 2. Guide/Postman verification (documenter-authored, statically checked)

The documenter-authored sync/realtime/pricing guides plus Postman folders
`09.Sync and recovery`, `10.Privacy and presence`, `11.Pricing insights`
(App.Chat) and the paged-messages Admin request were verified against the
implementation by static inspection (never executed). Findings repaired:

- `realtime.md`: `CloseOnAuthenticationExpiration` is *not* a server option —
  corrected to the equivalent enforcement (NormalAccess hub policy, per-call
  auth, 15-minute tokens, client refresh); orphan-cleanup claim corrected to
  lazy expiry (no sweep worker).
- `conversations.md`: NormalAccess policy, enforced role matrix (the "not
  enforced" gap note replaced), retained-history reopening after terminal
  bookings, stable id tie-break, validated monotonic reads, quota/expiry,
  rate limits; error table denial reasons updated.
- `messages.md`: NormalAccess, sender-bound replay, durable outbox replacing
  the "no retry loop" residual, enforced matrix.
- `reports.md`: paged `messages/page` endpoint, >1,000-message `409`
  `Chat.Report.HistoryRequiresPagination`, conditional single-winner
  resolution, 5/hour submit limit.
- `account.md` / `overview.md`: 10-field preference shape
  (+`helpRequestAlerts`, +`chatMessages`), optional-with-mirror PUT rule.
- `notifications.md`: canonical `ChatMessages` mapping, durable producer,
  `InvalidArgument` no longer retires tokens.
- `families/bookings.md`: optional `bookingAreaId`/`bookingServiceId`
  checkout contract.
- `chat/README.md`: links `operations.md`.
- Pricing example placement (App.Chat folder 11 with caregiver-JWT note,
  matching its guide) kept as-is; no Caregiver-collection duplicate.

## 3. Independent review (one bounded pass, all findings resolved)

A read-only review of the Chat Plus diff returned 6 concrete findings; all
were verified against the code and fixed in `896c46b`, each with a locking
fact (ChatPlus filter 58/58, up from 52):

1. Sync privacy 404 never forced: `Chat.Sync.NotFound` (unknown/foreign/
   hidden conversation, bad cursors) fell to the shared-mapper default 400.
   Fixed in `ChatController.IsPrivacyFailure` (+ comment). The single method
   covers both `Result`/`Result<T>` paths.
2. Read hidden-oracle: `MarkConversationReadCommandHandler` answered `204`
   on hidden conversations vs `404` on unknown. Fixed with the `IsHiddenFor`
   gate (parity with send/history/sync/presence). Fact:
   `Read_HiddenConversationIsPrivacy404`.
3. Provider payload missing ids: `BuildData` carried no `conversationId`/
   `messageId`. Fixed chat-scoped keys (destination + stable producer-key
   parse; non-chat rows unchanged). Fact:
   `FanOut_ChatPayloadCarriesConversationAndMessageIds`.
4. No dispatch-time re-check: intent delivered without block/visibility/
   account re-verification. Fixed: `DeliverIntentsAsync` re-resolves
   participant + visibility + recipient-blocked-counterpart + account
   liveness (optional lookup seam), suppressing via new
   `ChatOutboxItem.MarkSuppressed` (leaves the due set, keeps the reason).
   Direction mirrors send-time; push-processor preference/device checks
   unchanged. Facts: hidden/blocked/deactivated suppression (3).
5. Admin actor 500: `Actor()` threw on missing/malformed `sub`. Fixed with
   `TryActor` → 401 on dismiss/uphold. (Same pattern elsewhere in the repo
   is out of scope; noted, not widened.)
6. Attribution fail-open: null catalogue seam silently accepted unverified
   pairs. Fixed fail-closed (`Bookings.AttributionInvalid`); legacy omission
   unaffected; prior constructions compile unchanged. Fact:
   `Checkout_AttributedWithoutCatalogLookupFailsClosed`.

Residual, documented: a block/hide landing between in-app row creation and
the 15 s push-processor send still pushes (processor sees preference +
devices only); dispatcher-side suppression closes the commit-to-dispatch
window, which is the plan-mandated dispatch point.

## 4. Joint gates (executed, this branch at `896c46b`)

- `dotnet build Sanad.slnx` — 0 warnings, 0 errors.
- Full unit `Sanad.UnitTests` — 3070 passed, 0 failed, 3 skipped
  (pre-existing PostgreSQL-only subscription skips), total 3073.
- Architecture `Sanad.ArchitectureTests` — 1/1.
- `docs/internal/tools/Verify-ApiContractMapping.ps1` — 555 controller
  signatures, 619 Postman requests, 0 missing / 0 orphan Postman; Bruno
  1411 requests, 0 orphans, 148 missing (standing exclusion: Bruno is
  risk-based/optional; Chat Plus verification is unit-based per plan —
  new Chat/sync/privacy/pricing routes included in the 148, no new
  Postman gap).
- Postman JSON parse — 22/22 files valid.
- `git diff --check` — clean (CRLF notices only, repo-standard).
- `dotnet ef migrations has-pending-model-changes` — clean (no model
  changes) for Chat, Families, Identity.
- `ChatPlus` filter — 58/58; affected suites
  (Chat/Notifications/Families/Caregivers/Identity) — 2339 passed, 3 skips.

## 5. Migration status (authored-only, never applied)

1. Chat `20261013000659_AddChatPlusChanges` — authored, unapplied.
2. Families `20261013005844_AddBookingAreaServiceAttribution` — authored,
   unapplied.
3. Identity `20261013104759_AddChatMessagesPreference` — authored, unapplied.
4. `docs/operations/publish.md` `requiredMigrations` update and any
   application/rollback remain Phase 7 work on the exact target.

## 6. Executed vs authored vs blocked

- Executed: items in §4 (commands, revisions, counts, skips above).
- Authored (statically checked only): guides, Postman examples, operating
  instructions, migrations, client-harness suite (no socket runtime).
- Blocked (Phase 7, need exact-target authorizations): migration
  apply/rollback, disposable-target runtime (API startup, fixtures,
  two-client sockets incl. kill/REST-fallback, PostgreSQL aggregate
  equality, dispatcher restart), deployed proxy/TLS/origin/limit
  verification, Android + physical-iPhone Firebase/APNs delivery proof.

## 7. Open Phase 7 gates (unchanged, not claimed)

Exact-target migration/runtime, proxy/TLS/origins/redaction verification,
Android + physical iPhone delivery proof (taps, token refresh, logout,
preferences, block, retries, no-content disclosure). Implementation is
verified; release is not — production readiness is not claimed while
Phase 7 is open.
