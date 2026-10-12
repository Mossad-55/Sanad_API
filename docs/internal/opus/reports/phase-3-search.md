# Phase 3 report — Community search/filter/sort, favorites, per-user state

- **Phase:** 3. Community search/filter/sort + favorites + per-user state
- **Branch:** `opencode/opus-work`
- **Base SHA (phase start, = phase-2 report commit):** `52b425b7bbbbfeadee6a7fb93c03263ddfc0f980`
- **Final SHA (content commit):** `9c873ec09f6f630e81a61c2ae75458e3bf92f562`
- **Effective model:** `opencode/mimo-v2.6-flash-free` (Windows)

## Changed files

- `src/Modules/Community/Application/.../Posts/GetPostsQuery.cs` — extended
  with `Search` (case-insensitive titles+contents, lower+Contains so it
  behaves like ILIKE on PostgreSQL and translates on SQLite/InMemory),
  `CategoryId`, `FromDateUtc`/`ToDateUtc`, `Sort`
  (`Newest|Oldest|MostLiked`, deterministic tie-breaks), `ViewerUserId`;
  returns `PostFeedItem` (post + per-user state); includes `Category`.
- `src/Modules/Community/Application/.../Posts/PostViewerState.cs` — new;
  bulk-loads the caller's likes/favorites/ratings for a page of posts and
  attaches `IsLiked`/`IsFavorited`/`CurrentUserRating`.
- `src/Modules/Community/Application/.../Posts/GetFavoritePostsQuery.cs` —
  new; caller's favorited published posts, newest-favorited-first, paged,
  same `PostFeedItem` shape.
- `src/Modules/Community/Application/.../Posts/GetPostQuery.cs` — optional
  `ViewerUserId`; returns `PostFeedItem?` (null for unknown/unpublished).
- `src/Modules/Community/Application/.../Posts/CreatePostCommand.cs` +
  `CreatePostCommandHandler.cs` — optional `categoryId`; unknown →
  `400 Community.InvalidCategory`, inactive → `409 Community.CategoryInactive`
  (onboarding inactive-lookup precedent); valid → persisted on the post.
- `src/API/Sanad.API/Controllers/CommunityPostsController.cs` — feed query
  params (`search`, `categoryId`, `fromDate`, `toDate`, `sort`; invalid sort
  → 400 `Community.InvalidSort`, `fromDate > toDate` → 400
  `Community.InvalidDateRange`); new `GET /api/v1/community/favorites`;
  `CommunityPostResponse` additive fields (`categoryId`,
  `categoryArabicName`, `categoryEnglishName`, `isLiked`, `isFavorited`,
  `currentUserRating` — all defaulted, JSON-additive); detail passes the
  viewer; recommendations map with default state (correct: recommendations
  exclude already-interacted posts, so false/false/null is exact).
- `tests/Sanad.UnitTests/Community/CommunityFeedTests.cs` — 9 focused tests
  (search both languages, category+date filters, all three sorts, viewer
  state attach, favorites ordering/published-only/per-user, create-post
  category validation ×3, detail state + unknown).
- `docs/app/community/writes.md` — feed/favorites/per-user-state section,
  `categoryId` in post creation contract.
- `docs/postman/app/Sanad.App.Community.postman_collection.json` — feed
  params, favorites request, create-post body, state descriptions.

Moderation flow untouched. Bare-list envelope unchanged. Existing
`GetPostsQuery(PostStatus, Page, PageSize)` positional call sites remain
source-compatible (new parameters are optional and trailing).

## Verification (all on final content commit)

| Command | Exit | Result |
| --- | --- | --- |
| `dotnet build Sanad.slnx -warnaserror` | 0 | 0 warnings, 0 errors |
| `dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj --filter "FullyQualifiedName~Community" --no-build` | 0 | Passed 63 / Failed 0 / Skipped 0 (54 prior + 9 new) |
| `ConvertFrom-Json` on `Sanad.App.Community.postman_collection.json` | 0 | valid JSON |
| `git diff --check` | 0 | no whitespace errors |

## Skipped / blocked checks

- Full unit + architecture suites: deferred to Phase 7 per tasks.md.
- Stateful HTTP/Bruno: not authorized this package.

## Dependencies confirmed in main

- Phase 2 categories (entity, FK, error codes + mapper statuses) on the
  branch; `CommunityInteraction` like/favorite storage; rating rows — all
  present and green at base.

## Remaining owner decisions

None for this phase.
