# E07 append-only caregiver review notes — release receipt

Date: 2026-10-21 (Africa/Cairo). Clone, publish and backup ran `2026-10-21
05:2x` UTC; cutover ran `2026-10-21 05:30:28` UTC and answered 11 s later.
**Status: deployed and verified.**

## Source and package

- Feature commit: `f66492d` "feat(e07): append-only caregiver review notes",
  the current `origin/main` tip at release time. Full release revision:
  `f66492d877ab61d7057d74551d27feb8767fe509`.
- Clean detached clone (`deploy/releases/e07-f66492d/source`): `git clone
  --no-hardlinks`, `git checkout --detach f66492d`, `git rev-parse HEAD` =
  `f66492d877ab61d7057d74551d27feb8767fe509`, working tree clean (`0` changes).
  No local uncommitted tree was used as source.
- Built with `Publish-SanadApi.ps1 -Configuration Release` from that clean
  detached clone → `deploy/publish-out` (110 files). The publisher compiled the
  API as part of `dotnet publish` with `/p:TreatWarningsAsErrors=true`, **exit 0
  with zero warnings/errors**. No second solution build was run.
- Manifest revision `f66492d877ab61d7057d74551d27feb8767fe509`. Eight of the
  nine module heads are byte-identical to the prior (F8) manifest; **exactly one
  head moved**: Caregivers `20261020210727_AddCaregiverRatingModeration` →
  `20261021130000_AddCaregiverReviewNotes`.
- Archive: `sanad-api-e07-f66492d.tar.gz`, **12,052,542 bytes**, **118 entries**;
  SHA-256
  `1807a366c3f240a9eb0bada5e97b02339630637db1c8d67c3e1810c3babcd135`. The
  transferred archive hash matched the build-machine hash before use
  (`sha256sum` on the host returned the same digest and the same 12,052,542
  bytes). `appsettings*.json` was excluded from the archive exactly as in
  F5/F6/F7/F8/E05/E06, so the host settings could not be clobbered: the archive
  contains **0** `appsettings` entries. `.env`, local settings, keys and
  certificates are not in the package.

## Migrations this release (one — the E07 schema change)

- **E07 adds exactly one migration**, and it was applied by the production
  startup policy: Caregivers `20261021130000_AddCaregiverReviewNotes`
  (`Up` = `CreateTable caregiver_review_notes` + `CreateIndex
  (caregiver_id, created_on_utc)`; `Down` drops only).
- Production startup checked all nine contexts; the sanitized startup log
  contains **exactly 1** `Applying migration` line —
  `Applying migration '20261021130000_AddCaregiverReviewNotes'.` — and each of
  the other eight contexts reported `0 pending migration(s)` → `0 migration(s)
  applied`.
- No Production data was reset, dropped, deleted, backfilled or repaired: the
  startup log contains **0** `DROP TABLE`/`DROP COLUMN`/`ALTER COLUMN … TYPE`/
  `TRUNCATE`/`DELETE FROM` statements, and the new table starts empty.
- Post-cutover the table exists and is empty:
  `caregivers.caregiver_review_notes` → 5 columns
  (`id, caregiver_id, author_user_id, text, created_on_utc`), **0 rows**.

## Target, backup and preflight

- Continued the previously Owner-designated Production VPS `72.62.92.144:8091`,
  service `sanad-api.service`, application directory `/root/sanad-api`,
  environment file `/etc/sanad/sanad.env`. `/root/sanad-api` is **not** a git
  checkout (checked before and after the cutover: `git_checkout=no`).
- Preflight revalidated the target (not assumed): service **`active`, started
  `2026-10-21 02:04:23 UTC`, `NRestarts=0`, `MainPID=3010418`**;
  `GET /api/v1/deployment/info` returned HTTP 200 with manifest revision
  `7a651684c3618593dd090a979b40602aa9af8920` (the prior F8 release), environment
  `Production`, `databaseReachable=true`, and 9/9 applied heads (Caregivers at
  `20261020210727_AddCaregiverRatingModeration`).
- Only `ConnectionStrings__IdentityDatabase` was used from the environment file,
  parsed **privately on the host** by the backup script into libpq `PG*`
  variables. No environment-file content or value was printed at any point in
  this release; only key names, sizes and hashes were emitted. No connection
  string, user, host, password or database name is recorded here.
- Pre-cutover backup, before any cutover, under
  `/root/sanad-releases/e07-f66492d-20261021/` (directory mode `700`):
  - `database-before-e07.dump` — custom-format `pg_dump`, **725,936 bytes**,
    SHA-256
    `151155084e50b403ffea55f5ca5df68443d55bf0c6a1a6ac023ea7f5f7184236`;
    validated read-only with `pg_restore --list` → **798 TOC entries**
    (`database-before-e07.list`, 60,779 bytes) — byte-for-byte the same size and
    the same TOC count as the F8 pre-cutover dump, as expected for a release
    that adds one empty table.
  - `app-backup-before-e07.tar.gz` — current application directory,
    **12,042,031 bytes**, **120 entries**, SHA-256
    `3c18d71072fd29a8c12fd6c8bdfb5cb2aa9a272b9c9efbbe20ec7af657ad67c0`.
  - `host-config/` — private copies of the service unit and the environment file
    (2 files, mode `600`).
  - No backup was restored.

## Deployment and verification

- Only the hash-verified archive was transferred and overlaid into
  `/root/sanad-api`. Stop → overlay (`tar -xzf sanad-api-e07-f66492d.tar.gz -C
  /root/sanad-api`) → start. The host `appsettings.json`, service unit and
  environment file were preserved: SHA-256 fingerprints before and after the
  cutover are **identical** — `appsettings.json` `200332b4…`, the service unit
  `c018192e…`, the environment file `3624eff2…`.
- The deployment endpoint answered HTTP 200 **11 s** after `start`.
- **One migration was applied by the production startup migration policy** (the
  E07 one, above). 9/9 contexts were checked and 0 startup/DI exceptions were
  logged (no `Unhandled exception`, `Application startup exception`, `Unable to
  resolve service`, `No service for type` or service-descriptor validation
  error).
- **9/9 migration heads verified after cutover**, read two independent ways:
  1. `GET /api/v1/deployment/info` (HTTP 200) — `appliedMigrations` equals
     `requiredMigrations` for all nine contexts, `environment` `Production`,
     `databaseReachable` true, `manifestRevision`
     `f66492d877ab61d7057d74551d27feb8767fe509`.
  2. Direct read of each context's `__EFMigrationsHistory` (`"MigrationId"`,
     ordered desc, limit 1) in the Production database, after cutover:
     - Identity `20261020191733_AddSupportTicketWorkflow`
     - Cms `20261021120000_AddFamilyHomeSections`
     - **Caregivers `20261021130000_AddCaregiverReviewNotes`** (advanced)
     - Families `20261018170707_AddCaregiverMedicalAccessAudit`
     - CareHomes `20261020210910_AddCareHomeRatingModeration`
     - Notifications `20261010010618_AddEmailHtmlSnapshots`
     - Community `20261017154400_AddCommunityModeration`
     - Finance `20261019061622_AddE04PlatformControls`
     - Chat `20261013171755_AddAttachmentCleanupIndex`
     Eight heads are identical to the pre-release read; only Caregivers moved, to
     the head the manifest requires.
- Standard `Verify-SanadDeployment.ps1` passed (**exit 0**) against
  `http://72.62.92.144:8091` with the local manifest, expected revision
  `f66492d877ab61d7057d74551d27feb8767fe509` (full commit id, which the script
  compares exactly) and the sanitized startup log:
  - Remote revision exactly matched the deployed revision.
  - Environment `Production`; databases reachable.
  - All **9/9** applied migration heads matched the manifest.
  - Startup log proved migration checks for all nine contexts
    (`CaregiversDbContext, CareHomesDbContext, ChatDbContext, CmsDbContext,
    CommunityDbContext, FamiliesDbContext, FinanceDbContext, IdentityDbContext,
    NotificationsDbContext`).
  - Anonymous `GET /api/v1/lookups/services` returned HTTP 200.
- Service state after cutover and at close: **`active`, `NRestarts=0`,
  `MainPID=3463018`**, started `2026-10-21 05:30:28 UTC`; the service started
  cleanly and remained up throughout the post-deploy verification.

## Route / OpenAPI surface

- OpenAPI path count **before** cutover (F8 build): **515 paths**.
- OpenAPI path count **after** cutover (E07 build), HTTP 200: **516 paths** — a
  delta of **+1**, exactly the E07 addition.
- The new path is published with both operations:
  - `/api/v1/admin/caregivers/{caregiverId}/notes` [`get`, `post`]
- Every other admin landing / family-home / caregiver path is unchanged.
- **E07 surfaces re-proved live after the restart** — anonymous smokes with no
  token and no credentials, on both the public address and host loopback; both
  E07 routes returned **`401`**:
  - `GET /api/v1/admin/caregivers/{caregiverId}/notes` → **401**
  - `POST /api/v1/admin/caregivers/{caregiverId}/notes` → **401**
  - Controls: `GET /api/v1/lookups/services` → 200,
    `GET /api/v1/deployment/info` → 200.
- **No authenticated Production request, mutation, provider call, producer
  trigger, fixture write or OTP was performed.** No review note, account, ticket
  or notification row was created — the new table holds 0 rows after the
  release.

## Production data safety (before → after)

Read-only `psql` before the cutover and after the E07 restart on the same
Production database:

| Object | Before | After |
|---|---|---|
| `identity.users` | 15 | **15** |
| `identity.support_tickets` | 1 | **1** |
| `notifications.notifications` | 16 | **16** |
| `notifications.email_outbox` | 12 | **12** |
| `notifications.push_outbox` | 4 | **4** |
| `cms.landing_sections` | 0 | **0** |
| `caregivers.caregiver_review_notes` | (absent) | **0** |

- Every pre-existing count is identical. The only schema change is the additive
  E07 table, created empty.
- No `DROP`, `TRUNCATE`, `DELETE`, backfill or repair ran. No backup was
  restored and no row was written.

## Runtime evidence for the E07 change

- The deployment is healthy end-to-end: `Application started. Press Ctrl+C to
  shut down.`, `Hosting environment: Production`, `Content root path:
  /root/sanad-api`, `systemctl is-active` = `active`, `NRestarts=0`, and the
  startup log has no startup/DI exception — so the new `CaregiverReviewNote`
  registration, its `DbSet` and the two controller routes did not break service
  construction.
- The review-note behaviour itself is **not** exercised on Production: no
  authenticated request was made and no note was appended. That behaviour is
  covered by the focused test suite (`53 passed`) and by the local adapter
  (**22/22**) against the local Development database; neither was re-run here and
  neither is re-claimed.

## Gate evidence carried into the release

- E07's acceptance gates (strict Release build 0/0, 53 focused tests, adapter
  22/22 on the frozen tree, migration applied to the local `SanadDb` before the
  gate) were run before this release. This release verified build/publish, the
  single-migration restart, 9/9 heads, the +1 OpenAPI path, the 401 re-proof and
  the clean start.

## Limitations and open items

- **No authenticated Production exercise of E07.** The append-only note routes
  are proven only at the anonymous 401 boundary on Production; the behaviour
  rests on the focused tests and the local 22/22 adapter. No note, account or
  ticket was created.
- **Deploy mechanic.** Deployment used the recorded build-machine mechanic
  (publish from a clean detached clone → hash-verified archive → `scp` → overlay
  → restart), not a VPS git clone/pull: `/root/sanad-api` is not a git checkout.
- **Verifier revision form.** `Verify-SanadDeployment.ps1` compares the expected
  revision with exact string equality, so it was passed the full 40-character
  id.
- **HTTPS surface.** `:8091` serves plain HTTP only, so the public smokes used
  `http://72.62.92.144:8091` and the loopback smokes ran on the host against
  `http://127.0.0.1:8091`. No HTTPS surface was exercised.
- **Health endpoint.** `/health` is not a published route; health/status
  evidence is `GET /api/v1/deployment/info` (200) and
  `GET /api/v1/lookups/services` (200), both verified.
- The live OpenAPI document publishes no `security`/`securitySchemes` metadata,
  so anonymous-vs-signed-in is proven by the runtime 401/200 smokes, not by the
  document.
- The whole-unit suite, Bruno and live provider flows were not run and are not
  claimed. E08 is released separately after this; no other service was started
  or restarted.

## Command / exit-code log (non-secret)

- `git rev-parse f66492d` →
  `f66492d877ab61d7057d74551d27feb8767fe509`.
- `git clone --no-hardlinks` + `git checkout --detach f66492d` → exit 0; HEAD
  `f66492d…`, working tree clean (0 changes).
- `pwsh docs/internal/tools/Publish-SanadApi.ps1 -Configuration Release` → exit
  0, 0 warnings/0 errors, manifest revision `f66492d…`, 110 published files,
  Caregivers head `20261021130000_AddCaregiverReviewNotes`.
- `tar -czf` (excluding `appsettings*.json`) → exit 0, 118 entries, 12,052,542
  bytes, SHA-256 `1807a366…`, 0 `appsettings` entries.
- `scp` archive → exit 0; host `sha256sum` matched `1807a366…` and the byte size
  before use.
- Preflight (`systemctl`, `curl` deployment info, before counts) → exit 0.
- Pre-cutover backup script (dump + `pg_restore --list` + app tar + host-config)
  → exit 0 (dump 725,936 B / 798 TOC entries; app tar 12,042,031 B / 120
  entries; 2 host-config files).
- Cutover script (stop → overlay → start → poll; fingerprint diff) → exit 0;
  deployment info 200 after 11 s; fingerprints identical.
- Verification script (startup evidence, 9 head reads, table shape, after
  counts, OpenAPI, 401 smokes) → exit 0; 9 contexts, 1 `Applying migration`, 0
  DI exceptions, 0 destructive statements.
- `Verify-SanadDeployment.ps1` → exit 0 (9/9 heads, revision match, Production,
  nine startup contexts, smoke 200).
- Public anonymous smoke (`curl.exe` from the build machine) → E07 routes 401,
  controls 200.
- Nothing failed; no step was improvised or repeated.
