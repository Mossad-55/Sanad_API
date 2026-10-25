# Full demo corpus — approved scope (post-release slice)

## Control record

- Owner request (2026-10-25): seed a comprehensive fake dataset "all over, literally
  for everything", on the rehearsal VPS **and** the local `SanadDb`.
- Owner decisions: hybrid depth (several rows per list/state, one per rare state),
  generated `SANAD DEMO` media (one image + one video, reused everywhere), legal
  pages seeded as clearly marked demo text, implementation **after** the current
  portal second-pass release is deployed and verified.
- Status: `approved` / `in_progress` (2026-10-25). The Owner approved the combined
  mobile-report scope and directed worker dispatch. This file is the single active
  brief: it owns the mobile contract (M1–M5), the corpus (D1–D4), task state and
  acceptance. It is not release authorization — the mobile changes need a separate
  explicit release approval after acceptance.
- Pinned revision at dispatch: `5377bbc9` (portal second-pass release verified;
  runtime `beb315fe`).
- Owner authorization (2026-10-25, after M1–M3 acceptance): full release and
  "create everything" for this phase, continuing without further check-ins. This
  covers commit/push/deploy of M1–M5, the D1 dry run, and the additive demo
  seeding of both approved targets, including the single recorded
  `--allow-local-sanad-db` override for the local `SanadDb` (additive only, no
  reset, singleton rows reused and never overwritten). It does **not** authorize
  changing the rehearsal runtime environment/configuration or enabling the
  refund seam, so money-state groups still stop at that owner decision.
- Depends on: the fixture refund-outcome seam being enabled on each target before
  any money-state group is written, plus the Owner's local-`SanadDb` override
  approval before D2.
- Authority for target writes: per-target approval recorded in the task rows.
  Neither this document nor scope approval authorizes a target write, reset,
  provider call, clock change, commit, push or deploy.

## Non-negotiable boundaries

- Targets: rehearsal database `SanadDb` on `72.62.92.144` (through the identity-
  proven tunnel), and the **local** `SanadDb`, both via the existing
  `Sanad.PortalProvisioner` guards.
- Local `SanadDb` is normally refused by the provisioner. The corpus gets exactly
  one explicit override (`--allow-local-sanad-db`), accepted only with the demo
  run tag, `--target-kind local-demo`, the cluster-identity check, and a printed
  warning in the plan. It never applies to the report-fixture groups.
- Additive only: insert or reuse; never update or delete a row the corpus did not
  create. Existing singletons (plan versions, site settings, support contact,
  legal documents, landing sections) are reused when present and never
  overwritten; if a demo value is required and the singleton already exists, that
  group is skipped and reported.
- No Production, no reset/truncate/drop, no provider call, no clock change, no
  guard disablement, no service restart, no commit/push/deploy.
- Synthetic data only: invented people, addresses, phones, clinical notes and
  documents. No real personal data. Medical records remain id/ẓcontent fixtures
  with no real identities.
- Generated media only (no third-party stock): one JPEG with a visible
  `SANAD DEMO` mark and one short demo MP4, reused across every media field.
  Video frames cannot carry burned-in text without an encoder on this machine;
  the demo marking is carried by filename, title, description and the DEMO poster
  image. Installing `ffmpeg` would allow burned-in text if the Owner asks.
- Legal documents are published only with `DEMO — not legal text` in both the
  title and the body; they must never be mistaken for approved legal copy.
- Credentials for demo logins go only to the private file under
  `C:\Users\mosad\AppData\Local\Temp\opencode`; admin-type demo accounts are
  actors for data, not handed out.

## Corpus inventory (hybrid)

Deterministic identities keyed by tag `demo1`; reruns upsert, never duplicate.

| Group | Content | Count |
|---|---|---|
| `identity-accounts` | One account per role plus verified / unverified / suspended variants | 16 |
| `families-people` | 2 families, 4 dependents, medical access grants, device/session-free | 2 / 4 |
| `families-subscriptions` | Free, premium active, expired, cancelled, downgrade pending | 6 |
| `caregivers-profiles` | Approved, pending review, rejected, suspended; specialties, availability | 6 |
| `caregiver-ratings` | Visible and hidden ratings with bilingual review text | 8 |
| `care-home-facilities` | Draft, pending review, corrections requested, approved, suspended; documents, cover/logo media | 5 |
| `care-home-inventory` | Room types, rooms, beds, shared/private/suite | 3 / 6 / 12 |
| `care-home-visits` | Settings with operating hours, visiting windows, closures; visits pending/approved/rejected/cancelled/rescheduled | 1 / 10 |
| `care-home-bookings` | Every status: awaiting payment, awaiting decision, accepted, rejected, cancelled, refunded, completed, checked out; refund Pending/Failed/Completed; extension; dispute | 14 |
| `caregiver-bookings` | Every status incl. confirmed, in progress, completed, cancelled, refunds, payout, extension | 14 |
| `finance-platform` | Platform charge rules readouts, invoices, transaction rows paired to bookings | reuse / 8 |
| `finance-claims` | Duplicate-charge claims Submitted, Approved-awaiting-execution, Ambiguous, Failed; service-shutdown refunds | 4 / 2 |
| `finance-complaints` | Received, Investigating, Approved, Refused with award references | 4 |
| `chat` | 4 conversations (open, hidden, blocked, reported), 25–30 messages, 2 attachments, read marks | 4 threads |
| `community` | Posts, comments, reactions, one moderation removal with deletion audit | 6 / 12 |
| `cms-library` | Library image, video and article items; categories | 6 |
| `cms-wellness` | Wellness tips with free-text categories and the demo image | 6 |
| `cms-faq` | Published, hidden and deactivated FAQs with bilingual answers | 8 |
| `cms-landing` | Sections with image refs, testimonials and FAQ refs, ordered; stats | 6 |
| `cms-legal` | Privacy + Terms, published, `DEMO — not legal text` | 2 |
| `cms-support` | Support contact + 3 partner-contact messages | 1 / 3 |
| `notifications` | Every notification type, read and unread, both languages, email outbox rows | 12 |

Total: roughly 250–350 records plus media, excluding messages and ledger lines.

## Consistency rules the groups must satisfy

- One current subscription per family; claims reference their own capture and
  never compete with a shutdown refund for the same budget.
- Booking chronology is monotonic relative to the run instant: created → paid →
  decided → started → completed; refunds after payment, never before.
- Cancellation/refund reasons respect documented length caps (`≤500`, `≤1000`).
- Completed caregiver bookings carry a payout; completed care-home bookings have
  no open dispute.
- Every media field points at the single demo image/video pair; no dangling keys.
- Every seeded list row belongs to a seeded account, so role filters show data.
- Notifications are addressed only to demo accounts.
- Landing refs resolve to seeded testimonials/FAQs and stay ordered; hidden refs
  are hidden as intended.
- Legal pages are `Public` audience so the landing site can render them, and carry
  the demo marker in title and body.

## Execution stages (after the release)

| Task | Scope | Gate |
|---|---|---|
| D1 | Provisioner corpus groups + media generator + demo-mark indicators, local dry run on a throwaway nonce database | Code review; strict build; no target writes |
| D2 | Local `SanadDb`: identity proof, pre-run counts, plan review, provision, rerun, readback | Owner confirms local target and the override |
| D3 | Rehearsal `SanadDb`: SSH identity + cluster identifier, tunnel, verify, plan, provision, rerun, authenticated readback per role | Seam enabled and verified on the deployed revision |
| D4 | Receipt: counts, media URLs, per-role logins that work, demo markers confirmed, state coverage matrix per report item | Mastermind acceptance |

## Acceptance

- A second run changes zero row counts and zero identities.
- Pre-existing rows are byte-identical after the run (spot-checked counts and
  sampled rows).
- Each role can log in and see its own demo data; admin reads show the seeded
  states with the documented filters.
- The single image and video resolve anonymously through `/files/...` with the
  right content types, and every reference points at them.
- No provider call occurs during provisioning or readback; money-state rows exist
  only where the seam is enabled.
- Legal pages visibly carry `DEMO — not legal text`.
- Nothing is committed, pushed or deployed by this slice.

## Open items

1. Owner confirmation to install `ffmpeg` if burned-in video text is required;
   otherwise the documented demo labelling stands.
2. Final approval of the local `SanadDb` override before D2 writes.

## Combined mobile report — approved contract

Read-only investigation session: `ses_e8c2165daffeZ0i6TZ9Au1QIn5`,
`river/zai-org/GLM-5.3-Flash#high`. The Owner approved the recommendations below
on 2026-10-25 ("Approved. and Delegate the workers."). These are the locked
product decisions for M1–M5; a worker that finds a genuine conflict with existing
behavior reports it instead of inventing a different policy.

### Locked decisions

**M1 — post views.** `viewsCount` is returned on post list, detail,
recommendations and saved/favorites, sourced from a stored counter (same numeric
type as the existing likes/comments counters). Recording route:
`POST /api/v1/community/posts/{postId}/view` under the same authorization and
visibility rules as the post detail read (401 unauthenticated, 404 when the post
is not visible to the caller, including moderator-hidden/removed posts). One
viewer counts at most once per post for the post's lifetime (unique
post+viewer record), the post's own author never increments it, and a repeated
request returns the current count without incrementing. Response carries the post
id and the current `viewsCount`. Anonymous posts count views without revealing
their authors. Counters never go negative and the counter change is atomic with
the viewer record.

**M2 — post author identifier.** The same post DTOs carry `authorId`: the stored
author id for non-anonymous posts, and **always `null`** for anonymous posts —
for every caller, including the author and admins. No other identity field is
added.

**M3 — comment viewer state.** `GET /api/v1/community/posts/{postId}/comments`
items (and replies, consistently) carry `isOwn`, `canEdit`, `canDelete` and
`isLiked`. `isOwn` is true only when the authenticated caller is the stored
comment author; on anonymous comments it reveals nothing to other callers.
`canEdit`/`canDelete` mirror the **actual** current mutation authorization
(ownership, moderation/admin capability, and post/comment state), so they are
false whenever the corresponding mutation would be rejected.
`isLiked` reflects the caller's persisted like. No author identity is added to
anonymous comments.

**M4 — retry-safe creates.** Optional `Idempotency-Key` header on exactly:
`POST /api/v1/family/bookings/checkout`,
`POST /api/v1/family/care-home-bookings/checkout`,
`POST /api/v1/family/dependents/{dependentId}/care-records` and
`POST /api/v1/caregiver/care-records`. Absent header = today's behavior. Key:
1–128 printable ASCII, trimmed; otherwise a coded 400. Records are scoped to the
authenticated actor, the operation and the key, and stored in the same
persistence context as the write so the key and the created row commit
atomically. The request fingerprint covers the canonical request body plus every
route/path identifier. Same key + same fingerprint replays the original status
and body without creating another hold, record, audit entry or provider call;
same key + different fingerprint is a coded 409. Concurrent duplicates execute
exactly once (unique constraint; the loser replays). Authorization is re-checked
before any replay. Retention is 7 days, documented; no purge worker is added in
this slice. Payment-intent creation is a separate operation and stays out of
scope.

**M5 — date-specific availability.** A read endpoint accepts `date` and
`shiftType` and returns `timeZoneId` plus the available start/end intervals for
that date, in addition to the existing weekly schedule endpoint (additive; the
weekly contract is not removed or relabeled). Intervals exclude times outside the
caregiver's availability, manual unavailability, active booking overlaps and
temporary holds as **currently** defined by the code — no new expiry or timeout
is invented, and pending bookings are not blanket-treated as expired. Cross-
midnight and daylight-saving transitions are handled by the resolved IANA zone.
Checkout uses the same rule set and a final conflict-safe check inside its
transaction, rejecting unavailable slots with a coded error. Stored UTC
timestamps are not relabeled. The response is advisory, not a reservation.
`Africa/Cairo` is the documented display zone for demo fixtures; the owning
domain's stored zone semantics decide the authoritative answer.

### Implementation tasks

| Task | State | Scope / gate |
|---|---|---|
| M1 | `completed` | Accepted 2026-10-25 after diff review: `Post.ViewsCount` + `PostView` (`(PostId, ViewerUserId)` unique, cascade FK), `RecordPostViewCommand` reusing the detail visibility predicate (Published, not blocked), author excluded, repeat returns current count, one `SaveChangesAsync` for row+counter, Community migration `20261025170718` (generated, not applied), `viewsCount` on the shared post DTO, docs + Postman, 7 unit facts + HTTP 401/404/200 facts. |
| M2 | `completed` | Accepted: `authorId` on the same post DTO, stored id for named posts and `null` for anonymous for every caller; covered by the same facts. |
| M3 | `completed` | Accepted: `isOwn`/`canEdit`/`canDelete`/`isLiked` on comments, replies and create/update responses; `canEdit` = the update handler's ownership gate, `canDelete` = ownership + no replies (the delete handler's `CommentHasReplies`), replies expose no edit/delete capability, likes and reply-existence batched per page. |
| M4 | `completed` | Accepted 2026-10-25 after diff review: optional header on exactly the four creates, one-key parse with coded 400, canonical fingerprint (route ids in, server-derived values out), per-context `idempotency_requests` with actor+operation+key uniqueness, stored byte-exact replay, 409 on mismatch, loser detach/replay, care-home lookup inside its serializable guard. Gate-vs-replay ordering accepted as implemented (confirmed replay keeps its original answer; maintenance still blocks a new checkout). Mastermind fixed one corrupted comment block and one stale test double; strict build 0/0, full suite 5688/0/27 skipped, migration tests green. |
| M5 | `completed` | Accepted: additive `availability/day` with `Africa/Cairo` and UTC intervals, one shared rule object for read and checkout, busy windows through the API port using the exact checkout predicate, fixed-shift drop vs hourly subtraction, DST gap omission/standard-offset resolution, serializable + advisory-lock checkout guard, `Bookings.AvailabilityConflict` distinct from `ScheduleConflict`. Cross-midnight predicate gap mirrored and documented; no hold/expiry invented. |
| D1 | `pending` | Provisioner corpus groups + media generator + demo markers; local dry run on a throwaway nonce database covering the new M1–M5 states (named/anonymous posts, deduplicated views, own/other/liked comments, open/booked/unavailable slots, retry replay/conflict). Gate: code review, strict build, no target writes. |
| D2 | `pending` | Local `SanadDb`: identity proof, pre-run counts, plan review, provision, rerun, readback. Needs the Owner's local-override approval recorded here first. |
| D3 | `pending` | Rehearsal `SanadDb` via SSH tunnel: identity + cluster check, verify, plan, provision, rerun, authenticated readback per role. Money-state groups need the non-Production seam settled and verified. |
| D4 | `pending` | Receipt: counts, media URLs, working per-role logins, demo markers, state-coverage matrix per report item. |

### M1–M3 worker handback (2026-10-25)

Session: this Sanad phase worker session (opencode `deepseek-v4.1-flash-free`), base
revision `5377bbc9`, nothing committed. Scope: Community module only, plus the
shared id primitive, tests, app docs and the app Community Postman collection.
M4–M5, admin DTOs/routes, the private handoff and pre-existing untracked debris are
untouched.

Changed files:

- New: `src/BuildingBlocks/Domain/.../Primitives/Ids/CommunityPostViewId.cs`,
  `src/Modules/Community/Domain/.../Posts/PostView.cs`,
  `src/Modules/Community/Application/.../Posts/RecordPostViewCommand.cs`,
  `src/Modules/Community/Application/.../Comments/GetCommunityCommentViewerStateQuery.cs`,
  `src/Modules/Community/Infrastructure/.../Persistence/Configurations/PostViewConfiguration.cs`,
  `src/Modules/Community/Infrastructure/.../Persistence/Migrations/20261025170718_AddCommunityPostViews.cs`
  (+ `.Designer.cs`),
  `tests/Sanad.UnitTests/Community/CommunityPostViewsAndCommentViewerStateTests.cs`
  (7 facts), `tests/Sanad.UnitTests/API/CommunityPostViewApiTests.cs` (3 HTTP facts).
- Modified: `Post.cs` (`ViewsCount`, `IncrementViews`), `ICommunityDbContext.cs`
  and `CommunityDbContext.cs` (`PostViews`), the Community model snapshot,
  `CommunityPostsController.cs` (view route, `viewsCount`/`authorId`,
  `CommunityPostViewResponse`), `CommentsController.cs` (batched viewer state and
  the four flags on comments/replies incl. create/update responses),
  `CommunityFeedTests.cs` + `AdminCommunityRestrictionQueryTests.cs` (test doubles),
  `NewEndpointsAuthorizationContractTests.cs` (new `RecordView` 401 row).
- Docs/Postman: `docs/app/community/writes.md` (post views, `authorId`, anonymity,
  comment/reply viewer state), `docs/app/community/README.md`, and
  `docs/postman/app/Sanad.App.Community.postman_collection.json` (new `Record post view`
  item plus updated post/comment/reply descriptions); JSON validated.

Migration: `20261025170718_AddCommunityPostViews` — adds
`community.Posts.ViewsCount integer NOT NULL DEFAULT 0` (monotonic, increment-only)
and the `community.PostViews` table with unique index `(PostId, ViewerUserId)`,
cascade FK to `Posts`, `ViewedOnUtc timestamp with time zone`. Generated at design
time with a dummy connection string; **not applied** to any database.

Design summary: one `SaveChangesAsync` writes the viewer row and the counter
increment together; `DbUpdateException` re-checks whether the concurrent winner's
row exists and returns the current count only then. Visibility/authorization is the
post-detail predicate (`Published`, minus blocked authors); author reads return the
current count without a row. Reads share the single `CommunityPostResponse`
serializer, so all surfaces carry `viewsCount` and `authorId`. Comment
`canEdit` = ownership (the update route's only gate), `canDelete` = ownership AND
no replies (mirrors the `CommentHasReplies` 409), replies always
`canEdit`/`canDelete` = false (no reply mutation route), `isLiked` from persisted
likes.

Checks run (all in this working tree):

- `dotnet build Sanad.slnx -c Release -warnaserror --nologo` → **Build succeeded,
  0 Warning(s), 0 Error(s)**.
- `dotnet test tests\Sanad.UnitTests\Sanad.UnitTests.csproj -c Release --no-build
  --filter "FullyQualifiedName~Sanad.UnitTests.Community|FullyQualifiedName~CommunityPostViewApiTests|FullyQualifiedName~NewEndpointsAuthorizationContractTests"`
  → **Passed 219, Failed 0, Skipped 0**.
- Postman JSON: `[System.IO.File]::ReadAllText(...) | ConvertFrom-Json` → **VALID
  JSON**, 6 folders, 18 items in `01. Posts and interactions`.
- `git diff --check` → clean (only git's LF→CRLF normalization warnings).

Not run / limitations: full solution test suite; any live Postman request (no target
writes authorized); the migration against a database; a live concurrent duplicated
first-view race (the unique index is the backstop, the facts cover the sequential
dedup path). Admin Community DTOs/routes are unchanged — `viewsCount`/`authorId`
live on the app DTOs that admin app-surface reads also use. Reply `isLiked` is
data-only because no reply-like route exists. `favoritesCount`/`likesCount`/
`commentsCount` behavior and every existing status code are unchanged.

### M4–M5 worker handback (2026-10-25)

Session: this Sanad phase worker session (opencode `deepseek-v4.1-flash-free`), base
revision `5377bbc9`, nothing committed, no migration applied to any database, no
provider/target call. Scope: M4 retry-safe creates on exactly the four contracted
routes and M5 date-specific availability plus the conflict-safe checkout check.
M1–M3 Community files, admin DTOs/routes, the private handoff and the pre-existing
untracked debris are untouched.

Changed files:

- New shared kit: `src/BuildingBlocks/Application/Sanad.BuildingBlocks.Application/Idempotency/`
  (`IdempotencyKey`, `IdempotencyFingerprint`, `IdempotencyRequest`,
  `IdempotentOutcome`, `IdempotencyOperations`, `IIdempotentResponseSnapshotter`)
  and `src/BuildingBlocks/Infrastructure/.../Persistence/PostgresErrors.cs`.
- New API seams: `src/API/Sanad.API/Idempotency/MvcIdempotentResponseSnapshotter.cs`,
  `src/API/Sanad.API/CaregiversIntegration/ApiCaregiverBusyBookingLookup.cs`,
  `.../CaregiverAvailabilityRulesGateway.cs`.
- New per-context M4 persistence: `Families|CareHomes.Domain/Idempotency/IdempotencyRequest.cs`,
  `.../Configurations/IdempotencyRequestConfiguration.cs`, plus the unapplied
  migrations `20261025175627_AddIdempotencyRequests` (schema `families`) and
  `20261025175720_AddIdempotencyRequests` (schema `care_homes`) with `.Designer.cs`.
- New M5 rules: `Caregivers.Application/Availability/` (`CaregiverDayAvailability`,
  `CaregiverDayAvailabilityRules`, `CaregiverAvailabilityTimeZone`,
  `CheckCaregiverAvailabilityQuery`, `GetCaregiverDayAvailabilityQueryHandler`),
  `Caregivers.Application/Abstractions/Data/ICaregiverBusyBookingLookup.cs`;
  `Families.Application/Abstractions/Caregivers/ICaregiverAvailabilityRules.cs`,
  `Families.Application/Abstractions/Bookings/ICaregiverBookingCheckoutGuard.cs`,
  `Families.Infrastructure/Bookings/CaregiverBookingReservationGuard.cs`.
- New tests: `tests/Sanad.UnitTests/BuildingBlocks/IdempotencyKeyTests.cs`,
  `BuildingBlocks/IdempotencyFingerprintTests.cs`, `API/IdempotencyHeaderContractTests.cs`,
  `Families/Idempotency/FamiliesCheckoutIdempotencyTests.cs`,
  `Families/Idempotency/FamilyCareRecordIdempotencyTests.cs`,
  `CareHomes/CareHomeBookingCheckoutIdempotencyTests.cs`,
  `Caregivers/CaregiverDayAvailabilityTests.cs`,
  `Families/Bookings/BookingCheckoutAvailabilityTests.cs`,
  `Persistence/IdempotencyMigrationTests.cs`, `Support/IdempotencyTestDoubles.cs`.
- New doc: `docs/app/families/care-records.md` (both create endpoints + retry rules).
- Modified: `ApiControllerBase.cs` (one header parse + `IdempotentOk`/`IdempotentCreated`
  replay helpers), the four controllers, `DependencyInjection.cs`,
  `ResultProblemDetailsMapper.cs` (the eight codes), both `DbContext`s and
  `I*DbContext` (`IdempotencyRequests`, `DetachAdded`), the two model snapshots,
  the four handlers/commands, `CaregiverAvailabilityController.cs` (day route), the
  two `.csproj` files (`BuildingBlocks.Infrastructure` reference),
  `NewEndpointsAuthorizationContractTests.cs`, `docs/app/families/bookings.md`,
  `docs/app/caregivers/schedule-and-availability.md`,
  `docs/care-homes/booking-lifecycle.md`, and the four app Postman collections.

M4 behavior: absent header runs the current code path (byte-identical responses);
`Idempotency-Key` is trimmed and must be 1–128 printable ASCII, else
`*.IdempotencyKeyInvalid` (400) with no row written; the key is scoped to
`(actor, operation, key)` with operation strings `family.bookings.checkout`,
`family.care-home-bookings.checkout`, `family.care-records.create` and
`caregiver.care-records.create`; the fingerprint covers the canonical body plus
every route/path identifier and no server-derived value (no `UtcNow`, ids, or
resolved prices); replay returns the stored status/body snapshot with no second
hold, record, audit entry or provider call; same key with a different fingerprint
is `*.IdempotencyConflict` (409). The row is added to the same `SaveChangesAsync`
as the business write; a unique violation detaches the added row and replays the
winner. Retention is 7 days documented only — no purge worker, `created_on_utc`
indexed.

M5 behavior: additive `GET /api/v1/caregivers/{caregiverId:guid}/availability/day?date=&shiftType=`
returns `timeZoneId` (`Africa/Cairo`), the requested `shiftType` and UTC
`intervals`; the weekly endpoint is unchanged. One shared rule object serves the
read and the checkout check. Busy windows come from a new API-level port using
exactly the current checkout status/overlap predicate — no hold or expiry concept
was invented, and pending bookings are not blanket-treated as expired. DST gap
times are omitted; ambiguous fall-back resolves to the standard offset. Checkout
now runs exact containment of the booked window inside declared availability
before the existing overlap check (so overlap diagnostics keep precedence) inside
a serializable + advisory-lock guard: outside availability →
`Bookings.AvailabilityConflict` (409); provider-busy and serialization races →
`Bookings.ScheduleConflict`.

Reported ordering conflict (not a silent policy change): the pinned decision says
the maintenance gate runs before lookup and is skipped only on a confirmed replay.
The handlers therefore evaluate the gate, but when a valid header is present they
hold a gate error until after the lookup so a confirmed replay still returns its
stored response instead of a maintenance 503; without a header the gate returns
immediately exactly as before. Care-home checkout additionally keeps its local
facility/room-type/media validation ahead of the guard and the deferred gate, and
catches the unique violation outside the guard. A replay is documented as
historical, not a new offer. If the Mastermind wants maintenance to win over
replay, that is a two-line reorder — flag it as a deviation to accept or change.

Checks run (all in this working tree):

- `dotnet build Sanad.slnx -c Release -warnaserror --nologo -v q` → **Build
  succeeded, 0 Warning(s), 0 Error(s)**.
- Focused `dotnet test` filter (Idempotency tests +
  `BookingCheckoutAvailabilityTests` + `CaregiverDayAvailabilityTests` +
  `NewEndpointsAuthorizationContractTests` + `BookingRemediationTests` +
  `CareHomeBookingLifecycleTests` + `CareRecordHandlerTests` +
  `BookingPaymentIntentTests`) → **213 passed, 0 failed**; the
  idempotency-only run earlier → **70 passed**; the availability/migration run
  earlier → **26 passed**.
- `docs/internal/tools/Verify-ApiContractMapping.ps1` → controller signatures
  673, Postman requests 772, **missing 0, orphan 0**; all four edited collections
  parse with `ConvertFrom-Json` → **valid JSON**.
- `git diff --check` → clean (only git's LF→CRLF normalization advisories).

Not run / limitations: the full solution test suite (not authorized by the
contract); any live request against a target environment; the two migrations
against a real database (generated at design time with dummy connection strings,
**not applied**); a live concurrent duplicate through Postgres (the unique index
is the backstop, and the loser path is covered with a SQLite adapter that commits
a competing winner row). Availability subtraction works in local tick space using
the literal checkout predicate, so a booking that starts inside a cross-midnight
window is still not detected as busy — the pre-existing gap is mirrored and
documented in code, tests and docs rather than fixed here.

### Execution order

1. M1–M3 implemented by the first worker; M4–M5 follow in a second worker after
   M1–M3 are reviewed, because both touch the same solution build.
2. Mastermind integrated gate, then a separate release approval.
3. D1 dry run on a disposable database; then D2 local and D3 rehearsal seeding
   under their own approvals; D4 receipt.

The rehearsal service still runs with the Production environment setting.
Money-state fixtures remain blocked until the non-Production seam configuration
is settled; this plan does not authorize changing that setting or bypassing the
guard. A nonce dry run can proceed independently after phase approval.

### M4–M5 pinned implementation decisions (Mastermind, 2026-10-25)

Contract investigation: `ses_e8bfdb7aeffeGmASgAWQMSc1p4`. Accepted defaults:

- Replay returns a stored byte-exact response snapshot (`text`, not `jsonb`),
  serialized with MVC's own `JsonOptions`; never recomputed from the mutated
  business row.
- Idempotency rows live in each writing context (`families.idempotency_requests`,
  `care_homes.idempotency_requests`), keyed `(actor_id, operation,
  idempotency_key)` with the created `resource_id`, response status/body and
  `created_on_utc`; no FK to the business row.
- Absent header = byte-identical current behavior; key parse happens once in the
  controllers (1–128 printable ASCII, trimmed) with a coded 400; rejected
  attempts never write a row.
- Authorization and the platform-maintenance gate run before lookup; the gate is
  skipped only on a confirmed replay, and a replay is documented as historical,
  not a new offer.
- Retention is documentation-only: no expiry logic in code, `created_on_utc`
  indexed for a future purge.
- Availability is reported in `Africa/Cairo` for the caregiver-scoped endpoint;
  the wire `shiftType` is the existing `BookingShiftType`, mapped to declared
  windows by kind/duration. Busy windows use exactly the current checkout
  predicate and statuses; no hold/expiry concept is invented.
- Checkout uses a serializable transaction plus advisory lock and rejects a
  slot outside declared availability with a new `Bookings.AvailabilityConflict`
  (409), distinct from `Bookings.ScheduleConflict`; provider-busy races map to
  `ScheduleConflict`.
- The existing cross-midnight gap in the overlap predicate is mirrored exactly
  and documented, not silently fixed in this slice.

### Release (2026-10-25)

- **Release state: `verified`** for final revision
  `8913df42aadc519c8deee4de1a16ee56fabf2e67` (`456858d8..8913df42`, 2 files, no
  migration change), live at `http://72.62.92.144:8091` on service `sanad-api`, runtime
  `Production`. Feature revision `456858d8` (`5377bbc9..456858d8`) was deployed first;
  the fix redeploy used the same procedure. Pass two: publisher 0/0, archive SHA-256
  `914aedcb4924e7bc8ec30288acb1016d86047821370f4181081f561eb3d2c3f2`, validated
  `SanadDb` dump (833,385 bytes, 907-entry TOC) and app-directory backup, config
  fingerprints unchanged, service active/running with `NRestarts=0`, **0 migrations
  applied** and required heads unchanged (9/9, 0 pending), verifier exit 0, refund seam
  absent/disabled.
- The `456858d8` pass-1 role readbacks passed (post views/`authorId`, comment viewer
  state, day availability, support authorization); its one recorded FAIL — replay body
  not byte-identical to the fresh 201 (445 vs 473 bytes, `\uXXXX` escaping) — is
  **fixed and verified on `8913df42`**: Arabic hospital name + `<b>ملاحظة</b>` details,
  fresh and replay both **201, 440 bytes, `byte_equal=true`**, raw UTF-8 with no `\u`
  escapes, one stored row, and **409** `Families.CareRecords.IdempotencyConflict` on a
  changed payload. Community view/comments, day availability and support 403 were
  re-checked after the redeploy with no regression.
- Receipt: [mobile-report-release-2026-10-25.md](../portal-api-reconciliation/reports/mobile-report-release-2026-10-25.md).
- Synthetic release smoke writes (documented in the receipt, additive): one post view on
  `01a15588-3d20-78ee-a67b-7aa522aea4dc`; three `SANAD RELEASE CHECK Hospital` family
  care records `01a17478-d5d9-7e75-9931-31daca20e923`,
  `01a17479-2742-7a64-81b1-e5fb89c81f73`, `01a1747a-7eeb-71d6-a121-588e617edd23`; and
  the byte-identity check record `01a17485-010a-7fc4-81fd-cfeb09d31cfb`
  (`مستشفى سند التجريبي`, details `SANAD DEMO — <b>ملاحظة</b> للمراجعة`), all for
  dependent `01a17319-d059-7b7b-b92d-94e7ec18d0a4` with matching idempotency rows.
  D1-D4 must reuse/skip them, never delete them.
- D3 non-money groups can run on this revision; money-state groups remain blocked until
  the non-Production refund seam is settled and verified, which this release did not
  change.
