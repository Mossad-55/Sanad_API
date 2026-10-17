# FUR-04 — Care Home readback release receipt

Date: 2026-10-17 (Africa/Cairo). **Status: Done — accepted, committed, pushed,
published, deployed and verified.**

Traceability: [approved contract](../contracts/fur-04-care-home-readback.md) →
[worker delivery and correction record](fur-04-worker-delivery.md) → Mastermind
source/test/document review → this release receipt. Phase commit and deployed
revision: `31b13ccbb548b27267403e2db686e2beea63a732` (`Complete FUR-04 Care Home
readback`), pushed to `origin/main`; post-push `HEAD` and `origin/main` match.
The owner explicitly authorized the phase commit, push and Production
deployment on 2026-10-17.

## Review and gates

- Mastermind reviewed the changed implementation, tests, API schema, guides,
  Postman examples, dated Admin and Care Home Owner handoffs, and the worker's
  correction record. C1–C4 were accepted. The delivery report's unauthorized
  local database setup deviation remains disclosed; it is not erased by this
  release. No database action was performed during deployment.
- Strict solution build with warnings treated as errors: exit 0, 0 warnings,
  0 errors. Guarded full Unit suite (normal-access rerun after restricted
  testhost control-channel failures): **3,475 passed / 0 failed / 31 skipped**.
  The guarded child confirmed integration targets absent, all nine module
  targets isolated and migrations/seeders disabled. Guarded Architecture:
  **1 passed / 0 failed / 0 skipped**.
- API contract mapping script: exit 0. The phase adds no route; its reported
  missing/orphan counts are not treated as proof because the script's
  comparison variables are known to be unpopulated. The last independently
  verified normalized route-set comparison remains 556 unique routes on each
  side, zero missing/orphaned. `git diff --check` passed before the phase commit.
- Worker-reported evidence remains attributed to the worker: 15 focused tests,
  131 correction tests, and seven PostgreSQL proof tests on
  `SanadFur04VerifyDb`. The seven PostgreSQL tests were not rerun during C1
  corrections; no database was contacted for this release.

## Package and deployment

- Fresh Release package built from the pushed commit at
  `deploy/publish-out-fur-04-31b13cc/`; publisher exited 0. Manifest revision
  exactly matches `31b13ccbb548b27267403e2db686e2beea63a732`. All nine manifest
  heads were independently inspected and match the committed latest migration
  sources. FUR-04 adds no migration; the nine heads are unchanged from the prior
  release. Package contains no `appsettings*.json`, `.env`, keys/certificates
  or local settings. Final transferred archive: 118 entries, SHA-256
  `B7A53570757018C5DEE9A41CABB4A20CECB4496AB67C8188ED6166612282CC96`.
- Target: previously owner-designated Production-mode developer VPS
  `72.62.92.144`, service `sanad-api.service`, working directory
  `/root/sanad-api`. Immediately before deployment the service was active with
  `NRestarts=0`, deployed revision `5cd3526b53e8555d3233810b8a024fcb6aa0322e`,
  Production environment and database reachable. The effective database
  identity was privately verified as `localhost:5433/SanadDb`; module-specific
  connection overrides were absent. No connection string or credential is
  recorded here.
- A private, validated API-directory rollback archive was retained on-host at
  `/root/sanad-releases/fur-04-31b13cc-20261017/api-before.tar.gz`; SHA-256
  `e1558eb6c9233adb1eb9ce6815fde9182e0f381ecdd3f82ca8c92ab4a2ef6e13`.
  Existing env, systemd unit and appsettings were preserved (binaries overlaid;
  no configuration files transferred). Their SHA-256 fingerprints before and
  after deployment were unchanged:
  - service environment: `e5b4ec5e4f1f3a575eb4023ef6aa081ec87aa614d95a7b93877eb51b86877a65`
  - systemd unit: `c018192e0480b90cdb01af0e6d27c7505bbdca1a87aa9fd732361e74f843f98e`
  - host `appsettings.json`: `200332b434757a0054a3d356c91cde01f2c48712be8aa2bcb0f3e973619f06e3`
  - host `appsettings.Development.json` (preserved, not transferred):
    `e92cd3f4ec157c20bf868f8bd4e5de185903e7a46b24c7839a7fa583f0432cb8`
- Startup log confirms all nine contexts checked and **zero migrations applied**:
  Identity, CMS, Caregivers, Families, Care Homes, Notifications, Community,
  Chat and Finance. No database backup/migration/fixture action was needed or
  performed for this no-migration phase.
- The first immediate proxy probe returned 502 before the newly started
  application had finished binding; no failure loop or restart was observed.
  After startup completed, both on-host direct and proxy lookups returned HTTP
  200. Final service remained active with `NRestarts=0`.
- Standard `Verify-SanadDeployment.ps1` passed with exit 0 through a temporary
  localhost-only SSH tunnel, using the fresh manifest and sanitized startup
  log. It verified exact remote revision, Production environment, database
  reachability, all **9/9 required/applied migration heads**, and anonymous
  `GET /api/v1/lookups/services` HTTP 200. The tunnel was closed afterward.

## Scope and limitations

No real OTP/SMS/email/payment/push call, authenticated business mutation,
fixture, reset, CORS change, storage/key-ring change or provider configuration
change was made. The unrelated caregiver-completion `409 Bookings.AllowanceExceeded`
path was not exercised against Production; the FUR-04 guarded suite and existing
contract/docs remain its non-production evidence. Direct public HTTP behavior
from this build machine remains unproven and was not retried; tunneled/on-host
verification is the deployment evidence. The exact original frontend-reported
issue remains unproven; the frontend repositories were not inspected.

The worker's unauthorized creation/drop of three unintended lowercase
`sanadfur04verifydb` local databases is preserved in the delivery report. No
further local database action occurred. FUR-04 is Done; FUR-05 has not started.
Next: prepare a bounded FUR-05 read-only investigation and contract proposal
under the standing workflow; do not issue an implementation prompt until the
owner approves its complete contract.
