# FUR-07 Community moderation — release receipt

Date: 2026-10-17 (Africa/Cairo). Status: **Done**.

Accepted source revision `303a1319dd6da4752b727e93aec4dec0296b95d4`
(`feat: add community moderation`) was committed and pushed to `origin/main`.
The package was published from a clean detached checkout of that exact pushed
revision because two unrelated local edits were present in the main worktree.
The publisher's Release compile completed successfully; no tests were rerun
during packaging or deployment. The manifest revision matched the commit and
contained all nine module migration heads.

## Target and preflight

Deployed to the previously owner-designated Production VPS
`72.62.92.144:8091`, service `sanad-api.service`, working directory
`/root/sanad-api`, and existing environment file `/etc/sanad/sanad.env`.
The service was active before deployment, with zero restarts. The predeploy
deployment endpoint reported revision `73390344fcd2350a7ce33e7532e68813d79ba495`,
Production environment, and reachable database. All existing applied heads
matched the previous manifest. The sole expected pending migration was
Community `20261017154400_AddCommunityModeration`; the prior Community head was
`20261010004134_AddCommunityCategories`.

The live service process was privately inspected with credential values
withheld. All nine module connections resolve to the existing
`localhost:5433/SanadDb`. A custom-format PostgreSQL backup was created before
cutover and validated with `pg_restore --list`: 665,272 bytes,
SHA-256 `a8a5989f543750a91fe5798b236d6b1393f622281f3067b00330a319bf5bd662`,
729 archive entries. It remains at the private VPS path
`/root/sanad-releases/fur-07-303a131-20261017/database-before.dump`.

The prior API directory and host configuration were separately archived and
validated at the same private release path as `api-before.tar.gz` and
`host-config-before.tar.gz`. Their SHA-256 hashes are respectively
`bd2f69d0fff90bbfc7918e964d5b8087f9e549e46454ec464dbf10b668716775` and
`addf0c3e68364000acb681e92694b193f872e6ea3e43b323da2f0eb54ddc9132`.
The transferred package archive was 11,815,067 bytes with SHA-256
`ce0889b9046cb7e5b12bf6e9682da966f44f97019b1617ad4429803d4b53d89c`; the
server-side hash matched. The archive had 119 entries and no `.env`, local
settings, development settings, keys, or certificates.

## Deployment and verification

Only application files were overlaid; existing `appsettings*.json`, service
unit, environment file, storage, and key-ring configuration were preserved.
The API started in Production and applied exactly one migration:

- Community: **1 applied** (`20261017154400_AddCommunityModeration`).
- Identity, Cms, Caregivers, Families, CareHomes, Notifications, Finance, and
  Chat: **0 applied** each.

Post-deployment service status was active with `NRestarts=0`. The environment,
systemd unit, and both existing appsettings fingerprints matched their
predeploy values. The deployment endpoint reported the exact package revision
and `databaseReachable=true`.

Direct public HTTP was intercepted by the ISP's HTML redirect page, so the
single documented verifier was run through a temporary SSH localhost tunnel.
It exited 0: exact manifest/deployed revision match, Production environment,
all **9/9 applied migration heads match**, all nine startup context checks were
present, and `GET /api/v1/lookups/services` returned **HTTP 200**. The
credential-free startup evidence is retained locally at
`deploy/fur-07-303a131-startup-sanitized.log`; the private full deployment
artifacts remain on the VPS. No provider calls, fixture writes, data imports,
database reset, or unrelated feature smoke were performed.

The worker's accepted strict build and 153 focused Community/model tests remain
the source verification evidence; neither was repeated during release.

## Follow-up

FUR-07 is Done. Next: discuss the FUR-08 contract and whether any later phase
can run in parallel before issuing another worker assignment.
