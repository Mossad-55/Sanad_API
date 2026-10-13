# Remaining VPS readiness — worker report (Prompt 5)

Branch: `worker/chat-plus-vps-readiness`.
Base SHA: `1120434b23d0a8794dba01a27625a46e68768903` (= `origin/main` at branch time; tree clean, no other branches touched).
Implementation commit (final code state): `873c15ebada47f469bb8b741c6fcc29f578bdf94`.
This report is committed separately; the branch tip SHA is given in the worker handoff.

Owned manifest (20 files; no `main` merge/push, no VPS login/write, no
package deployment, migrations, seeding, or provider sends in this task):

- `src/API/Sanad.API/Options/SanadDataProtectionOptions.cs` (new)
- `src/API/Sanad.API/Options/StorageRootPolicy.cs` (new)
- `src/API/Sanad.API/Program.cs` (persistent key-ring wiring)
- `src/API/Sanad.API/ApplicationBuilderExtensions.cs` (storage-root guard)
- `src/API/Sanad.API/DependencyInjection.cs` (deployment-info DI only)
- `src/API/Sanad.API/Deployment/DeploymentInfo.cs` (new: manifest reader + 9-context probe)
- `src/API/Sanad.API/Controllers/DeploymentInfoController.cs` (new: `GET /api/v1/deployment/info`)
- `tests/Sanad.UnitTests/API/VpsReadinessTests.cs` (new: 13 focused tests)
- `docs/internal/tools/Verify-SanadDeployment.ps1` (remote revision + 9-context evidence)
- `docs/internal/tools/Verify-ApiContractMapping.ps1` (Postman-only gate)
- `docs/postman/app/Sanad.App.Public.postman_collection.json` (`deployment/info` coverage)
- `docs/internal/operations/publish.md`, `deploy-runbook.md`, `deployment.md`, `configuration.md`
- `docs/internal/operations/vps/sanad-api.service.template` (new)
- `docs/internal/operations/vps/sanad-api.nginx.conf.template` (new)
- `docs/internal/operations/vps/staging.md` (new)
- `.env.example`, `README.md` (keys-only additions; CORS owner decision)

No live VPS changes and no credentials were handled in this task. The stale
`deploy/publish-out` manifest (`41f9c91`, pre-Chat Plus) was read as evidence
only and never rebuilt, deployed, or presented as a release; after this branch
is integrated, the mastermind publishes the exact clean pushed `main` revision
with all nine current migration IDs
(`Identity 20261013104759`, `Cms 20261012071340`,
`Caregivers 20261009004058`, `Families 20261013005844`,
`CareHomes 20261012143753`, `Notifications 20261010010618`,
`Community 20261010004134`, `Finance 20261010143327`,
`Chat 20261013000659` — verified against the `Migrations/` folders).

## Gap resolutions

1. **Stale manifest:** documented as stale in `publish.md`; publish flow
   regenerates from the exact pushed HEAD. Nothing deployed from `41f9c91`.
2. **CORS — OWNER-DIRECTED NO CHANGE.** The owner explicitly confirmed
   `AllowAnyOrigin` stays as intentional configuration (native clients send
   no `Origin` header) and is not a fault or security gap. No CORS code was
   changed; `README.md` now records the decision so a future worker does not
   "fix" it without a new explicit owner order. The nginx template therefore
   specifies no origin allowlist (nothing for the edge to enforce).
3. **DataProtection:** `SanadDataProtectionOptions` (`DataProtection:KeyRingPath`,
   `DataProtection:ApplicationName`, default `Sanad.API`) wired in
   `Program.cs`. Outside Development the host refuses to start without a
   persistent absolute key ring outside the release folder; the ephemeral
   provider remains only on the Development-guarded fixture-seeding path and
   can never be selected on a public host. Old-cursor recovery is proven by
   `Persistent_keys_recover_old_cursor_after_restart` (shared ring, new
   provider instance) and `Ephemeral_keys_do_not_recover_old_cursor`
   documents why ephemeral keys are forbidden on the VPS.
4. **Storage:** `StorageRootPolicy` refuses startup outside Development when
   the effective root sits inside the versioned release folder; durable
   absolute path (e.g. `/var/sanad/files`, private sibling `<root>-private`)
   is configured via `Storage:Local:RootPath`. Deploy/rollback rules
   (never delete/move shared files) are in `staging.md` and the service
   template (`ReadWritePaths`, releases under `/opt/sanad/api/releases/`).
5. **Verifier:** `Verify-SanadDeployment.ps1` now proves the remote revision
   via anonymous `GET /api/v1/deployment/info` (manifest-revision match, no
   DB details exposed), requires all 9 actually applied migrations to equal
   the manifest identifiers, optionally validates the captured startup log's
   9-context evidence (`-StartupLogPath`), and keeps the anonymous smoke at
   HTTP 200. A local-manifest-only check no longer passes as release
   evidence. New controller/probe covered by focused tests with fake
   reader/probe (no DB); the EF probe itself is thin and unverified against
   a live database here.
6. **Bruno removed from release gate:** `Verify-ApiContractMapping.ps1` scans
   Postman only and exits 0 on 0 missing / 0 orphans (verified this branch:
   556 signatures, 620 Postman requests, exit 0). Historical Bruno files are
   untouched history. `publish.md`/`deploy-runbook.md` updated accordingly;
   no passing exit was invented (the exit-0 run above is the actual run).

## Verification actually run

- `dotnet build Sanad.slnx --no-restore -m:1 --nologo /p:TreatWarningsAsErrors=true`: exit 0, 0 warnings, 0 errors.
- `dotnet test ... --filter FullyQualifiedName~VpsReadiness`: 13/13 passed.
- `dotnet test ... --filter FullyQualifiedName~Sanad.UnitTests.API`: 308/308 passed.
- `dotnet test ... --filter FullyQualifiedName~Chat`: 221/221 passed.
- `Sanad.ArchitectureTests`: 1/1 passed.
- `Verify-ApiContractMapping.ps1`: exit 0 (556/620/0/0).
- `Verify-SanadDeployment.ps1`: syntax-valid; guard path exercised
  (missing-manifest throw). Full remote verification requires the authorized
  host and is NOT run here.
- `git diff --check`: clean. Full unit suite not rerun (no cross-cutting
  product change; mastermind reuses/reruns gates at integration).
- Tracked templates live under `docs/internal/operations/vps/` because
  `deploy/` is fully git-ignored; the service/proxy files are templates with
  `__FILL__` placeholders and zero secrets.

## Verdicts

**Isolated staging: NOT READY** (code/templates READY for review; environment NOT READY).
**Production: NOT READY.**

Every unresolved target/environment/secret/device dependency:

- Target: exact staging host + public URL + service/port; exact production
  host + URL (separate owner order). Neither is named.
- Environment: staging database name (separate test DB, never production);
  all injected values (9 connection strings or Identity fallback, JWT
  issuer/audience/signing key ≥32 bytes, one-time AdminSeed, blank-or-real
  provider settings); TLS certificate for the staging hostname.
- Secrets: DB passwords, JWT signing key, AdminSeed password
  (set-once then remove/rotate), SMTP/SMS/Paymob/FCM values, TLS private
  key, DataProtection key-ring backup. None exist in this task by design.
- Backup/restore/rollback: pre-deploy backup + verified restore procedure +
  recorded rollback-compatibility decision. Required before any deployment;
  application rollback alone cannot undo database changes; no lossy
  down-migrations on populated data.
- Runtime proof (post-deploy, separate authorization): Prompt-6 verification
  run; Firebase/APNs + physical Android/iPhone delivery matrix
  (owner/mobile team); N≥5 Hourly + N≥5 EightHour paid-pool API-vs-SQL
  equality (backend verifier); edge TLS/WebSocket/origin/limit validation
  (operator). None run here.
- Session-bound tokens, physical orphan cleanup, and the Care Homes
  processor fix remain with Prompts 1–3 and are not staging blockers per the
  owner direction; device push proof is not a staging-REST-chat blocker.

Staging startup will apply all pending module migrations including Finance
and invoke the startup seeders per `docs/internal/operations/vps/staging.md`
§2 — host/database/backup authorization is therefore required even for the
test deployment. Never set the public host to Development; `Staging` with
`App:TestUserSeed:Enabled=false` is mandatory and verified in §2 of that file.
