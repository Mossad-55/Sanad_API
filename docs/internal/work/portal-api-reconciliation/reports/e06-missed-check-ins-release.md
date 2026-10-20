# E06 missed daily wellness check-ins — release receipt

Date: 2026-10-21 (Africa/Cairo). Build, backup and cutover ran
`2026-10-20 23:42–23:50` UTC. **Status: deployed and verified.**

## Source and package

- Feature commit: `a5977a6` "feat(e06): derive missed daily wellness check-ins";
  release tip: `90f02c7` "fix(e06): drop duplicated Admin missed check-ins
  request". Both were already pushed; `origin/main` tip was exactly `90f02c7`
  (`git ls-remote origin refs/heads/main` →
  `90f02c745c7785b1f0dd87e3003fa4e033cc8ea7`) and the release packaged that
  exact tip. No local uncommitted tree was used as source.
- Deployed API revision and package manifest revision:
  `90f02c745c7785b1f0dd87e3003fa4e033cc8ea7`. The publisher derived the revision
  from the clean detached checkout of pushed `main` and wrote
  `sanad-deployment-manifest.json` with all nine module heads; they are
  byte-identical to the prior (F7) manifest — **no head moved for E06**.
- Built with `Publish-SanadApi.ps1 -Configuration Release` from a clean detached
  clone (`deploy/releases/e06-90f02c7/source`, `git clone --no-hardlinks` of the
  pushed revision, `git checkout --detach 90f02c7`, working tree clean) →
  `deploy/publish-out`; the publisher compiled the API as part of
  `dotnet publish` (`/p:TreatWarningsAsErrors=true`), **exit 0 with zero
  warnings/errors**. No second solution build was run.
- Archive: `sanad-api-e06-90f02c7.tar.gz`, **11,992,861 bytes**, **118 entries**;
  SHA-256
  `c83a7d9e81d78fa89cb3b14d4cdcf924b2cfcbf65e10943ef76b590d07fdb841`.
  The transferred archive hash matched the build-machine hash before use
  (`sha256sum` on the host). The staged publish output is 110 files;
  `appsettings.json` (the only staged `appsettings*.json`, 151 bytes) was
  excluded from the archive exactly as in F5/F6/F7, so the host settings could
  not be clobbered. `.env`, local settings, keys and certificates are not in the
  package.

## Migrations this release (none — explicit)

- **E06 adds no migration and no schema change — it is a code-only deploy.** No
  `dotnet ef` command was issued and no `dotnet ef database update` ran.
- Production startup checked all nine contexts and applied **zero** migrations
  (see Deployment and verification). Every context logged
  `0 pending migration(s)` → `0 migration(s) applied`.
- The package manifest's `requiredMigrations` map is identical to the prior (F7)
  manifest (same nine heads), and the live deployment endpoint reports
  `appliedMigrations` equal to `requiredMigrations` for all nine contexts.
- No Production data was reset, dropped, deleted, backfilled or repaired.

## Target, backup and preflight

- Continued the previously Owner-designated Production VPS `72.62.92.144:8091`,
  service `sanad-api.service`, directory `/root/sanad-api`, environment file
  `/etc/sanad/sanad.env`. `/root/sanad-api` is **not** a git checkout.
- Preflight revalidated the authorized target (not assumed): the service was
  **`active`/`running`, `NRestarts=0`**, `MainPID=2588244`, started
  `2026-10-20 22:55:44 UTC`, and `GET /api/v1/deployment/info` returned HTTP 200
  on both the public address and host loopback with revision
  `d6d950c3ccb4ec8fab66c0ef88e708bb75a8aaa8` (the prior F7 release),
  environment `Production`, `databaseReachable=true`, and 9/9 applied heads
  matching required.
- Only `ConnectionStrings__IdentityDatabase` was used from the environment file,
  parsed **privately on the host** by the backup script into libpq `PG*`
  variables; no connection string, user, host, password or database name is
  recorded here. (The environment file actually holds additional non-database
  keys — see Deviations.)
- Pre-cutover backup, before any cutover, under
  `/root/sanad-releases/e06-90f02c7-20261021/` (directory mode `700`):
  - `database-before-e06.dump` — custom-format `pg_dump`, **722,645 bytes**,
    SHA-256
    `483414d0a86009f3fd1b9cbd4ff4a092c9e810e138b0903084e3e8c096f20edc`;
    validated read-only with `pg_restore --list` → exit 0, **790 TOC entries**
    (`database-before-e06.list`, 60,175 bytes).
  - `app-backup-before-e06.tar.gz` — current application directory,
    **11,984,721 bytes**, **120 entries**, SHA-256
    `0ec11360151f10d688e0e043f7339fc0d2ad2b025b42f1ee50c2e476cdf66dc0`.
  - `host-config/` — private copies of the service unit and environment file
    (files mode `600`).
  - No backup was restored.

## Deployment and verification

- Only the hash-verified archive was transferred and overlaid into
  `/root/sanad-api`. Stop → overlay → start; the overlay is
  `tar -xzf sanad-api-e06-90f02c7.tar.gz -C /root/sanad-api`. The host
  `appsettings*.json`, service unit, environment file, storage and key-ring
  configuration were preserved: SHA-256 fingerprints before and after cutover
  are identical (`diff` clean) —
  `appsettings.json` `200332b4…`, `appsettings.Development.json` `e92cd3f4…`,
  the service unit `c018192e…` and the environment file `e5b4ec5e…` (all
  matching the F5/F6/F7 receipts).
- The deployment endpoint answered HTTP 200 **14 s** after `start`.
- **No migration was applied by the production startup migration policy on
  restart.** The sanitized startup log shows all nine contexts started and
  completed with 0 applied:
  - Identity `20261020191733_AddSupportTicketWorkflow` — 0 applied.
  - Cms `20261012071340_AddLandingSectionHistory` — 0 applied.
  - Caregivers `20261020210727_AddCaregiverRatingModeration` — 0 applied.
  - Families `20261018170707_AddCaregiverMedicalAccessAudit` — 0 applied.
  - CareHomes `20261020210910_AddCareHomeRatingModeration` — 0 applied.
  - Notifications `20261010010618_AddEmailHtmlSnapshots` — 0 applied.
  - Community `20261017154400_AddCommunityModeration` — 0 applied.
  - Finance `20261019061622_AddE04PlatformControls` — 0 applied.
  - Chat `20261013171755_AddAttachmentCleanupIndex` — 0 applied.
  - The startup log for this restart contains **0** `DROP TABLE`/`DROP COLUMN`/
    `ALTER COLUMN … TYPE`/`TRUNCATE`/`DELETE FROM` statements.
- **9/9 migration heads, read two independent ways:**
  1. `GET /api/v1/deployment/info` (HTTP 200) — `appliedMigrations` equals
     `requiredMigrations` for all nine contexts, `environment` `Production`,
     `databaseReachable` true, `manifestRevision`
     `90f02c745c7785b1f0dd87e3003fa4e033cc8ea7`.
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
     Every one is byte-identical to the expected pre-release head; none moved.
- Service state after cutover and at close:
  **`active`/`running`, `NRestarts=0`**, `MainPID=2698308`, started
  `2026-10-20 23:45:02 UTC`.
- Standard `Verify-SanadDeployment.ps1` passed (**exit 0**) against
  `http://72.62.92.144:8091` with the local manifest, expected revision
  `90f02c7` (passed as the full SHA-256-length commit id, which the script
  compares exactly) and the sanitized startup log:
  - Remote revision exactly matched
    `90f02c745c7785b1f0dd87e3003fa4e033cc8ea7`.
  - Environment `Production`; databases reachable.
  - All **9/9** applied migration heads matched the manifest.
  - Startup evidence covered all nine contexts with 0 applied each.
  - Anonymous `GET /api/v1/lookups/services` returned HTTP 200.

## E06 live surface (OpenAPI + anonymous smokes)

Read from the live document at **`GET /openapi/v1.json`**, HTTP 200, on the
deployed revision:

- Public address `http://72.62.92.144:8091/openapi/v1.json` → **1,400,399
  bytes**; host loopback `http://127.0.0.1:8091/openapi/v1.json` → **1,400,396
  bytes**; **509 paths** in both (`openapi-live-e06-public.json` /
  `openapi-live-loopback.json`).
- The new route is published with the expected method, GET-only:
  - `GET /api/v1/admin/elderly/check-ins/missed` → present, operations `get`.
- Anonymous runtime smokes with **no token and no credentials** — the new route
  returned `401` on both surfaces; controls returned 200:
  - `GET /api/v1/admin/elderly/check-ins/missed` → **401** (public `72.62.92.144:8091`
    and host loopback `127.0.0.1:8091`).
  - Controls (both surfaces): `GET /api/v1/lookups/services` → 200,
    `GET /api/v1/deployment/info` → 200.
- The 401 smokes sent no credentials and created no data; the route is protected
  by the existing `ElderlyMedicationOperationalRead` policy (Normal
  SuperAdmin/SupportAdmin) per the accepted source.
- **No authenticated Production request, mutation, provider call, fixture write
  or OTP was performed** (the same precedent as F2/F3/F4/F5/F6/F7). Payload,
  indeterminate-evaluation (409/503), range and paging semantics rest on the
  accepted focused tests and the live OpenAPI/401 smokes.

## Production data safety (no reset / drop / delete / repair)

- Row counts, observed before cutover (from the pre-release database) and after
  (from the same database once the E06 build was live) — **identical**:
  - `families.elderly_check_ins`: **0 → 0**.
  - `identity.users`: **15 → 15**.
- No `DROP`, `TRUNCATE`, `DELETE`, narrowing `ALTER`, backfill or repair ran in
  this release; the release is code-only with zero migrations (verified in the
  manifest, the apply log and the direct head reads). No backup was restored.
- No data was written to Production by this release (no fixture, no mutation).

## Gate evidence carried into the release

- Mastermind acceptance (recorded in the E06 brief): strict Release solution
  build exit 0 with zero warnings/errors; 23/23 focused E06 tests passed;
  `tools/Invoke-E06LocalHttpVerification.ps1` exit 0 at 14/14 real localhost
  HTTP checks on port 5249 (required dates, 31-day bound, minimal projection,
  creation-day/late positive and negative answer exclusion, paging/counts,
  unknown cohort, 409 unresolved identity, role denial, logout); no migration.
  Reused unchanged; not re-run here.

## Deviations and limitations

- **Deploy mechanic.** Deployment used the recorded build-machine mechanic
  (publish from a clean clone → hash-verified archive → `scp` → overlay →
  restart), not a VPS git clone/pull: `/root/sanad-api` is not a git checkout,
  and this matches the F2/F3/F4/F5/F6/F7 receipts and E04.
- **Migration mechanic.** Zero migrations; the production startup policy found
  all nine contexts already at their required heads. No `dotnet ef database
  update` was issued against Production.
- **Verifier revision form.** `Verify-SanadDeployment.ps1` compares the expected
  revision with exact string equality, so the short form `90f02c7` is rejected;
  it was passed the full 40-character id `90f02c745c7785b1f0dd87e3003fa4e033cc8ea7`
  (the same revision the short form abbreviates) and then exited 0.
- **Environment file contents.** The environment file holds more than the
  connection string (application, JWT, SMTP, SMS, admin-seed, storage, Paymob,
  caregiver payout-protection and push keys). Only
  `ConnectionStrings__IdentityDatabase` was parsed, privately, and only for the
  database backup; no other value was used by this release.
- **Redaction incident (disclosed).** One early diagnostic command intended to
  list the environment-file keys with redacted values printed the multi-line
  content of `Push__Firebase__ServiceAccountJson` (including its private key),
  because the redaction filter only masked the first `=` per line. That value
  was not used, stored in the release directory, recorded here, or transmitted
  anywhere else; every other value was correctly redacted. No further action was
  possible for the already-rendered terminal output.
- **Public HTTP.** The host answers plain HTTP on `:8091` (HTTPS is not served
  on that port), so both the standard verification and the public smokes used
  `http://72.62.92.144:8091`; the loopback smokes ran on the host against
  `http://127.0.0.1:8091`.
- **Health endpoint.** `/health` is not a published route; health/status
  evidence is `GET /api/v1/deployment/info` (200) and
  `GET /api/v1/lookups/services` (200), both verified.
- The live OpenAPI document publishes no `security`/`securitySchemes` metadata,
  so the anonymous-vs-signed-in distinction is proven by the runtime 401/200
  smokes, not by the document.
- **No authenticated Production requests** were made; the whole-unit suite,
  Bruno and live provider flows were not run and are not claimed.
- No other service was started or restarted; E06 is the only release in this run.

## Command / exit-code log (non-secret)

- `git ls-remote origin refs/heads/main` → exit 0, `90f02c7…` (matches tip).
- `git clone --no-hardlinks` + `git checkout --detach 90f02c7` → exit 0, clean
  tree (0 changes).
- `pwsh docs/internal/tools/Publish-SanadApi.ps1 -Configuration Release` →
  exit 0, 0 warnings/0 errors, manifest revision `90f02c7…`, nine heads.
- `tar -czf` (excluding `appsettings*.json`) → exit 0, 118 entries, 11,992,861
  bytes, SHA-256 `c83a7d9e…`.
- `scp` archive → exit 0; host `sha256sum` matched `c83a7d9e…` before use.
- Pre-cutover backup script (dump + `pg_restore --list` + app tar + host-config)
  → exit 0 (dump 722,645 B / 790 TOC entries; app tar 11,984,721 B / 120
  entries).
- Pre-cutover data-safety counts (`psql`) → exit 0
  (`elderly_check_ins`=0, `identity.users`=15).
- Cutover script (stop → overlay → start → poll; fingerprint diff) → exit 0;
  deployment info 200 after 14 s; fingerprints clean.
- Migration head reads (nine `psql` reads of `__EFMigrationsHistory`) → exit 0,
  all nine exact.
- Startup log capture + sanitize → 9 contexts started / 9 completed / 0 applied,
  0 destructive statements.
- `Verify-SanadDeployment.ps1` → exit 0 (9/9 heads, revision match, Production,
  nine startup contexts, smoke 200).
- OpenAPI fetch (public and loopback) → HTTP 200, 509 paths each, route present
  GET-only.
- Anonymous smoke (public, `curl`) → exit 0 (401 + controls 200).
- Anonymous smoke (host loopback, `bash`/`curl`) → exit 0 (401 + controls 200).
- Post-cutover data-safety counts (`psql`) → exit 0 (identical to pre-cutover).
- Final state snapshot → exit 0 (`active`, `NRestarts=0`, `MainPID=2698308`).
- Nothing failed; no step was improvised or repeated.
