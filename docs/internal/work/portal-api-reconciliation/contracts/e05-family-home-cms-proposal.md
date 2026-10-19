# E05 — Family-home CMS: Owner-approved phase scope brief

**Owner-approved scope; implementation not started — 2026-10-19.** Audience: App Family/mobile and Admin CMS developers.
Owner expressly directed: “select all the best recommended options and lock them and approve them.” This brief records that approval and the delegated bounded defaults below. Phase state: `approved` (queued); release state: `not_requested`.
Scope approval is not implementation/start, Worker dispatch or release permission. This single brief owns scope, pending tasks and current evidence; the existing filename is retained for continuity.
**Dispatch-ready, queued:** no remaining Owner product decisions. Start only after E04 implementation is complete/accepted, incompatible file ownership is released, and Owner gives separate E05 dispatch direction. E05 and E06 run sequentially, never alongside E04.

## Intake and verified source evidence

- Requirement: banners, quick links, featured services and mobile publication. Owner confirms no UI screens are available; none are awaited or required. The locked backend contract needs no further content/design intake and does not prescribe UI layout or editorial copy.
- Roadmap: `docs/internal/work/portal-api-reconciliation/plan.md` at Git revision `b847de46226e9f5089e8a044d3c2d23458aef3ac` contains E05; its current compact working copy omits the roadmap rows.
- Existing consumer truth: `docs/app/public/landing.md`, `docs/admin/landing.md`, `docs/postman/app/Sanad.App.Public.postman_collection.json`, `docs/postman/admins/Sanad.Admin.postman_collection.json`.
- `src/API/Sanad.API/Controllers/LandingController.cs` and `src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Landing/GetPublishedLandingSectionsQueryHandler.cs` serve anonymous published/visible content, not personalized Family home. Existing type-based publication archives earlier public sections of the same type; separate home storage prevents collisions.
- `src/Modules/Cms/Domain/Sanad.Modules.Cms.Domain/Landing/LandingSection.cs` defines title/body/image-key caps of 200/4000/500. `src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Landing/LandingAdminCommands.cs` supplies draft-twin, publish/archive and actor/UTC history patterns.
- `src/API/Sanad.API/DependencyInjection.cs` verifies `CmsContent` = Normal SuperAdmin/ContentAdmin and `FamilyAccess` = Normal Family. `src/Modules/Families/Application/Sanad.Modules.Families.Application/Families/FamilyAccess.cs` resolves non-deleted owned/member Families, including read-only members; it is internal, so expose access through a Families query rather than copying permissions into CMS.
- Catalog: `src/Modules/Caregivers/Domain/Sanad.Modules.Caregivers.Domain/Caregivers/Lookups/Service.cs` has bilingual names, icon, caregiver type and active flag; `src/Modules/Caregivers/Application/Sanad.Modules.Caregivers.Application/Lookups/ServiceLookups.cs` already projects public service fields. Service name/icon caps are 150/500.
- `tests/Sanad.UnitTests/Cms/LandingReadsTests.cs` provides empty/published/hidden/order/relational regression examples. Existing tests were inspected, not executed as E05 evidence.
- Technical readiness: CMS already references Families/Caregivers application modules; API assembly scanning registers their handlers/validators. `src/BuildingBlocks/Infrastructure/Sanad.BuildingBlocks.Infrastructure/Storage/LocalDiskFileStorage.cs` restricts `OpenReadAsync` to private keys. Public landing-key validation must instead use the configured public root from `src/BuildingBlocks/Infrastructure/Sanad.BuildingBlocks.Infrastructure/Storage/LocalStorageOptions.cs`, with path containment and existence checks in the new Admin controller/helper; do not change the shared storage interface or add media lifecycle operations.

## Objective and locked scope decisions

1. **Surface/readers:** separate authenticated, shared-editorial Family home. Normal Family owners and authorized Editor/Viewer members of a non-deleted Family may read. Preserve anonymous public Landing API/storage. No private personalization, audience targeting or data-based recommendations.
2. **Editors/publication:** existing Normal SuperAdmin/ContentAdmin may both author and publish. Three section kinds: `banners`, `quick-links`, `featured-services`. Immediate independent section publication; no whole-home atomic release. Draft edits to published content create/update its draft twin; publishing archives the prior version of that home section kind. Draft-only deletion; archived history is retained. Visibility and display order follow existing CMS conventions.
3. **Typed content:** banners require Arabic/English title and image key, with optional bilingual body and optional action; quick links require Arabic/English label and target, with optional icon key; featured entries require an active service ID and use live catalog name/icon/type without price, availability or editorial overrides. Optional copy is supplied in both languages or omitted in both.
4. **Bounded technical defaults:** titles/labels ≤200 characters, banner body ≤4000, image/icon keys ≤500, following existing CMS constraints. Each nonempty published section holds 1–10 items; draft sections may be empty. Ten items is a delegated conservative cap, not existing source policy. Section/item display order is a unique nonnegative integer within its scope; read order is ascending. Featured IDs cannot repeat within a section.
5. **Navigation:** allowlisted semantic targets `services` (catalog) and `service` (one active catalog service ID); no arbitrary URL, free-form route or other parameters. Banner actions and quick links use this same typed target. Featured entries target their referenced service. Mobile maps these tokens to its existing catalog/detail navigation; no frontend changes are included.
6. **Reference/read behavior:** validate targets and media references at publish. Missing/inactive service references later omit the affected item, not the whole response; an action-bearing item with an unavailable target is omitted. Emit no drafts, hidden or archived sections. Omit sections with no remaining items. No published content returns `200 []`; client fallback is to omit CMS sections and retain normal app navigation, with no invented fallback cards/copy.
7. **Persistence/privacy:** separate `cms` home-section and append-only publication/visibility/deletion history storage; UTC actor/time attribution and optimistic concurrency for edits/publish. Preserve public Landing data and media. Only editorial fields reach Family readers; no Family/profile/medical information is embedded or used for selection. Assets use existing authorized CMS upload behavior; no new media lifecycle or cleanup.

## API and lifecycle contract (approved new behavior, not implemented)

- Family: `GET /api/v1/family/home/sections`, `FamilyAccess` plus server-side Family resolution. `200` ordered typed section list; `401/403` for auth/policy denial; inaccessible Family `404` through existing Family error conventions.
- Admin base `/api/v1/admin/family-home/sections`: `GET` paged list, `POST` draft creation, `PUT /{id}` draft/twin update, `POST /{id}/publish`, `POST /{id}/visibility`, `DELETE /{id}` draft deletion, `POST /reorder`. Reuse existing landing success conventions (200/201/204), but separate home DTOs/storage. No new media endpoint.
- New errors use `Cms.FamilyHome.*`: validation/incomplete `400`, missing section `404`, immutable-history or stale-version conflict `409`. Map locally in the new controllers without changing E04-owned shared error/policy files. Return expected version on Admin reads/writes; publish is transactional per section and cannot overwrite a concurrent publication.
- DTO/strong-ID serialization, media-key checks and pagination normalization must follow verified existing conventions. These are implementation details, not permission to invent content types or navigation destinations.

## Explicit out of scope

UI/layout/content production, personalization, analytics/experiments, external links, extra service catalogs, pricing/booking changes, notifications, scheduled publication, token preview, whole-home release/rollback and E08 media deletion/retention. No production content edits or migration application are authorized.

## Sequential tasks — all pending

| Task | Dependency | Acceptance | State |
|---|---|---|---|
| 1. Implementation preparation | E04 implementation accepted and incompatible ownership released | Mastermind pins current base/local delta, confirms the phase Worker model/profile and updates this brief before requesting dispatch direction. Verify manifest/new-file collisions, reserved migration name, DTO/strong-ID serialization and public-root media validation; document the locked navigation tokens for mobile consumption, without waiting for UI binding/screens. Record evidence here | pending |
| 2. Authoring and publication | Accepted task 1 + Owner dispatch direction | Separate storage, validation/roles, draft-twin/history/concurrency and focused tests satisfy locked scope; Mastermind accepts handback | pending |
| 3. Mobile delivery and consumer contract | Accepted task 2 | Family read/access, ref/empty behavior, consumer guides/Postman, focused HTTP/schema checks and final build pass review | pending |

Worker updates assigned task/result only and stops at each handback. Mastermind alone completes tasks and controls phase/release states. No worker has been dispatched.

## Exact selected-scope file manifest — future implementation only

Paths are repository-relative. **New** means an exact planned file, not created by this planning task. Each new file's parent is its directory in the listed path; all those parents already exist. Existing public Landing controllers/models are read-only references, not owned edits. Additional necessary files require a bounded Mastermind manifest amendment, not policy expansion or concurrent E04 edits.
Ownership sequencing: E04 also owns the Family Postman collection during consumer delivery; do not acquire it before E04 acceptance/release of file ownership. E05 and E06 share the Admin Postman collection, so E06 follows accepted E05 delivery. Shared policies, registration, storage and error-mapper source remain read-only. The selected manifest needs no expansion for the inspected dependencies; public-media validation fits the new Admin controller file.

- **New:** `src/API/Sanad.API/Controllers/AdminFamilyHomeController.cs`
- **New:** `src/API/Sanad.API/Controllers/FamilyHomeController.cs`
- **New:** `src/Modules/Families/Application/Sanad.Modules.Families.Application/Families/GetFamilyHomeAccessQuery.cs`
- **New:** `src/Modules/Cms/Domain/Sanad.Modules.Cms.Domain/Landing/FamilyHomeSection.cs`
- **New:** `src/Modules/Cms/Domain/Sanad.Modules.Cms.Domain/Landing/FamilyHomeSectionHistory.cs`
- **New:** `src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Landing/FamilyHomeCommands.cs`
- **New:** `src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Landing/FamilyHomeQueries.cs`
- **New:** `src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Landing/FamilyHomeModels.cs`
- **New:** `src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Landing/FamilyHomeValidators.cs`
- `src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Abstractions/Data/ICmsDbContext.cs`
- `src/Modules/Cms/Infrastructure/Sanad.Modules.Cms.Infrastructure/Persistence/CmsDbContext.cs`
- **New:** `src/Modules/Cms/Infrastructure/Sanad.Modules.Cms.Infrastructure/Persistence/Configurations/FamilyHomeSectionConfiguration.cs`
- **New:** `src/Modules/Cms/Infrastructure/Sanad.Modules.Cms.Infrastructure/Persistence/Configurations/FamilyHomeSectionHistoryConfiguration.cs`
- **New, reserved migration pair:** `src/Modules/Cms/Infrastructure/Sanad.Modules.Cms.Infrastructure/Persistence/Migrations/20261019235900_AddFamilyHomeSections.cs`
- **New:** `src/Modules/Cms/Infrastructure/Sanad.Modules.Cms.Infrastructure/Persistence/Migrations/20261019235900_AddFamilyHomeSections.Designer.cs`
- `src/Modules/Cms/Infrastructure/Sanad.Modules.Cms.Infrastructure/Persistence/Migrations/CmsDbContextModelSnapshot.cs`
- **New:** `tests/Sanad.UnitTests/Cms/FamilyHomeTests.cs`
- **New:** `tests/Sanad.UnitTests/API/FamilyHomeControllerTests.cs`
- **New:** `docs/admin/family-home.md`
- **New:** `docs/app/families/home.md`
- `docs/postman/admins/Sanad.Admin.postman_collection.json`
- `docs/postman/app/Sanad.App.Family.postman_collection.json`
- `docs/internal/work/portal-api-reconciliation/contracts/e05-family-home-cms-proposal.md` (this lifecycle record).

## Focused verification and safety

- Cover editor/reader role matrix and Family membership/deletion, public/home publication isolation, draft/hidden/archive exclusion, stale/concurrent edits, bilingual bounds, typed targets, active refs at publish and later omission, order and empty results.
- Test actual HTTP status/schema and minimal response projections; run existing Landing read regressions to prove the separate surface does not alter anonymous reads. Validate storage/history and no deletion of referenced assets; migration authored only, never claim applied.
- Deliver the listed Family-home consumer guides and Postman examples with the feature as primary API truth. Validate changed Markdown links/JSON and actual methods/routes/DTOs against source/OpenAPI; no separate documentation relay.
- Strict Release solution build and affected focused tests: successful exits and zero warnings. Isolated synthetic/disposable fixtures only; no live providers/DB operations without separate authority. Reuse valid evidence; no automatic full-suite/Bruno run. Record authored versus executed checks and `git diff --check`.

## Current result, technical follow-up and release gate

Scope/policy blockers: **none**. UI awaited: **no**; unavailable screens/editorial assets need no follow-up. Task 1 owns ordinary technical preflight, including serializer/media checks and the reserved migration identifier; mobile token mapping is a consumer integration responsibility, not a backend start prerequisite. Worker model/profile confirmation is dispatch-time operations, not product intake.
Current evidence: read-only source/docs/test/dependency mapping; existing manifest paths and planned-file parents checked, with no implementation/build/runtime tests. No current dispatch SHA is pinned while E04 changes the worktree. Next action only: after E04 implementation is accepted and incompatible ownership released, Mastermind pins current base/local delta, updates the phase model/brief, then obtains Owner direction for E05 Worker dispatch. All tasks remain pending; E06 stays queued behind E05.
After delivery and Mastermind review, phase becomes `accepted`, not deployed. Explicit release authorization is required for commit/push/deploy or migration application; record verified revision/results/limitations here before `completed`. No release authority has been granted.
