# E02 Owner legal support — release receipt

Date: 2026-10-18. **Status: deployed and verified.**

## Source and package

- Implementation and deployed API revision: `80d87bb1cf9059f3dcb7efbdd8df4ef6219ec627`.
- Pushed to `origin/main`; the working tree's unrelated Owner changes were not
  staged or committed.
- The Release publisher succeeded from a clean checkout of that pushed revision
  with warnings treated as errors. The package manifest revision matches the
  deployed revision and lists all nine registered migration heads.
- Archive: `sanad-api-e02-80d87bb.tar.gz`, 118 entries, 11,824,278 bytes;
  SHA-256 `C10FDFC7401B2FFB4B85D0FCDC2CEA4EF2D37708B6A18B2D91E246D92AF03DAA`.
  The archive excludes all `appsettings*.json`, `.env`, local settings, keys,
  and certificates. The transferred archive hash matched the build-machine hash.
- Local package, manifest, and sanitized startup migration evidence are retained
  under ignored `deploy/releases/e02-80d87bb/`.

## Target and preflight

- Continued deployment on the previously Owner-designated Production VPS
  `72.62.92.144:8091`, service `sanad-api.service`, API directory
  `/root/sanad-api`, with its existing `/etc/sanad/sanad.env`.
- Before cutover, the service was active/running with zero restarts. The
  deployment endpoint reported the prior revision
  `ea14e668a0dee95a7b4aac61e507abb235ba8d7c`, Production environment, and
  reachable database. All nine applied migration heads matched the package's
  required heads.
- Validated, private recovery archives of the existing API directory and host
  configuration were created under
  `/root/sanad-releases/e02-80d87bb-20261018/`. No database backup or migration
  was required: E02 changes no schema or persisted data.

## Deployment and verification

- Only the verified archive was staged and overlaid onto the API directory;
  existing appsettings, environment/service configuration, persistent storage,
  and key ring were preserved. Before/after fingerprints for the service unit,
  environment file, and host appsettings matched.
- Startup checked all nine database contexts and applied **zero** migrations.
- The service is **active/running** with `NRestarts=0`. The standard
  `Verify-SanadDeployment.ps1` passed through a temporary localhost-only SSH
  tunnel:
  - Remote revision exactly matched `80d87bb1cf9059f3dcb7efbdd8df4ef6219ec627`.
  - Production environment and database reachability were confirmed.
  - All **9/9** applied migration heads matched the package manifest.
  - Startup evidence covered all **9/9** contexts with zero migrations applied.
  - Anonymous `GET /api/v1/lookups/services` returned HTTP 200.
- The tunnel was closed after verification. No authenticated Owner legal request
  was made in Production; legal audience, publication, and authorization behavior
  remain covered by focused local tests.
- No provider calls, seeding, database writes, or configuration changes
  occurred.

## Evidence and limitations

- Strict Release build: 0 warnings, 0 errors.
- Focused Legal/Help/Owner API/OpenAPI tests: 20/20 passed.
- Both relevant Postman collections validated as JSON; `git diff --check` clean
  apart from pre-existing CRLF warnings.
- The broader `PortalRemainingResponseContractTests` run had one documented
  unrelated Owner-dispute `checkoutOnUtc` nullability failure (52 passed, 1
  failed). It remains disclosed and unmasked. Full suite and Bruno were not run.
- No database migration was added or applied. No Production feature endpoint
  request was made.
