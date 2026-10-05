# Deployment handoff

The repository contains a repeatable package step in
`docs/tools/Publish-SanadApi.ps1`. It publishes the API and writes
`sanad-deployment-manifest.json` containing the Git revision, package time, and
the latest required migration identifier for each registered DbContext.

From the repository root, run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\docs\tools\Publish-SanadApi.ps1 -Configuration Release
```

The default package directory is `deploy/publish-out/` and is intentionally
ignored by Git. Do not copy `appsettings.Development.json` or development seed
settings to a VPS or production environment.

After the published API starts, verify the exact package and run the anonymous
smoke contract with:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\docs\tools\Verify-SanadDeployment.ps1 `
  -ManifestPath .\deploy\publish-out\sanad-deployment-manifest.json `
  -BaseUrl https://your-authorized-host `
  -ExpectedRevision <pushed-sha>
```

The verifier checks the manifest revision and calls the existing anonymous
`GET /api/v1/lookups/services` endpoint for HTTP 200. The API currently has no
separate health route; startup migration output plus this smoke result are the
release health evidence.

Before a remote launch:

1. Verify the exact pushed revision in `sanad-deployment-manifest.json`.
2. Verify the configured database targets and ensure the production database
   user has the required migration permissions. Outside Development, the API
   applies pending migrations for all registered contexts (including Finance)
   synchronously before startup completes. Production cannot disable this
   behavior; migration failure is logged by context and prevents startup.
3. Start the published API with production configuration and capture startup
   migration output, including each context's pending/applied count.
4. Capture an authenticated-safe health/smoke result and confirm the caregiver
   completion path, including the allowance-exhausted `409` contract.

The script packages the application; it does not contain hostnames, credentials,
SSH keys, or connect to a database. Deploying the package and starting the API
causes the configured production databases to be migrated automatically.
Deployment evidence must be returned
by the deployment owner and recorded without credentials in the active slice
identified by the Mastermind handoff. The former generated release task file is
superseded and is not release authorization.
The [Mastermind handoff](../Mastermind_Handoff.md) links to the active checkpoint.

## Automatic migrations on deploy/restart

Status: Implemented and unit-tested. The final commit and Release package
revision are recorded in the deployment manifest.

All registered DbContexts now run their EF Core migrations before API startup
completes outside Development. Restarting after a deploy is safe: EF applies
only pending migrations and does nothing when a context is current.

Acceptance criteria:

- Audit the API startup migration path, deployment configuration, and server
  launch mechanism for Identity, CMS, Caregivers, Families, Care homes,
  Notifications, Community, and Finance.
- `Database:ApplyMigrationsOnStartup=false` is accepted only in Development.
  Finance remains separately opt-in only in Development; it is automatic in
  every non-Development environment.
- Migration failure aborts startup. Logs identify context and pending/applied
  counts and do not include connection strings.
- Unit tests cover production enforcement and Development exceptions. Runtime
  idempotent restart and migration-history readback were not run against a
  production database; the deployment owner should verify startup logs and the
  schema history tables on the exact authorized target.
