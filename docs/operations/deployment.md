# Deployment handoff

The repository contains a repeatable package step in
`tools/Publish-SanadApi.ps1`. It publishes the API and writes
`sanad-deployment-manifest.json` containing the Git revision, package time, and
the billing migration expected by this release.

From the repository root, run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Publish-SanadApi.ps1 -Configuration Release
```

The default package directory is `deploy/publish-out/` and is intentionally
ignored by Git. Do not copy `appsettings.Development.json` or development seed
settings to a VPS or production environment.

After the published API starts, verify the exact package and run the anonymous
smoke contract with:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Verify-SanadDeployment.ps1 `
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
SSH keys, or an implicit production write. VPS deployment evidence must be
returned by the deployment owner and recorded in the private operations handoff.
