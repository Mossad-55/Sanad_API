# Phase 2 report — Community categories (+migration)

- **Phase:** 2. Community categories (+migration)
- **Branch:** `opencode/opus-work`
- **Base SHA (phase start, = phase-1 report commit):** `0d726b8e387c5dc3196346f87c29f250cf7cf4db`
- **Final SHA (content commit):** `b35a04004c88d0fadf99854e44be6e67ebb76f48`
- **Effective model:** `opencode/mimo-v2.6-flash-free` (Windows, dotnet + dotnet-ef 10.0.12)

## Changed files

- `src/Modules/Community/Domain/.../Categories/CommunityCategoryId.cs` — new
  typed id **defined inside the Community module** (deliberately not in
  BuildingBlocks, to avoid a shared-contract change; owner rule observed).
- `src/Modules/Community/Domain/.../Categories/CommunityCategory.cs` — entity:
  bilingual names (required, ≤200, trimmed), `IsActive`, UTC timestamps,
  `Create`/`Update`.
- `src/Modules/Community/Domain/.../Posts/Post.cs` — additive optional
  `CategoryId` + `Category` navigation; `Post.Create` gains optional
  `categoryId = null` parameter (non-breaking; consumed by Phase 3).
- `src/Modules/Community/Application/.../Categories/CommunityCategoryErrors.cs`
  — `Community.Category.Invalid` (400), `Community.CategoryNotFound` (404),
  `Community.CategoryInactive` (409), `Community.InvalidCategory` (400),
  `Community.CategoryInUse` (409).
- `src/Modules/Community/Application/.../Categories/GetCategoriesQueries.cs` —
  public active-only list + admin list.
- `src/Modules/Community/Application/.../Categories/CategoryCommands.cs` —
  admin create/update/delete.
- `src/Modules/Community/Application/.../Abstractions/Data/ICommunityDbContext.cs`
  — `DbSet<CommunityCategory> Categories`.
- `src/Modules/Community/Infrastructure/.../Persistence/CommunityDbContext.cs` —
  `DbSet` registration.
- `src/Modules/Community/Infrastructure/.../Configurations/CommunityCategoryConfiguration.cs`
  — table `community.CommunityCategories`.
- `src/Modules/Community/Infrastructure/.../Configurations/PostConfiguration.cs`
  — nullable `CategoryId` conversion + `SetNull` FK.
- `src/Modules/Community/Infrastructure/.../Migrations/20261010004134_AddCommunityCategories.cs`
  (+ `.Designer.cs`, snapshot regenerated) — **additive, authored only, never
  applied**: new `CommunityCategories` table; nullable `Posts.CategoryId`
  column + index + `SET NULL` FK.
- `src/API/Sanad.API/Controllers/AdminCommunityCategoriesController.cs` —
  CRUD under `CommunityModeration` at `/api/v1/admin/community/categories`.
- `src/API/Sanad.API/Controllers/CommunityCategoriesController.cs` — public
  `GET /api/v1/community/categories` (authenticated, active-only).
- `src/API/Sanad.API/ProblemDetail/ResultProblemDetailsMapper.cs` — see
  shared-surface note below.
- `tests/Sanad.UnitTests/Community/CommunityCategoryTests.cs` — 12 focused
  tests (domain + handlers, InMemory).
- `docs/app/community/writes.md` — Categories section.
- `docs/postman/app/Sanad.App.Community.postman_collection.json` — public
  list request.
- `docs/postman/admins/Sanad.Admin.Community.postman_collection.json` —
  admin CRUD requests + `categoryId` variable.

## Shared-surface note (owner visibility required)

`ResultProblemDetailsMapper.cs` is outside the Phase 2 file manifest. The
frozen plan mandates `404` for unknown category ids and `409` for
inactive/in-use categories; the mapper's code→status table is the only place
those statuses can be produced (unmapped codes default to `400`). Five
Community lines were added (`CategoryNotFound`=404, `CategoryInactive`=409,
`CategoryInUse`=409, `InvalidCategory`=400, `Category.Invalid`=400). No
existing mapping changed. Flagging for Integrator/owner review.

## Verification (all on final content commit)

| Command | Exit | Result |
| --- | --- | --- |
| `dotnet ef migrations add AddCommunityCategories --project src/Modules/Community/Infrastructure/Sanad.Modules.Community.Infrastructure --context CommunityDbContext --output-dir Persistence/Migrations` (dummy design-time connection string; **no apply**) | 0 | migration + designer + snapshot generated |
| `dotnet build Sanad.slnx -warnaserror` | 0 | 0 warnings, 0 errors |
| `dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj --filter "FullyQualifiedName~Community" --no-build` | 0 | Passed 54 / Failed 0 / Skipped 0 (42 prior + 12 new) |
| `ConvertFrom-Json` on both touched Postman collections | 0 | valid JSON |
| `git diff --check` | 0 | no whitespace errors |

## Skipped / blocked checks

- Full unit + architecture suites: deferred to Phase 7 per tasks.md.
- Migration apply / stateful HTTP: forbidden this package (no provider calls,
  no migration applies). Migration verified by generation + review only.

## Dependencies confirmed in main

- `CommunityModeration` policy, `AdminCommunityPostsController` pattern,
  design-time `CommunityDbContextFactory`, `dotnet ef` 10.0.12, Postman
  admin/app community collections — all present at base.

## Remaining owner decisions

- None blocking. Owner may review the five mapper lines (shared surface)
  noted above.
