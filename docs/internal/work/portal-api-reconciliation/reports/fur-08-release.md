# FUR-08 Owner dispute readback — release receipt

Date: 2026-10-18. Status: **Done**.

Accepted implementation revision `14757ccce6ad1fb59482ca26d4df5e17ca8c56d9`
(`feat(care-homes): add owner dispute readback`) was pushed to `origin/main`.
The Release package was built from a clean checkout of that exact pushed
revision. The package manifest revision matched the source commit, listed all
nine module migration heads, and the archive contained no development settings,
local settings, keys, certificates, or `.env` files. Package archive:
`Sanad.API-14757cc.tar.gz`; SHA-256
`87adfb6dc2c490c95627c9b71c92ea76f4e32266fb82f60ca95e64be7c59e418` (119
entries). The strict Release publisher completed successfully.

## Target preflight and backup

Deployment continued on the previously owner-designated Production VPS
`72.62.92.144:8091`, service `sanad-api.service`, working directory
`/root/sanad-api`, with its existing `/etc/sanad/sanad.env`. Before cutover the
service was active, database reachable, and all nine applied migration heads
matched the current deployed revision `303a1319dd6da4752b727e93aec4dec0296b95d4`.
The target's unique FUR-08 release directory did not exist before setup.

Recoverable archives of the current API directory and host configuration were
created on the target and validated by listing their contents. No database
backup was created because FUR-08 adds no migration or database write. The
package archive was transferred to the target and its SHA-256 matched the
build-machine archive.

## Deployment and verification

Only published application files were overlaid onto `/root/sanad-api`, excluding
`appsettings*.json`. Existing environment/service configuration, storage, and
key-ring files were preserved. Pre/post SHA-256 fingerprints of the service
unit, environment file, and existing appsettings files matched; values and hashes
are intentionally not recorded here.

The service started in Production and remained active/running with
`NRestarts=0`. Startup checked all nine contexts and applied **zero** migrations.
The standard deployment verifier exited 0 through a temporary SSH tunnel:

- Remote revision exactly matched package/source revision
  `14757ccce6ad1fb59482ca26d4df5e17ca8c56d9`.
- Production environment and database reachability were confirmed.
- All **9/9** applied migration heads matched the package manifest.
- All **9/9** startup context checks were present; each reported zero migrations
  applied.
- Anonymous `GET /api/v1/lookups/services` returned HTTP 200.

Direct public HTTP from the build machine was redirected by the ISP to an HTML
page; the documented SSH tunnel provided the successful verification path. No
live authenticated FUR-08 endpoint request or Bruno run was performed. The
focused worker-reported tests are recorded in the [delivery report](fur-08-worker-delivery.md).

No database data, provider, or production configuration changes were made. No
commit/push beyond the scoped phase and receipt commits, no unrelated worktree
changes were staged, and no later phase was started.
