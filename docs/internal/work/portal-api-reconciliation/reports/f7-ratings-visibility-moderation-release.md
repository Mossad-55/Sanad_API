# F7 ratings visibility and moderation — release receipt

Date: 2026-10-20/21 UTC (`2026-10-20 22:53–23:00` UTC; Africa/Cairo
`2026-10-21 00:53–01:00`). **Status: deployed and verified.**

## Source and package

- Feature commit: `d6d950c3ccb4ec8fab66c0ef88e708bb75a8aaa8` "feat(f7): ratings
  visibility and moderation" (51 files). It was already pushed; `origin/main`
  tip was exactly `d6d950c` (`git ls-remote origin refs/heads/main` →
  `d6d950c3ccb4ec8fab66c0ef88e708bb75a8aaa8`) and the release packaged that
  exact tip.
- Deployed API revision and package manifest revision:
  `d6d950c3ccb4ec8fab66c0ef88e708bb75a8aaa8`. The publisher derived the revision
  from the clean checkout of pushed `main` HEAD and wrote
  `sanad-deployment-manifest.json` with all nine module heads, of which exactly
  two differ from the prior (F6) manifest:
  - Caregivers `20261009004058_AddCaregiverPayoutAccountReview` →
    `20261020210727_AddCaregiverRatingModeration`.
  - CareHomes `20261012143753_AddCareHomeRoomTypeAmenitiesAndMedia` →
    `20261020210910_AddCareHomeRatingModeration`.
  - The other seven heads are byte-identical to the F6 manifest.
- Archive: `sanad-api-f7-d6d950c.tar.gz`, **11,987,479 bytes**, **118 entries**;
  SHA-256
  `b8bc5aa0516df75a1410eb6f97b4729b45355b3dc9c9c01aae37552c4f971930`.
  The transferred archive hash matched the build-machine hash before use.
  `appsettings*.json`, `.env`, local settings, keys and certificates are **not**
  in the package (the staged entry set is the F6 package plus zero entries;
  the `appsettings.json` that `dotnet publish` stages was excluded from the
  archive exactly as in F5/F6, so the host settings could not be clobbered).
- Built with `Publish-SanadApi.ps1 -Configuration Release` from a clean detached
  clone (`deploy/releases/f7-d6d950c/source`, `git clone --no-hardlinks` of the
  pushed revision, working tree clean at `d6d950c`) → `deploy/publish-out`; the
  publisher compiled the API as part of `dotnet publish`
  (`/p:TreatWarningsAsErrors=true`), exit 0 with no warnings. No second solution
  build was run.

## Migrations this release (two, additive only)

F7 is the first release since F5 to change schema, and it carries **two**
migrations. Both are additive: the only operations are `AddColumn` (every new
column nullable or `NOT NULL` with an explicit default `0`), `CreateTable` and
`CreateIndex`. No column is dropped, narrowed or rewritten, no row is modified,
and every `NOT NULL` addition carried a default, so it applied to the existing
rows.

- Caregivers `20261020210727_AddCaregiverRatingModeration` — adds
  `moderation_state` (integer, `NOT NULL` default `0`), `hidden_by_user_id`
  (uuid, null), `hidden_on_utc` (timestamptz, null) and `hide_reason`
  (varchar(500), null) to `caregivers.caregiver_ratings`; adds index
  `IX_caregiver_ratings_caregiver_id_moderation_state`; creates the append-only
  `caregivers.caregiver_rating_moderation_actions` table.
- CareHomes `20261020210910_AddCareHomeRatingModeration` — the same four columns
  on `care_homes.ratings`; adds index `IX_ratings_care_home_id_moderation_state`;
  creates the append-only `care_homes.rating_moderation_actions` table; creates
  `care_homes.rating_flags` with the **filtered unique index**
  `IX_rating_flags_rating_id_owner_user_id` on (`rating_id`, `owner_user_id`)
  `WHERE (status = 0)`.
- Both migrations in `Up` were read directly from the deployed revision before
  applying; the `Down` methods (drops only) were never invoked.

## Target, backup and preflight

- Continued the previously Owner-designated Production VPS `72.62.92.144:8091`,
  service `sanad-api.service`, directory `/root/sanad-api`, environment file
  `/etc/sanad/sanad.env`. `/root/sanad-api` is **not** a git checkout.
- Before cutover the service was `active`/`running` with `NRestarts=0` and the
  deployment endpoint reported revision
  `bc32be0664d0f7d42c7c0406d63c73c0edae5a98` (the prior F6 release),
  environment `Production`, databases reachable — the authorized target was
  revalidated, not assumed. `/etc/sanad/sanad.env` fingerprint was
  `e5b4ec5e…`, matching the F5/F6 receipts.
- Only `ConnectionStrings__IdentityDatabase` is configured, so all nine
  contexts resolve to the same effective Production database. That identity was
  resolved **privately on the host** by the backup script (parsed from the
  environment file into `PG*` variables); no connection string, user, host,
  password or database name is recorded here.
- **Database backup first**, before any cutover, because this release runs
  schema changes, under `/root/sanad-releases/f7-d6d950c-20261020/`
  (directory mode `700`; the directory name uses the UTC date `20261020`):
  - `database-before-f7.dump` — custom-format `pg_dump`, **714,227 bytes**,
    SHA-256
    `a2d83323da3e92dc0d0d68c0f03f53b69a8b5a85943746f4afc1e4e403bc69d7`;
    validated read-only with `pg_restore --list` → exit 0, **757 TOC entries**
    (`database-before-f7.list`), including the `TABLE DATA` entries for
    `caregivers caregiver_ratings` and `care_homes ratings`.
  - `app-backup-before-f7.tar.gz` — current application directory,
    **11,941,254 bytes**, **120 entries**, SHA-256
    `40e804eb7781cf2aca241c409870d7f99f2b3c83c9667d3fd4790a615fa3c97b`.
  - `host-config/` — private copies of the service unit and environment file
    (files mode `600`).
  - No backup was restored; no Production data was reset, dropped, deleted or
    repaired.

## Deployment and verification

- Only the hash-verified archive was transferred and overlaid into
  `/root/sanad-api`. Stop → overlay → start; the overlay is
  `tar -xzf sanad-api-f7-d6d950c.tar.gz -C /root/sanad-api`. The host
  `appsettings*.json`, service unit, environment file, storage and key-ring
  configuration were preserved: SHA-256 fingerprints before and after cutover
  are identical (`diff` clean) —
  `appsettings.json` `200332b4…`, `appsettings.Development.json` `e92cd3f4…`,
  the service unit `c018192e…` and the environment file `e5b4ec5e…`.
- **Migration applied by the production startup migration policy during the
  restart** (the F2/F3/F4/F5/E04 mechanic), not by `dotnet ef database update`.
  Production startup checked all nine contexts and applied exactly **two**:
  - Caregivers `20261020210727_AddCaregiverRatingModeration` — **1** applied
    (log: `1 pending migration(s)` → `1 migration(s) applied`).
  - CareHomes `20261020210910_AddCareHomeRatingModeration` — **1** applied
    (log: `1 pending migration(s)` → `1 migration(s) applied`).
  - Identity, Cms, Families, Notifications, Community, Finance, Chat — **0**
    applied each (`0 pending migration(s)` → `0 migration(s) applied`).
  - The startup log for this restart contains **0** `DROP TABLE`/`DROP COLUMN`/
    `ALTER COLUMN … TYPE`/`TRUNCATE`/`DELETE FROM` statements.
- **9/9 migration heads, read two independent ways:**
  1. `GET /api/v1/deployment/info` (HTTP 200) — `appliedMigrations` equals
     `requiredMigrations` for all nine contexts (this is the host's own
     migration history reflection).
  2. Direct read of each context's `__EFMigrationsHistory` (`"MigrationId"`,
     ordered desc, limit 1) in the Production database:
     - Identity `20261020191733_AddSupportTicketWorkflow`
     - Cms `20261012071340_AddLandingSectionHistory`
     - Caregivers `20261020210727_AddCaregiverRatingModeration`
     - Families `20261018170707_AddCaregiverMedicalAccessAudit`
     - CareHomes `20261020210910_AddCareHomeRatingModeration`
     - Notifications `20261010010618_AddEmailHtmlSnapshots`
     - Community `20261017154400_AddCommunityModeration`
     - Finance `20261019061622_AddE04PlatformControls`
     - Chat `20261013171755_AddAttachmentCleanupIndex`
  The other seven are exactly their pre-release heads; only Caregivers and
  CareHomes moved, and each moved by exactly the one F7 migration. The two F7
  rows are present in their contexts' history with the exact ids above.
- Post-restart schema confirmation on the Production database (all 8/8 new
  columns present, all nullability/defaults as authored): the four new columns
  exist on both `caregivers.caregiver_ratings` and `care_homes.ratings` —
  `moderation_state` (`integer`, `NO` null, default `0`), `hidden_by_user_id`
  (`uuid`, null), `hidden_on_utc` (`timestamp with time zone`, null),
  `hide_reason` (`character varying`, null). All three new tables exist
  (`caregivers.caregiver_rating_moderation_actions`,
  `care_homes.rating_moderation_actions`, `care_homes.rating_flags`), each with
  its PK and its new indexes. The filtered unique index is exactly
  `CREATE UNIQUE INDEX "IX_rating_flags_rating_id_owner_user_id" ON
  care_homes.rating_flags USING btree (rating_id, owner_user_id) WHERE (status = 0)`.
- Service state after cutover and at close:
  **`active`/`running`, `NRestarts=0`**, `MainPID=2588244`, started
  `2026-10-20 22:55:44 UTC`. The deployment endpoint first answered HTTP 200
  12 s after `start`.
- Standard `Verify-SanadDeployment.ps1` passed (**exit 0**) against
  `http://72.62.92.144:8091` with the local manifest, expected revision
  `d6d950c` and the sanitized startup log:
  - Remote revision exactly matched
    `d6d950c3ccb4ec8fab66c0ef88e708bb75a8aaa8`.
  - Environment `Production`; databases reachable.
  - All **9/9** applied migration heads matched the manifest.
  - Startup evidence covered all nine contexts with the counts above.
  - Anonymous `GET /api/v1/lookups/services` returned HTTP 200.

## F7 live surface (OpenAPI + anonymous smokes)

Read from the live document at **`GET /openapi/v1.json`**, HTTP 200, on the
deployed revision:

- Public address `http://72.62.92.144:8091/openapi/v1.json` → **1,396,624
  bytes**; host loopback `http://127.0.0.1:8091/openapi/v1.json` → **1,396,621
  bytes**; **508 paths** in both. (`openapi-live-public.json` /
  `openapi-live-loopback.json`.)
- All F7 routes are published with the expected methods. The brief's prose calls
  this "eight" routes, but the controllers publish **nine** concrete paths
  (three admin-caregiver, four admin-care-home, two owner) and I verified all
  nine:
  - `GET  /api/v1/admin/caregivers/ratings`
  - `POST /api/v1/admin/caregivers/ratings/{ratingId}/hide`
  - `POST /api/v1/admin/caregivers/ratings/{ratingId}/restore`
  - `GET  /api/v1/admin/care-homes/ratings`
  - `POST /api/v1/admin/care-homes/ratings/{ratingId}/hide`
  - `POST /api/v1/admin/care-homes/ratings/{ratingId}/keep`
  - `POST /api/v1/admin/care-homes/ratings/{ratingId}/restore`
  - `GET  /api/v1/care-homes/facilities/mine/ratings`
  - `POST /api/v1/care-homes/facilities/mine/ratings/{ratingId}/flag`
- Anonymous runtime smokes with no token — **all nine returned `401` on both the
  host loopback and the public address** (POSTs sent with an empty JSON body and
  a well-formed GUID in the path):
  - `GET  /api/v1/admin/caregivers/ratings` → **401** (loopback and public)
  - `POST /api/v1/admin/caregivers/ratings/{id}/hide` → **401**
  - `POST /api/v1/admin/caregivers/ratings/{id}/restore` → **401**
  - `GET  /api/v1/admin/care-homes/ratings` → **401**
  - `POST /api/v1/admin/care-homes/ratings/{id}/hide` → **401**
  - `POST /api/v1/admin/care-homes/ratings/{id}/keep` → **401**
  - `POST /api/v1/admin/care-homes/ratings/{id}/restore` → **401**
  - `GET  /api/v1/care-homes/facilities/mine/ratings` → **401**
  - `POST /api/v1/care-homes/facilities/mine/ratings/{id}/flag` → **401**
  - Controls (both surfaces): `GET /api/v1/lookups/services` → 200,
    `GET /api/v1/platform/status` → 200, `GET /api/v1/deployment/info` → 200.
- **No authenticated Production request, mutation, provider call, fixture write
  or OTP was performed** (the same precedent as F2/F3/F4/F5/F6). Authorization,
  payload, hidden/restore aggregate semantics and the 400/404/409 paths rest on
  the accepted focused tests and the live OpenAPI/401 smokes.

## Production data safety (no reset / drop / delete / repair)

- Rating row counts, observed before (from the pre-release database) and after
  (from the same database once the F7 migrations had applied) — **identical**:
  - `care_homes.ratings`: **1 → 1**.
  - `caregivers.caregiver_ratings`: **0 → 0**.
- The single pre-existing Care Home rating is byte-for-byte unchanged: the
  pre-release dump's `COPY care_homes.ratings …` row and the post-release read
  agree on `id` `bde1ab01-6951-4e6d-b45f-8bf61705b781`, `stars` `5`,
  `created_on_utc` `2026-10-19 19:28:23.262117+00` and `updated_on_utc`
  `2026-10-19 19:35:02.476384+00`; its only change is the additive
  `moderation_state = 0` default. `caregivers.caregiver_ratings` was empty
  before and after.
- The three new moderation tables are empty (`0` rows each) as expected; no
  moderation action, flag or visibility change was performed on Production.
- No `DROP`, `TRUNCATE`, `DELETE`, narrowing `ALTER`, backfill or repair ran in
  this release; the two migrations are additive only (verified in source and in
  the apply log). No backup was restored.

## Gate evidence carried into the release

- Mastermind acceptance (recorded in the F7 brief and independently re-run on
  the frozen tree): strict Release build 0 warnings/0 errors; 104/104 focused F7
  tests; `tools/Invoke-F7LocalHttpVerification.ps1` exit 0 at 179/179 real
  localhost checks; both migrations verified applied to the local `SanadDb` with
  the other seven contexts unchanged. Reused unchanged; not re-run here.

## Deviations and limitations

- **Route count.** The release order says "eight" F7 routes; the three F7
  controllers publish **nine** concrete paths. I verified all nine (see above);
  no route is missing and none is unexpected.
- **Deploy mechanic.** Deployment used the recorded build-machine mechanic
  (publish from a clean clone → hash-verified archive → `scp` → overlay →
  restart), not a VPS git clone/pull: `/root/sanad-api` is not a git checkout,
  and this matches the F2/F3/F4/F5/F6 receipts and E04.
- **Migration mechanic.** Both migrations were applied by the production startup
  migration policy during the restart, not by an explicit
  `dotnet ef database update`; no `dotnet ef` command was issued against
  Production. The Caregivers migration ran and completed before the CareHomes
  migration, and both completed before the app accepted traffic.
- **`appsettings.json` exclusion.** `dotnet publish` stages
  `appsettings.json`; it was deliberately excluded from the archive (as in
  F5/F6) so the overlay could not overwrite the host settings, whose
  fingerprint is preserved. This makes the F7 archive one entry smaller than a
  naive `tar` of the publish output.
- **Public HTTP.** The host answers plain HTTP on `:8091` (HTTPS is not served
  on that port), so both the standard verification and the public smokes used
  `http://72.62.92.144:8091`; the loopback smokes ran on the host against
  `http://127.0.0.1:8091`. No SSH tunnel was needed this release.
- **Health endpoint.** `/health` is not a published route; health/status
  evidence is `GET /api/v1/deployment/info` (200), `GET /api/v1/lookups/services`
  (200) and `GET /api/v1/platform/status` (200).
- The live OpenAPI document publishes no `security`/`securitySchemes` metadata,
  so the anonymous-vs-signed-in distinction is proven by the runtime 401/200
  smokes, not by the document.
- **No authenticated Production requests** were made; the whole-unit suite,
  Bruno and live provider flows were not run and are not claimed.
- No other phase's deployment was started; F7 is the only release in this run.

## Command / exit-code log (non-secret)

- `git clone --no-hardlinks` of `d6d950c` → clean tree → exit 0.
- `pwsh Publish-SanadApi.ps1 -Configuration Release` → exit 0, 0 warnings/0
  errors, manifest revision `d6d950c…`.
- `tar -czf` (excluding `appsettings*.json`) → exit 0, 118 entries,
  SHA-256 `b8bc5aa0…`.
- `scp` archive → exit 0; host `sha256sum` matched `b8bc5aa0…` before use.
- Pre-cutover backup script (dump + `pg_restore --list` + app tar + host-config)
  → exit 0.
- Cutover script (stop → overlay → start → poll) → exit 0; deployment info 200
  after 12 s.
- Database verification scripts → exit 0 (heads, columns, indexes, counts).
- Public anonymous smoke (`pwsh`) → exit 0 (9×401, controls 200).
- Loopback anonymous smoke (host `bash`/`curl`) → exit 0 (9×401, controls 200).
- `Verify-SanadDeployment.ps1` → exit 0 (9/9 heads, revision match,
  Production, 9 startup contexts, smoke 200).
- Final state snapshot → exit 0 (`active`, `NRestarts=0`).
- Nothing failed; no step was improvised or repeated.
