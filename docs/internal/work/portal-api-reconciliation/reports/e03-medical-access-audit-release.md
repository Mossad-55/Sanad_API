# E03 caregiver medical-access audit — release receipt

Date: 2026-10-18. **Status: deployed and verified.**

## Source and package

- Implementation commit: `9ab790da692129cdb7a15e288cfc5206f014d686`.
- Package manifest and deployed API revision: `845f2920bd68597b15698643e489210d0782557a`.
  The package was built from a clean checkout of pushed `main` at that revision;
  the intervening commit adds only the Admin feature-guide link.
- Release publisher succeeded with warnings treated as errors. The manifest
  contains all nine registered module migration heads, including Families
  `20261018170707_AddCaregiverMedicalAccessAudit`.
- Archive: `sanad-api-e03-845f292.tar.gz`, 118 entries, 11,835,822 bytes;
  SHA-256 `3CCA297C27406F2E1A53EE8935186DC902D8D4CB967AF828D5C1A018417BAB2D`.
  `appsettings*.json`, `.env`, local settings, keys, and certificates are
  excluded. The transferred archive hash matched the build-machine hash.
- The package, manifest, and sanitized startup migration evidence are retained
  under ignored `deploy/releases/e03-845f292/`.

## Target, migration backup, and preflight

- Continued deployment on the previously Owner-designated Production VPS
  `72.62.92.144:8091`, service `sanad-api.service`, working directory
  `/root/sanad-api`, with its existing `/etc/sanad/sanad.env`.
- Before cutover, the service was active/running with zero restarts. The
  deployment endpoint reported revision `80d87bb1cf9059f3dcb7efbdd8df4ef6219ec627`,
  Production environment, and reachable databases. Eight context heads matched
  the package; Families was at `20261013005844_AddBookingAreaServiceAttribution`,
  with E03 `20261018170707_AddCaregiverMedicalAccessAudit` as the only pending
  head.
- The effective Families database identity was privately verified against the
  existing loopback PostgreSQL target; no credentials or connection strings are
  recorded here.
- Before migration, private validated backups were created under
  `/root/sanad-releases/e03-845f292-20261018/`: the current API directory, host
  service/environment configuration, and a custom-format database dump. The
  database dump was validated with `pg_restore --list` (741 TOC entries); all
  backup files are protected by a private release directory. No backup was
  restored.

## Deployment and verification

- Only the verified archive was staged and overlaid into `/root/sanad-api`.
  Existing host appsettings, service unit, environment file, storage, and key
  ring were preserved. Before/after fingerprints for service, environment, and
  appsettings matched.
- Production startup checked all nine contexts and applied exactly **one**
  migration: Families `20261018170707_AddCaregiverMedicalAccessAudit`. The other
  eight contexts applied zero migrations.
- The service is **active/running** with `NRestarts=0`. The standard
  `Verify-SanadDeployment.ps1` passed through a temporary localhost-only SSH
  tunnel:
  - Remote revision exactly matched package revision
    `845f2920bd68597b15698643e489210d0782557a`.
  - Production environment and database reachability were confirmed.
  - All **9/9** applied migration heads matched the package manifest.
  - Startup evidence covered all nine contexts with the expected migration
    counts above.
  - Anonymous `GET /api/v1/lookups/services` returned HTTP 200.
- The tunnel was closed after verification. No authenticated E03 Admin audit or
  Family grant request was made in Production.
- No provider call, seed, reset, or database data mutation occurred; the only
  database change was the approved additive Families audit table migration.

## Verification and limitations

- Strict Release solution build: 0 warnings, 0 errors.
- Focused E03 Families/Admin/retention/OpenAPI tests: 39/39 passed, 0 skipped.
- Both Postman collections parse as UTF-8 JSON. Removing the single E03 Admin
  request leaves the parsed Admin collection identical to `HEAD`, including its
  Arabic content; the E03 Admin and existing Family grant routes map to their
  controller routes.
- `git diff --check`: exit 0; only pre-existing LF-to-CRLF working-copy notices.
- Full suite and Bruno were not run. No live authenticated feature behavior was
  requested or claimed.
