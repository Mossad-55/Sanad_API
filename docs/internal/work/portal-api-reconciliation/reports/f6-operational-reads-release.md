# F6 minimal operational reads — release receipt

Date: 2026-10-20 (Africa/Cairo). **Status: deployed and verified.**

## Source and package

- Feature commit: `2db2735` "feat(f6): minimal operational reads" (21 files).
  Documentation closeout/acceptance commit: `bc32be0` "docs: accept F6 and
  record its release handback". Both were already pushed; `origin/main` tip was
  `bc32be0` and the release packaged that exact tip.
- Deployed API revision and package manifest revision:
  `bc32be0664d0f7d42c7c0406d63c73c0edae5a98`. The publisher derived the revision
  from pushed `main` HEAD and wrote `sanad-deployment-manifest.json` with all
  nine module heads; they are byte-identical to the prior (F5) manifest.
- Archive: `sanad-api-f6-bc32be0.tar.gz`, 11,942,515 bytes, 118 entries; SHA-256
  `a938ea308f5d5c0af5e099ab5c5e4a24a780e6efc0f4b86e0287ad96990c2d3c`. The
  transferred archive hash matched the build-machine hash before use. The staged
  entry set is byte-identical to the F5 package (0-entry diff); `appsettings*.json`,
  `.env`, local settings, keys and certificates are not in the package.
- Built with `Publish-SanadApi.ps1 -Configuration Release` from a clean detached
  checkout (`deploy/releases/f6-bc32be0/source`) of the pushed revision with a
  clean working tree; the publisher compiled the API as part of `dotnet publish`
  (warnings treated as errors). No second solution build was run.

## No migration this release (explicit)

- **F6 adds no migration and no schema change — it is a code-only deploy.** No
  `dotnet ef` update was run, and **no database dump was taken**, because the
  production startup migration policy had nothing to apply. The backup recorded
  below is the **application** backup only; that omission is deliberate, not an
  oversight.
- The package manifest's `requiredMigrations` map is identical to the F5
  manifest (same nine heads), and the live deployment endpoint reports
  `appliedMigrations` equal to `requiredMigrations` for all nine contexts.
- No Production data was reset, dropped, deleted or backfilled.

## Target, application backup and preflight

- Continued the previously Owner-designated Production VPS `72.62.92.144:8091`,
  service `sanad-api.service`, directory `/root/sanad-api`, environment file
  `/etc/sanad/sanad.env`. `/root/sanad-api` is **not** a git checkout.
- Before cutover the service was active/running with zero restarts and the
  deployment endpoint reported revision
  `99979272e7ef5257b7eb9b66e16bd337da77043f` (the prior F5 release),
  `Production`, databases reachable — the authorized target was revalidated,
  not assumed.
- **Application backup first**, before any cutover, under
  `/root/sanad-releases/f6-bc32be0-20261020/` (directory mode `700`):
  - `app-backup-before-f6.tar.gz` — current application directory, 11,923,175
    bytes, 120 entries, SHA-256
    `71ed59b70cfbdb4e72b49bb210ec4c372093df784bc8b006226a2d78fe9a410e`;
    listing read back to confirm it is a readable archive.
  - `host-config/` — private copies of the service unit and environment file
    (files mode `600`).
  - **No `database-before-*.dump` was created** (no migration runs this release;
    see above).
  - No backup was restored.

## Deployment and verification

- Only the hash-verified archive was transferred and overlaid into
  `/root/sanad-api`. The host `appsettings*.json`, service unit, environment
  file, storage and key-ring configuration were preserved: SHA-256 fingerprints
  of `appsettings.json` (`200332b4…`), `appsettings.Development.json`
  (`e92cd3f4…`), the service unit (`c018192e…`) and the environment file
  (`e5b4ec5e…`) are identical before and after cutover.
- The service was stopped, the verified files overlaid, and the service started.
- **No migration was applied by the production startup migration policy on
  restart.** Production startup checked all nine contexts and applied **zero**
  migrations:
  - Identity `20261020191733_AddSupportTicketWorkflow` — 0 applied (unchanged).
  - Cms `20261012071340_AddLandingSectionHistory` — 0 applied.
  - Caregivers `20261009004058_AddCaregiverPayoutAccountReview` — 0 applied.
  - Families `20261018170707_AddCaregiverMedicalAccessAudit` — 0 applied.
  - CareHomes `20261012143753_AddCareHomeRoomTypeAmenitiesAndMedia` — 0 applied.
  - Notifications `20261010010618_AddEmailHtmlSnapshots` — 0 applied.
  - Community `20261017154400_AddCommunityModeration` — 0 applied.
  - Finance `20261019061622_AddE04PlatformControls` — 0 applied.
  - Chat `20261013171755_AddAttachmentCleanupIndex` — 0 applied.
- Service state after cutover: **active/running**, `NRestarts=0`, started
  `2026-10-20 20:36:50 UTC`. The deployment endpoint answered HTTP 200
  (`Production`, databases reachable, 9/9 applied heads match the manifest).
- Standard `Verify-SanadDeployment.ps1` passed (exit 0) through a temporary
  localhost-only SSH tunnel with the local manifest, expected revision `bc32be0`
  and the sanitized startup log:
  - Remote revision exactly matched
    `bc32be0664d0f7d42c7c0406d63c73c0edae5a98`.
  - Environment `Production`; databases reachable.
  - All **9/9** applied migration heads matched the manifest — every context,
    including Identity, unchanged.
  - Startup evidence covered all nine contexts with 0 applied each.
  - Anonymous `GET /api/v1/lookups/services` returned HTTP 200.
  - The tunnel was closed after verification.
- The target also answers direct public HTTP: `http://72.62.92.144:8091/api/v1/lookups/services`
  → 200 (HTTPS on that port is not served).

## F6 live surface (OpenAPI + anonymous smokes)

Read from the live document at **`GET /openapi/v1.json`** (HTTP 200,
1,362,104 bytes, 499 paths) on the deployed revision:

- Published F6 paths, all present and **GET-only** (no POST/PUT/PATCH/DELETE):
  - `GET /api/v1/admin/families` — family register list.
  - `GET /api/v1/admin/families/{familyId}` — family detail.
  - `GET /api/v1/admin/care-homes/visits` — Care Home visit register list.
  - `GET /api/v1/admin/care-homes/visits/{visitId}` — Care Home visit detail.
- `BookingDetailResponse` gained exactly five additive nullable members and none
  of them is in the schema `required` list: `cancellationActorSide`
  (`oneOf [null, BookingCancellationActorSide]`), `cancellationAction`
  (`oneOf [null, BookingCancellationAction]`), `cancellationReasonCategory`
  (`type ["null","string"]`), `refundEntitlement`
  (`oneOf [null, BookingRefundEntitlement]`) and `refundDecisionReason`
  (`oneOf [null, BookingRefundDecisionReason]`). The pre-existing `refundState`
  member is unchanged in shape.
- Anonymous runtime smokes on the new admin routes returned **401** with no token,
  on both the host loopback and the public target:
  - `GET /api/v1/admin/families` → **401**
  - `GET /api/v1/admin/families/{id}` → **401**
  - `GET /api/v1/admin/care-homes/visits` → **401**
  - `GET /api/v1/admin/care-homes/visits/{id}` → **401**
  - Controls: `GET /api/v1/lookups/services` → 200,
    `GET /api/v1/platform/status` → 200.
- No authenticated Production request, mutation, provider call, fixture write or
  OTP was performed.

## Gate evidence carried into the release

- Mastermind acceptance (recorded in the F6 brief): strict solution build
  0 warnings/0 errors; 31/31 F6 focused tests; whole unit project 4021 passed with
  only the retained pre-existing baseline failure; `tools/Invoke-F6LocalHttpVerification.ps1`
  exit 0 with 27/27 real local HTTP checks. Reused unchanged; not re-run here.

## Deviations and limitations

- **Deploy mechanic.** Deployment used the recorded build-machine mechanic
  (publish from a clean clone → hash-verified archive → `scp` → overlay →
  restart), not a VPS git clone/pull: `/root/sanad-api` is not a git checkout,
  and this matches the F2/F3/F4/F5 receipts and E04.
- **Cutover invocation.** During the cutover, the first remote invocation
  stopped the service and then aborted on a shell quoting artifact before
  overlaying; nothing had been overlaid. The overlay and restart were then
  completed in a second invocation. The net mechanic and end state are exactly
  the standard stop → overlay → start, and no data or configuration was touched
  in the aborted invocation; it is recorded here for completeness.
- **No migration mechanic.** Unlike F5, this release ran **no** migration: the
  production startup policy found all nine contexts already at their required
  heads. No `dotnet ef database update` was issued.
- **Health endpoint.** `/health` is not a published route (HTTP 404 on this
  build); health/status evidence is the deployment info endpoint,
  `GET /api/v1/lookups/services` (200) and `GET /api/v1/platform/status` (200).
- **No authenticated Production requests** were made (same precedent as
  F2/F3/F4/F5): authorization, payload and schema behavior rest on the accepted
  focused tests and the live OpenAPI/401 smokes.
- The live OpenAPI document publishes no `security`/`securitySchemes` metadata,
  so the anonymous-vs-signed-in distinction is proven by the runtime 401/200
  smokes above, not by the document.
- The whole-unit suite, Bruno, and live provider flows were not run and are not
  claimed.
