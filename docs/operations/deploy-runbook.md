# Deploy runbook (production releases)

Deployment is never implied by `publish`, a merge, or a passing gate battery. It starts only on an explicit owner order naming the exact target host and database. The deploying agent or owner follows this page top to bottom and records evidence without secrets.

## 1. Pre-flight (on the build machine)

1. Confirm `git status` clean and the release revision equals `origin/main` HEAD.
2. Confirm a fresh package exists from [publish.md](publish.md) with a matching manifest revision.
3. Confirm the manifest `requiredMigrations` table matches the release revision's `Migrations/` folders (all 9 contexts).
4. Confirm no secret material in or next to the package: no `.env`, no `*.local.json`, no keys/certs, no `appsettings.Development.json` (the publish script enforces the last one).

## 2. Local-only environment (never committed)

Copy `.env.example` (tracked, keys only, zero secrets) to `.env` on the **target host only** and fill real values there. `.env`, `deploy/*.local.json`, `*.pem`, `*.key`, and `*.publishsettings` are git-ignored and must never be committed, printed, or pasted into chat/docs/logs.

| Variable | Purpose | Production rule |
|---|---|---|
| `ConnectionStrings__IdentityDatabase` | Fallback for all modules | Loopback or private host only; least-privilege user with migration rights at deploy time |
| `ConnectionStrings__{Cms,Caregivers,Families,Notifications,Community,Finance,CareHomes,Chat}Database` | Per-module override (each falls back to Identity) | Same as above; set explicitly, never rely on dev defaults |
| `DOTNET_ENVIRONMENT` / `ASPNETCORE_ENVIRONMENT` | Must be `Production` (or Staging) | Never `Development` on a live host — Development disables migration enforcement and enables seed paths |
| `ASPNETCORE_URLS` | Listen address | Exact authorized host/port; verify the port is free first |
| `Paymob__SecretKey` / `Paymob__HmacSecret` | Payment provider | Real vault values on prod; blank means no provider calls leave the machine |
| `Identity__Sms__SmsMisr__Username/Password/Sender` | SMS provider | Real values on prod; blank disables sending |
| `Identity__Email__Smtp__Host/FromAddress` (+ `Port/Username/Password`) | SMTP sender | Real values on prod; blank host disables sending |
| `Identity__AdminSeed__*` | Bootstrapping admin (first start only) | Set once, then remove or rotate; never reuse across hosts |
| `App__TestUserSeed__Enabled` | Fixture seeder | Must be `false` outside Development |
| `FinanceMigrations__ApplyOnStartup` | Finance auto-migration | Automatic outside Development regardless; keep default |

Prove ignore-status before every release: `git check-ignore -v .env deploy/publish-out` must list both as ignored.

## 3. Copy + start

1. Copy only the `deploy/publish-out/` contents to the host (no repo, no `.env` from elsewhere).
2. Place the host-local `.env` beside the binaries (or inject via the host's secret store — preferred).
3. Start the API and capture the full startup log. Outside Development the API applies pending migrations for **all** contexts synchronously before accepting traffic; migration failure aborts startup per context.

## 4. Verify on the target

1. Confirm the startup log shows each context's pending/applied counts (9 contexts) and the listen URL.
2. Run the verifier from the build machine:
   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\docs\tools\Verify-SanadDeployment.ps1 `
     -ManifestPath .\deploy\publish-out\sanad-deployment-manifest.json `
     -BaseUrl https://your-authorized-host `
     -ExpectedRevision <pushed-sha>
   ```
   Anonymous smoke `GET /api/v1/lookups/services` must return 200.
3. Confirm the caregiver completion path including the allowance-exhausted `409` contract (see [deployment.md](deployment.md)).

## 5. Record evidence (no secrets)

Return to the repo: release revision, manifest revision match, per-context migration counts from the startup log, smoke result, date, and target host alias — never connection strings, tokens, or payloads. File it under the active slice handoff. The former generated release task file is superseded and is not release authorization.
