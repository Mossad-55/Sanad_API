# E04 platform controls and public business status — release receipt

Date: 2026-10-19. **Status: deployed and verified.**

## Source and package

- Implementation commit: `bdc06057caa2c4b4589565c8c7c456ea57bdf8e9` (pushed to
  `origin/main`). Two preparatory commits precede it in the same push:
  `1ae5c50` (the Owner-approved documentation cleanup) and `07e4da9` (the
  approved E05/E06 scope briefs). None of the three changes API behavior outside
  E04.
- Package manifest and deployed API revision:
  `bdc06057caa2c4b4589565c8c7c456ea57bdf8e9`. The publisher derived the
  revision from pushed `main` HEAD and wrote
  `sanad-deployment-manifest.json` with all nine registered module migration
  heads, including the new Finance head
  `20261019061622_AddE04PlatformControls`.
- Archive: `sanad-api-e04-bdc0605.tar.gz`, 11,871,591 bytes, 110 staged files;
  SHA-256
  `93A5CD3F21425BCF0638DFA66678C3410615A4CF69F13376A92823FDB089A474`. The
  transferred archive hash matched the build-machine hash before it was used.
  `appsettings.Development.json`, `.env`, local settings, keys and
  certificates are not in the package.
- The publisher compiles the API as part of `dotnet publish`; no second
  solution build was run for packaging. The package was built from the working
  tree whose `src/` tree was identical to pushed `main` (only unrelated
  documentation/tooling edits remained uncommitted and are not part of the
  package output).

## Target, migration backup and preflight

- Continued deployment on the previously Owner-designated Production VPS
  `72.62.92.144:8091`, service `sanad-api.service`, working directory
  `/root/sanad-api`, environment file `/etc/sanad/sanad.env`.
- Before cutover the service was active/running with zero restarts and the
  deployment endpoint reported revision
  `845f2920bd68597b15698643e489210d0782557a`, environment `Production` and
  reachable databases — the same target and revision recorded by the E03
  receipt, so the authorized target was revalidated rather than assumed.
- Eight context heads matched the package; Finance was at
  `20261010143327_AddCaregiverPayoutPolicyDescriptions` with the E04 head
  `20261019061622_AddE04PlatformControls` as the only pending migration.
- Only `ConnectionStrings__IdentityDatabase` is configured on the host, so the
  Finance context resolves to the same effective database as Identity. That
  identity was confirmed privately on the host; no connection string, user,
  host or password is recorded here.
- Before the restart, private validated backups were created under
  `/root/sanad-releases/e04-bdc0605-20261019/` (mode `700`): the current
  application directory as `app-backup.tar.gz`, the host service unit and
  environment file, and a custom-format database dump (674,193 bytes) whose
  `pg_restore --list` produced 762 TOC entries. No backup was restored.

## Deployment and verification

- Only the hash-verified archive was staged and overlaid into
  `/root/sanad-api`. The host `appsettings*.json`, service unit, environment
  file, storage and key-ring configuration were preserved: SHA-256 fingerprints
  of the application settings, the service unit and the environment file are
  identical before and after the cutover.
- Production startup checked all nine contexts and applied exactly **one**
  migration: Finance `20261019061622_AddE04PlatformControls`. The other eight
  contexts applied zero migrations. The migration creates the two `finance`
  control tables and seeds exactly one deterministic `Operational` row.
- The service is **active/running** with `NRestarts=0`. The standard
  `Verify-SanadDeployment.ps1` passed through a temporary localhost-only SSH
  tunnel:
  - Remote revision exactly matched the package revision
    `bdc06057caa2c4b4589565c8c7c456ea57bdf8e9`.
  - Production environment and database reachability were confirmed.
  - All **9/9** applied migration heads matched the package manifest.
  - Startup evidence covered all nine contexts with the expected migration
    counts above.
  - Anonymous `GET /api/v1/lookups/services` returned HTTP 200.
  - The tunnel was closed after verification.

## E04 feature smoke

- Anonymous `GET /api/v1/platform/status` returned HTTP 200 with
  `{"state":1,"maintenanceMessageArabic":null,"maintenanceMessageEnglish":null,"availablePaymentMethods":[1,2]}`.
  This proves the new public route is live, the seeded `Operational` control row
  exists, and Card and Wallet are both enabled and configured on the host.
- No authenticated Admin request, no control mutation, no provider call, no
  data repair and no seed were performed. The maintenance path, the
  SuperAdmin control routes, the disabled-method path and the callback
  continuity behavior are covered by the accepted focused tests, not by a live
  Production request.

## Gate evidence carried into the release

- Strict Release solution build: 0 warnings, 0 errors.
- Focused E04 and affected-regression test set in a process-local isolated
  child (dummy `ConnectionStrings__*Database` for all nine modules, startup
  migrations and seeders disabled, temporary storage root): Failed 0, Passed
  403, Skipped 0.
- `dotnet ef migrations has-pending-model-changes --context FinanceDbContext`
  → no model changes.
- Consumer delivery: 11 Markdown links/anchors resolve, four Postman
  collections parse as UTF-8 JSON, and an explicit controller↔Postman route
  comparison reports 0 missing mappings (9 pre-existing Care Homes media
  orphans are outside this phase and unchanged). `git diff --check` clean.

## Limitations

- Direct public HTTP from the build machine is still unproven for this host
  path; verification used the temporary localhost-only SSH tunnel, as in the
  E03 release.
- The whole Unit suite, `Test-SanadPortalGate.ps1`, Bruno and live provider
  flows were not run and are not claimed.
- The legacy `Verify-ApiContractMapping.ps1` still does not compute its
  missing/orphan sets; its exit code is not coverage evidence. The tool itself
  was not repaired in this phase.
