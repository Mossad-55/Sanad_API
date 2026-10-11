# Publish trigger (`publish`)

## Trigger contract

When the owner says the word **`publish`** (optionally with a scope, e.g. `publish`, `publish release`), any agent — human or AI — follows exactly this page. No other word starts a publish. The trigger never authorizes deployment, production access, migration applies outside the package build, or secret exposure.

## Preconditions (stop if any fails)

1. Working tree clean (`git status --short` empty) on `main`.
2. `main` == `origin/main` (`git rev-parse main` equals `git rev-parse origin/main`).
3. The revision to publish is the pushed `main` HEAD. Publishing an unpushed or dirty tree is forbidden.

## Steps

1. Record HEAD SHA: `git rev-parse HEAD`.
2. Gate battery (all must exit 0):
   - `git diff --check`
   - `dotnet build Sanad.slnx --no-restore -m:1 --nologo /p:TreatWarningsAsErrors=true` (0 warnings, 0 errors)
   - `dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj --no-restore --nologo -v minimal` (0 failures; only pre-declared static skips)
   - `dotnet test tests/Sanad.ArchitectureTests/Sanad.ArchitectureTests.csproj --no-restore --nologo -v minimal` (pass)
   - `powershell -NoProfile -ExecutionPolicy Bypass -File docs/tools/Verify-ApiContractMapping.ps1` (Postman 0 missing / 0 orphan)
3. Package:
   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\docs\tools\Publish-SanadApi.ps1 -Configuration Release
   ```
   Output goes to `deploy/publish-out/` (git-ignored by design).
4. Verify the manifest: `sanad-deployment-manifest.json` `revision` must equal the HEAD SHA from step 1; `requiredMigrations` must list all 9 contexts (Identity, Cms, Caregivers, Families, CareHomes, Notifications, Community, Finance, Chat) at their current latest identifiers.
5. Report back: HEAD SHA, gate evidence (commands + exit codes + counts), package path, manifest revision match. Then stop.

## Hard prohibitions

- Never copy `appsettings.Development.json`, seed settings, `.env`, or any secret into or alongside the package (the script refuses Development settings itself).
- Never start the package against production, never apply migrations anywhere, never push, tag, or deploy.
- Deployment is a separate owner order handled only via [deploy-runbook.md](deploy-runbook.md).
