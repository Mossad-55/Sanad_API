# E05 — Family-home CMS: Owner-approved phase scope brief

**2026-10-21 Mastermind dispatch amendment (contract-authorized).** The
[approved follow-up contract](frontend-follow-up-2026-10-20.md) pre-authorizes the
Mastermind to fix wave order after checking actual shared ownership. E06 is therefore
**accepted and committed before E05** (`a5977a6`, correction `90f02c7`), and line 62's
"E06 follows accepted E05 delivery" is superseded in that direction: E06 has released
the shared `docs/postman/admins/Sanad.Admin.postman_collection.json`, and **E05 acquires
it now**. E05's other consumer collection
(`docs/postman/app/Sanad.App.Family.postman_collection.json`) is free of E04/E06 hands.

Task 1 (implementation preparation) is **completed** by the Mastermind on the pinned
base `90f02c7`:

- **Base pin and local delta:** base is pushed `main` tip `90f02c7`; the working tree
  holds unrelated dirt (`README.md`, `.codex/*`, scratch files, other briefs) that E05
  must preserve and exclude. No E05-owned path is dirty; `src/Modules/Cms`, the Family
  Postman collection and the Admin Postman collection are clean.
- **Manifest collisions:** none. Every "New" path in the selected-scope manifest was
  checked and does not exist. All planned parents exist.
- **Migration identifier:** the stale reservation `20261019235900_AddFamilyHomeSections`
  is replaced by **`20261021120000_AddFamilyHomeSections`** (+ `.Designer.cs`), authored
  only (never applied locally or on Production by the worker). It is later than the Cms
  head `20261012071340_AddLandingSectionHistory` and is a fresh current-dated id.
- **DTO/strong-ID serialization:** follow the verified Disney/CMS convention — request
  and response DTOs carry `Guid`; strong IDs are
  `public readonly record struct FamilyHomeSectionId(Guid Value)` with
  `New() => new(Guid.CreateVersion7())`; controllers bind `Guid` and wrap/unwrap
  explicitly. Do not add global converters.
- **Public media validation (source-corrected):** `LocalStorageOptions` has **one**
  `RootPath`; `LocalDiskFileStorage.ResolveFullPath` serves public keys from
  `GetEffectiveRootPath()` and switches to the sibling `<root>-private` directory only
  for keys that start with `private/`. A valid public key therefore: does not start with
  `private/`, normalizes to a relative path that stays inside the effective public root
  after `Path.GetFullPath` containment, and exists on disk. Implement this in the new
  Admin controller (inject `IOptions<LocalStorageOptions>`), reuse the existing CMS
  upload folder convention (`landing`), and do not change
  `IFileStorage`/`LocalDiskFileStorage`.
- **Navigation tokens (locked for mobile):** `services` (whole catalog) and
  `service` + an active catalog `serviceId` only. Banner actions, quick links and
  featured entries all use this same typed target. No arbitrary URLs, routes or
  parameters.
- **Phase worker:** Worker A `opencode/beam-free#high` (fallbacks
  `opencode/minimax-m3.1-flash-free#high`, River `river/deepseek-ai/DeepSeek-V4.1-Flash#high`).
- **Runtime slot:** the Mastermind holds the single local runtime slot for the E06
  release and the F8 local HTTP gate. E05 authors source, tests, docs/Postman and its
  adapter **only**, and does not build, test, start an API or write local fixture data
  until the Mastermind explicitly transfers the slot. E05's adapter is loopback-only on port **5253**,
  INSERT-only fixtures, dedicated identities, never `SanadBrunoTestDb`.

**2026-10-20 sequencing amendment:** the [approved follow-up contract](frontend-follow-up-2026-10-20.md)
now owns delivery order and persistent local HTTP verification. E04 is released;
ready operational follow-up fixes precede this phase. Locked behavior below remains
approved. New frontend suggestions for public reads, expiry and arbitrary links are
excluded pending clarification, not silently added. Pin a fresh base/file manifest
and migration identifier at actual dispatch; the old reserved timestamp is not an
instruction to insert an out-of-order migration. No Worker/start/release authorized.

**Owner-approved scope; implementation not started — 2026-10-19.** Audience: App Family/mobile and Admin CMS developers.
Owner expressly directed: “select all the best recommended options and lock them and approve them.” This brief records that approval and the delegated bounded defaults below. Phase state: `accepted` (implementation complete, gates green, see **Acceptance** below); release state: `not_requested`.
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
| 1. Implementation preparation | E04 implementation accepted and incompatible ownership released | Mastermind pins current base/local delta, confirms the phase Worker model/profile and updates this brief before requesting dispatch direction. Verify manifest/new-file collisions, reserved migration name, DTO/strong-ID serialization and public-root media validation; document the locked navigation tokens for mobile consumption, without waiting for UI binding/screens. Record evidence here | **completed** 2026-10-21 — see the dispatch amendment above |
| 2. Authoring and publication | Accepted task 1 + Owner dispatch direction | Separate storage, validation/roles, draft-twin/history/concurrency and focused tests satisfy locked scope; Mastermind accepts handback | **ready_for_review** — correction round on base `48f9864` (uncommitted): typed item payloads with one serializer, full content validation, Admin paging, no invented media route, local 400/404/409 mapping in both controllers, real `FamilyHomeTests`/`FamilyHomeControllerTests` (172 + 73 focused tests pass) and a real port-5253 adapter. Strict Release build 0 errors / 8 pre-existing foreign-lane warnings. **No HTTP check executed** — the adapter fails closed while `Cms` has 1 pending migration; see the evidence line below |
| 3. Mobile delivery and consumer contract | Accepted task 2 | Family read/access, ref/empty behavior, consumer guides/Postman, focused HTTP/schema checks and final build pass review | **in_progress** — consumer docs/Postman authored with task 2 |

Worker updates assigned task/result only and stops at each handback. Mastermind alone completes tasks and controls phase/release states. No worker has been dispatched.

## Exact selected-scope file manifest — future implementation only

Paths are repository-relative. **New** means an exact planned file, not created by this planning task. Each new file's parent is its directory in the listed path; all those parents already exist. Existing public Landing controllers/models are read-only references, not owned edits. Additional necessary files require a bounded Mastermind manifest amendment, not policy expansion or concurrent E04 edits.
Ownership sequencing: E04 also owns the Family Postman collection during consumer delivery; do not acquire it before E04 acceptance/release of file ownership. E05 and E06 both touch the Admin Postman collection; by the 2026-10-21 amendment E06 (accepted) handed it over, so E05 owns it now. Shared policies, registration, storage and error-mapper source remain read-only. The selected manifest was widened once, on 2026-10-21, by four bounded amendments: five strongly-typed files split out of the two domain aggregates, the generated migration id, the phase's scripted HTTP verifier, and the `Cms.FamilyHome.*` code set. No policy expansion, no concurrent E04 edit.

- **New:** `src/API/Sanad.API/Controllers/AdminFamilyHomeController.cs`
- **New:** `src/API/Sanad.API/Controllers/FamilyHomeController.cs`
- **New:** `src/Modules/Families/Application/Sanad.Modules.Families.Application/Families/GetFamilyHomeAccessQuery.cs`
- **New:** `src/Modules/Cms/Domain/Sanad.Modules.Cms.Domain/Landing/FamilyHomeSection.cs`
- **New:** `src/Modules/Cms/Domain/Sanad.Modules.Cms.Domain/Landing/FamilyHomeSectionHistory.cs`
- **New (2026-10-21 bounded amendment — strongly-typed identifiers split into their own files, matching the F7 pattern):** `src/Modules/Cms/Domain/Sanad.Modules.Cms.Domain/Landing/FamilyHomeSectionId.cs`
- **New (bounded amendment):** `src/Modules/Cms/Domain/Sanad.Modules.Cms.Domain/Landing/FamilyHomeSectionHistoryId.cs`
- **New (bounded amendment):** `src/Modules/Cms/Domain/Sanad.Modules.Cms.Domain/Landing/FamilyHomeSectionKind.cs`
- **New (bounded amendment):** `src/Modules/Cms/Domain/Sanad.Modules.Cms.Domain/Landing/FamilyHomeSectionStatus.cs`
- **New (bounded amendment — the `Cms.FamilyHome.*` code set):** `src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Landing/FamilyHomeErrors.cs`
- **New:** `src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Landing/FamilyHomeCommands.cs`
- **New:** `src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Landing/FamilyHomeQueries.cs`
- **New:** `src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Landing/FamilyHomeModels.cs`
- **New:** `src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Landing/FamilyHomeValidators.cs`
- `src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Abstractions/Data/ICmsDbContext.cs`
- `src/Modules/Cms/Infrastructure/Sanad.Modules.Cms.Infrastructure/Persistence/CmsDbContext.cs`
- **New:** `src/Modules/Cms/Infrastructure/Sanad.Modules.Cms.Infrastructure/Persistence/Configurations/FamilyHomeSectionConfiguration.cs`
- **New:** `src/Modules/Cms/Infrastructure/Sanad.Modules.Cms.Infrastructure/Persistence/Configurations/FamilyHomeSectionHistoryConfiguration.cs`
- **New, migrated migration pair (2026-10-21 bounded amendment — the reserved id `20261019235900` was replaced by the generated `20261021120000` so the pair orders after the F7/Cms head; name unchanged):** `src/Modules/Cms/Infrastructure/Sanad.Modules.Cms.Infrastructure/Persistence/Migrations/20261021120000_AddFamilyHomeSections.cs`
- **New:** `src/Modules/Cms/Infrastructure/Sanad.Modules.Cms.Infrastructure/Persistence/Migrations/20261021120000_AddFamilyHomeSections.Designer.cs`
- `src/Modules/Cms/Infrastructure/Sanad.Modules.Cms.Infrastructure/Persistence/Migrations/CmsDbContextModelSnapshot.cs`
- **New:** `tests/Sanad.UnitTests/Cms/FamilyHomeTests.cs`
- **New:** `tests/Sanad.UnitTests/API/FamilyHomeControllerTests.cs`
- **New:** `docs/admin/family-home.md`
- **New:** `docs/app/families/home.md`
- `docs/postman/admins/Sanad.Admin.postman_collection.json`
- `docs/postman/app/Sanad.App.Family.postman_collection.json`
- `docs/admin/admin-overview.md` — bounded manifest addition: one index row pointing at
  `docs/admin/family-home.md` (preserve the file's existing encoding/bytes; edit only that row).
- `docs/internal/work/portal-api-reconciliation/contracts/e05-family-home-cms-proposal.md` (this lifecycle record).
- **New (2026-10-21 bounded amendment — the phase's one scripted HTTP verifier):** `tools/Invoke-E05LocalHttpVerification.ps1`

## Focused verification and safety

- Cover editor/reader role matrix and Family membership/deletion, public/home publication isolation, draft/hidden/archive exclusion, stale/concurrent edits, bilingual bounds, typed targets, active refs at publish and later omission, order and empty results.
- Test actual HTTP status/schema and minimal response projections; run existing Landing read regressions to prove the separate surface does not alter anonymous reads. Validate storage/history and no deletion of referenced assets; migration authored only, never claim applied.
- Deliver the listed Family-home consumer guides and Postman examples with the feature as primary API truth. Validate changed Markdown links/JSON and actual methods/routes/DTOs against source/OpenAPI; no separate documentation relay.
- Strict Release solution build and affected focused tests: successful exits and zero warnings. Isolated synthetic/disposable fixtures only; no live providers/DB operations without separate authority. Reuse valid evidence; no automatic full-suite/Bruno run. Record authored versus executed checks and `git diff --check`.

## Current result, technical follow-up and release gate

Scope/policy blockers: **none**. UI awaited: **no**; unavailable screens/editorial assets need no follow-up. Task 1 owns ordinary technical preflight, including serializer/media checks and the reserved migration identifier; mobile token mapping is a consumer integration responsibility, not a backend start prerequisite. Worker model/profile confirmation is dispatch-time operations, not product intake.
Current evidence (2026-10-21): task 1 completed on base `90f02c7` — manifest/new-file collision check, fresh migration id `20261021120000_AddFamilyHomeSections`, DTO/strong-ID convention and the source-corrected public-media rule are all pinned in the dispatch amendment above. Task 2 is `ready_for_review` after the correction round on base `48f9864` (uncommitted, branch `main`, Worker `river/deepseek-ai/DeepSeek-V4.1-Flash`): the off-contract skeleton was replaced by typed `FamilyHomeItem` payloads behind exactly one serializer, `FamilyHomeContentRules` validation, Admin paging (`page`/`pageSize` clamped to a maximum of 100 with `page`/`pageSize`/`totalCount` in the response), the invented `POST .../media/validate` route removed, local 400/404/409 mapping of the eight `Cms.FamilyHome.*` codes used by both new controllers, exactly one `ToAdminResponse`, reads that fail closed on unreadable stored JSON, and published/archived-never-deleted semantics that still append the history row. Two bounded additions beyond the skeleton: `SourceId` on the Admin section response (a twin must be identifiable) and `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` in the single stored-payload serializer, without which the pinned `items_json` Admin `search` predicate can never match Arabic copy.
Checks actually run and their results: strict Release `dotnet build Sanad.slnx -c Release` succeeded with 0 errors and 8 warnings, all of them pre-existing in the foreign-lane `tests/Sanad.UnitTests/Notifications/**` files that this phase does not own; focused `dotnet test` filters `FullyQualifiedName~Cms` 172/172 passed and `FullyQualifiedName~FamilyHome` 73/73 passed; an offline `-LibraryOnly` smoke of the adapter's pure helpers, its ten fixture/route/code pins, both SQL readbacks and every assertion (positively and negatively) passed without an API, database or target call; the Postman deliverables re-inventoried against source — `Sanad.Admin` carries exactly one request per owned Admin route (GET list, GET detail, POST create, PUT update, POST publish, POST visibility, DELETE, POST reorder) plus the `familyHomeSectionId` collection variable set from the create response, and `Sanad.App.Family` carries the single public `GET /api/v1/family/home/sections` read, both files parsing as valid JSON, and the collection diff is purely additive (`+171/-0` and `+14/-0`) because the additions were written in the collections' existing formatting rather than by re-serializing them. `tools/Invoke-E05LocalHttpVerification.ps1` was authored and then run against `http://localhost:5253`: it failed closed at `phase=target-verification` with `exit 1`, `apiStarted=false`, no fixture SQL, no public file and empty `httpChecks`/`sqlChecks`, because the read-only target verification reports `Cms` pending migrations = 1 (head `20261012071340_AddLandingSectionHistory`); a read-only readback afterwards confirms no `cms.family_home_sections`, zero E05 identity/family/service rows, no synthetic file and no listener on 5253. Therefore **the HTTP and SQL-readback half of acceptance is authored, not executed**, and the phase migration must be applied by the release owner before the adapter can run. No migration was applied, and nothing was committed, pushed or deployed. Changed files on this base: the two new controllers; `GetFamilyHomeAccessQuery`; the Cms domain `FamilyHomeSection`/`FamilyHomeSectionHistory` plus the `FamilyHomeSectionId`/`Kind`/`Status`/`HistoryId` value objects; the Cms Application `FamilyHomeCommands`/`FamilyHomeQueries`/`FamilyHomeModels`/`FamilyHomeValidators`/`FamilyHomeErrors`; the two EF configurations, the `20261021120000_AddFamilyHomeSections` migration pair and the model snapshot; the additive `ICmsDbContext`/`CmsDbContext` edits (96 insertions, 0 deletions in the shared Cms files); the two focused test suites; `docs/admin/family-home.md`, `docs/app/families/home.md` and the single `docs/admin/admin-overview.md` index row; both owned Postman collections; and `tools/Invoke-E05LocalHttpVerification.ps1`. Flagged for the Mastermind: the manifest's `New:` path list does not name the five strongly-typed files (`FamilyHomeSectionId`, `FamilyHomeSectionKind`, `FamilyHomeSectionStatus`, `FamilyHomeSectionHistoryId`, `FamilyHomeErrors`) or the adapter's tool path, although the 2026-10-21 dispatch amendment authorizes the adapter, the strong-ID convention and the `Cms.FamilyHome.*` codes by description; the path list itself was not amended, so that remains the Mastermind's call.
## Acceptance (2026-10-21, Mastermind)

Phase state: **`accepted`** (worker tasks complete; not committed, not released). The manifest amendment the handback flagged was applied by the Mastermind: the five strongly-typed files, the `Cms.FamilyHome.*` code set file, the generated migration id `20261021120000_AddFamilyHomeSections` and `tools/Invoke-E05LocalHttpVerification.ps1` are now named in the selected manifest above.

Gate evidence actually run on the accepted tree (base `48f9864`, uncommitted, branch `main`):

- Strict Release `dotnet build Sanad.slnx -c Release /p:TreatWarningsAsErrors=true` → **succeeded, 0 warnings, 0 errors**. The 8 analyzer errors the worker reported as pre-existing were real and were fixed by the Mastermind in the foreign-lane `tests/Sanad.UnitTests/Notifications/**` files (`xUnit2031`: four `Assert.Single(x.Where(...))` sites rewritten to `Assert.Single(x, pred)`).
- Focused tests: `FullyQualifiedName~Cms` **172/172 passed**, `FullyQualifiedName~FamilyHome` **73/73 passed**.
- Local Development migration application was authorized to unblock the fail-closed target verification (the same precedent as F5/F7): `App__TestUserSeed__Enabled=false`, `dotnet ef dbcontext info --context CmsDbContext` resolved `SanadDb` (not `SanadBrunoTestDb`), exactly one pending migration `20261021120000_AddFamilyHomeSections`, applied with `dotnet ef database update`, then confirmed applied and `cms.family_home_sections` / `cms.family_home_section_history` present with `items_json varchar(40000)`.
- `tools/Invoke-E05LocalHttpVerification.ps1 -BaseUrl http://localhost:5253` → **exit 0, 73/73 checks passed** (71 real authenticated HTTP checks + 2 SQL readbacks), `phase=http-checks`, `apiStarted=true`, against persistent `SanadDb`.

Mastermind corrections applied to the adapter itself (adapter defects, not product defects):

1. In-plan references: the second-twin and deleted-twin checks addressed a section id that only stage three produces, so the DELETE/GET/publish paths were built with an empty id and cascaded into 15 failures (`405`, `404`, the `409 StaleVersion` on the second twin, and the reorder/superseded assertions). Added `Resolve-E05PlanReference` plus `{checkName.property}` substitution in `Invoke-E05PlannedChecks`, with the reference split at its **last** dot because check names themselves contain dots.
2. Stage four split out: the visibility and reorder checks address the second published twin, whose id stage three only produces while it runs, and their Family-read assertions embed that id at plan-build time. They now run as `New-E05CheckPlanStageFour`, invoked after `$state.TwinTwoId` is known.

No product behaviour was changed by either correction. Documented limitations carried into the phase record: each successful adapter run appends the sections it creates and publishes through the real routes; the malformed-stored-JSON read is unit-only; `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` and `SourceId` are deliberate additions; the `items_json` cap (40,000 characters) is tighter than ten maximal items.

Release state: **`not_requested`**. Explicit release authorization is still required for commit/push/deploy; Production still has the `20261021120000_AddFamilyHomeSections` migration pending and is expected to apply exactly that one Cms migration (the eight other module heads unchanged) in the E05 release.

