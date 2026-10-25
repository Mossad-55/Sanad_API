# Portal second pass — release receipt (2026-10-25)

**Status: deployed and verified on the authorized rehearsal VPS.** Release revision
`beb315fe89dfa2f0a5367b57c694674ecb4352d4` (`4690b5b2..beb315fe`, 57 files) is live at
`http://72.62.92.144:8091`, service `sanad-api` (`/root/sanad-api`), runtime environment
`Production`, API bound to `127.0.0.1:5001`.

Active scope/acceptance authority:
[portal-report-second-pass-2026-10-24.md](../worker-prompts/portal-report-second-pass-2026-10-24.md).

## Evidence

| Item | Result |
|---|---|
| Pushed revision built | `beb315fe89dfa2f0a5367b57c694674ecb4352d4` (HEAD == `origin/main`, clean except the private handoff and pre-existing untracked debris) |
| Publisher | `docs/internal/tools/Publish-SanadApi.ps1 -Configuration Release` (direct API project; Chat assemblies present in the Release package) — 0 warnings/errors |
| Package | 110 files / 34,328,613 bytes; no `.env`, `*.local.json`, keys/certs or `appsettings.Development.json`; only host-provided `appsettings.json` was excluded from the copy |
| Archive | `deploy/sanad-api-beb315fe.tar.gz`, 12,490,882 bytes, SHA-256 `c2973388dd02e515ee49ba4c0ea47bb2a6768fa521c597d2b07a37e8bd68e0d8` — local and remote checksums match |
| Backup (database) | `database-before.dump` 824,488 bytes, `pg_restore -l` TOC validated with 892 entries, taken against the exact validated target (`SanadDb`, loopback port 5433, single connection) |
| Backup (binaries) | `app-before.tar.gz` 12,445,346 bytes of `/root/sanad-api` |
| Release directory | `/root/sanad-releases/portal-second-pass-beb315fe-20261025154944` (startup log, sanitized log, config hashes, deployment info, service status) |
| Migration heads | 9/9 applied equal to required; 0 pending; **no migration was added or applied** (heads unchanged from `4690b5b2`) |
| Configuration | `CONFIG_UNCHANGED` — SHA-256 fingerprints of `appsettings.json`, `appsettings.Development.json`, `/etc/sanad/sanad.env` and the systemd unit identical before and after |
| Fixture refund seam | **Disabled** — absent from effective configuration; no `FixtureRefundSeamState` line; no configuration change was made |
| Service | `ActiveState=active`, `SubState=running`, `NRestarts=0` |
| Verifier | `Verify-SanadDeployment.ps1` exit 0 — remote revision match, `Production`, 9/9 required/applied, startup log proves all nine context checks (`0 pending`, `0 applied`), anonymous smoke `GET /api/v1/lookups/services` HTTP 200 |
| Rollback | Both artifacts retained on-host; no automatic restore was performed or is authorized |

## Authenticated readback (API over a loopback-only SSH tunnel)

Logins used the private fixture password in memory only; tokens were never printed or
persisted, and every created session was logged out (`204`). All reads are read-only; no
money execution, no provider request, no fixture writes.

| Role | Readback |
|---|---|
| Care-home owner (Approved facility `01a17319-e6a9-728d-b2ed-9a5f2d1b8c61`, status 5) | `facilities/mine` 200; `residents/mine` default, `page=1&pageSize=1`, `search=Portal` and literal `search=%` all **200** (the reported 500 is gone); `dashboard/mine` 200; `revenue/mine` 200 |
| Draft care-home owner (`r2.carehome.owner2`, facility `01a17319-fac0-7cdc-8b36-939b416daeea`, status 1 = Draft) | `facilities/mine` 200; `dashboard/mine` 200; `revenue/mine` 200 (zero/empty activity) — the A2 Draft read is live |
| SupportAdmin | `admin/subscriptions` 200 (3 subscriptions) + detail 200; `admin/feedback/app-ratings` 200 (empty page, `totalCount 0`); `admin/subscriptions/plans` **403** (plan authoring not widened) |
| SuperAdmin | `admin/subscriptions` 200 + detail 200; `admin/feedback/app-ratings` 200 |
| Family owner | `family` 200; `chat/conversations` 200 (0 rows); `admin/feedback/app-ratings` **403** |
| Anonymous | `admin/subscriptions` **401**; `admin/feedback/app-ratings` **401** |

Every session was revoked in `finally`; the smoke tunnel was started for this check only
and stopped afterwards.

## Deferred, not part of this release

- **G1–G4 provisioning** (care-home/caregiver Failed refunds, Approved-awaiting-execution
  and Ambiguous duplicate claims) remains unprovisioned. The seam code ships inert and
  Production refuses to enable it; provisioning needs a separately settled non-Production
  runtime configuration and the startup-log id gate.
- **Full demo corpus (D1–D4)** is the next slice, starting with a nonce-database dry run.
- **No migration, reset, deletion, clock change, provider request or money movement** was
  performed in this release. Preserved storage, host configuration, service unit and the
  pre-existing fixture rows are untouched.
- Historical note, not erased: an earlier worker's failing reload-host test made one
  unintended sandbox refund POST with a synthetic id (HTTP 400, no money moved); the
  test configuration now pins the offline client. It is not a settlement proof and does
  not change this release verification.
- Chat bilingual participant fields were not observed on live rows because the fixture
  users have zero conversations; they remain covered by the focused unit facts.

## Earlier checkpoints (historical, not this release)

The `S4` integrated-gate numbers recorded in the brief (5,592 / 36 provisioner) pre-date
the final correction round. Final gates: strict Release solution build 0 warnings/0
errors; `Sanad.UnitTests` 5,596 passed / 0 failed / 27 skipped; provisioner 37/37;
architecture 1/1; controller↔Postman mapping 671/770 with 0 missing / 0 orphan;
`git diff --check` clean. Those gates were not re-run during this release stage; the
publisher's independent Release build produced 0 warnings/errors.
