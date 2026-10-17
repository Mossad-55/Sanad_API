# FUR-07 — Community moderation contract (proposed)

**Status: Approved by the owner on 2026-10-17; implementation is authorized
within this contract.** Base revision: `119ba56`. Owner approved posts-only
reporting, community-only timed restrictions, required rejection reason codes
with optional notes, and no hide/restore action for reported published posts;
the remaining proposed defaults in this contract were approved with the
owner's “Perfect continue please.”

## Purpose and evidence

FUR-07 addresses frontend intake items #7 and #8. The current Community admin
API can publish or reject posts, but has no moderation list/detail or report
storage. The audit records report counters, persisted rejection reasons, and
timed author blocking as new policy decisions. See
[`frontend-report-audit-2026-10-16.md`](../frontend-report-audit-2026-10-16.md)
and the Admin audience alignment report.

## Approved behavior

### Moderation queue and detail

- Add a paged, searchable admin post list with optional post-status and
  report-state filters. Default to newest first, then stable ID ordering;
  default page size 25, maximum 100. Search bilingual title/content and return
  `items`, `page`, `pageSize`, and `totalCount`; counters are not filtered by
  page.
- List and detail include post ID, bilingual title/content, image URL,
  category, anonymous flag, status, created/updated timestamps, and moderator
  readback of the author's ID and display names. Do not return author email,
  phone, or other contact data. Detail includes report and moderation history.
- Keep the existing Community moderation policy (normal authenticated
  SuperAdmin or ContentAdmin) for all moderation reads/actions.
- Preserve existing publish behavior. Reject remains limited to pending-review
  posts and now requires a reason code plus an optional note (up to 1000
  characters); persist moderator ID and UTC decision time. Proposed rejection
  codes: `CommunityGuidelines`, `Spam`, `Duplicate`, `UnsafeContent`, `Other`;
  `Other` requires a note.

### Report intake and lifecycle

- Reports apply to **published posts only**. An authenticated normal Community
  user can report a published post, but cannot report their own post. Anonymous
  reports are not accepted. Reported published posts remain visible. FUR-07
  adds no hide, restore, delete, or unpublish action; moderation can resolve a
  report or apply a community-only author restriction.
- Proposed report reason codes: `Spam`, `HarassmentOrAbuse`,
  `HarmfulOrUnsafeContent`, `MisleadingOrFalseInformation`, `Other`. `Other`
  requires a note; notes are optional for the other codes and limited to 1000
  characters.
- Permit one retained report per reporter/post pair. A repeat request returns
  the existing report without creating another row; reports are not hard
  deleted.
- A report starts `Open`. A moderator resolves it as `ActionTaken` or
  `Dismissed`, with moderator ID, UTC time, and optional resolution note (1000
  characters). Resolution does not itself publish/reject content or restrict an
  author; those remain explicit moderation actions.

### Proposed routes

- `GET /api/v1/admin/community/posts` — paged queue; optional `status`,
  `reportStatus`, and bilingual title/content `search` filters; returns
  `items`, `page`, `pageSize`, `totalCount`, and the counters below.
- `GET /api/v1/admin/community/posts/{postId}` — post detail with retained
  reports and moderation history; author fields are ID and Arabic/English
  display names only.
- `POST /api/v1/community/posts/{postId}/reports` — authenticated report intake;
  JSON fields `reasonCode` and optional `note`.
- `POST /api/v1/admin/community/reports/{reportId}/resolve` — JSON fields
  `resolution` (`ActionTaken` or `Dismissed`) and optional `note`.
- `POST /api/v1/admin/community/authors/{authorId}/restrictions` — JSON fields
  `duration` (`Hours24`, `Days7`, or `Days30`), `reasonCode`, and optional
  `note`; returns the retained restriction.
- `POST /api/v1/admin/community/restrictions/{restrictionId}/revoke` — JSON
  field `reason`; preserves the original restriction and records revocation.
- Existing `POST /api/v1/admin/community/posts/{postId}/reject` gains required
  JSON fields `reasonCode` and optional `note`. Existing publish route/body is
  unchanged.

### Community-only timed author restriction

- Add a retained moderation action that restricts an author from creating
  Community posts, comments, and replies until a UTC expiry. Existing content
  remains unchanged. The restriction does not change Identity status, login,
  sessions, or access to any other module.
- Proposed durations are fixed presets: 24 hours, 7 days, or 30 days. Require a
  reason code and allow an optional 1000-character note. Record moderator,
  start, and expiry timestamps. Expiry is enforced by comparing UTC timestamps
  during Community write commands; no background job is introduced.
- Only the existing Community moderation roles can add a restriction. No route
  in this phase can invoke global Identity suspend/block or grant ContentAdmin
  that capability.

### Counters

Return actual persisted/query-derived values, never fabricated zeroes:

- pending-review, published, and rejected post totals;
- open report total;
- distinct authors with at least one open report;
- distinct authors with an active Community restriction.

Counts use the same database snapshot/query filters as the admin queue where
applicable. No arbitrary dashboard aggregates, historical trend series, or
cross-module user statistics are included.

### Approved restriction details

- One author can have at most one active Community restriction. A moderator can
  end it early with a required resolution reason; keep the original action and
  revocation actor/time in history. Restriction blocks new posts, comments, and
  replies only; edits/deletes of existing comments/replies and non-write
  interactions remain available.
- Report and rejection reason lists above are separate closed sets; unknown
  codes are validation errors. Notes are plain text, trimmed, and length-capped.

## Exact approved owned-file manifest

The worker may modify only these existing files and add the named files below.
EF tooling may add one timestamped Community migration and its designer beside
the snapshot. Any additional file requires a reported blocker and owner
approval before editing.

**Existing source files:**

- `src/API/Sanad.API/Controllers/AdminCommunityPostsController.cs`
- `src/API/Sanad.API/Controllers/CommunityPostsController.cs`
- `src/Modules/Community/Domain/Sanad.Modules.Community.Domain/Posts/Post.cs`
- `src/Modules/Community/Application/Sanad.Modules.Community.Application/Abstractions/Data/ICommunityDbContext.cs`
- `src/Modules/Community/Application/Sanad.Modules.Community.Application/Posts/ModeratePostCommands.cs`
- `src/Modules/Community/Application/Sanad.Modules.Community.Application/Posts/CreatePostCommandHandler.cs`
- `src/Modules/Community/Application/Sanad.Modules.Community.Application/Comments/CreatePostCommentCommandHandler.cs`
- `src/Modules/Community/Application/Sanad.Modules.Community.Application/Comments/CreateCommentCommandHandler.cs`
- `src/Modules/Community/Application/Sanad.Modules.Community.Application/Comments/CreateReplyCommandHandler.cs`
- `src/Modules/Community/Infrastructure/Sanad.Modules.Community.Infrastructure/Persistence/CommunityDbContext.cs`
- `src/Modules/Community/Infrastructure/Sanad.Modules.Community.Infrastructure/Persistence/Configurations/PostConfiguration.cs`
- `src/Modules/Community/Infrastructure/Sanad.Modules.Community.Infrastructure/Persistence/Migrations/CommunityDbContextModelSnapshot.cs`
- `tests/Sanad.UnitTests/Community/CommunityModerationAndCheckInTests.cs`
- `tests/Sanad.UnitTests/Community/CommunityRecommendationsTests.cs`
- `tests/Sanad.UnitTests/Community/CommunityImageFileTests.cs`
- `tests/Sanad.UnitTests/Persistence/CommunityAndFamilyModelReadinessTests.cs`
- `docs/app/community/writes.md`
- `docs/postman/admins/Sanad.Admin.Community.postman_collection.json`
- `docs/postman/app/Sanad.App.Community.postman_collection.json`
- `docs/internal/work/portal-api-reconciliation/plan.md`
- `docs/internal/Mastermind_Handoff.md`

**New source, tests, and feature documentation:**

- `src/Modules/Community/Domain/Sanad.Modules.Community.Domain/Posts/CommunityPostReport.cs`
- `src/Modules/Community/Domain/Sanad.Modules.Community.Domain/Posts/CommunityAuthorRestriction.cs`
- `src/Modules/Community/Application/Sanad.Modules.Community.Application/Posts/AdminCommunityPostsQueries.cs`
- `src/Modules/Community/Application/Sanad.Modules.Community.Application/Posts/CommunityPostReportCommands.cs`
- `src/Modules/Community/Application/Sanad.Modules.Community.Application/Posts/CommunityAuthorRestrictionCommands.cs`
- `src/Modules/Community/Infrastructure/Sanad.Modules.Community.Infrastructure/Persistence/Configurations/CommunityPostReportConfiguration.cs`
- `src/Modules/Community/Infrastructure/Sanad.Modules.Community.Infrastructure/Persistence/Configurations/CommunityAuthorRestrictionConfiguration.cs`
- one generated Community migration named `*_AddCommunityModeration.cs` and its `*.Designer.cs`, in `src/Modules/Community/Infrastructure/Sanad.Modules.Community.Infrastructure/Persistence/Migrations/`
- `tests/Sanad.UnitTests/Community/CommunityPostReportTests.cs`
- `tests/Sanad.UnitTests/Community/CommunityAuthorRestrictionTests.cs`
- `tests/Sanad.UnitTests/API/AdminCommunityPostsModerationTests.cs`
- `tests/Sanad.UnitTests/API/CommunityPostReportsTests.cs`
- `tests/Sanad.UnitTests/API/CommunityAuthorRestrictionsTests.cs`
- `docs/admin/community-moderation.md`
- `docs/internal/work/portal-api-reconciliation/reports/fur-07-worker-delivery.md`

No Identity module or schema, global suspension policy, provider, user
statistics service, unrelated Community feature, frontend repository, or
production database file is in scope. A Community migration may be authored;
the worker must not apply it to any database.

## Acceptance and verification

- Contract tests cover queue paging/filter/search, detail and author-data
  minimization, report authorization/validation/deduplication/resolution,
  required rejection reason persistence, counter definitions, restriction
  expiry/enforcement on all three Community write paths, and unchanged global
  Identity state.
- Persistence/model tests cover the new Community entities, constraints,
  indexes, and generated migration/model alignment.
- Run one strict solution build after final code changes and the focused tests
  for changed Community behavior. Do not run the full suite unless the final
  implementation becomes cross-cutting or a concrete contract risk requires
  it. No Bruno/live HTTP run is required by default; use it only if an approved
  disposable target and stateful HTTP boundary make it necessary.
- Update only the named Community guides and Postman collections. Validate
  their JSON/examples and route mapping because routes and examples change.
- No migration application, fixture writes, database reset, provider call,
  commit, push, publish, or deployment is authorized by this contract.

## Approval record

The owner approved this complete contract on 2026-10-17 with “Perfect continue
please,” after selecting posts-only reporting, community-only timed
restrictions, required rejection reason codes with an optional note, and no
hide/restore action for reported published posts. The approved defaults also
include the reason-code lists, deduplication behavior, duration presets,
restriction effects, counters, routes, file manifest, and acceptance checks
above. Any change to these boundaries requires a revised contract before
implementation.

**Owner-approved manifest amendment (2026-10-17):** The owner authorized the
next worker to modify `CommunityRecommendationsTests.cs` and
`CommunityImageFileTests.cs` to update the two existing `Post.Reject` call sites
that failed compilation after the contract-required rejection signature
change. This expands only the test-file manifest; behavior, permissions, data
policy, migration limits, and release boundaries remain unchanged.
