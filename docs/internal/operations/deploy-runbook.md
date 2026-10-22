# Deploy runbook (production releases)

Deployment is never implied by `publish`, a merge, or a passing gate battery. It starts only on an explicit owner deployment order for an accepted phase and an exact target host/database. The target may be incorporated by reference to the immediately prior owner-designated deployment receipt when the owner asks to continue that release workflow; revalidate its live service, effective database identity and configuration before any write. Do not repeatedly request the same authorization or a literal `publish` word when the owner has already ordered deployment of that accepted phase. If the target differs or cannot be verified, stop and ask about that specific mismatch. The deploying agent or owner follows this page top to bottom and records evidence without secrets.

## 1. Pre-flight (on the build machine)

1. Confirm the exact owner-authorized host/service/environment from the current order or immediately prior receipt. Revalidate that the live service is the same target before writing.
2. Confirm clean pushed `main`, a fresh package, and manifest revision match. The publisher already derives and records all ten source migration heads; do not compare those folders a second time here.
3. Confirm the package contains no `.env`, `*.local.json`, keys/certs, or `appsettings.Development.json`. Transfer only the package archive; never transfer local secrets or host configuration.
4. Read the remote deployment endpoint once before cutover to confirm the current revision, environment, database reachability, and applied migration heads. If the phase adds a migration or otherwise writes database state, privately verify the effective database identity, determine expected pending migrations, and make a validated backup on that exact target before restart. A binary rollback does not undo schema changes; prove compatibility or define separately authorized recovery. Never automatically restore a backup over new writes.

## 2. Local-only environment (never committed)

Copy `.env.example` (tracked, keys only, zero secrets) to `.env` on the **target host only** and fill real values there. `.env`, `deploy/*.local.json`, `*.pem`, `*.key`, and `*.publishsettings` are git-ignored and must never be committed, printed, or pasted into chat/docs/logs.

ASP.NET Core never loads a `.env` file automatically. The host (systemd
`EnvironmentFile=` / `Environment=` or the container/host secret store)
must inject the actual environment; placing a `.env` beside the binaries
without host wiring has no effect. Prefer the host secret store.

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

1. Copy only the fresh package archive to a new release location; never copy the repository, `.env`, local settings, or secrets.
2. Preserve the existing service unit, environment file/secret-store wiring, `appsettings*.json`, storage, and key-ring configuration. ASP.NET Core does not load `.env` automatically.
3. Keep a recoverable copy of the current binary directory, stop the service, replace only application files, then start the service. Capture the full startup log. Outside Development the API checks/applies pending migrations for all contexts before accepting traffic; any migration failure aborts startup.

## 4. Verify on the target

1. Capture the startup log once; confirm it records all ten context checks, pending/applied counts, and the listen URL. Pass it to the verifier so startup evidence is checked mechanically. This complements the remote endpoint's current applied-head report.
2. Run the verifier once from the build machine:
   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\docs\internal\tools\Verify-SanadDeployment.ps1 `
     -ManifestPath .\deploy\publish-out\sanad-deployment-manifest.json `
     -BaseUrl https://your-authorized-host `
     -ExpectedRevision <pushed-sha> `
     -StartupLogPath <captured-startup-log-path>
   ```
   The verifier proves the remote revision through the safe anonymous
   `GET /api/v1/deployment/info` endpoint (revision match, no DB details
   exposed), requires actually applied latest migrations for all 9 contexts
   to equal the manifest's required identifiers, checks the optional
   startup-log 9-context evidence when supplied, and keeps the anonymous
   smoke `GET /api/v1/lookups/services` at HTTP 200. A local-manifest-only
   check or a single lookup response alone is not release evidence.
3. Add a feature-specific smoke only when the approved phase contract requires behavior that the deployment verifier does not cover. Do not run unrelated feature flows or real provider calls as a generic deployment gate.

For a changed Identity verification flow, use sanitized anonymous metadata/on-host smoke plus approved fake-provider tests. Do not send a real OTP merely to claim deployment verification. Preserve the existing Production CORS, storage, key-ring, provider configuration, service unit, and environment wiring; compare safe fingerprints before and after. If direct public HTTP from the build machine returns HTML while on-host and a temporary localhost-only SSH tunnel verify JSON, record the external-path limitation rather than changing API/CORS configuration without evidence.

## 5. Record evidence (no secrets)

Return to the repo: release revision, manifest revision match, per-context migration counts from the startup log, smoke result, date, and target host alias — never connection strings, tokens, or payloads. File it under the active slice handoff. The former generated release task file is superseded and is not release authorization.
