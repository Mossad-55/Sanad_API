# Staging VPS runbook (isolated development/testing host)

Never publish `Development` to a publicly reachable host. The isolated
development/testing VPS runs `Staging` with a separately named test
database (for example `SanadStagingDb` â€” never the production database
name, never `SanadBrunoTestDb`, which belongs to local disposable runs).

Templates: `sanad-api.service.template` (systemd, single instance),
`sanad-api.nginx.conf.template` (TLS edge). Both are reviewable templates
with `__FILL__` placeholders and zero secrets. `deploy/` is git-ignored, so
tracked templates live in this directory.

## 1. Pre-deployment requirements (all mandatory, in order)

1. Owner authorization naming the exact host, public URL, service name/port
   (`http://127.0.0.1:5000` loopback behind nginx), environment (`Staging`),
   database host/name, backup/restore boundary, and startup
   migration/seed permission. No default inference that any existing
   VPS/database is disposable.
2. Fresh package published from the exact clean pushed `main` revision per
   `docs/internal/operations/publish.md`, with manifest revision match and
   all 9 current migration identifiers.
3. Full database backup with a verified restore procedure BEFORE the first
   start. Application rollback alone cannot undo database changes.
4. Rollback compatibility decision recorded: which prior package revision
   the migrated database remains compatible with, and the explicit rule
   that lossy down-migrations are never run on populated data.
5. Durable directories created outside the release folder with
   least-privilege ownership (commands are templates â€” run on the host):
   `install -d -o sanad -g sanad -m 700 /var/sanad/keys`
   `install -d -o sanad -g sanad -m 750 /var/sanad/files`
   `install -d -o sanad -g sanad -m 750 /var/sanad/files-private`
6. Host-local environment injected by systemd (`EnvironmentFile=`), never
   assumed from a `.env` file beside the binaries (ASP.NET Core never loads
   `.env` automatically).

## 2. Actual Staging startup side effects (from current code)

Staging startup (`Staging` = non-Development) performs ALL of the following
before accepting traffic; each is a real database/host write:

1. **Module migrations (8 contexts, synchronous, ordered):** Identity, Cms,
   Caregivers, Families, CareHomes, Notifications, Community, Chat â€”
   `Database.Migrate()` per context. Failure in any context aborts startup.
2. **Finance migrations (automatic outside Development):** applied whenever
   the environment is not Development, regardless of
   `FinanceMigrations:ApplyOnStartup` (that flag is Development-only
   opt-in). See `StartupMigrationPolicy.ShouldApplyFinanceMigrations`.
3. **Bootstrap admin seeders (first start only):** `SuperAdminSeeder` and
   `FinanceAdminSeeder` each insert exactly one admin user when their
   `Identity:AdminSeed:*` / `Identity:FinanceAdminSeed:*` options are
   configured AND no admin of that kind exists yet; otherwise they are
   no-ops. Set once, verify login, then remove or rotate the seed
   credentials â€” never reuse them across hosts.
4. **Fixture seeder â€” OFF:** `TestUserDataSeeder.SeedAsync` returns
   immediately when the environment is not Development or when
   `App:TestUserSeed:Enabled=false`. Verify `App__TestUserSeed__Enabled=false`
   in the injected environment; `true` outside Development throws at guard
   time (`TestUserSeedTargetGuard`). Never enable fixture seeding or
   ephemeral DataProtection keys on the publicly reachable VPS.
5. **Hosted background services start:** license/booking expiry monitors,
   transfer/booking/review notification processors, email and push outbox
   processors, and the chat dispatch service. Provider-safe defaults: with
   blank `Paymob`, SMS Misr, SMTP, and Firebase settings, no provider call
   can leave the machine (development senders); configuring any real
   provider value enables real sends on its first eligible outbox row.
6. **DataProtection key ring:** keys persist under
   `DataProtection:KeyRingPath` (for example `/var/sanad/keys`) with the
   stable `DataProtection:ApplicationName` discriminator (`Sanad.API`).
   Missing/relative/release-folder paths refuse startup outside
   Development. Old chat sync cursors decrypt after restarts and redeploys
   only while the ring and discriminator are unchanged.
7. **Upload storage:** the effective `Storage:Local:RootPath` (for example
   `/var/sanad/files`, private sibling `<root>-private`) is created if
   missing. A root inside the release folder refuses startup outside
   Development.

## 3. Deploy steps (host)

1. Copy ONLY the `deploy/publish-out/` contents into a NEW versioned
   directory (for example `/opt/sanad/api/releases/<revision>/`). Never copy
   the repo, a `.env` from elsewhere, seed settings, keys, or certificates.
2. Confirm ignore-status hygiene on the build machine before every release:
   `git check-ignore -v .env deploy/publish-out` must list both as ignored.
3. Repoint `/opt/sanad/api/current` to the new directory (never delete or
   move files under `/var/sanad/files` or `/var/sanad/keys`).
4. `systemctl daemon-reload`, then `systemctl restart sanad-api`.
   The service uses Type=simple: process start does not prove API/migration
   readiness. Wait for successful startup evidence and HTTP verification.
5. Capture the FULL startup log (`journalctl -u sanad-api`) â€” it is the
   9-context migration evidence. Save it with the release record.

The API processes forwarded scheme/address from the template's loopback nginx
only, before HTTPS redirection. This prevents an HTTP-upstream redirect loop;
it does not change the owner's CORS policy. If moving the proxy off loopback,
review the explicit proxy trust configuration before deploying. The template's
access log omits query/header values; stock nginx request-error output is sent
to /dev/null because it can contain tokens. Use sanitized access metrics and
application logs for diagnosis; do not enable full-request logging on this API.

## 4. Verify (build machine)

Run `Verify-SanadDeployment.ps1` with the manifest, the authorized base URL,
the expected pushed SHA, and the captured startup log path (see
`docs/internal/operations/deploy-runbook.md` Â§4). Required: remote revision
match, 10/10 applied-migration match, startup-log 10-context evidence, and the
anonymous smoke at HTTP 200.

## 5. Rollback

1. Application-only rollback = repoint `current` to the prior release
   directory and restart. This NEVER undoes migrated database schemas.
2. Database rollback = restore the pre-deploy backup into the authorized
   target per the recorded restore procedure, then start the compatible
   prior package revision. Never run lossy down-migrations on populated
   data; down-migrations were proven only on the disposable local target
   during Chat Plus Phase 7, never on populated VPS data.
3. Uploads and key ring are shared durable state: rollback must not delete,
   move, or replace files under `/var/sanad/files` or `/var/sanad/keys`;
   previously issued sync cursors keep decrypting only while the ring and
   application discriminator are untouched.

## 6. Production vs. staging

Production deployment is NOT authorized by this runbook, by a staging
success, or by any worker branch. It needs a separate owner order plus the
production host/database/backup/secret/device dependencies named in
`reports/remaining-vps-readiness.md`. CORS stays `AllowAnyOrigin` by
explicit owner confirmation (intentional configuration for native clients,
not a gap); device push proof (Firebase/APNs, physical Android + iPhone)
and the Nâ‰¥5 paid-pool aggregate equality remain with their documented
owners and are not staging-shipping blockers.
