# Media, care records, payments and Rentals removal — release receipt

Date: 2026-10-24 (Africa/Cairo). Status: **Done** — released and verified on Production.

## Release revision and gates

Released revision: `e29bc2c91e38c2ff76f2c1143ed943e416d9b13f`, pushed to
`origin/main` as a normal fast-forward (`9433c1d..e29bc2c`), commit
`feat(media-care-payments): care records, library video and media hardening, payment
history and refund budget F3 fix, community post management; remove Rentals runtime`.
Staged manifest: 193 files (36 Rentals deletions + 53 adds + 104 modifications).
Excluded and preserved uncommitted: `docs/internal/Mastermind_Handoff.md`
(parent-owned), `worker-prompts/portal-report-follow-up-2026-10-24.md` (queued
plan, out of scope), debris (`buildout*.txt`, `testout*.txt`, `migout.txt`,
`e07-library-smoke.ps1`, `.opencode-audit-tmp/`,
`Mastermind_Handoff_Historical_2026-10-23.md`, `workflow-audit/`).

Secret sweep over the staged manifest: 11 files matched pattern classes; all are
masked/redacted doc text, prefix literals inside key-safety code and test
placeholders. The one JWT-shaped literal in
`tests/LocalFixtureVerification.Tests.ps1` was verified synthetic in-process
(header carries only `alg`, payload only `sub`, no `exp`/`iat`, 15-char
signature). No real credential entered the commit. `git diff --cached --check`
PASS.

Reused accepted evidence (no source input changed since the parent-reviewed
final gate): strict Release build 0 warnings/0 errors; final full suite 5,324
passed / 26 skipped / 0 failed; FUR-02 4/4, FUR-03 12/12, FUR-04 7/7; W4-F3 2/2;
slice-schema 3/3; Families F1 8/8; Chat cleanup 3/3; Identity relational 2/2;
real-disk storage/media 34/34; sandbox Card callback E2E 6/6; route/Postman
mapping 662 signatures / 758 requests, 0 missing / 0 orphan; 25 collections
parse; `git diff --check` PASS. The publisher rebuilt the Release output only.

## Package

`Publish-SanadApi.ps1 -Configuration Release` exit 0 at the pushed revision.
Manifest `deploy/publish-out/sanad-deployment-manifest.json`: revision matches
the push; nine source-derived heads — Identity
`20261022094500_AddPartnerContactOptionalFields`, Cms
`20261023114500_AddLibraryContentVideoStorageKey`, Caregivers
`20261021130000_AddCaregiverReviewNotes`, Families
`20261023131500_AddCareRecords`, CareHomes
`20261022213000_AddCareHomeRefundPolicySnapshots`, Notifications
`20261010010618_AddEmailHtmlSnapshots`, Community
`20261023150000_AddPostDeletionAudit`, Finance
`20261022230000_AddComplaintReviewsAndSubscriptionRefundRequests`, Chat
`20261013171755_AddAttachmentCleanupIndex`. Archive
`deploy/sanad-api-e29bc2c.tar.gz`, SHA-256
`bccf146a93aca756eebfa04c810085a87433773eaff68e1ca3ad4b3729addad6`
(12,418,899 bytes; 119 entries). No `.env`, no
`appsettings.Development.json`, no key/cert material; zero Rentals artifacts;
embedded manifest identical. `git check-ignore -v`.env` and
`deploy/publish-out` both ignored. SHA-256 verified identical on the target
after transfer.

## Target preflight, rnt_ gate and backup

Production `72.62.92.144:8091`, service `sanad-api.service`, `/root/sanad-api`,
existing `/etc/sanad/sanad.env`. Before cutover the service was active
(NRestarts=0) at revision `edbccc1…`, Production, database reachable. Effective
database identity verified privately on-host: single database `SanadDb` on
localhost:5433, PostgreSQL 16.15, ~18 MB. Credentials stayed on-host and were
never printed.

**rnt_/Rentals inventory gate (read-only, CRITICAL condition): the retained
`rentals` schema holds 5 tables — rental_bookings, rental_payment_transactions,
rental_refund_movements, rental_equipment, rental_damage_deductions — all 0
rows. Families payment/subscription tables and all Finance refund tables are
also 0 rows. There are no pending or unapplied money events; the locked D5
disposition (explicit 503 reconciliation response, no acknowledge/settle) has
nothing outstanding. Cutover proceeded.**

Backup before restart (schema writes expected: Cms 1, Families 1, Community 1)
in `/root/sanad-releases/media-care-payments-e29bc2c-20261024/`:
`database-before-media-care-payments-e29bc2c-20261024.dump` (custom format,
792,752 bytes, **884 TOC entries**, validated with `pg_restore -l`),
`app-before.tar.gz` (12,484,635 bytes) and pre-change
`config-fingerprints-before.txt`.

## Cutover and verification

Only application files were replaced (shipped `appsettings*.json` deleted from
the staging copy before copy; host config untouched). The previous package's
six stale `Sanad.Modules.Rentals.*` assemblies/PDBs were removed by exact name.
Existing service unit, environment file, storage and key-ring preserved. The
endpoint answered with the new revision after ~21 seconds; service
`active`/`running`, NRestarts=0, bound `http://127.0.0.1:5001` behind nginx.
Config fingerprints before/after identical (**CONFIG_UNCHANGED**). Startup log
(3022 lines, retained on host in the alias directory) records all nine context
checks: Cms 1 migration applied (`…_AddLibraryContentVideoStorageKey`), Families
1 (`…_AddCareRecords`), Community 1 (`…_AddPostDeletionAudit`), the other six 0
pending.

`Verify-SanadDeployment.ps1` exit 0 over direct public HTTP: remote revision
exactly matches the manifest/pushed revision, Production environment,
**9/9 required vs applied heads**, startup log 9-context evidence accepted,
anonymous smoke `GET /api/v1/lookups/services` HTTP 200.

Post-verification, bounded and anonymous: former Rentals routes
`/api/v1/admin/rentals`, `/api/v1/family/rentals/bookings`,
`/api/v1/admin/rental-refund-cases` all return **404** (removed, not
auth-guarded). New care-record routes (`/api/v1/elderly/care-records`,
family dependent care-records, `/api/v1/admin/care-records`) return **401**
(exist, auth-protected). Retained schema re-proven on-host: all five `rentals`
tables still present with 0 rows and the `rentals` history row intact; new
schema present (`families.care_records` 13 columns; `cms.…library_contents.video_storage_key`;
`community"."Posts"."Version"/"DeletedBy"/"DeletedOnUtc"` — quoted PascalCase
names).

No provider calls were made, no OTP sent, no refunds executed, no money
movement, no test fixtures sent to Production, and no database restore was
performed. The only changes were the three startup migrations on the validated
backup's database.

## Limitations and Owner follow-ups

- Consumer-screen integration (Flutter/Admin) remains unverified — no UI
  source tree; API-only smokes above.
- Genuine R1 replacement video content, wallet end-to-end (deferred by Owner
  direction) and the fixed-name FUR-02/03/04 relational targets retain their
  documented BLOCKED/NOT RUN status from the acceptance gate.
- The queued duplicate-charge support follow-up model is **not** implemented or
  shipped; it stays in the queued plan only.
- Secret rotation remains an Owner follow-up for the previously redacted doc
  text and any exposed test credentials from earlier phases.
- `appsettings.Development.json` noted in the prior receipt is no longer present
  under `/root/sanad-api` (only `appsettings.json`); the static file warning is
  moot for the current directory state.
- Release-command transcripts and this receipt intentionally exclude all
  credential values; the startup.log retains EF Info SQL (column names only, no
  parameter values).
