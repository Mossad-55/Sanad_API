# Phase 2 report — admin CRUD + lifecycle + media (+migration)

Branch: `opencode/landing-cms`
Base: `main @ b0ac0d0`
Prior: `bb9f0ec` (Phase 1 closeout)
Final: `0bb1199` (implementation; report committed separately after)

Owner decisions applied (2026-10-12):
- PUT on published id auto-creates/updates a draft twin (`sourceId` link).
- Twin publish archives previous published rows of the same type
  (`Archived` status added; history retained; public reads ignore archived).
- Reorder `{items:[{id,displayOrder}]}`, 400 on dup/unknown; admin list with
  status/type/search filters + paging.
- Media `201 {key}`, prefix `landing`, jpeg/png/webp signature checks, 5 MB.
- Per-run approval granted for the live admin cycle on disposable
  `localhost:5432/SanadBrunoTestDb` (migration apply + disposable CRUD +
  cleanup + revoke + stop).

## Changed files

New:
- `Landing/LandingAdminCommands.cs` (create/update + twin, publish +
  supersede-archive, visibility, delete-draft-only, reorder),
  `LandingAdminQueries.cs` (filtered paged admin list),
  `LandingAdminResponses.cs`, `LandingErrors.cs`, `LandingTypeParser.cs`
  (kebab/enum keys; stats never accepted), `LandingAdminValidators.cs`.
- `AdminLandingController.cs` (`api/v1/admin/landing`, `CmsContent`, 8 routes).
- `Migrations/20261012061755_AddLandingSections.cs` (+ Designer; snapshot
  updated). Authored via `dotnet ef migrations add`; applied ONLY to the
  disposable target during the approved live gate, never elsewhere.
- `tests/.../Cms/LandingAdminTests.cs` (9 tests).
- `docs/admin/landing.md` (endpoints 4–11, lifecycle, twin/supersede rules).
- This report.

Edited:
- Domain: `Archived` status, `SourceId`, `UpdateDraft`, `Archive`;
  per-type asset rules kept at publish-validation (handler) level, not in
  `Publish()`, so direct domain publishes stay stable.
- EF config (+ `source_id`), error mapper (+6 Landing codes),
  Admin Postman collection (+ "20. Landing CMS", 8 requests), `tasks.md`.

## Gates

- Build: `dotnet build Sanad.slnx --no-restore -m:1 --nologo
  /p:TreatWarningsAsErrors=true` → 0 warnings, 0 errors.
- Focused tests (LandingReads + LandingAdmin): 17 passed, 0 failed,
  0 skipped. InMemory + SQLite twins for new queries and the admin
  command flow (create→publish→twin→supersede→delete-guard→reorder).
- `git diff --check`: clean. Postman JSONs parse. Contract-map: 589
  requests, 0 missing / 0 orphan Postman (all 8 admin routes mapped;
  Bruno missing is pre-existing and out of scope).
- Live gate (approved, disposable DB, port 5235, seeds off, migrations off
  at startup): migration `..._AddLandingSections` applied (history verified —
  it was the single pending Cms migration); admin cycle on disposable
  `about` rows: create-partial 201 Draft; unknown type 400 InvalidType;
  publish-partial 400 Incomplete; PUT 200; publish 200 + public read shows
  row; visibility 204 (public `[]` while hidden); PUT published → 200 twin
  with `sourceId`; twin publish 200, old row Archived; delete
  archived/published → 409 ×2, missing → 404; reorder 204 idempotent,
  dup/unknown → 400 ReorderInvalid; media PNG → 201 key, txt → 400
  UnsupportedType; stats 200 = direct counts (8/1/1/1).
- Cleanup: 2 fixture rows deleted (public list `[]` again); task device
  sessions revoked via scoped SQL (`device_name LIKE 'landing-p2%'` only —
  5 rows after hitting the 5-session cap, 3 rows at close; no proper-API
  logout was possible without a token, and `logout-all` would have revoked
  other users' shared-fixture sessions); API stopped, port free. Residue:
  one uploaded media key file on the disposable host disk (no delete-media
  endpoint; disclosed artifact).

## Notes and deviations

- One transient: the first valid reorder call returned 400; immediate reruns
  return 204 consistently (idempotent), with dup/unknown correctly 400. No
  code change made; recorded as observed, not explained — automated reorder
  tests (InMemory + SQLite) pass deterministically.
- Visibility toggle allowed on any existing row (catalog lists only 404);
  archived delete returns the published-history 409 (history retained).
- No push, no merge, no seeds written, no production contact.
