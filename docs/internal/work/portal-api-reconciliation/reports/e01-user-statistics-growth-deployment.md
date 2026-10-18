# E01 user statistics and growth — deployment receipt

Date: 2026-10-18 (Africa/Cairo). **Status: deployed and verified.**

## Source and package

- Implementation commit: `d7f1e086043ba4807b1d5b1cd4ab3b10f9e2b857`.
- Documentation receipt commit: `ea14e668a0dee95a7b4aac61e507abb235ba8d7c`.
- Package manifest revision and deployed API revision:
  `ea14e668a0dee95a7b4aac61e507abb235ba8d7c`.
- Built with `Publish-SanadApi.ps1 -Configuration Release` from a clean
  detached checkout of pushed `main`; publication succeeded with warnings
  treated as errors.
- Archive: `Sanad.API-ea14e66.tar.gz`, 118 entries; SHA-256
  `51D2EA289586ED1FE797B0FE7377892CB476C7338C49A47B707E0586D3467D27`.
  Build-machine and server hashes matched. The archive excludes appsettings,
  `.env`, local settings, keys, certificates, and publish settings. Manifest
  contains all nine required migration heads.
- Local package and sanitized startup evidence are retained under the ignored
  `deploy/releases/e01-ea14e66/` directory.

## Target and preflight

- Continued the previously owner-designated Production VPS:
  `72.62.92.144:8091`, service `sanad-api.service`, API directory
  `/root/sanad-api`, environment file `/etc/sanad/sanad.env`.
- Before cutover, the same service was active with zero restarts; the live
  deployment endpoint reported the immediately prior accepted deployment
  revision, Production environment, and reachable database. The effective
  Identity database identity was privately matched to the previous deployment
  receipt without recording connection details here.
- All nine live applied migration heads matched the package manifest before
  cutover. E01 adds no schema or database writes; no database backup or
  migration was required.
- Private recovery copies of the existing API directory and host configuration
  were created and archive-validated under
  `/root/sanad-releases/e01-ea14e66-20261018/`. Service-unit, environment-file,
  and appsettings fingerprints were unchanged after cutover.

## Deployment and verification

- Uploaded only the verified archive to the new private release directory and
  confirmed its SHA-256 on the server. Existing appsettings and host
  configuration were preserved while published application files were
  overlaid; persistent storage, key ring, service unit, and environment were
  unchanged.
- Startup checked all nine database contexts and applied **zero** migrations.
- Service is **active/running** with `NRestarts=0`. The anonymous deployment
  endpoint reports the exact manifest revision, Production environment, and
  reachable database; all **9/9** applied migration heads match the manifest.
- `Verify-SanadDeployment.ps1` passed with the local manifest, expected pushed
  revision, and sanitized startup log through a temporary localhost-only SSH
  tunnel. The anonymous services smoke returned **HTTP 200**. The tunnel was
  closed after verification.
- No authenticated request to `/api/v1/admin/users/statistics` was made in
  Production, consistent with E01's live-feature-request exclusion. Its
  authorization, payload, and schema remain covered by the focused local tests.
- No provider calls, seed, reset, lookup import, or database mutation occurred.

The earlier broad portal response-contract test run had one unrelated owner-
dispute nullable-schema failure (39/40); E01's focused endpoint/OpenAPI tests
passed 67/67 and the strict Release build passed with zero warnings/errors.
