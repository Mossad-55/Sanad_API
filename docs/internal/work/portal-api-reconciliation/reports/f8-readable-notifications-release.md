# F8 readable recipient-language notifications — release receipt

Date: 2026-10-21 (Africa/Cairo). Clone, publish, backup and cutover ran
`2026-10-21 01:33–01:42` UTC. **Status: deployed and verified.**

## Source and package

- Feature commit: `7a65168` "feat(f8): readable, recipient-language notification
  copy", the current `origin/main` tip. Full release revision:
  `7a651684c3618593dd090a979b40602aa9af8920`.
- Clean detached clone (`deploy/releases/f8-7a65168/source`): `git checkout
  --detach 7a65168`, `git rev-parse HEAD` =
  `7a651684c3618593dd090a979b40602aa9af8920`, working tree clean (`0` changes).
  No local uncommitted tree was used as source.
- Deployed API revision and package manifest revision:
  `7a651684c3618593dd090a979b40602aa9af8920`. The manifest's nine module heads
  are **byte-identical** to the prior (E05) manifest — no head moved for F8.
- Built with `Publish-SanadApi.ps1 -Configuration Release` from the clean
  detached clone → `deploy/publish-out`; the publisher compiled the API as part
  of `dotnet publish` (`/p:TreatWarningsAsErrors=true`), **exit 0 with zero
  warnings/errors**. No second solution build was run.
- Archive: `sanad-api-f8-7a65168.tar.gz`, **12,039,139 bytes**, **118 entries**;
  SHA-256
  `80731ce3234d95e63f9aedd88415bee6d6e145849dea4630f55dfc92d7bafbb8`.
  The transferred archive hash matched the build-machine hash before use
  (`sha256sum` on the host). The staged publish output is 110 files;
  `appsettings.json` (151 bytes) was excluded from the archive exactly as in
  F5/F6/F7/E05, so the host settings could not be clobbered. `.env`, local
  settings, keys and certificates are not in the package.

## Migrations this release (none — explicit)

- **F8 is code-only and changes no schema.** No migration was added, and no
  `dotnet ef` command was issued against Production.
- Production startup checked all nine contexts and applied **zero** migrations:
  every context logged `0 pending migration(s)` → `0 migration(s) applied`, and
  the startup log contains **0** `Applying migration` lines.
- The package manifest's `requiredMigrations` map is identical to the E05
  manifest (same nine heads), and the live deployment endpoint reports
  `appliedMigrations` equal to `requiredMigrations` for all nine contexts.
- No Production data was reset, dropped, deleted, backfilled or repaired.

## Target, backup and preflight

- Continued the previously Owner-designated Production VPS `72.62.92.144:8091`,
  service `sanad-api.service`, directory `/root/sanad-api`, environment file
  `/etc/sanad/sanad.env`. `/root/sanad-api` is **not** a git checkout (checked
  before and after: `IS_GIT_CHECKOUT=no`).
- Preflight revalidated the target (not assumed): service
  **`active`/`running`, `NRestarts=0`**, `MainPID=2895360`, started
  `2026-10-21 01:13:43 UTC`; `GET /api/v1/deployment/info` returned HTTP 200 with
  revision `e2be9badaab0db9fa9f012769c696993ec234b05` (the prior E05 release),
  environment `Production`, `databaseReachable=true`, 9/9 applied heads
  matching required (Cms at `20261021120000_AddFamilyHomeSections`).
- Only `ConnectionStrings__IdentityDatabase` was used from the environment file,
  parsed **privately on the host** by the backup script into libpq `PG*`
  variables. No environment-file content or value was printed at any point in
  this release; only key names, sizes and hashes were emitted. No connection
  string, user, host, password or database name is recorded here.
- Pre-cutover backup, before any cutover, under
  `/root/sanad-releases/f8-7a65168-20261021/` (directory mode `700`):
  - `database-before-f8.dump` — custom-format `pg_dump`, **725,936 bytes**,
    SHA-256
    `22e7b7633902e4bd8b60efa419d5e1ff81013fcc5fd5e00ec3373d863acd733f`;
    validated read-only with `pg_restore --list` → exit 0, **798 TOC entries**
    (`database-before-f8.list`, 60,779 bytes; eight more than E05's 790 for the
    E05 Family-home tables/indexes).
  - `app-backup-before-f8.tar.gz` — current application directory,
    **12,037,648 bytes**, **120 entries**, SHA-256
    `ee87a8e8542e0bfe2fda3330fc7beae0f70abcd1b66a3e4b7f66a7ea5443f5fe`.
  - `host-config/` — private copies of the service unit and environment file
    (files mode `600`).
  - No backup was restored.

## Deployment and verification

- Only the hash-verified archive was transferred and overlaid into
  `/root/sanad-api`. Stop → overlay → start; the overlay is
  `tar -xzf sanad-api-f8-7a65168.tar.gz -C /root/sanad-api`. The host
  `appsettings*.json`, service unit, environment file, storage and key-ring
  configuration were preserved: SHA-256 fingerprints before and after cutover
  are identical (`diff` clean) —
  `appsettings.json` `200332b4…`, `appsettings.Development.json` `e92cd3f4…`,
  the service unit `c018192e…` and the environment file `e5b4ec5e…`.
- The deployment endpoint answered HTTP 200 **14 s** after `start`.
- **No migration was applied by the production startup migration policy on
  restart.** The sanitized startup log shows all nine contexts with 0 applied:
  - Identity `20261020191733_AddSupportTicketWorkflow` — 0 applied.
  - Cms `20261021120000_AddFamilyHomeSections` — 0 applied.
  - Caregivers `20261020210727_AddCaregiverRatingModeration` — 0 applied.
  - Families `20261018170707_AddCaregiverMedicalAccessAudit` — 0 applied.
  - CareHomes `20261020210910_AddCareHomeRatingModeration` — 0 applied.
  - Notifications `20261010010618_AddEmailHtmlSnapshots` — 0 applied.
  - Community `20261017154400_AddCommunityModeration` — 0 applied.
  - Finance `20261019061622_AddE04PlatformControls` — 0 applied.
  - Chat `20261013171755_AddAttachmentCleanupIndex` — 0 applied.
  - The startup log contains 0 `Applying migration`, 0
    `DROP TABLE`/`DROP COLUMN`/`ALTER COLUMN … TYPE`/`TRUNCATE`/`DELETE FROM`
    statements, and **0 startup/DI exceptions** (no `Unhandled exception`,
    `Application startup exception`, `Unable to resolve service`, service-
    descriptor validation error or `No service for type`).
- **9/9 migration heads unchanged**, read two independent ways:
  1. `GET /api/v1/deployment/info` (HTTP 200) — `appliedMigrations` equals
     `requiredMigrations` for all nine contexts, `environment` `Production`,
     `databaseReachable` true, `manifestRevision`
     `7a651684c3618593dd090a979b40602aa9af8920`.
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
     Every head is identical to the pre-release read; none moved.
- Standard `Verify-SanadDeployment.ps1` passed (**exit 0**) against
  `http://72.62.92.144:8091` with the local manifest, expected revision
  `7a651684c3618593dd090a979b40602aa9af8920` (full commit id, which the script
  compares exactly) and the sanitized startup log:
  - Remote revision exactly matched the deployed revision.
  - Environment `Production`; databases reachable.
  - All **9/9** applied migration heads matched the manifest.
  - Startup evidence covered all nine contexts with 0 applied each.
  - Anonymous `GET /api/v1/lookups/services` returned HTTP 200.
- Service state after cutover and at close:
  **`active`/`running`, `NRestarts=0`**, `MainPID=2953952`, started
  `2026-10-21 01:39:43 UTC`; the service starts cleanly and remained up
  throughout the post-deploy verification.

## Route / OpenAPI surface (unchanged)

- OpenAPI path count **before** cutover (E05 build): **515 paths**.
- OpenAPI path count **after** cutover (F8 build), HTTP 200:
  - Public `http://72.62.92.144:8091/openapi/v1.json` → **1,423,820 bytes**,
    **515 paths** (`openapi-live-f8-public.json`).
  - Host loopback `http://127.0.0.1:8091/openapi/v1.json` → **1,423,817 bytes**,
    **515 paths** (`openapi-live-loopback.json`).
  - **Delta: 0 paths** — F8 adds no routes, and the document is byte-identical in
    size to the E05 document.
- The six E05 Family-home paths are still published with the same operations:
  - `/api/v1/admin/family-home/sections` [`get`, `post`]
  - `/api/v1/admin/family-home/sections/{id}` [`delete`, `get`, `put`]
  - `/api/v1/admin/family-home/sections/{id}/publish` [`post`]
  - `/api/v1/admin/family-home/sections/{id}/visibility` [`post`]
  - `/api/v1/admin/family-home/sections/reorder` [`post`]
  - `/api/v1/family/home/sections` [`get`]
- **E05 surfaces re-proved live after the F8 restart** — anonymous smokes with
  no token and no credentials, on both the public address and host loopback;
  every E05 route/method returned **`401`**:
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
- **No authenticated Production request, mutation, provider call, producer
  trigger, fixture write or OTP was performed.** No account, ticket,
  notification or outbox row was created.

## Production data safety (before → after)

Read-only `psql` before cutover and after the F8 restart on the same Production
database:

| Object | Before | After |
|---|---|---|
| `identity.users` | 15 | **15** |
| `identity.support_tickets` | 1 | **1** |
| `identity."__EFMigrationsHistory"` (rows) | 15 | **15** |
| `notifications.notifications` | 16 | **16** |
| `notifications.email_outbox` | 12 | **12** |
| `notifications.push_outbox` | 4 | **4** |
| `identity.support_ticket_replies` | 0 | **0** |

- Every pre-existing count is identical. The task's "identity.notification/
  email outbox" equivalents live in the `notifications` schema and are listed
  above (`notifications.notifications`, `notifications.email_outbox`,
  `notifications.push_outbox`); there is no separate `identity.*` outbox table.
- No `DROP`, `TRUNCATE`, `DELETE`, `ALTER`, backfill or repair ran; F8 is
  code-only with zero migrations. No backup was restored and no row was written.

## Runtime evidence for the F8 change

- The deployment is healthy end-to-end: the service started cleanly
  (`Application started. Press Ctrl+C to shut down.`, `Hosting environment:
  Production`, `Content root path: /root/sanad-api`), `systemctl is-active` =
  `active`, `NRestarts=0`, and the startup log has no startup/DI exception — so
  the new scoped `INotificationLanguageGateway` registration did not break
  service construction or the notification handlers that depend on it.
- The localized-copy behaviour itself is **not** exercised on Production: no
  producer was triggered and no notification was generated. That behaviour is
  covered by the focused test suite and the local adapter verification
  (13/13), and no authenticated Production request was made.

## Gate evidence carried into the release

- F8's localized notification-copy change is covered by the accepted focused
  test suite plus the local adapter (13/13). Those gates were not re-run here and
  are not claimed to have been re-run; this release verified build/publish, the
  zero-migration restart, 9/9 unchanged heads, the unchanged OpenAPI surface,
  the E05 401 re-proof and the clean start.

## Limitations and open items

- **No authenticated Production exercise of F8.** The recipient-language
  notification copy is not proven end-to-end on Production; it rests on the
  focused tests and the 13/13 local adapter. No producer, notification,
  account or ticket was created (per instruction).
- **Deploy mechanic.** Deployment used the recorded build-machine mechanic
  (publish from a clean detached clone → hash-verified archive → `scp` →
  overlay → restart), not a VPS git clone/pull: `/root/sanad-api` is not a git
  checkout.
- **Verifier revision form.** `Verify-SanadDeployment.ps1` compares the expected
  revision with exact string equality, so it was passed the full 40-character id.
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
  claimed. No other service was started or restarted; F8 is the only release in
  this run.

## Command / exit-code log (non-secret)

- `git ls-remote origin refs/heads/main` → exit 0, tip `7a65168…`;
  `git rev-parse 7a65168` → `7a651684c3618593dd090a979b40602aa9af8920`;
  `git merge-base --is-ancestor` → exit 0.
- `git clone --no-hardlinks` + `git checkout --detach 7a65168` → exit 0; HEAD
  `7a65168…`, working tree clean (0 changes).
- `pwsh docs/internal/tools/Publish-SanadApi.ps1 -Configuration Release` →
  exit 0, 0 warnings/0 errors, manifest revision `7a65168…`, nine heads
  identical to E05.
- `tar -czf` (excluding `appsettings*.json`) → exit 0, 118 entries, 12,039,139
  bytes, SHA-256 `80731ce3…`.
- `scp` archive → exit 0; host `sha256sum` matched `80731ce3…` before use.
- Pre-cutover snapshot (nine heads + counts + notification tables) → exit 0.
- Pre-cutover backup script (dump + `pg_restore --list` + app tar + host-config)
  → exit 0 (dump 725,936 B / 798 TOC entries; app tar 12,037,648 B / 120
  entries).
- Cutover script (stop → overlay → start → poll; fingerprint diff) → exit 0;
  deployment info 200 after 14 s; fingerprints clean.
- Startup log capture → exit 0; 9 contexts 0/0, 0 `Applying migration`, 0
  startup/DI exceptions, 0 destructive statements.
- `Verify-SanadDeployment.ps1` → exit 0 (9/9 heads, revision match, Production,
  nine startup contexts, smoke 200).
- OpenAPI fetch (public and loopback) → HTTP 200; 515 paths each (delta 0); six
  E05 paths present.
- Anonymous smoke (public, `curl`) → exit 0 (9×401, controls 200).
- Anonymous smoke (host loopback, `bash`/`curl`) → exit 0 (9×401, controls 200).
- Post-cutover snapshot → exit 0 (all counts identical).
- Final state snapshot → exit 0 (`active`, `NRestarts=0`, `MainPID=2953952`,
  not a git checkout).
- Nothing failed; no step was improvised or repeated.
