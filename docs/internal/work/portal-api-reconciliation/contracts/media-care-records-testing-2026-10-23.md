# Testing contract — media, care records and payments

Scope/status authority: [execution brief](../worker-prompts/media-care-records-payments-2026-10-23.md).
This document defines acceptance, not a second progress ledger. All new checks
are NOT RUN until execution evidence is recorded. No tests run in planning.

## Strategy: fastest reliable verification

Build reusable fixtures once: actual HTTP API host, disposable PostgreSQL,
temporary public/private storage roots, distinct authenticated roles/tenants and
a deterministic gateway double with controllable failure/timeout outcomes.
Use existing harnesses where sound; avoid creating a parallel test framework.
Use focused checks after each change, then one final combined full gate. Re-run
only checks invalidated by changes or unresolved failures. One runtime slot.

Unit tests establish domain boundaries; PostgreSQL integration establishes
constraints/transactions; HTTP tests establish binding/auth/serialization;
consumer checks establish actual image/video behavior; real-provider evidence
establishes integration. None substitutes for the others.

## Endpoint and media coverage inventory

P0 produces an inventory section/table in the active brief (large machine-derived
inventories may be attached once and linked, not duplicated in multiple trackers).
Include EVERY endpoint, classified changed/removed/unchanged, its consumers,
roles, ownership predicates, request/response shapes, persisted data/file links,
executable check IDs and coverage gaps. Every media input/read needs an explicit
storage classification, including nested content JSON and accepted URL/key fields.

For changed/new endpoints require meaningful valid-request HTTP success,
anonymous denial, wrong-role denial, foreign-owner/tenant denial, validation,
fresh persisted read and replay/concurrency where applicable. For unchanged
endpoints retain and map existing valid evidence; report missing behavioral
coverage and add risk-prioritized tests. Do not claim all endpoints work because
route mapping passes. Empty 200 responses do not establish fixture behavior.

## Required regression suites

### Community post ownership, editing and deletion — Owner addition

- Author edit/delete success via real HTTP and fresh persisted reads; other
  authenticated user denied without any content/author/status mutation. Forged
  author/user IDs, cross-post IDs and stale ownership checks must not bypass scope.
- Anonymous caller denied. Admin deletion of another author's post succeeds only
  under existing CommunityModeration policy; other staff roles denied. Admin
  deletion does not imply Admin edit permission or an app-route ownership bypass.
- Post author and anonymity remain server-controlled; editing cannot bypass
  existing moderation workflow. Verify allowed fields, whitespace/limits,
  version conflicts and retry semantics under the agreed existing conventions.
- Deleted post disappears from feed/detail/search/saved views; child comment,
  reply and reaction operations cannot continue. Verify privacy statuses, audit
  retention and no deletion of unrelated/shared images or another user's data.
- Replacement image must be an authorized uploaded asset, not arbitrary URL or
  another user's private asset; rollback/cleanup is verified with actual storage.
- Race author-edit vs Admin-delete using separate PostgreSQL contexts: no
  resurrection, lost deletion or partial media/record state. Verify app and Admin
  route contracts/Postman; actual screen checks remain distinct from API checks.

### Media, documents and video

- Upload actual supported bytes using multipart binding; assert database storage
  reference and physical file, then use a NEW request/context to follow the exact
  returned URL with the consumer's actual authorization pattern. Verify content
  type, bytes/hash and access; do not use a fake file store for persistence proof.
- Cover every discovered public/private uploader/downloader and generated file
  type. Check relative/base URLs through the configured proxy/base-path pattern.
- Reject empty, unsupported, malformed, oversized content and unsafe paths;
  examine claimed MIME/length versus actual content behavior. Streaming limits,
  cancelled/failed writes, database failure cleanup and replacement/deletion
  must not leave accessible stale private data or references to removed files.
- Ensure an uploaded asset reference cannot attach another user's private file.
  Public files are publicly readable only where intended; private paths cannot
  be reached through static serving. Cross-tenant requests follow agreed privacy
  statuses, not accidental existence disclosure.
- R1: library video uploaded, persisted, entitlement enforced and playable. Test
  byte ranges/206 and invalid ranges, seeking, MIME and actual consumer playback.
  Do not publish a random clip as replacement for the reported content. A missing
  genuine content asset remains a named content dependency.
- R2: visible author image 200; absent file/no avatar/anonymity yields null URL at
  response generation. Test stale references, correct author identity and
  unrelated user. Post-generation file deletion still permits a neutral client
  fallback. Never substitute the requester's avatar.
- Restart host with same durable storage root and verify retrieval persists.
  Production storage-root/proxy/permissions checks are separately authorized;
  local success alone does not prove target file existence.

### Care records across portals

- Family administrator/editor CRUD, viewer denial for writes; medical AND
  companion caregiver creation; authorized care-home staff/resident mapping;
  elderly self read; admin role policy from the approved P0 permissions table.
- Attempt forged actor/source/family/facility fields and foreign elderly IDs;
  server attribution must remain authoritative. Verify expired assignment and
  stay boundaries, consent changes, edit/delete rights and audit history after
  those policies are locked. No silent policy invented by tests.
- Seed all three sources for one elderly person and another unrelated person.
  Check merged timeline, each source filter, unknown filter, pagination limits,
  page boundaries and deterministic same-date ordering without cross-person leaks.
- Date-only visits round-trip unchanged across timezones; UTC audit timestamps
  have correct semantics. Fresh reads after edits/deletes match the contract.
- Verify the mobile views AND dashboard authoring/read workflows; table/handler
  tests alone do not establish actual consuming screen integration.

### Payment history, gateway and finance

- Payment/invoice linkage uses the same attempt identity accepted by existing
  duplicate-charge POST. Own/foreign family checks; status filters, money units,
  EGP currency and nullable invoice association. Do not count a succeeded charge
  as automatically eligible for duplicate refund.
- Gateway-double tests separately assert card and wallet integration selection,
  billing/request serialization, decline, malformed upstream response, timeout,
  safe error handling and advertised availability prerequisites.
- Recover sanitized upstream evidence for BOTH reported traces if retained.
  If unavailable, explicitly document this and seek an approved reproduction;
  never manufacture a rejected-field diagnosis from HTTP 400 alone.
- Real Paymob test-environment intention creation for wallet is mandatory to
  claim R6 externally verified. Establish distinct card evidence for card claims.
  No charge, refund or real OTP as a generic test. Provider credentials/config
  availability is a separate blocker; do not echo values, tokens, billing PII or
  raw sensitive provider payloads into receipts.
- Complaint cap: below/equal/above 50,000 piastres, zero/negative amount, role
  precedence, configured cap semantics, invalid configuration, no ledger/verdict
  writes on refusal, and all reachable approval boundaries. Test real persistence
  and HTTP error mapping, not only a fake store. Final approved cap behavior must
  be documented consistently in Admin guide/Postman.
- Refund PostgreSQL checklist 4.1–4.7: concurrent same-key replay; seeded latest
  timeout selection and evidence-based reconciliation with no second payout;
  atomic credit+ledger and rollback; cancellation versus callback race; actual
  Npgsql city/governorate filtering; persisted NetRecorded/TotalCompleted
  accounting; persisted business-day deadline behavior with approved calendar.
  Use separate contexts/connections and synchronization barriers for races;
  sequential calls labeled concurrent are not acceptable.
- Exercise positive AND negative candidates, not an empty database only. Repair
  stale fixture expectations against source/approved policy; explicitly record
  any contract contradiction rather than changing expected output to match code.

### Removal and schema evolution

- Rentals absent from active routes, DI, project references, consumer contracts
  and runtime migration enumeration; historical receipts remain historical.
- Per Owner decision, retain deployed schema/data initially. Verify fresh database
  omits runtime Rentals and upgraded DB keeps historical tables without probe
  failure. No table drop in this slice; later cleanup needs exact target, inventory,
  backup and separate authorization. Removing migration source does not remove
  already-applied tables/history. Verify safe handling of pending `rnt_` events.
- Manifest, startup checks and deployment probe agree with the actual remaining
  context set; do not mechanically replace every historical 'ten' with 'nine'.
- No destructive tests against shared SanadDb/production. Use positively verified
  disposable target, explicit double selection and isolated filesystem roots.

## Gate order and evidence

### Final mobile report additions — mandatory coverage

- Family reads all three sources but edits/deletes family-source rows only;
  caregiver and CareHome authors have full CRUD on their own source with audit and
  concurrency checks, never cross-source. Elderly is self read-only. Assert both
  medical and companion caregiver creation.
- Caregiver create/read is restricted to own booking-linked dependents while
  Confirmed/InProgress and for the 30-day grace period after completion. Test
  immediately before, at and after the boundary, including permission recheck.
- CareHome access is facility-owner-only, scoped to that facility's accepted
  resident bookings. Admin list/detail is SuperAdmin/SupportAdmin read-only with
  payload-free audit; no write or impersonation path.
- Test authorized recipient picker pagination and Arabic/English names, exclusion
  of foreign dependents and permission revoked between picker GET and create POST.
- Care-home dashboard creation must appear in fresh family/elderly timeline reads;
  Admin list/detail works only for the approved roles and scopes.
- Test all locked search/filter/sort combinations and invalid values; same-date
  stable ties, inclusive date ranges, bounded pages, empty lists, Arabic searches,
  foreign filter values and no unauthorized count leakage. Test money units and
  pending paidOnUtc null ordering in payment history if those reads include pending.
- Repeat photo 200/null/privacy assertions for POSTS, COMMENTS AND REPLIES,
  including cached reads after identity-hiding changes. Do not close R2 using only
  one post response or the download endpoint in isolation.
- Exact recommended limits/permissions become assertions only after P0 locks
  them; do not silently promote a recommendation into approved product behavior.

1. Per-feature focused domain/HTTP/PostgreSQL/media regressions during development.
2. Frozen final revision: strict Release build (exit 0, zero warnings), architecture
   suite, full unit suite, selected full HTTP/PostgreSQL/storage suite, upgrade
   checks, OpenAPI/route/Postman parity and valid collection JSON.
3. Reported consumer screens: show actual library playback, author photo retrieval,
   records by all sources and payment selection. Record app build/environment;
   if no consumer build/device is available, mark NOT RUN, not passed by proxy.
4. After separate release authorization: validated backup when needed, startup
   evidence, revision/head match, bounded feature smokes and required provider
   evidence. Never reuse an old runtime receipt as proof of the new revision.

Record in the active brief per flow: endpoint/test ID, revision, environment,
fixture alias, roles, command and exit code, success/denial/persistence outcomes,
consumer/provider result, sanitized artifact location. Values:
**PASS / FAIL / BLOCKED / NOT RUN**. Skipped tests are not PASS. A required
disposable database missing in the acceptance job must fail or block that job,
not silently skip the whole relational gate and show green.

Completion requires review of actual evidence. If external evidence is blocked,
distinguish local implementation acceptance from end-to-end issue closure. No
claim of zero future defects; every reported regression becomes a lasting check.
