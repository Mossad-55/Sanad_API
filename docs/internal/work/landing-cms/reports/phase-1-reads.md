# Phase 1 report — section registry + public reads + stats

Branch: `opencode/landing-cms`
Base: `main @ b0ac0d0` (`b0ac0d040adb314e0bfaa140becc14dd00d7cd31`)
Package commit: `90bb9d7` (frozen execution package port)
Final: `4a7b75f6398e12c3625666caed2db2cf358e5bfa` (implementation; report committed separately after)

Owner decisions applied (2026-10-12, via execution prompt):
- Registry = 12 keys including virtual `stats` (resolves plan.md "10 vs 12" conflict).
- Endpoint 2 (token-gated preview) deferred to Phase 2 → Phase 1 ships
  `GET /api/v1/landing/sections` + `GET /api/v1/landing/stats` only.
- Stats predicates: families `DeletedOnUtc IS NULL`; caregivers
  `Status == Active` by `Type`; facilities `Status == Approved (5)`.
- Virtual `stats` key excluded from the sections list.

## Changed files

New:
- `src/Modules/Cms/Domain/Sanad.Modules.Cms.Domain/Landing/`
  (`LandingSection.cs`, `LandingSectionType.cs` (12 keys),
  `LandingSectionStatus.cs` (Draft/Published), `LandingSectionId.cs`).
  Drafts may be partial; `Publish()` requires bilingual titles; the virtual
  `Stats` type cannot be constructed as a row.
- `src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Landing/`
  (`GetPublishedLandingSectionsQuery[Handler]` — published + visible,
  virtual excluded, `displayOrder` ordering; `GetLandingStatsQuery[Handler]` —
  four live counts; responses + kebab-case type-key mappings).
- `src/API/Sanad.API/Controllers/LandingController.cs`
  (`api/v1/landing`, `[AllowAnonymous]` `GET sections`, `GET stats`).
- `src/Modules/Cms/Infrastructure/.../Persistence/Configurations/LandingSectionConfiguration.cs`
  (`cms.landing_sections`; no migration authored — Phase 2 owns it per plan §4).
- `tests/Sanad.UnitTests/Cms/LandingReadsTests.cs` (8 tests).
- `docs/app/public/landing.md` (reads section only; preview marked deferred).
- This report.

Edited:
- `ICmsDbContext.cs` / `CmsDbContext.cs` (+ `LandingSections` set each).
- `Sanad.Modules.Cms.Application.csproj` (+ Caregivers/CareHomes
  Application + Domain references; Families precedent).
- `docs/postman/app/Sanad.App.Public.postman_collection.json`
  (+ "04. Landing": sections + stats; preview omitted per deferral).

## Gates

- Build: `dotnet build Sanad.slnx --no-restore -m:1 --nologo
  /p:TreatWarningsAsErrors=true` → succeeded, 0 warnings, 0 errors.
  (One-time `dotnet restore` first; fresh worktree had no restored obj.)
- Focused tests: `dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj
  /p:TreatWarningsAsErrors=true --filter FullyQualifiedName~LandingReadsTests`
  → 8 passed, 0 failed, 0 skipped. InMemory + SQLite twins for BOTH new
  queries. SQLite stats twin uses onboarding caregiver rows: Active rows carry
  schedule windows whose ids are database-generated on Npgsql and violate
  NOT NULL on SQLite (pre-existing Caregivers mapping gap, out of scope);
  Active-row semantics are proven InMemory. Same-query translation is proven
  on SQLite.
- `git diff --check` (tree + cached): clean.
- Postman JSON: parses (`ConvertFrom-Json`); contract-map
  (`docs/internal/tools/Verify-ApiContractMapping.ps1`): Postman requests 581,
  Missing Postman 0, Orphan Postman 0, Orphan Bruno 0. Both new routes mapped.
  (Pre-existing repo-wide "MISSING BRUNO" entries remain, including the 2 new
  routes — Bruno is not Phase 1 scope.)
- Live API (localhost:5432/SanadBrunoTestDb, port 5235, read-only startup:
  `Database__ApplyMigrationsOnStartup=false`, seeds off, no sessions created):
  - `GET /api/v1/landing/stats` → 200
    `{"familiesCount":8,"medicalCaregiversCount":1,"companionCaregiversCount":1,
    "approvedFacilitiesCount":1,"totalCaregiversCount":2}` — EQUAL to direct
    read-only DB counts (8/1/1/1). PASS.
  - `GET /api/v1/landing/sections` → 500. Cause evidenced: `cms.landing_sections`
    does not exist (direct check `to_regclass = false`); no migration applies
    per constraints and Phase 2 authors the migration. LIMITATION, not a pass.
  - Preview 401, seeded-row sections: UNRUN (preview deferred; seeded rows need
    per-run mutating approval + the Phase 2 migration). LIMITATIONS, never passes.
  - API stopped after checks; port 5235 free; no fixture sessions to revoke
    (anonymous reads only).

## Deviations

Owner-directed scope adjustments (not policy inventions):
- 2 public routes shipped, not 3 (preview deferred to Phase 2).
- Persistence support files (`CmsDbContext`/`ICmsDbContext` sets, EF config,
  4 project references) added although absent from the Phase 1 owned-files
  bullet — mechanically required for the read queries; no admin/lifecycle code.
- Workspace recovery: primary checkout was switched externally to
  `opencode/admin-users` mid-task; work continued in approved separate worktree
  (`.../Temp/opencode/ltree`) at the same base; admin branch untouched.

No push, no merge, no migration applied, no seeds written.
