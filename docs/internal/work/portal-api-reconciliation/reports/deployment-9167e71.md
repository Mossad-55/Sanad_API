# Deployment receipt — 9167e71

Date: 2026-10-15 (Africa/Cairo). Status: API deployment and anonymous verification
Done. Owner explicitly approved publish/deploy and enabled SSH key access.
Source/package/deployed revision:
`9167e7111c5321b063fa9d893001e77dd42a32da`.

## Target and preflight

Owner-designated existing Production-mode developer VPS,
`http://72.62.92.144:8091`; service `sanad-api.service`, existing working directory
`/root/sanad-api`, dotnet DLL invocation. Existing environment file remains
`/etc/sanad/sanad.env`. No environment/secret values printed or copied locally.
Key path used only from the local user's SSH directory; no key committed.

Before deployment: service active, revision `8cbeedd`, databases reachable and
all nine applied migration heads already equal the new manifest. No new schema
change expected. Source clean/synchronized and package manifest verified.
Release archive SHA-256:
`8B86402407ADE9741CFD5150880776078D1846FD627C4A79794F16E5FDD47ED9`.
Server verified the same archive hash before extraction.

## Operations performed

- Uploaded only the API tar archive into a new private release directory.
- Retained private configuration backup, stopped the service, and backed up the
  existing API directory. Recovery files remain on the VPS in
  `/root/sanad-releases/9167e71-20261015/` as `host-config-before.tar.gz` and
  `api-before.tar.gz`. These contain private host data; never publish them.
- Replaced binaries from the verified archive, excluding all appsettings JSON.
  Existing environment file, systemd service and appsettings checksums matched
  before/after. Persistent storage and DataProtection paths not changed.
- Started the existing service. Health was briefly 502 during startup, then
  recovered within the bounded check window. No rollback was needed.
- No lookup import, delete, reset, fixture seeding, configuration edits, CORS
  change or provider test was performed. Normal application jobs resume with
  the service; no business-provider behavior is claimed tested.

## Executed verification

Service: **active/running**, `NRestarts=0`. Deployment endpoint reports
**Production**, exact revision above, `databaseReachable=true`.
Startup migration evidence: **all nine contexts checked, zero migrations applied**.

Ran `Verify-SanadDeployment.ps1` against the public base URL with the matching
local manifest, expected revision and sanitized startup migration log:
**exit 0**, exact remote revision match, **9/9 applied migration heads match**,
services HTTP 200. Additional public OpenAPI and governorates GETs: **HTTP 200**.
Anonymous requests only; no tokens sent over HTTP. Sanitized startup evidence
retained locally under the ignored `deploy/releases/9167e71/` directory.

Publication gates remain: strict build zero warnings/errors; full unit
3358/0/8 explained skips; architecture 1/1; focused 113/113; mapping 556/620/0/0.
No new code/build input changed during deployment. This receipt is a later
docs-only record, not a new binary release or another publish/deploy instruction.

## Remaining scope

The collected geography catalogue is **not loaded on the VPS**; deploying the
API alone cannot populate it. The standalone tool and candidate remain local.
Separate exact data-operation authority, reference check, backup and maintenance
are needed before replacing old locations. No new API runtime configuration was
needed. Temporary `SANAD_GEOGRAPHY_CONNECTION` is needed only for that future tool
invocation. Caregiver operator evidence, authenticated frontend checks and
separate Chat runtime/device/soak coverage are not closed by these smoke GETs.
