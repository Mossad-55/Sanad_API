# E08 unreferenced landing asset deletion — release receipt

Date: 2026-10-21 (Africa/Cairo). Clone, publish and backup ran `2026-10-21
05:3x` UTC; cutover ran `2026-10-21 05:34:54` UTC and answered 19 s later.
**Status: deployed and verified.** This release is **code-only**: it applies no
migration.

## Source and package

- Feature commit: `042a7fd` "feat(e08): unreferenced landing asset deletion",
  the current `origin/main` tip at release time. Full release revision:
  `042a7fd4dd898c2c93579335fe34dd1e61a4d2c5`.
- Clean detached clone (`deploy/releases/e08-042a7fd/source`): `git clone
  --no-hardlinks`, `git checkout --detach 042a7fd`, `git rev-parse HEAD` =
  `042a7fd4dd898c2c93579335fe34dd1e61a4d2c5`, working tree clean (`0` changes).
  No local uncommitted tree was used as source.
- Built with `Publish-SanadApi.ps1 -Configuration Release` from that clean
  detached clone → `deploy/publish-out` (110 files). The publisher compiled the
  API as part of `dotnet publish` with `/p:TreatWarningsAsErrors=true`, **exit 0
  with zero warnings/errors**.
- Manifest revision `042a7fd4dd898c2c93579335fe34dd1e61a4d2c5`. **All nine
  module heads are byte-identical to the E07 manifest** — E08 authored no
  migration, so no head moved and the required-head set is unchanged.
- Archive: `sanad-api-e08-042a7fd.tar.gz`, **12,055,686 bytes**, **118 entries**;
  SHA-256
  `10a85d10bc6c974eca9c9ea36e8f226ad06e00ef031896e81213993becc8c420`. The
  transferred archive hash matched the build-machine hash before use
  (`sha256sum` on the host returned the same digest and the same 12,055,686
  bytes). `appsettings*.json` was excluded exactly as in every prior release, so
  the host settings could not be clobbered: the archive contains **0**
  `appsettings` entries.

## Migrations this release (none)

- **E08 adds no migration.** The production startup policy still checked all
  nine contexts; the sanitized startup log contains **0** `Applying migration`
  lines and **9** `0 pending migration(s)` lines, one per context.
- No `DROP`, `TRUNCATE`, `DELETE`, `ALTER … TYPE`, backfill or repair ran: the
  startup log contains **0** destructive statements.
- Every observed head is identical to the pre-release read, including
  Caregivers `20261021130000_AddCaregiverReviewNotes` (applied by the preceding
  E07 release, untouched here).

## Target, backup and preflight

- Continued the Owner-designated Production VPS `72.62.92.144:8091`, service
  `sanad-api.service`, application directory `/root/sanad-api`, environment file
  `/etc/sanad/sanad.env`. `/root/sanad-api` is **not** a git checkout (checked
  before and after: `git_checkout=no`).
- Preflight revalidated the target (not assumed): service **`active`, started
  `2026-10-21 05:30:28 UTC` (the E07 restart), `NRestarts=0`,
  `MainPID=3463018`**; `GET /api/v1/deployment/info` returned HTTP 200 with
  manifest revision `f66492d877ab61d7057d74551d27feb8767fe509` (the E07
  release), environment `Production`, `databaseReachable=true`, and 9/9 applied
  heads (Caregivers already at `20261021130000_AddCaregiverReviewNotes`).
- Only `ConnectionStrings__IdentityDatabase` was used from the environment file,
  parsed **privately on the host** by the backup script into libpq `PG*`
  variables. No environment-file content or value was printed at any point; only
  key names, sizes and hashes were emitted. No connection string, user, host,
  password or database name is recorded here.
- Pre-cutover backup, before any cutover, under
  `/root/sanad-releases/e08-042a7fd-20261021/` (directory mode `700`):
  - `database-before-e08.dump` — custom-format `pg_dump`, **728,063 bytes**,
    SHA-256
    `739a032e5c9c61ec449330d97bdf23822d3c0131be09e57641db16caae3cfe65`;
    validated read-only with `pg_restore --list` → **803 TOC entries**
    (`database-before-e08.list`, 61,230 bytes).
  - `app-backup-before-e08.tar.gz` — current application directory (as left by
    the E07 release), **12,050,128 bytes**, **120 entries**, SHA-256
    `a27fe672a868f9bb827136a201befa1b545a08940c8e2a84b692d879c9ead874`.
  - `host-config/` — private copies of the service unit and the environment file
    (2 files, mode `600`).
  - No backup was restored.

## Deployment and verification

- Only the hash-verified archive was transferred and overlaid into
  `/root/sanad-api`. Stop → overlay (`tar -xzf sanad-api-e08-042a7fd.tar.gz -C
  /root/sanad-api`) → start. The host `appsettings.json`, service unit and
  environment file were preserved: SHA-256 fingerprints before and after the
  cutover are **identical** — `appsettings.json` `200332b4…`, the service unit
  `c018192e…`, the environment file `3624eff2…`.
- The deployment endpoint answered HTTP 200 **19 s** after `start`.
- **Zero migrations were applied** (code-only release). 9/9 contexts were
  checked and 0 startup/DI exceptions were logged (no `Unhandled exception`,
  `Application startup exception`, `Unable to resolve service`, `No service for
  type` or service-descriptor validation error).
- **9/9 migration heads verified after cutover**, read two independent ways:
  1. `GET /api/v1/deployment/info` (HTTP 200) — `appliedMigrations` equals
     `requiredMigrations` for all nine contexts, `environment` `Production`,
     `databaseReachable` true, `manifestRevision`
     `042a7fd4dd898c2c93579335fe34dd1e61a4d2c5`.
  2. Direct read of each context's `__EFMigrationsHistory` after cutover:
     - Identity `20261020191733_AddSupportTicketWorkflow`
     - Cms `20261021120000_AddFamilyHomeSections`
     - Caregivers `20261021130000_AddCaregiverReviewNotes`
     - Families `20261018170707_AddCaregiverMedicalAccessAudit`
     - CareHomes `20261020210910_AddCareHomeRatingModeration`
     - Notifications `20261010010618_AddEmailHtmlSnapshots`
     - Community `20261017154400_AddCommunityModeration`
     - Finance `20261019061622_AddE04PlatformControls`
     - Chat `20261013171755_AddAttachmentCleanupIndex`
     All nine are identical to the pre-release read.
- Standard `Verify-SanadDeployment.ps1` passed (**exit 0**) against
  `http://72.62.92.144:8091` with the local manifest, expected revision
  `042a7fd4dd898c2c93579335fe34dd1e61a4d2c5` and the sanitized startup log:
  - Remote revision exactly matched the deployed revision.
  - Environment `Production`; databases reachable.
  - All **9/9** applied migration heads matched the manifest.
  - Startup log proved migration checks for all nine contexts.
  - Anonymous `GET /api/v1/lookups/services` returned HTTP 200.
- Service state after cutover and at close: **`active`, `NRestarts=0`,
  `MainPID=3473356`**, started `2026-10-21 05:34:54 UTC`.

## Route / OpenAPI surface

- OpenAPI path count **before** cutover (E07 build): **516 paths**.
- OpenAPI path count **after** cutover (E08 build), HTTP 200: **516 paths** — the
  delta is **0 paths, +1 operation**: E08 added the `delete` operation to the
  existing `/api/v1/admin/landing/media` path, which E05 had published with
  `post` only.
  - `/api/v1/admin/landing/media` [`delete`, `post`] ← `delete` is the E08
    addition.
- Every other admin landing / family-home / caregiver path is unchanged.
- **E08 surface re-proved live after the restart** — anonymous smokes with no
  token and no credentials, on both the public address and host loopback:
  - `DELETE /api/v1/admin/landing/media?key=landing/…png` → **401** (loopback and
    public)
  - `POST /api/v1/admin/landing/media?key=landing/…png` → **401**
  - `GET /api/v1/admin/landing/media?key=landing/…png` → **405** — expected: the
    media route is DELETE-only for reads of stored keys and POST for uploads; it
    is not a GET route.
  - Controls: `GET /api/v1/lookups/services` → 200,
    `GET /api/v1/deployment/info` → 200.
- **No authenticated Production request, upload, deletion, provider call,
  fixture write or OTP was performed.** No landing media key, landing section,
  family-home section or file was created or removed by this release.

## Production data safety (before → after)

Read-only `psql` before the cutover and after the E08 restart on the same
Production database:

| Object | Before | After |
|---|---|---|
| `identity.users` | 15 | **15** |
| `identity.support_tickets` | 1 | **1** |
| `notifications.notifications` | 16 | **16** |
| `notifications.email_outbox` | 12 | **12** |
| `notifications.push_outbox` | 4 | **4** |
| `cms.landing_sections` | 0 | **0** |
| `caregivers.caregiver_review_notes` | 0 | **0** |

- Every count is identical, and there is **no schema change at all** for E08.
- No `DROP`, `TRUNCATE`, `DELETE`, backfill or repair ran. No backup was
  restored and no row or file was written.

## Runtime evidence for the E08 change

- The deployment is healthy end-to-end: `Application started. Press Ctrl+C to
  shut down.`, `Hosting environment: Production`, `Content root path:
  /root/sanad-api`, `systemctl is-active` = `active`, `NRestarts=0`, and the
  startup log has no startup/DI exception — so the new delete endpoint's
  registration, the `LandingErrors` codes and the modified
  `AdminLandingController` did not break service construction, and the delete
  route answers before any handler body runs (401 unauthenticated).
- The deletion behaviour itself is **not** exercised on Production: no
  authenticated request was made, no asset was uploaded and none was deleted.
  That behaviour is covered by the focused test suite (`51 passed`) and by the
  local adapter (**26/26**) against the local Development database; neither was
  re-run here and neither is re-claimed.

## Gate evidence carried into the release

- E08's acceptance gates (strict Release build 0/0, 51 focused tests, adapter
  26/26 on the frozen tree after the three adapter fixes) were run before this
  release. This release verified build/publish, the zero-migration restart, 9/9
  unchanged heads, the +1 OpenAPI operation, the DELETE 401 re-proof and the
  clean start.

## Limitations and open items

- **No authenticated Production exercise of E08.** The delete route is proven
  only at the anonymous 401 boundary (and the DELETE-only 405) on Production;
  the behaviour rests on the focused tests and the local 26/26 adapter. No
  asset, section or file was created or deleted.
- **The route's reference-discovery safety is not re-proved on production
  data.** The live database has 0 landing sections, so the "referenced ⇒ 409"
  branch was not exercised against real production rows; it is covered by the
  local adapter (which created real landing and family-home drafts through the
  API) and the focused suite.
- **Deploy mechanic.** Deployment used the recorded build-machine mechanic
  (publish from a clean detached clone → hash-verified archive → `scp` → overlay
  → restart), not a VPS git clone/pull: `/root/sanad-api` is not a git checkout.
- **Verifier revision form.** `Verify-SanadDeployment.ps1` compares the expected
  revision with exact string equality, so it was passed the full 40-character
  id.
- **HTTPS surface.** `:8091` serves plain HTTP only, so the public smokes used
  `http://72.62.92.144:8091` and the loopback smokes ran on the host against
  `http://127.0.0.1:8091`.
- **Health endpoint.** `/health` is not a published route; health/status
  evidence is `GET /api/v1/deployment/info` (200) and
  `GET /api/v1/lookups/services` (200), both verified.
- The live OpenAPI document publishes no `security`/`securitySchemes` metadata,
  so anonymous-vs-signed-in is proven by the runtime 401/200 smokes, not by the
  document.
- The whole-unit suite, Bruno and live provider flows were not run and are not
  claimed. E07 was released separately immediately before this; no other service
  was started or restarted. Both releases ran in serialized slots, one cutover
  at a time.

## Command / exit-code log (non-secret)

- `git rev-parse 042a7fd` →
  `042a7fd4dd898c2c93579335fe34dd1e61a4d2c5`.
- `git clone --no-hardlinks` + `git checkout --detach 042a7fd` → exit 0; HEAD
  `042a7fd…`, working tree clean (0 changes).
- `pwsh docs/internal/tools/Publish-SanadApi.ps1 -Configuration Release` → exit
  0, 0 warnings/0 errors, manifest revision `042a7fd…`, 110 published files.
- `tar -czf` (excluding `appsettings*.json`) → exit 0, 118 entries, 12,055,686
  bytes, SHA-256 `10a85d10…`, 0 `appsettings` entries.
- `scp` archive → exit 0; host `sha256sum` matched `10a85d10…` and the byte size
  before use.
- Preflight (`systemctl`, `curl` deployment info, before counts) → exit 0.
- Pre-cutover backup script (dump + `pg_restore --list` + app tar + host-config)
  → exit 0 (dump 728,063 B / 803 TOC entries; app tar 12,050,128 B / 120
  entries; 2 host-config files).
- Cutover script (stop → overlay → start → poll; fingerprint diff) → exit 0;
  deployment info 200 after 19 s; fingerprints identical.
- Verification script (startup evidence, 9 head reads, after counts, OpenAPI,
  401/405 smokes) → exit 0; 9 contexts, 0 `Applying migration`, 9 pending-zero
  lines, 0 DI exceptions, 0 destructive statements.
- `Verify-SanadDeployment.ps1` → exit 0 (9/9 heads, revision match, Production,
  nine startup contexts, smoke 200).
- Public anonymous smoke (`curl.exe` from the build machine) → DELETE 401,
  POST 401, GET 405, controls 200.
- Nothing failed; no step was improvised or repeated.
