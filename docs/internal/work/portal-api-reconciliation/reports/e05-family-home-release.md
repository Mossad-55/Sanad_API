# E05 typed Family home CMS sections — release receipt

Date: 2026-10-21 (Africa/Cairo). Clone, publish, backup and cutover ran
`2026-10-21 01:10–01:17` UTC. **Status: deployed and verified.**

## Source and package

- Feature commit: `e2be9ba` "feat(e05): typed Family home CMS sections with
  draft-twin publication". Full release revision:
  `e2be9badaab0db9fa9f012769c696993ec234b05`. It is an ancestor of `origin/main`.
- **The pushed tip was `7a65168` (F8) and was deliberately NOT deployed.** The
  release cloned the repository and checked out `e2be9ba` explicitly
  (`git checkout --detach e2be9ba`); `git rev-parse HEAD` returned
  `e2be9badaab0db9fa9f012769c696993ec234b05` with a clean working tree
  (`0` changes). No local uncommitted tree was used as source, and F8 is left
  for a separate later release.
- Deployed API revision and package manifest revision:
  `e2be9badaab0db9fa9f012769c696993ec234b05`. The publisher derived the revision
  from the detached checkout and wrote `sanad-deployment-manifest.json` with all
  nine module heads. Exactly **one** differs from the prior (E06) manifest:
  - Cms `20261012071340_AddLandingSectionHistory` →
    `20261021120000_AddFamilyHomeSections`.
  - The other eight heads are byte-identical to the E06 manifest.
- Built with `Publish-SanadApi.ps1 -Configuration Release` from a clean detached
  clone (`deploy/releases/e05-e2be9ba/source`) → `deploy/publish-out`; the
  publisher compiled the API as part of `dotnet publish`
  (`/p:TreatWarningsAsErrors=true`), **exit 0 with zero warnings/errors**. No
  second solution build was run.
- Archive: `sanad-api-e05-e2be9ba.tar.gz`, **12,031,854 bytes**, **118 entries**;
  SHA-256
  `fb1a3dffa123c64d2a08c85e179358e883c7ede388033d5fb179e6e3fb724047`.
  The transferred archive hash matched the build-machine hash before use
  (`sha256sum` on the host). The staged publish output is 110 files;
  `appsettings.json` (151 bytes) was excluded from the archive exactly as in
  F5/F6/F7/E06, so the host settings could not be clobbered. `.env`, local
  settings, keys and certificates are not in the package.

## Migration this release (one, additive only)

- **One** migration applies: Cms `20261021120000_AddFamilyHomeSections`. Its
  `Up` was read directly from the deployed revision before applying; it is
  additive only:
  - `CreateTable cms.family_home_sections` (PK `id`; `kind`, `items_json`,
    `display_order`, `is_visible`, `source_id`, `status`, `version`,
    `created_on_utc`, `updated_on_utc`, `published_on_utc`).
  - `CreateTable cms.family_home_section_history` (PK `id`; `section_id`,
    `action`, `actor_user_id`, `details`, `created_on_utc`).
  - `CreateIndex IX_family_home_sections_status_display_order`
    (`status`, `display_order`).
  - `CreateIndex IX_family_home_section_history_section_id_created_on_utc`
    (`section_id`, `created_on_utc`).
  - `Down` only drops the two tables; it was never invoked. No column is
    dropped, narrowed or rewritten, no row is modified, and no existing table is
    altered.
- **Proof exactly one was pending before cutover:** the pre-cutover read of
  `cms."__EFMigrationsHistory"` head was
  `20261012071340_AddLandingSectionHistory` (the prior head), while the deployed
  source's Cms head was `20261021120000_AddFamilyHomeSections`; the other eight
  contexts were already at their required heads. The new tables were `ABSENT`
  (`to_regclass` returned null) before the overlay.
- Applied by the production startup migration policy during the restart (the
  F2–F7/E06 mechanic), not by `dotnet ef database update`. The sanitized startup
  log shows:
  - Cms: `1 pending migration(s)` →
    `Applying migration '20261021120000_AddFamilyHomeSections'` →
    `1 migration(s) applied`.
  - The other eight contexts: `0 pending` → `0 applied` each.
  - The startup log for this restart contains **0** `DROP TABLE`/`DROP COLUMN`/
    `ALTER COLUMN … TYPE`/`TRUNCATE`/`DELETE FROM` statements.

## Target, backup and preflight

- Continued the previously Owner-designated Production VPS `72.62.92.144:8091`,
  service `sanad-api.service`, directory `/root/sanad-api`, environment file
  `/etc/sanad/sanad.env`. `/root/sanad-api` is **not** a git checkout (checked
  before and after: `IS_GIT_CHECKOUT=no`).
- Preflight revalidated the target (not assumed): service
  **`active`/`running`, `NRestarts=0`**, `MainPID=2698308`, started
  `2026-10-20 23:45:02 UTC`; `GET /api/v1/deployment/info` returned HTTP 200 with
  revision `90f02c745c7785b1f0dd87e3003fa4e033cc8ea7` (the prior E06 release),
  environment `Production`, `databaseReachable=true`, 9/9 applied heads
  matching required (Cms at `20261012071340_AddLandingSectionHistory`).
- Only `ConnectionStrings__IdentityDatabase` was used from the environment file,
  parsed **privately on the host** by the backup script into libpq `PG*`
  variables. No environment-file content or value was printed at any point in
  this release; only key names, sizes and hashes were emitted. No connection
  string, user, host, password or database name is recorded here.
- Pre-cutover backup, before any cutover, under
  `/root/sanad-releases/e05-e2be9ba-20261021/` (directory mode `700`):
  - `database-before-e05.dump` — custom-format `pg_dump`, **722,645 bytes**,
    SHA-256
    `2f67a821972c87676f855e90db027678a9902f51866d53eecf2dd53acc806351`;
    validated read-only with `pg_restore --list` → exit 0, **790 TOC entries**
    (`database-before-e05.list`, 60,175 bytes).
  - `app-backup-before-e05.tar.gz` — current application directory,
    **11,994,032 bytes**, **120 entries**, SHA-256
    `454fe926b888ae1351223d3e58456b2a73e378e348d74f26999aa45e74cae450`.
  - `host-config/` — private copies of the service unit and environment file
    (files mode `600`).
  - No backup was restored.

## Deployment and verification

- Only the hash-verified archive was transferred and overlaid into
  `/root/sanad-api`. Stop → overlay → start; the overlay is
  `tar -xzf sanad-api-e05-e2be9ba.tar.gz -C /root/sanad-api`. The host
  `appsettings*.json`, service unit, environment file, storage and key-ring
  configuration were preserved: SHA-256 fingerprints before and after cutover
  are identical (`diff` clean) —
  `appsettings.json` `200332b4…`, `appsettings.Development.json` `e92cd3f4…`,
  the service unit `c018192e…` and the environment file `e5b4ec5e…`.
- The deployment endpoint answered HTTP 200 **14 s** after `start`.
- **9/9 migration heads, read two independent ways:**
  1. `GET /api/v1/deployment/info` (HTTP 200) — `appliedMigrations` equals
     `requiredMigrations` for all nine contexts, `environment` `Production`,
     `databaseReachable` true, `manifestRevision`
     `e2be9badaab0db9fa9f012769c696993ec234b05`.
  2. Direct read of each context's `__EFMigrationsHistory` (`"MigrationId"`,
     ordered desc, limit 1) in the Production database, after cutover:
     - Identity `20261020191733_AddSupportTicketWorkflow`
     - Cms `20261021120000_AddFamilyHomeSections`
     - Caregivers `20261020210727_AddCaregiverRatingModeration`
     - Families `20261018170707_AddCaregiverMedicalAccessAudit`
     - CareHomes `20261020210910_AddCareHomeRatingModeration`
     - Notifications `20261010010618_AddEmailHtmlSnapshots`
     - Community `20261017154400_AddCommunityModeration`
     - Finance `20261019061622_AddE04PlatformControls`
     - Chat `20261013171755_AddAttachmentCleanupIndex`
     Only Cms moved, and it moved by exactly the one E05 migration. The other
     eight are byte-identical to their pre-release heads.
- Standard `Verify-SanadDeployment.ps1` passed (**exit 0**) against
  `http://72.62.92.144:8091` with the local manifest, expected revision
  `e2be9badaab0db9fa9f012769c696993ec234b05` (full commit id, which the script
  compares exactly) and the sanitized startup log:
  - Remote revision exactly matched the deployed revision.
  - Environment `Production`; databases reachable.
  - All **9/9** applied migration heads matched the manifest.
  - Startup evidence covered all nine contexts, 1 applied (Cms) + 8 with 0.
  - Anonymous `GET /api/v1/lookups/services` returned HTTP 200.
- Service state after cutover and at close:
  **`active`/`running`, `NRestarts=0`**, `MainPID=2895360`, started
  `2026-10-21 01:13:43 UTC`.

## E05 live surface (OpenAPI + anonymous smokes)

- OpenAPI path count **before** cutover (E06 build): **509 paths**,
  `1,400,396 bytes` on the host loopback.
- OpenAPI path count **after** cutover (E05 build), HTTP 200:
  - Public `http://72.62.92.144:8091/openapi/v1.json` → **1,423,820 bytes**,
    **515 paths** (`openapi-live-e05-public.json`).
  - Host loopback `http://127.0.0.1:8091/openapi/v1.json` → **1,423,817 bytes**,
    **515 paths** (`openapi-live-loopback.json`).
  - **Delta: +6 paths**, exactly the six new E05 paths; no other path was added
    or removed.
- The six new paths and their published operations:
  - `/api/v1/admin/family-home/sections` [`get`, `post`]
  - `/api/v1/admin/family-home/sections/{id}` [`delete`, `get`, `put`]
  - `/api/v1/admin/family-home/sections/{id}/publish` [`post`]
  - `/api/v1/admin/family-home/sections/{id}/visibility` [`post`]
  - `/api/v1/admin/family-home/sections/reorder` [`post`]
  - `/api/v1/family/home/sections` [`get`]
- Anonymous runtime smokes with **no token and no credentials** — every E05
  route/method returned **`401`** on both the public address and host loopback
  (POST/PUT sent an empty JSON body and a well-formed GUID in the path; no
  credentials, no data created):
  - `GET /api/v1/admin/family-home/sections` → **401**
  - `POST /api/v1/admin/family-home/sections` → **401**
  - `GET /api/v1/admin/family-home/sections/{id}` → **401**
  - `PUT /api/v1/admin/family-home/sections/{id}` → **401**
  - `DELETE /api/v1/admin/family-home/sections/{id}` → **401**
  - `POST /api/v1/admin/family-home/sections/{id}/publish` → **401**
  - `POST /api/v1/admin/family-home/sections/{id}/visibility` → **401**
  - `POST /api/v1/admin/family-home/sections/reorder` → **401**
  - `GET /api/v1/family/home/sections` → **401**
  - Controls (both surfaces): `GET /api/v1/lookups/services` → 200,
    `GET /api/v1/deployment/info` → 200.
- **No authenticated Production request, mutation, provider call, fixture write
  or OTP was performed** (the F2–F7/E06 precedent). Authorization, payload,
  draft-twin publication and reorder semantics rest on the accepted focused
  tests and the live OpenAPI/401 smokes.

## Production data safety (before → after)

Read-only `psql` before cutover and after the migration applied on the same
Production database:

| Object | Before | After |
|---|---|---|
| `identity.users` | 15 | **15** |
| `cms.landing_sections` | 0 | **0** |
| `identity."__EFMigrationsHistory"` (rows) | 15 | **15** |
| `cms."__EFMigrationsHistory"` (rows) | 10 | **11** |
| `cms.family_home_sections` | absent | **present, 0 rows** |
| `cms.family_home_section_history` | absent | **present, 0 rows** |

- The only count change is `cms."__EFMigrationsHistory"` 10 → 11, exactly the
  one applied E05 migration row. All pre-existing row counts are identical, and
  the two new tables went from absent to present and empty.
- No `DROP`, `TRUNCATE`, `DELETE`, narrowing `ALTER`, backfill or repair ran; the
  migration is additive only. No backup was restored. No fixture or other data
  was written to Production.

## Gate evidence carried into the release

- Mastermind acceptance of E05 (recorded in the phase brief) is the gate
  evidence for payload/publication/reorder semantics and focused tests; it was
  not re-run here and is not claimed to have been re-run. This release verified
  the build/publish, the single additive migration, 9/9 heads, the OpenAPI
  surface and the anonymous 401 protection.

## Limitations and open items

- **F8 is not deployed.** The pushed tip `7a65168` (F8) was explicitly excluded;
  it must ship in its own separate release. Production now reports E05's
  `e2be9ba` revision, and F8's changes are not present.
- **Deploy mechanic.** Deployment used the recorded build-machine mechanic
  (publish from a clean detached clone → hash-verified archive → `scp` →
  overlay → restart), not a VPS git clone/pull: `/root/sanad-api` is not a git
  checkout.
- **Migration mechanic.** The single migration was applied by the production
  startup migration policy during the restart, not by an explicit
  `dotnet ef database update`; no `dotnet ef` command was issued against
  Production.
- **Verifier revision form.** `Verify-SanadDeployment.ps1` compares the expected
  revision with exact string equality, so it was passed the full 40-character id.
- **HTTPS surface.** `:8091` serves plain HTTP only (no HTTPS on that port), so
  the public smokes used `http://72.62.92.144:8091` and the loopback smokes ran
  on the host against `http://127.0.0.1:8091`. No HTTPS surface was exercised.
- **Health endpoint.** `/health` is not a published route; health/status
  evidence is `GET /api/v1/deployment/info` (200) and
  `GET /api/v1/lookups/services` (200), both verified.
- The live OpenAPI document publishes no `security`/`securitySchemes` metadata,
  so anonymous-vs-signed-in is proven by the runtime 401/200 smokes, not by the
  document.
- **No authenticated Production requests** were made; the whole-unit suite,
  Bruno and live provider flows were not run and are not claimed.
- No other service was started or restarted; E05 is the only release in this run.

## Command / exit-code log (non-secret)

- `git ls-remote origin refs/heads/main` → exit 0; tip `7a65168…` (F8, excluded);
  `git rev-parse e2be9ba` → `e2be9badaab0db9fa9f012769c696993ec234b05`;
  `git merge-base --is-ancestor e2be9ba origin/main` → exit 0.
- `git clone --no-hardlinks` + `git checkout --detach e2be9ba` → exit 0; HEAD
  `e2be9ba…`, working tree clean (0 changes).
- `pwsh docs/internal/tools/Publish-SanadApi.ps1 -Configuration Release` →
  exit 0, 0 warnings/0 errors, manifest revision `e2be9ba…`, Cms head
  `20261021120000_AddFamilyHomeSections`, other eight unchanged.
- `tar -czf` (excluding `appsettings*.json`) → exit 0, 118 entries, 12,031,854
  bytes, SHA-256 `fb1a3dff…`.
- `scp` archive → exit 0; host `sha256sum` matched `fb1a3dff…` before use.
- Pre-cutover snapshot (nine heads + counts + `to_regclass`) → exit 0; Cms at
  `20261012071340…`, new tables `ABSENT`.
- Pre-cutover backup script (dump + `pg_restore --list` + app tar + host-config)
  → exit 0 (dump 722,645 B / 790 TOC entries; app tar 11,994,032 B / 120
  entries).
- Cutover script (stop → overlay → start → poll; fingerprint diff) → exit 0;
  deployment info 200 after 14 s; fingerprints clean.
- Startup log capture + sanitize → exit 0; 9 contexts, 1 applied (Cms), 0
  destructive statements.
- `Verify-SanadDeployment.ps1` → exit 0 (9/9 heads, revision match, Production,
  nine startup contexts, smoke 200).
- OpenAPI fetch (public and loopback) → HTTP 200; 515 paths each (+6).
- Anonymous smoke (public, `curl`) → exit 0 (9×401, controls 200).
- Anonymous smoke (host loopback, `bash`/`curl`) → exit 0 (9×401, controls 200).
- Post-cutover snapshot + new-table counts → exit 0 (identical pre-existing
  counts; new tables present, 0 rows).
- Final state snapshot → exit 0 (`active`, `NRestarts=0`, `MainPID=2895360`,
  not a git checkout).
- Nothing failed; no step was improvised or repeated.
