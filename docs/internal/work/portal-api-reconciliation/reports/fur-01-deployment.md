# FUR-01 — phase deployment receipt

Date: 2026-10-16 (Africa/Cairo).
**Status: Done — accepted complete phase committed, pushed, published and deployed.**

Implementation/package/deployed revision:
`f9231fb24e02ed72e7d08903c55f3e3193201b83`.
Later documentation-only receipt commits do not change this API binary revision.

Standing owner order: after a complete phase passes Mastermind review and all
required gates, commit/push, publish/deploy and verify before advancing.
Local acceptance: [final review appendix](fur-01-mastermind-review.md).

Target preflight verified on the owner-designated existing VPS via SSH:
`http://72.62.92.144:8091`, service `sanad-api.service`, Production revision
`9167e7111c5321b063fa9d893001e77dd42a32da`, service active, database reachable,
all nine migration heads match the required unchanged identifiers. This phase
adds only API metadata; no config, environment, CORS, storage or migration delta.

Local release gates: strict build zero warnings/errors, guarded full unit
3421 passed / 0 failed / 8 explained skips, architecture 1/1. Actual route-key
comparison zero missing/orphaned. Raw owner intake remains local-only.

## Executed publication and deployment

Main was clean and synchronized with origin before publication. The owner raw
intake remained untouched and local-only via `.git/info/exclude`; sanitized
audit/planning/handoff artifacts were included in the scoped phase commit.
No hook bypass, force-push or branch/worktree operation occurred.

Release publication succeeded with TreatWarningsAsErrors enabled and no
warnings/errors. Archive: `sanad-api-f9231fb.tar.gz`, **11,743,634 bytes**,
118 entries; SHA-256:
`6787B555624487E4B2C5B501EBAC67439C9C8CFAFD584E6B664274E0575FB07F`.
Local/server archive hashes matched before extraction. No environment files,
keys/certs or Development settings transferred; all appsettings JSON excluded
from the deployment archive to preserve existing server configuration.

On the already verified target, retained private API and host-config rollback
backups, stopped the existing service, extracted verified binaries into the
same validated API directory, and started the same service. A rollback trap
protected the operation; no rollback was required. No files/data were deleted
or reset. Persistent storage/key-ring configuration remained unchanged. Existing
env file, systemd service and appsettings checksums matched before and after.

## Executed verification

- Service **active/running**, `NRestarts=0`.
- Exact deployed revision above, environment **Production**, database reachable.
- All **9/9 required/applied latest migration heads match**, unchanged.
- Sanitized startup log: all nine contexts checked, **zero migrations applied**.
- Actual deployed OpenAPI: **29/29 phase operations** have the required success
  status/shape; 201 operations have no spurious 200; removals have no 204 body
  schema; nested visit kind/status refs and values verified.
- Anonymous services/governorates return HTTP 200; governorates count remains 27.
- Standard `Verify-SanadDeployment.ps1` passed with expected revision, local
  manifest and sanitized startup log through a temporary localhost-only SSH
  tunnel to the exact service. Tunnel closed after verification.

Direct HTTP from this machine to the public IP returned HTML rather than API
JSON, reproducing the already known local network/redirection limitation.
That attempt failed JSON parsing and is **not** counted as a public-path pass.
On-host and SSH-tunneled checks passed; external-client reachability from this
machine remains unproven. Do not present it as a newly diagnosed API defect or
change CORS/proxy configuration to hide that limitation. Frontend developers
can confirm their own normal network path when refreshing the released schema.

Discarded environment attempts: restricted local test-runner communication
timeouts and a restricted publication attempt. Successful guarded test reruns
and normal-access publication are the recorded gates, not those failed attempts.

## Scope and remaining work

No new endpoint, runtime behavior, permission, configuration or schema changes.
No seeding, data repair, lookup import, provider test or authenticated business
smoke was conducted. Normal production background jobs resume with the service;
no end-to-end SMS/payment/push/device result is claimed.

The pre-existing mapping-script comparison defect is documented with independent
zero-difference evidence, not repaired in this phase. FUR-02 and all later
feature contracts remain unstarted; next action is owner discussion/contract lock
for verification continuity, not automatic implementation.
