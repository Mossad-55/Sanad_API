# Phase 5 report — Library in CMS (+migration)

- **Phase:** 5. Library in CMS (+migration)
- **Branch:** `opencode/opus-work`
- **Base SHA (phase start, = session-1 handoff commit):** `87338a965e3e698449849c705bd649c7866f1e3c`
- **Final SHA (content commit):** `8676b6a6c00e687b22fab5c1ed90466f9849aa03`
- **Effective model:** `opencode/mimo-v2.6-flash-free` (Windows, dotnet 10)

Session-note: the tree arrived with uncommitted, in-progress Phase 5 source
(handoff did not mention it — likely an interrupted prior attempt). It was
audited against the frozen contract in `plan.md` §3, completed (tests, docs,
Postman), verified, and committed as the single Phase 5 content commit.

## Changed files

- `src/Modules/Cms/Domain/.../Library/` — `LibraryCategory`, `LibraryContent`
  (Article|Video with type-specific rules, ordered owned sections with bullet
  lists, Draft/Published/Archived lifecycle), `LibraryContentSection`,
  `LibraryContentInteraction` (Like/Save rows), typed ids, status/type enums.
- `src/Modules/Cms/Application/.../Library/` — category CRUD + public
  active-only list; content create/update/publish/archive; public list
  (type/categoryId/search/Sort Newest|MostViewed/paged, bare-list envelope,
  per-item `isLiked/isSaved/isPremium/canAccess`); detail; recommendations
  (same-category newest + top-viewed fallback, seed precedence
  contentId > categoryId, no ML); saved list; saved/liked explicit-set
  idempotent commands; per-call view increment; admin paged list + detail;
  `LibraryErrors`; premium gate (`LibraryPremiumAccess`: owner family first,
  else earliest joined non-deleted family; current subscription must grant
  `SubscriptionBenefitKey.PremiumContent` = key 8); viewer-state bulk loader.
- `src/Modules/Cms/Application/.../Sanad.Modules.Cms.Application.csproj` —
  **new cross-module references** to `Families.Application`/`Families.Domain`
  (needed to evaluate the frozen premium gate; no Families→Cms back-edge, no
  cycle — see owner note).
- `src/Modules/Cms/Application/.../Abstractions/Data/ICmsDbContext.cs` —
  three library `DbSet`s.
- `src/Modules/Cms/Infrastructure/.../Persistence/CmsDbContext.cs` — `DbSet`s
  (configs applied via existing assembly scan).
- `src/Modules/Cms/Infrastructure/.../Configurations/LibraryCategoryConfiguration.cs`,
  `LibraryContentConfiguration.cs` — tables `cms.library_categories`,
  `cms.library_contents`, `cms.library_content_sections` (owned collection,
  jsonb bullet arrays, unique (content, displayOrder)),
  `cms.library_content_interactions` (unique (content, user, kind)).
- `src/Modules/Cms/Infrastructure/.../Migrations/20261010131314_AddLibraryContent.cs`
  (+ `.Designer.cs`, snapshot regenerated) — **additive, authored only, never
  applied**: four new tables + indexes + FKs (`SET NULL` on category,
  cascade on sections).
- `src/API/Sanad.API/Controllers/LibraryController.cs` — public
  `/api/v1/library/*` (all `[Authorize]`, identity from JWT): `categories`,
  `content` (invalid `type` → `400 Cms.Library.InvalidType`, invalid `sort`
  → `400 Cms.Library.InvalidSort`), `content/{id}`, `recommendations`,
  `saved`, `PUT content/{id}/saved|liked` (`{value: bool}`, 204),
  `POST content/{id}/view` (204).
- `src/API/Sanad.API/Controllers/AdminLibraryController.cs` — CmsContent
  policy at `/api/v1/admin/library/*`: category CRUD (delete-in-use → 409),
  paged admin content list + detail, multipart create/update with cover
  upload (jpeg/png/webp signature checks, 5 MB cap, cleanup on failed
  command), publish/archive.
- `src/API/Sanad.API/ProblemDetail/ResultProblemDetailsMapper.cs` — see
  shared-surface note below.
- `tests/Sanad.UnitTests/Cms/LibraryTests.cs` — 12 focused tests (InMemory
  CmsDbContext + FamiliesDbContext; no SQLite — no raw SQL): model tables,
  category CRUD + active-only public list + in-use guard, category validation
  on content (unknown 400 / inactive 409), type-specific create rules,
  draft-only update + publish/archive rules, list filters/search/sorts +
  published-only, idempotent saved/liked + likes counter + per-call views +
  published-only interactions, saved list ordering, detail not-published,
  premium metadata-only vs PremiumPlus entitlement, recommendations
  precedence/fallback/exclusion, admin list paging/filters.
- `docs/app/library/README.md` — full frontend reference (categories,
  lifecycle, multipart contract, public reads, premium gating, saved/liked/
  views, recommendations, error-code table).
- `docs/postman/app/Sanad.App.Library.postman_collection.json` — new App
  collection (categories, feed/detail, recommendations, saved/liked/view).
- `docs/postman/admins/Sanad.Admin.postman_collection.json` — new
  `19. Library CMS (manual)` folder (category CRUD, content list/detail,
  multipart create/update, publish, archive) + six `library*` variables.

## Shared-surface notes (owner visibility required)

1. `ResultProblemDetailsMapper.cs` is outside the Phase 5 file manifest. The
   frozen contract's status semantics (404 not-found/not-published, 409
   inactive category / in-use / lifecycle violations, 400 validation) can
   only be produced via the mapper's code→status table (unmapped codes
   default to 400). Nine `Cms.Library*` lines were added
   (`LibraryCategory.NotFound`=404, `.Invalid`=400, `.InUse`=409,
   `LibraryContent.NotFound`=404, `.NotPublished`=404, `.Invalid`=400,
   `.InvalidOperation`=409, `Library.InvalidCategory`=400,
   `Library.CategoryInactive`=409). No existing mapping changed — same
   precedent as the phase-2 Community lines.
2. `Cms.Application` now references `Families.Application`/`Families.Domain`
   for the frozen premium gate (benefit key 8 must be evaluated against the
   caller's current family subscription). The reference is one-directional
   (no Families→Cms edge; architecture suite unchanged and green).

## Verification (all on final content commit)

| Command | Exit | Result |
| --- | --- | --- |
| `dotnet build Sanad.slnx -warnaserror` | 0 | 0 warnings, 0 errors |
| `dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj --filter "FullyQualifiedName~Sanad.UnitTests.Cms" --no-build` | 0 | Passed 92 / Failed 0 / Skipped 0 (80 prior + 12 new) |
| `ConvertFrom-Json` on `Sanad.App.Library.postman_collection.json` and `Sanad.Admin.postman_collection.json` | 0 | valid JSON |
| `git diff --check` (working tree + staged) | 0 | no whitespace errors |

## Skipped / blocked checks

- Full unit + architecture suites: deferred to Phase 7 per tasks.md.
- Migration apply / provider calls / stateful HTTP: forbidden this package;
  migration verified by authoring + build + review only.

## Dependencies confirmed in main

- `CmsContent` policy, Cms module lifecycle precedents (Wellness tips,
  ElderlyWelcome), `IFileStorage` + `StorageErrors`, design-time
  `CmsDbContextFactory`, Families subscription model
  (`SubscriptionPlan`/`SubscriptionPlanVersion`/`FamilySubscription`,
  `SubscriptionBenefitKey.PremiumContent = 8`), `Family` owner/member
  resolution — all present at base. Phase 4 remains blocked and untouched.

## Remaining owner decisions

- Review the two shared-surface notes above (mapper lines; Cms→Families
  application reference). Neither blocks integration; both are required by
  the frozen contract as written.
