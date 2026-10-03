# Deployment handoff

The repository contains a repeatable package step in
`docs/tools/Publish-SanadApi.ps1`. It publishes the API and writes
`sanad-deployment-manifest.json` containing the Git revision, package time, and
the billing migration expected by this release. Development settings are
excluded from publish output, and the publisher fails if
`appsettings.Development.json` is present in the package.

From the repository root, run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\docs\tools\Publish-SanadApi.ps1 -Configuration Release
```

The default package directory is `deploy/publish-out/` and is intentionally
ignored by Git. Do not copy `appsettings.Development.json` or development seed
settings to a VPS or production environment.

## Required post-push package handoff

After every successful commit **and push** to `main`, the mastermind must prepare
and verify a Release package from that exact pushed revision before handing the
work back to the owner. This is package preparation only; it does not upload,
apply a production migration, or restart a service. The owner remains responsible
for the authorized deployment.

Run these commands from the linked `main` worktree
(`D:\Sanad_API\.codex\worktrees\care-homes-main`), not from a checkout on another
branch. The SHA-specific output directory avoids confusing an older package with
the new release:

```powershell
$revision = (git rev-parse HEAD).Trim()
$remoteRevision = (git rev-parse origin/main).Trim()
if ((git branch --show-current) -ne 'main' -or $revision -ne $remoteRevision) {
  throw 'The checked-out main revision is not the pushed origin/main revision.'
}
$shortRevision = $revision.Substring(0, 12)
./docs/tools/Publish-SanadApi.ps1 -Configuration Release -OutputPath "deploy/publish-out-$shortRevision"
$package = "deploy/publish-out-$shortRevision"
$manifest = Get-Content -LiteralPath "$package/sanad-deployment-manifest.json" -Raw | ConvertFrom-Json
if ($manifest.revision -ne $revision -or $manifest.configuration -ne 'Release') {
  throw 'The package manifest does not identify this pushed Release revision.'
}
if (Test-Path -LiteralPath "$package/appsettings.Development.json") {
  throw 'Development settings are present; do not upload this package.'
}
Get-Item -LiteralPath "$package/Sanad.API.dll", "$package/sanad-deployment-manifest.json"
```

Report the full revision, package directory, manifest check, and any blockers to
the owner. If the owner uploads from the repository root, use the matching
SHA-specific folder (replace `<short-sha>` with the first 12 characters reported
above):

```powershell
scp -r .\.codex\worktrees\care-homes-main\deploy\publish-out-<short-sha>\* root@72.62.92.144:/root/sanad-api/
```

The root checkout's current branch does not affect that command: its source path
explicitly selects the package in the linked `main` worktree. Never use the
root checkout's `deploy/publish-out` as a substitute. If the linked worktree
path or target host changes, confirm the exact paths with the owner before
uploading.

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
