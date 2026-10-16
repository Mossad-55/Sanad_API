# Deploy runbook (production releases)

Deployment is never implied by `publish`, a merge, or a passing gate battery. It starts only on an explicit owner deployment order for an accepted phase and an exact target host/database. The target may be incorporated by reference to the immediately prior owner-designated deployment receipt when the owner asks to continue that release workflow; revalidate its live service, effective database identity and configuration before any write. Do not repeatedly request the same authorization or a literal `publish` word when the owner has already ordered deployment of that accepted phase. If the target differs or cannot be verified, stop and ask about that specific mismatch. The deploying agent or owner follows this page top to bottom and records evidence without secrets.

## 1. Pre-flight (on the build machine)

1. Confirm `git status` clean and the release revision equals `origin/main` HEAD.
2. Confirm a fresh package exists from [publish.md](publish.md) with a matching manifest revision.
3. Confirm the manifest `requiredMigrations` table matches the release revision's `Migrations/` folders (all 9 contexts).
4. Confirm no secret material in or next to the package: no `.env`, no `*.local.json`, no keys/certs, no `appsettings.Development.json` (the publish script enforces the last one).
5. If the phase adds a migration, privately verify the running service's effective database host/port/name and current applied head, determine the expected pending migrations for every context, and make a validated private database backup on that exact target before restart. A binary rollback does not undo schema changes; prove the old binary remains compatible with any additive schema or define a separate, explicitly authorized data-safe recovery. Never automatically restore a database backup over new production writes.

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

1. Copy only the `deploy/publish-out/` contents to the host (no repo, no `.env` from elsewhere).
2. Place the host-local `.env` beside the binaries (or inject via the host's secret store — preferred).
3. Start the API and capture the full startup log. Outside Development the API applies pending migrations for **all** contexts synchronously before accepting traffic; migration failure aborts startup per context.

## 4. Verify on the target

1. Confirm the startup log shows each context's pending/applied counts (9 contexts) and the listen URL. Pass the captured log to the verifier via `-StartupLogPath` so the 9-context startup evidence is checked mechanically.
2. Run the verifier from the build machine:
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
3. Confirm the caregiver completion path including the allowance-exhausted `409` contract (see [deployment.md](deployment.md)).

For a changed Identity verification flow, use sanitized anonymous metadata/on-host smoke plus approved fake-provider tests. Do not send a real OTP merely to claim deployment verification. Preserve the existing Production CORS, storage, key-ring and provider configuration; compare safe fingerprints before and after. If direct public HTTP from the build machine returns HTML while on-host and a temporary localhost-only SSH tunnel verify JSON, record the external-path limitation rather than changing API/CORS configuration without evidence.

## 5. Record evidence (no secrets)

Return to the repo: release revision, manifest revision match, per-context migration counts from the startup log, smoke result, date, and target host alias — never connection strings, tokens, or payloads. File it under the active slice handoff. The former generated release task file is superseded and is not release authorization.
