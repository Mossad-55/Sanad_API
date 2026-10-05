# Deployment handoff

The repository contains a repeatable package step in
`docs/tools/Publish-SanadApi.ps1`. It publishes the API and writes
`sanad-deployment-manifest.json` containing the Git revision, package time, and
the billing migration expected by this release.

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
2. Verify the exact database and schema targets; apply the listed EF migration
   using the deployment environment's approved connection string.
3. Start the published API with production configuration and capture startup
   migration output.
4. Capture an authenticated-safe health/smoke result and confirm the caregiver
   completion path, including the allowance-exhausted `409` contract.

The script packages the application; it does not contain hostnames, credentials,
SSH keys, or an implicit production write. Deployment evidence must be returned
by the deployment owner and recorded without credentials in the active slice
identified by the Mastermind handoff. The former generated release task file is
superseded and is not release authorization.
The [Mastermind handoff](../Mastermind_Handoff.md) links to the active checkpoint.

## Queued follow-up task: automatic migrations on deploy/restart

Status: Not started. Owner-requested follow-up after the current Bruno execution
pass; that pass completed, but several stateful feature folders still need
fixture corrections.

Objective: verify and, if needed, harden the deployment path so pending EF Core
migrations for every registered module are applied to the configured server
database when the API starts after deployment or restart.

Acceptance criteria:

- Audit the API startup migration path, deployment configuration, and server
  launch mechanism for Identity, CMS, Caregivers, Families, Care homes,
  Notifications, and Community.
- Ensure deployments cannot silently disable startup migrations; migration
  failure must prevent the API from being treated as ready, and logs must
  identify the context and migration outcome without exposing connection data.
- Verify idempotent restart behavior and pending-migration application against
  an explicitly disposable database, including migration-history readback.
- Update deployment/migration documentation with the verified server behavior
  and safe recovery steps. Do not apply migrations to production as part of
  this task without separate exact-target authorization.
