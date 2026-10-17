# FUR-06 — Eligible caregiver payouts release receipt

Date: 2026-10-17 (Africa/Cairo). **Status: Done — accepted, committed,
pushed, published, deployed, and verified.**

## Revision and pre-release evidence

- Implementation/deployed revision: `73390344fcd2350a7ce33e7532e68813d79ba495`
  (`feat: add eligible caregiver payout selector`), pushed to `origin/main`.
- Before commit, the strict solution build passed with 0 warnings/errors and
  focused caregiver payout/Admin authorization tests passed 21/21.
- The guarded full Unit and Architecture attempts failed to connect to
  `testhost`; they were not repeated during publication or deployment. No test
  suite was run as part of publication or deployment.
- Fresh Release package was published from the pushed revision. Manifest
  revision matched. All nine required migration heads were present and matched
  the source-derived heads. Deployment archive SHA-256:
  `964A13BE1A5484791D9540A1B010AF0625BE4EC8B6BEAFE78286E1C75A5337C5`.

## Deployment

- Target: previously owner-designated Sanad VPS, `72.62.92.144:8091`; service
  `sanad-api.service`; working directory `/root/sanad-api`. The running
  environment reports `Production`.
- Pre-deploy service was active with `NRestarts=0` at FUR-05 revision
  `ca2ef2fbe16c395194301a88637aa93008e7066f`; database reachable and all nine
  applied migration heads already matched the FUR-06 package. FUR-06 adds no
  migration.
- A private binary rollback archive was retained on-host at
  `/root/sanad-releases/fur-06-7339034-20261017/api-before.tar.gz`; SHA-256
  `fe0ef69df117db925c2a5ff334641b641152e9893985c1f8e3f30ad1be5d93b5`.
- After startup, `sanad-api.service` was active with `NRestarts=0`. The
  anonymous deployment endpoint reported exact revision
  `73390344fcd2350a7ce33e7532e68813d79ba495`, Production environment,
  `databaseReachable=true`, and 9/9 applied heads matching the package
  manifest. Anonymous `GET /api/v1/lookups/services` returned HTTP 200.
- The endpoint briefly returned HTTP 502 during startup, then recovered. No
  rollback was needed.
- Existing host configuration fingerprints were unchanged:
  - service environment: `e5b4ec5e4f1f3a575eb4023ef6aa081ec87aa614d95a7b93877eb51b86877a65`
  - systemd unit: `c018192e0480b90cdb01af0e6d27c7505bbdca1a87aa9fd732361e74f843f98e`
  - host `appsettings.json`: `200332b434757a0054a3d356c91cde01f2c48712be8aa2bcb0f3e973619f06e3`
  - host `appsettings.Development.json`:
    `e92cd3f4ec157c20bf868f8bd4e5de185903e7a46b24c7839a7fa583f0432cb8`

No database backup was needed because the phase adds no migration and the
pre-deploy endpoint showed every context already at the package head. No
fixture, payout, provider, or configuration data was changed. Startup log
pending/applied counts were not retained in this receipt; the remote applied
heads and reachability were verified before and after deployment.
