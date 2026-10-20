# F7 — Ratings visibility and moderation (A8/A15)

Phase: `accepted` (Owner authorized); task: `completed`; release: authorized for the
delegated release executor. The Worker never commits, publishes or deploys. Base: pushed
`main` HEAD `66c96b6`. Worker B: `river/deepseek-ai/DeepSeek-V4.1-Flash#high`; the
dispatched session is recorded in Current result.

**Mastermind acceptance (2026-10-20/21):** phase `accepted`, task `completed`, release
`authorized`. Accepted after the Mastermind independently re-ran the whole gate on the
frozen tree and re-verified the pinned semantics in source rather than trusting the
reported counts: strict `dotnet build Sanad.slnx -c Release /p:TreatWarningsAsErrors=true`
→ 0 warnings / 0 errors; the four focused F7 classes → 104/104 passed in Release; and
`tools/Invoke-F7LocalHttpVerification.ps1` → exit 0 with 179/179 real localhost checks
(auth 9, role 28, read 24, owner 22, admin 35, consumer 22, family 2, final 10, session 7,
sql 19, normalise 1). The four new test classes are real (2,588 lines, 77 facts, no
placeholders). Source verification confirmed the pinned behavior: the stored caregiver
aggregate is recomputed over the non-hidden set inside the same hide/restore transaction
(`CaregiverRatingAggregate.RefreshAsync`, called from both the family re-rate handler and
the admin action), the live top-10 SQL carries `AND r.moderation_state = 0`, discovery
reads reconcile through the recomputed stored value, the Care Home top-10 and the owner
read filter visible, `LandingRefResolver` skips a hidden caregiver testimonial, flag
resolution is transactional with the 409 concurrency path, and error precedence is
404 → 400 → 409. Migrations verified directly in the database: Caregivers
`20261020210727_AddCaregiverRatingModeration` and CareHomes
`20261020210910_AddCareHomeRatingModeration` sit on top of their own context heads in the
local `SanadDb`, and the other seven contexts are at their unchanged heads (Identity
`20261020191733_AddSupportTicketWorkflow`). The mis-targeting incident was checked and is
clean: `SanadBrunoTestDb` holds **0** F7 migration rows, **0**
`rating_moderation_actions`/`rating_flags` tables and **0** moderation columns. Both
requested manifest amendments were approved (see below). F7 carries two migrations, so its
release applies both to Production in one serialized step.

Read root `AGENTS.md`, [governance](../../../governance/AGENTS.md), the
[handoff](../../../Mastermind_Handoff.md) and the approved
[F7 section](../contracts/frontend-follow-up-2026-10-20.md#f7--ratings-visibility-and-moderation-a8a15).
E06 runs concurrently and owns `docs/postman/admins/Sanad.Admin.postman_collection.json`,
the Identity/elderly check-in files and `docs/admin/elderly-check-ins.md`. Preserve every
unrelated dirty edit and never stage it. No subagents, no new test framework, no OTP work.

## Anchors already verified (do not re-derive)

- Caregiver rating: `src/Modules/Caregivers/Domain/Sanad.Modules.Caregivers.Domain/Caregivers/CaregiverRating.cs:7`
  (`Entity<Guid>`, raw `Guid` PK; `CaregiverId`, `BookingId`, `FamilyId`,
  `CreatedByUserId`, `UpdatedByUserId`, `Stars`, `ReviewText`, `CreatedOnUtc`,
  `UpdatedOnUtc`; `Create:47`, `Edit:78`, `ValidateStars:95`, max text 2000).
  Write path `.../Caregivers/Application/.../Discovery/FamilyCaregiverRatings.cs:41-154`
  (`RateCompletedCaregiverBookingCommand`, `CaregiverRatingErrors:62-75`), route
  `src/API/Sanad.API/Controllers/FamilyCaregiverRatingsController.cs:28-48`
  (`PUT /api/v1/family/caregiver-ratings/bookings/{bookingId}`, `FamilyAccess`), one rating
  per `BookingId` (unique index `CaregiverRatingConfiguration.cs:55-56`), no edit window.
- Stored caregiver aggregate: `Caregiver.cs:71-72` (`AverageRating`, `ReviewsCount`),
  recomputed at `FamilyCaregiverRatings.cs:135-142` via `Caregiver.cs:81-96`. Consumers:
  live top-10 SQL `CaregiversDbContext.cs:188-237:199-206`, discovery search SQL
  `CaregiversDbContext.cs:288-429:317,342-343`, discovery DTOs
  `CaregiverDiscovery.cs:26-27,100-101,337-338`, earnings
  `GetCaregiverEarningsSummaryQuery.cs:18,82`, public `GET /api/v1/caregivers`
  (`minRating`). CMS landing testimonials read **raw** `caregiver_ratings` rows
  (`src/Modules/Cms/Application/.../Landing/LandingRefResolver.cs:103-133`).
- Care Home rating: `src/Modules/CareHomes/Domain/.../Bookings/CareHomeRating.cs:7`
  (same shape, `CareHomeId`), handler
  `.../CareHomes/Application/.../Discovery/FamilyCareHomeRatings.cs:34-100`
  (`CareHomeRatingErrors:34-40`), route
  `src/API/Sanad.API/Controllers/FamilyCareHomeRatingsController.cs:25-35`
  (`PUT /api/v1/family/care-home-ratings/bookings/{bookingId}`, eligibility =
  `FamilyCheckInConfirmedOnUtc != null`), config `CareHomeRatingConfiguration.cs:13`
  (table `ratings`), **no stored facility aggregate** — the only aggregate is computed per
  query in `GetTopRatedCareHomesQueryHandler`.
- Moderation precedent to reuse (FUR-07): `CommunityPostReport.cs:22-57,103-170`
  (status enum, reason codes, one-shot `Resolve` with actor/time, `Version` concurrency
  token), `CommunityPostReportCommands.cs:89-152` (unique-index race recovery returning the
  existing row) and `:166-207` (concurrency catch), config
  `CommunityPostReportConfiguration.cs:31-59`, controller
  `AdminCommunityPostsController.cs:19-28,156-184` (policy + route + forced-status mapping).
  `CareHomeCheckInDispute.cs:31-55` is the sibling "owner opens one, admin resolves with
  attribution" shape.
- **No rating moderation of any kind exists today**, and there is **no admin ratings read
  and no owner facility-ratings read**.
- Policies (`AuthorizationPolicies.cs` / `DependencyInjection.cs`): `CaregiverReviewAdmin`
  (`:16-17`, `:380-394`) and `CareHomesOperationalAdmin` (`:19-20`, `:396-404`) are both
  exactly Normal SuperAdmin + SupportAdmin; `CareHomeOwnerAccess` (`:31-32`);
  `FamilyAccess` (`:28-29`). `AccountType`: Family=1, Medical=2, Companion=3, Elderly=4,
  SuperAdmin=5, ContentAdmin=6, SupportAdmin=7, CareHomeOwner=8, FinanceAdmin=9.
- Error mapping: `src/API/Sanad.API/ProblemDetail/ResultProblemDetailsMapper.cs:8-13`
  (unknown codes default to **400**); existing `Caregivers.Rating.*` `:46-48` and
  `CareHomes.Rating.*` `:56-57`.
- Latest migrations: Caregivers `20261009004058_AddCaregiverPayoutAccountReview`, CareHomes
  `20261012143753_AddCareHomeRoomTypeAmenitiesAndMedia`. New migrations are added with
  `dotnet ef migrations add <Name> --project <Infrastructure csproj> --context <Ctx> --output-dir Persistence/Migrations`
  (design-time connection string, never applied) and are recorded in
  `docs/internal/operations/migrations.md`.
- Test templates: `tests/Sanad.UnitTests/Caregivers/FamilyCaregiverRatingsTests.cs`,
  `tests/Sanad.UnitTests/CareHomes/CareHomeRatingTests.cs`,
  `tests/Sanad.UnitTests/API/AdminCommunityPostsModerationTests.cs` (policy/route reflection,
  forced 400/404/409, `StubSender : ISender` + `sub` claim), `tests/Sanad.UnitTests/API/PortalApiMetadataHost.cs`.

## Pinned behavior (Mastermind decision — implement exactly; do not extend)

1. **One moderation policy, two rating targets.** A rating (caregiver or Care Home) carries
   a moderation state: visible (default) or hidden, with hide actor, hide UTC time and hide
   reason (≤500 chars) while hidden. Hiding and restoring are **explicit, attributed,
   append-only-recorded** actions. The same verbs, reason bound, status codes and error-code
   shape apply to both targets; the only difference is that a Care Home owner may also flag a
   rating about their own facility. Do not invent a second, different moderation mechanism.
2. **Moderation meaning (pinned from the frontend intake).** A hidden rating is excluded from
   every non-admin rating read **and from every aggregate average/count**. Restore returns it
   to both. Concretely: the stored caregiver aggregate `AverageRating`/`ReviewsCount` must be
   **recomputed on hide and on restore over the non-hidden set**, the live top-10 SQL and the
   discovery/owner aggregates must filter hidden, and the CMS testimonial resolver must not
   surface a hidden rating. "Hidden/restore behavior agrees across existing aggregate
   consumers" is the acceptance test: after a hide or restore, the stored aggregate and every
   SQL-computed aggregate must agree.
3. **Ratings are never mutated or deleted by moderation.** Stars, review text, reviewer and
   history are retained. Eligibility to rate (which booking, once per booking, no edit
   window) is **unchanged**. A family edit (`PUT` again) updates stars/text and **must not**
   clear the moderation state; a re-rating of a hidden rating stays hidden.
4. **Admin reads (SuperAdmin + SupportAdmin).**
   `GET /api/v1/admin/caregivers/ratings?caregiverId=&stars=&moderation=&page=&pageSize=`
   and `GET /api/v1/admin/care-homes/ratings?facilityId=&stars=&moderation=&flagged=&page=&pageSize=`.
   Newest-first, bounded paging (default 25, clamp 1..100), `totalCount` after filtering and
   before paging; `moderation` accepts `visible|hidden|all` (default `all`); the Care Home
   list sorts **unresolved-flagged first**, then newest-first. Items expose only: rating id,
   target id (caregiver id / facility id), booking id, family id, stars, review text,
   created/updated times, moderation state + hide actor/time/reason, and (Care Home only) the
   unresolved flag's id/reason/flagged-on. No reviewer name, phone, email or avatar.
5. **Admin actions.**
   `POST /api/v1/admin/caregivers/ratings/{ratingId}/hide` `{"reason"}` and
   `POST /api/v1/admin/caregivers/ratings/{ratingId}/restore` `{"reason"}`;
   `POST /api/v1/admin/care-homes/ratings/{ratingId}/hide` `{"reason"}`,
   `POST .../{ratingId}/keep` `{"reason"}` and `POST .../{ratingId}/restore` `{"reason"}`.
   All return **204** on success (repo convention). `keep` resolves the unresolved owner flag
   by leaving the rating visible and changes no visibility. Hiding or keeping a Care Home
   rating resolves that rating's unresolved flag with attribution in the same transaction.
   Non-success: unknown rating → **404**; blank/missing/over-500 reason → **400**; hide when
   already hidden, restore when already visible, or keep when there is no unresolved flag →
   **409** (no silent no-op).
6. **Owner facility ratings (Care Home owner only, anonymous).**
   `GET /api/v1/care-homes/facilities/mine/ratings?page=&pageSize=` returns
   `{ items, page, pageSize, totalCount, averageStars, ratingCount }` where each item is
   `{ ratingId, stars, reviewText, createdOnUtc, flagStatus, myFlagReason, myFlaggedOnUtc }`.
   The reviewer is **never** identified — no name, user id, family id, phone, email or
   avatar, in items or errors. Items and both aggregates exclude hidden ratings, so a hidden
   rating leaves the average. `POST /api/v1/care-homes/facilities/mine/ratings/{ratingId}/flag`
   `{"reason"}` → **204**, at most **one unresolved flag per rating per owner**: a repeat flag
   of the same rating returns **204** and writes nothing (no duplicate row, no history row).
   Flagging a rating that is not about one of the caller's own facilities → **404**; blank or
   over-500 reason → **400**. Owners cannot hide, restore, keep or see who reviewed.
7. **Flag model.** One unresolved case per (rating, owner) enforced by a unique index, with
   the FUR-07 resolve-once + concurrency-token pattern: `Open` → `Resolved`, resolution
   carries the resolving admin, UTC time and the action taken (`Hidden` or `Kept`). Resolving
   an already-resolved flag, or a concurrent resolution, is a **409**, never a silent second
   write.
8. **README-of-record wording.** The guide line "No moderation workflow is included"
   (`docs/care-homes/discovery.md:63-64`) must be replaced with the implemented reality.

## Exact writable manifest

Caregivers module:

- `src/Modules/Caregivers/Domain/Sanad.Modules.Caregivers.Domain/Caregivers/CaregiverRating.cs`
- **New:** `src/Modules/Caregivers/Domain/Sanad.Modules.Caregivers.Domain/Caregivers/CaregiverRatingModerationAction.cs`
- **New:** `src/Modules/Caregivers/Application/Sanad.Modules.Caregivers.Application/Discovery/AdminCaregiverRatingQueries.cs`
- **New:** `src/Modules/Caregivers/Application/Sanad.Modules.Caregivers.Application/Discovery/AdminCaregiverRatingCommands.cs`
- `src/Modules/Caregivers/Application/Sanad.Modules.Caregivers.Application/Discovery/FamilyCaregiverRatings.cs`
- `src/Modules/Caregivers/Application/Sanad.Modules.Caregivers.Application/Abstractions/Data/ICaregiversDbContext.cs`
- `src/Modules/Caregivers/Infrastructure/Sanad.Modules.Caregivers.Infrastructure/Persistence/CaregiversDbContext.cs`
- `src/Modules/Caregivers/Infrastructure/Sanad.Modules.Caregivers.Infrastructure/Persistence/Configurations/CaregiverRatingConfiguration.cs`
- **New:** `src/Modules/Caregivers/Infrastructure/Sanad.Modules.Caregivers.Infrastructure/Persistence/Configurations/CaregiverRatingModerationActionConfiguration.cs`
- **New:** `src/Modules/Caregivers/Infrastructure/Sanad.Modules.Caregivers.Infrastructure/Persistence/Migrations/<fresh>_AddCaregiverRatingModeration.cs` + `.Designer.cs`
- `src/Modules/Caregivers/Infrastructure/Sanad.Modules.Caregivers.Infrastructure/Persistence/Migrations/CaregiversDbContextModelSnapshot.cs`

CareHomes module:

- `src/Modules/CareHomes/Domain/Sanad.Modules.CareHomes.Domain/Bookings/CareHomeRating.cs`
- **New:** `src/Modules/CareHomes/Domain/Sanad.Modules.CareHomes.Domain/Bookings/CareHomeRatingModerationAction.cs`
- **New:** `src/Modules/CareHomes/Domain/Sanad.Modules.CareHomes.Domain/Bookings/CareHomeRatingFlag.cs`
- **New:** `src/Modules/CareHomes/Application/Sanad.Modules.CareHomes.Application/Discovery/AdminCareHomeRatingQueries.cs`
- **New:** `src/Modules/CareHomes/Application/Sanad.Modules.CareHomes.Application/Discovery/AdminCareHomeRatingCommands.cs`
- **New:** `src/Modules/CareHomes/Application/Sanad.Modules.CareHomes.Application/Discovery/OwnerCareHomeRatingQueries.cs`
- **New:** `src/Modules/CareHomes/Application/Sanad.Modules.CareHomes.Application/Discovery/OwnerCareHomeRatingCommands.cs`
- `src/Modules/CareHomes/Application/Sanad.Modules.CareHomes.Application/Discovery/FamilyCareHomeRatings.cs`
- `src/Modules/CareHomes/Application/Sanad.Modules.CareHomes.Application/Abstractions/Data/ICareHomesDbContext.cs`
- `src/Modules/CareHomes/Infrastructure/Sanad.Modules.CareHomes.Infrastructure/Persistence/CareHomesDbContext.cs`
- `src/Modules/CareHomes/Infrastructure/Sanad.Modules.CareHomes.Infrastructure/Persistence/Configurations/CareHomeRatingConfiguration.cs`
- **New:** `.../Configurations/CareHomeRatingModerationActionConfiguration.cs`
- **New:** `.../Configurations/CareHomeRatingFlagConfiguration.cs`
- **New:** `.../Migrations/<fresh>_AddCareHomeRatingModeration.cs` + `.Designer.cs`
- `.../Migrations/CareHomesDbContextModelSnapshot.cs`

API and Cms:

- **New:** `src/API/Sanad.API/Controllers/AdminCaregiverRatingsController.cs`
- **New:** `src/API/Sanad.API/Controllers/AdminCareHomeRatingsController.cs`
- **New:** `src/API/Sanad.API/Controllers/OwnerCareHomeRatingsController.cs`
- `src/API/Sanad.API/ProblemDetail/ResultProblemDetailsMapper.cs` (new codes only)
- `src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Landing/LandingRefResolver.cs` (exclude hidden caregiver ratings only)

Tests, docs, Postman:

- **New:** `tests/Sanad.UnitTests/Caregivers/AdminCaregiverRatingModerationTests.cs`
- **New:** `tests/Sanad.UnitTests/CareHomes/AdminCareHomeRatingModerationTests.cs`
- **New:** `tests/Sanad.UnitTests/CareHomes/OwnerCareHomeRatingTests.cs`
- **New:** `tests/Sanad.UnitTests/API/RatingsModerationAuthorizationTests.cs`
- **New:** `docs/admin/ratings-moderation.md`
- `docs/care-homes/discovery.md` (replace the "No moderation workflow" statement; owner read)
- `docs/care-homes/README.md` (route the owner collection row)
- `docs/app/caregivers/family-ratings.md` (hidden-rating exclusion note)
- `docs/internal/operations/migrations.md` (the two new ids)
- **New:** `docs/postman/admins/Sanad.Admin.Ratings.postman_collection.json`
- `docs/postman/care-homes/Sanad.CareHomes.Owner.postman_collection.json`
- New: `tools/Invoke-F7LocalHttpVerification.ps1`
- This brief.

Everything else — including `AuthorizationPolicies.cs`, `DependencyInjection.cs`,
`docs/postman/admins/Sanad.Admin.postman_collection.json`, `docs/postman/app/*`, all
Identity/Families/Notifications files and the handoff — is read-only. Request a precise
manifest amendment if the scope truly needs one. Reuse `CaregiverReviewAdmin` (caregiver
ratings) and `CareHomesOperationalAdmin` (Care Home ratings) — both already resolve to
Normal SuperAdmin + SupportAdmin — so **no** policy or DI file is touched.

## Parallel verification gate

E06 runs concurrently and owns the shared Admin Postman collection, the Identity/elderly
files and `docs/admin/elderly-check-ins.md`; F7 must not touch them. F7 is the only phase in
this wave with a schema change, so apply its two migrations to the local `SanadDb` yourself,
once, in this order (Caregivers then CareHomes), before starting the API; E06 runs no
migration. Use port **5247** for the F7 adapter and never write a fixture row that E06 owns.

Acceptance then requires: one strict successful `dotnet build Sanad.slnx -c Release
/p:TreatWarningsAsErrors=true` with zero warnings; the focused tests; and real localhost HTTP
proof from `tools/Invoke-F7LocalHttpVerification.ps1`. The adapter must dot-source
`tools/Invoke-LocalFixtureVerification.ps1` `-LibraryOnly` and reuse its helpers as
`tools/Invoke-F6LocalHttpVerification.ps1` does, be loopback-only, fail closed on the
all-context/non-live preflight, and cover: role denial (family, caregiver, ContentAdmin,
FinanceAdmin denied; owner denied on every admin route), owner flag → admin hide resolves it,
hide changes the aggregate, restore restores it, repeated owner flag writes nothing extra,
a family edit cannot unhide, unknown id 404, blank/over-long reason 400, and status conflicts
409. INSERT-only fixtures with dedicated session identities (never sign in as a shared or
capped identity), every created session logged out by the same run, no reset/delete/bulk seed,
no production or provider call.

## Current result

**Handback `ready_for_review`.** Dispatched session `river/deepseek-ai/DeepSeek-V4.1-Flash#high`
at base `66c96b6`. All F7 work below is uncommitted in the dirty working tree; nothing was
staged, committed, pushed, published, deployed or pointed at a provider.

PASS — strict build gate: `dotnet build Sanad.slnx -c Release /p:TreatWarningsAsErrors=true`
→ **0 Warning(s), 0 Error(s)** (final run after every source/doc addition).

PASS — focused tests:
`dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj -c Debug --no-build --filter "FullyQualifiedName~AdminCaregiverRatingModerationTests|FullyQualifiedName~AdminCareHomeRatingModerationTests|FullyQualifiedName~OwnerCareHomeRatingTests|FullyQualifiedName~RatingsModerationAuthorizationTests"`
→ **Failed 0, Passed 104, Skipped 0, Total 104**. Four new classes, plus
`HiddenRating_IsSkippedByLandingTestimonials_WithoutCountingAsDangling` on the caregiver class.

PASS — required localhost HTTP gate:
`pwsh -NoProfile -File tools\Invoke-F7LocalHttpVerification.ps1` (loopback, port 5247)
→ **exit 0, 179/179 checks passed, failed 0**, on three consecutive runs including the final
run on the frozen tree, so subscription/rerun stability is proven, not asserted. The adapter
dot-sources `tools/Invoke-LocalFixtureVerification.ps1 -LibraryOnly` and reuses its helpers,
fails closed on the all-context/non-live preflight, writes INSERT-only fixtures with dedicated
session identities and logs out every session it created. It covers the required surface:
family/caregiver/ContentAdmin/FinanceAdmin denial, owner denial on both admin route groups,
owner flag → admin hide resolves the flag, hide changes the aggregate, restore restores it,
repeat owner flag writes nothing extra, a family edit cannot unhide, unknown id `404`,
blank/over-500 reason `400` and the `409` conflicts.

PASS — both migrations authored **and applied to the local `SanadDb`**, in the pinned order:
Caregivers `20261020210727_AddCaregiverRatingModeration` then CareHomes
`20261020210910_AddCareHomeRatingModeration`. `SanadDb` was inspected with `dotnet ef dbcontext
info` plus the migration history before each `database update`. Recorded in
`docs/internal/operations/migrations.md` together with the targeting caveat below.

Implementation (pinned behavior 1–8, implemented exactly; no extension). One moderation policy
for both targets: rating-level `visible`/`hidden` state with hide actor/instant/reason (≤500),
two append-only `*RatingModerationAction` tables, and a Care Home-only
`CareHomeRatingFlag` with a unique index over the unresolved case. `CaregiverRating`/`CareHomeRating`
gained the moderation facts; the stored caregiver aggregate is recomputed over the non-hidden
set inside the same hide/restore transaction, the live top-10 SQL and discovery/owner aggregates
filter hidden, and `LandingRefResolver` skips a hidden caregiver rating without incrementing the
`dangling` counter. Three new controllers
(`AdminCaregiverRatingsController`, `AdminCareHomeRatingsController`, `OwnerCareHomeRatingsController`)
reuse `CaregiverReviewAdmin` and `CareHomesOperationalAdmin` as instructed, so
`AuthorizationPolicies.cs` and `DependencyInjection.cs` are untouched; all seven new codes were
added to `ResultProblemDetailsMapper.cs` only. Where the pin is silent: admin query validation
runs in the handler (never a clamp) so an invalid filter answers the documented
`*.Rating.InvalidQuery` `400`; error precedence is NotFound → InvalidReason → AlreadyHidden/
AlreadyVisible/NoOpenFlag (the owner flag handler checks ownership before reason shape); restore
clears the rating's hide facts while the append-only action row keeps its reason; `keep` appends
an action row and resolves the flag; an owner with no facility gets `404 CareHomes.Facility.NotFound`;
a foreign or unknown rating on the owner route is the same `404 CareHomes.Rating.NotFound`;
`flagStatus` is `None`/`Open`/`Resolved`; a repeat flag while open is a `204` no-op with no row
and no history row.

Docs and Postman. **New `docs/admin/ratings-moderation.md`** (access table, hide semantics,
both admin reads, all five actions, the full error table, the owner read + flag, verification).
`docs/care-homes/discovery.md` (the "No moderation workflow is included" statement replaced with
the implemented reality, plus the owner read/flag and top-10 hidden exclusion),
`docs/care-homes/README.md` (owner ratings row), `docs/app/caregivers/family-ratings.md`
(hidden-rating exclusion note), `docs/internal/operations/migrations.md`. **New
`docs/postman/admins/Sanad.Admin.Ratings.postman_collection.json`** (17 authored requests:
both list reads, the flagged filter, both invalid-filter `400` shapes, all five actions, the
`409` transitions, unknown-id `404`, blank and over-long reason `400`, and a role-denial read).
`docs/postman/care-homes/Sanad.CareHomes.Owner.postman_collection.json` gained a
`careHomeRatingId` collection variable and a trailing **"10. Facility ratings (F7)"** folder
(8 requests: owner read, second page, invalid-paging `400`, flag, repeat-flag no-op, blank-reason
`400`, unknown-rating `404`, owner denied on the admin route). Both files parse as valid JSON
(`ConvertFrom-Json`); the owner collection stayed UTF-8 without BOM and CRLF, the new admin
collection is UTF-8 without BOM and LF, matching its `Sanad.Admin.Families` sibling.

NOT RUN (and not claimed): whole-solution test pass, Bruno, any E06-owned file, any non-local
database. The E06-owned `docs/postman/admins/Sanad.Admin.postman_collection.json`,
`docs/admin/elderly-check-ins.md` and Identity/Families/Notifications files were not touched.

### Manifest amendments (both approved and applied by the Mastermind)

1. **Disclosure — 4 lines outside the manifest, already edited.** The interface additions to
   `ICaregiversDbContext`/`ICareHomesDbContext` are a breaking change for four pre-existing
   in-repo test fakes, which are single-line pass-through decorators (4 files, 9 added lines):
   `tests/Sanad.UnitTests/Caregivers/CaregiverPayoutHandlerTests.cs`,
   `tests/Sanad.UnitTests/Caregivers/PayoutAccountReviewHandlerTests.cs`,
   `tests/Sanad.UnitTests/CareHomes/CareHomeBookingLifecycleTests.cs`,
   `tests/Sanad.UnitTests/CareHomes/CareHomeStayLifecycleTests.cs`. Each only forwards the new
   `DbSet` to the inner context; no test behavior changed. Please add them to the manifest.
2. **`docs/admin/admin-overview.md` is out of manifest and is now stale.** Add a "Current admin
   surface" row `| Rating visibility moderation (caregiver and Care Home ratings) |
   \`docs/admin/ratings-moderation.md\` |` and extend the Postman row with
   `Ratings moderation: \`docs/postman/admins/Sanad.Admin.Ratings.postman_collection.json\``.
   Not edited here.

**Resolved.** Both are approved and applied: (1) the four single-line pass-through `DbSet`
decorators belong to the F7 manifest, because the interface additions are a real breaking
change for those pre-existing in-repo test fakes and no test behavior changed; and (2)
`docs/admin/admin-overview.md` now carries the row
`| Ratings visibility and moderation (caregiver and Care Home) | docs/admin/ratings-moderation.md |`
and its Postman row also names
`docs/postman/admins/Sanad.Admin.Ratings.postman_collection.json`. No other manifest
expansion was granted, and the Mastermind applied item 2 itself.

### Incidents and limitations found while verifying

- **Migration targeting (root-caused and undone).** The shell/user-scope
  `App__TestUserSeed__Enabled=true` makes the CareHomes design-time factory resolve
  `SanadBrunoTestDb`, so a CareHomes `database update` silently targeted that database; the
  applied CareHomes migration was rolled back and `SanadBrunoTestDb` was restored to its pre-F7
  baseline. Every subsequent migration command pinned the target explicitly
  (`App__TestUserSeed__Enabled=false` for `SanadDb`, `=true` for the rollback) after a
  `dbcontext info` check. Documented in `docs/internal/operations/migrations.md`.
- `psql` is not on PATH locally (used via
  `C:\Program Files\PostgreSQL\18\bin\psql.exe`); CareHomes migration commands need
  `--startup-project src/API/Sanad.API/Sanad.API.csproj`.
- The moderation-action and owner-flag tables are append-only, so the adapter asserts their row
  counts as **deltas** from a capture taken inside the same run (never absolute numbers), and the
  owner read's pre-run flag facts are derived from the run's own baseline readback rather than
  hard-coded. The adapter's own reported limitations (startup migrations stay disabled for every
  process it starts; the local outbox/notification services run with SMS/SMTP/Firebase/Push inputs
  cleared and the Development no-op push sender; fixture rows are retained and a rerun repairs only
  the visibility facts it depends on; the top-10 consensus and "no open flag anywhere" assertions
  need the verified local fixture target; trace correlation is reported unavailable when no
  captured local log line carries the trace id) are the accepted boundaries of this evidence.
- Pre-existing, unrelated and untouched: `docs/app/caregivers/family-ratings.md` had a truncated
  sentence ("…Repeated edits are safe and no booking or"); the F7 edit only added the
  hidden-rating exclusion note around it.
- Unrelated dirty/untracked files (buildout*.txt, testout*.txt, migout.txt, `.opencode-audit-tmp/`,
  `.opencode/agents/`, `.codex/*`, `README.md`, governance `AGENTS.md`,
  `tools/opencode_bridge/opencode_bridge.py`, the contract/brief files and the E06 files) were left
  byte-for-byte as found.

## Limitations (explicit)

1. Hiding is a **visibility** control, not an accuracy judgement: it removes the rating from
   aggregates and non-admin reads, exactly as the frontend intake decided ("a hidden rating
   leaves the average"). It does not correct the stars, and restore is always available.
2. Caregiver ratings have no owner surface (a caregiver is not a facility owner), so the
   flag + owner read exist only for Care Home ratings; the admin moderation policy and the
   hidden/restore semantics are identical for both targets.
3. The caregiver aggregate is stored, so hide/restore must recompute it inside the same
   transaction; any consumer that reads the stored value is reconciled by that recompute
   rather than by its own filter.
4. Two migrations are authored and applied to the local `SanadDb` only; the Production
   application is a separate, serialized release step owned by the delegated release
   executor.

The Worker claimed no reviewer pass, no acceptance and no release; the Mastermind accepted
completion after the independent gate above and handed the release to the delegated release
executor.
