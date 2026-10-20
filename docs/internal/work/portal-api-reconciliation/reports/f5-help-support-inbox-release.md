# F5 owner help, support inbox and ticket replies — release receipt

Date: 2026-10-20 (Africa/Cairo). **Status: deployed and verified.**

## Source and package

- Feature commit: `d5f287d` "feat(f5): owner help, support inbox and ticket
  replies" (43 files). Documentation closeout/acceptance commit: `9997927`
  "docs: accept F5 and record the F6 handback". Both were already pushed;
  `origin/main` tip was `9997927` and the release packaged that exact tip.
- Deployed API revision and package manifest revision:
  `99979272e7ef5257b7eb9b66e16bd337da77043f`. The publisher derived the
  revision from pushed `main` HEAD and wrote `sanad-deployment-manifest.json`
  with all nine module heads, including the new Identity head
  `20261020191733_AddSupportTicketWorkflow`; the other eight heads are
  byte-identical to the prior (F3) manifest.
- Archive: `sanad-api-f5-9997927.tar.gz`, 11,919,060 bytes, 118 entries;
  SHA-256 `ea16d63da098bea7e5f5a7591a71d18178e6016f31c0dd861364deda63ff003b`.
  The transferred archive hash matched the build-machine hash before use.
  `appsettings*.json`, `.env`, local settings, keys and certificates are not in
  the package.
- Built with `Publish-SanadApi.ps1 -Configuration Release` from a clean detached
  checkout (`deploy/releases/f5-9997927/source`) of the pushed revision with a
  clean working tree; the publisher compiled the API as part of `dotnet publish`
  (warnings treated as errors). No second solution build was run.

## Target, migration backup and preflight

- Continued the previously Owner-designated Production VPS
  `72.62.92.144:8091`, service `sanad-api.service`, directory `/root/sanad-api`,
  environment file `/etc/sanad/sanad.env`.
- Before cutover the service was active/running with zero restarts and the
  deployment endpoint reported revision
  `607d33fcb651bed25239050989cb94a8cb36de41` (the prior F3 release),
  `Production`, reachable databases — the authorized target was revalidated,
  not assumed. `/root/sanad-api` is **not** a git checkout.
- Only `ConnectionStrings__IdentityDatabase` is configured, so the Identity
  context (which owns this migration) resolves to the same effective Production
  database the other contexts use. That identity was confirmed privately on the
  host; no connection string, user, host, password or database name is recorded
  here.
- **Backup first**, before any cutover, under
  `/root/sanad-releases/f5-9997927-20261020/` (directory mode `700`):
  - `database-before-f5.dump` — custom-format `pg_dump`, 712,955 bytes, SHA-256
    `662841abcc92e9e4e0158f4057a78205ee02901c92a63d93e98a997d06e3aedc`;
    validated read-only with `pg_restore --list` (769 TOC entries).
  - `app-backup-before-f5.tar.gz` — current application directory, 11,893,898
    bytes, 120 entries.
  - `host-config/` — private copies of the service unit and environment file
    (files mode `600`).
  - No backup was restored; no data was reset, dropped or deleted.

## Deployment and verification

- Only the hash-verified archive was transferred and overlaid into
  `/root/sanad-api`. The host `appsettings*.json`, service unit, environment
  file, storage and key-ring configuration were preserved: SHA-256 fingerprints
  of `appsettings.json`, the service unit and the environment file are
  identical before and after cutover (`200332b4…`, `c018192e…`, `e5b4ec5e…`);
  the host `appsettings.Development.json` (`e92cd3f4…`) was preserved.
- The service was stopped, the verified files overlaid, and the service started.
- **Migration applied by the production startup migration policy on restart**
  (the mechanic recorded by the prior F2/F3/F4 receipts and by E04), not by an
  explicit `dotnet ef database update`. Production startup checked all nine
  contexts and applied exactly **one** migration:
  - Identity `20261020191733_AddSupportTicketWorkflow` — **1** applied.
  - Cms, Caregivers, Families, CareHomes, Notifications, Community, Chat,
    Finance — **0** applied each.
- Post-restart schema confirmation on the Production database:
  - `identity.support_tickets` gained the two nullable columns
    `status_changed_by_user_id` (uuid) and `status_changed_on_utc`
    (timestamp with time zone) — 2/2 present.
  - New table `identity.support_ticket_replies` created with exactly
    `id, ticket_id, actor_user_id, body, created_on_utc` (5 columns). No drops,
    renames or backfill.
  - Identity EF history head is `20261020191733_AddSupportTicketWorkflow`.
- Service state after cutover: **active/running**, `NRestarts=0`, started
  `2026-10-20 20:16:08 UTC`. The deployment endpoint answered HTTP 200
  (`Production`, database reachable, 9/9 heads match).
- Standard `Verify-SanadDeployment.ps1` passed (exit 0) with the local manifest,
  expected revision `9997927` and the sanitized startup log:
  - Remote revision exactly matched `99979272e7ef5257b7eb9b66e16bd337da77043f`.
  - Environment `Production`; databases reachable.
  - All **9/9** applied migration heads matched the manifest (Identity now at
    the new F5 head; the other eight unchanged).
  - Startup evidence covered all nine contexts with the counts above.
  - Anonymous `GET /api/v1/lookups/services` returned HTTP 200.

## F5 live surface (OpenAPI + anonymous smokes)

Read from the live document at `GET /openapi/v1.json` (HTTP 200) on the deployed
revision:

- Published support paths, all present:
  `GET /api/v1/support/contact`, `POST /api/v1/support/contact`,
  `GET /api/v1/support/tickets`, `GET /api/v1/support/tickets/{id}`,
  `GET /api/v1/admin/support-tickets`,
  `GET /api/v1/admin/support-tickets/{id}`,
  `POST /api/v1/admin/support-tickets/{id}/status`,
  `POST /api/v1/admin/support-tickets/{id}/replies`.
- `LegalAudience` publishes `enum [1,2,3,4,5]` /
  `x-enumNames [Family, MedicalCaregiver, CompanionCaregiver, Elderly, CareHomeOwner]`
  — `CareHomeOwner = 5` is now published.
- F3 surface re-confirmed unchanged: `CareHomeOwnerProfile.openRevisionId` is
  still published as a nullable string/uuid.
- Anonymous runtime smokes: `GET /api/v1/support/contact` **200** with only the
  support phone/email keys (no FAQs, ticket data or other Help Center fields);
  `GET /api/v1/support/tickets` **401**; `GET /api/v1/admin/support-tickets`
  **401**.
- No authenticated Production feature request, mutation, provider call, fixture
  write or OTP was performed.

## Gate evidence carried into the release

- Mastermind acceptance (recorded in the F5 brief): strict solution build
  0 warnings/0 errors; 81/81 focused tests; `tools/Invoke-F5LocalHttpVerification.ps1`
  exit 0 with 32/32 real local HTTP checks. Reused unchanged; not re-run here.

## Deviations and limitations

- **Deploy mechanic.** Deployment used the recorded build-machine mechanic
  (publish from a clean clone → hash-verified archive → `scp` → overlay →
  restart), not a VPS git clone/pull: `/root/sanad-api` is not a git checkout,
  and this matches the F2/F3/F4 receipts and E04.
- **Migration mechanic.** The migration was applied by the production startup
  migration policy during the restart, not by an explicit
  `dotnet ef database update`; this is the mechanic the prior receipts record.
  The migration ran as one Identity transaction (additive only) and completed
  before the app accepted traffic.
- **Receipt location.** Filed under `reports/` per the release order; the F3 and
  F4 receipts were recorded inside their phase briefs instead.
- The live OpenAPI document publishes no `security`/`securitySchemes` metadata
  (0 occurrences), so the anonymous-vs-signed-in distinction is proven by the
  runtime 401/200 smokes above, not by the document.
- No authenticated Production feature requests were made (same precedent as
  F2/F3/F4): authorization, payload and schema behavior rest on the accepted
  focused tests and the live OpenAPI/DB checks.
- The whole-unit suite, Bruno, and live provider flows were not run and are not
  claimed.
