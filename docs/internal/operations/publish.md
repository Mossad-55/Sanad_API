# Publish trigger (`publish`)

## Trigger contract

When the owner requests **`publish`** as a package-only operation (optionally with a scope, e.g. `publish release`), any agent — human or AI — follows exactly this page and stops after packaging. That request never authorizes deployment, production access, migration applies outside the package build, or secret exposure. In the owner-approved complete-phase workflow, an explicit authorization to **deploy an accepted phase** includes preparing its necessary fresh package; it does not require the owner to repeat a literal trigger word. Deployment still follows the separate exact-target preflight in [deploy-runbook.md](deploy-runbook.md).

## Preconditions (stop if any fails)

1. Working tree clean (`git status --short` empty) on `main`.
2. `main` == `origin/main` (`git rev-parse main` equals `git rev-parse origin/main`).
3. The revision to publish is the pushed `main` HEAD. Publishing an unpushed or dirty tree is forbidden.

## Steps

1. Record HEAD SHA: `git rev-parse HEAD`.
2. Gate battery (all must exit 0):
   - `git diff --check`
   - `dotnet build Sanad.slnx --no-restore -m:1 --nologo /p:TreatWarningsAsErrors=true` (0 warnings, 0 errors)
   - `pwsh -NoProfile -File docs/internal/tools/Test-SanadPortalGate.ps1 -Suite Unit` (0 failures; only pre-declared static skips). This isolates all nine module targets and removes integration opt-in variables in the actual test child; do not use an unguarded inherited test process.
   - `pwsh -NoProfile -File docs/internal/tools/Test-SanadPortalGate.ps1 -Suite Architecture` (pass).
   - `powershell -NoProfile -ExecutionPolicy Bypass -File docs/internal/tools/Verify-ApiContractMapping.ps1`, plus an independent normalized method/route set comparison when route coverage is relevant. The mapping script's printed missing/orphan counts alone are vacuous until its comparison variables are populated. Bruno is excluded from this gate by owner direction and cannot fail or delay publication.
3. Package:
   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\docs\internal\tools\Publish-SanadApi.ps1 -Configuration Release
   ```
   Output goes to `deploy/publish-out/` (git-ignored by design).
4. Verify the manifest: `sanad-deployment-manifest.json` `revision` must equal the HEAD SHA from step 1; `requiredMigrations` must list all 9 contexts (Identity, Cms, Caregivers, Families, CareHomes, Notifications, Community, Finance, Chat) at their actual latest migration-source identifiers. The publisher derives these heads from the checked-out source and refuses missing migration/designer files. Independently compare all nine values before transfer. Any `deploy/publish-out` manifest predating the published revision is stale by definition and is never a release; always use a fresh output directory for a phase deployment.
5. Report back: HEAD SHA, gate evidence (commands + exit codes + counts), package path, manifest revision match. Then stop.

## Hard prohibitions

- Never copy `appsettings.Development.json`, seed settings, `.env`, or any secret into or alongside the package (the script refuses Development settings itself).
- Never start the package against production, never apply migrations anywhere, never push, tag, or deploy.
- Deployment is a separate owner order handled only via [deploy-runbook.md](deploy-runbook.md).
