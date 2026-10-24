# Duplicate-charge claims, admin booking filters and W5 fixture pack — release receipt

Date: 2026-10-24 (Africa/Cairo). Status: **Done** — released and verified on Production.

## Release revision and gates

Released revision: `c1081620723a0ff2352711b4a3dce5d80c993e3a`, pushed to
`origin/main` as a normal fast-forward (`eb218a00..c1081620`), commit
`feat(finance-subscriptions): duplicate-charge claims, admin booking filters, W5 portal fixture pack`.
Baseline live revision before cutover: `e29bc2c…`.

Gate evidence before packaging:

| Gate | Result |
|---|---|
| Strict Release build (`TreatWarningsAsErrors`) | 0 warnings / 0 errors |
| Full unit suite | 5,523 passed, 26 skipped, 0 failed (5 m 19 s) |
| W5 fixture host strict build | 0 warnings / 0 errors |
| W5 offline tests | 17/17 |
| `git diff --cached --check` | clean |
| Postman collections | valid JSON (Admin Finance, App Family) |

Excluded and preserved uncommitted: `docs/internal/Mastermind_Handoff.md`
(parent-owned), plus debris (`buildout*.txt`, `testout*.txt`, `migout.txt`,
`e07-library-smoke.ps1`, `.opencode-audit-tmp/`, `workflow-audit/`,
`Mastermind_Handoff_Historical_2026-10-23.md`).

## W5 fixture pack: runtime acceptance (local)

Full W5 was a release condition and is now runtime-verified; the evidence
receipt is `w5-portal-fixture-runtime-2026-10-24.md`. Summary: guarded local
target `localhost:5432/SanadDb` with nine contexts and zero pending migrations
after 15 owner-approved additive migrations behind a validated backup; all six
provider seams proven scripted/inert with no surviving hosted services; all
twelve groups provisioned with persisted readbacks over three consecutive
idempotent runs; authenticated family (private credential file outside the
repository) and admin HTTP readbacks browsable against the loopback listener;
report-only cleanup with no delete surface.

That pass also caught a real Wave A defect that would have shipped a 500:
`GET /api/v1/admin/bookings` ordered and filtered on unwrapped value-converted
ids (`b.Id.Value`, `f.Id.Value`, `caregiver.Id.Value`), which Npgsql cannot
translate. Fixed by ordering on the mapped id (`BookingId` now implements
`IComparable`) and filtering on typed ids with unwrapping after materialization.

## Package

`Publish-SanadApi.ps1 -Configuration Release` exit 0 at the pushed revision.
Manifest `deploy/publish-out/sanad-deployment-manifest.json` revision matches the
push; nine source-derived heads — Identity `20261022094500_AddPartnerContactOptionalFields`,
Cms `20261023114500_AddLibraryContentVideoStorageKey`, Caregivers
`20261021130000_AddCaregiverReviewNotes`, Families `20261023131500_AddCareRecords`,
CareHomes `20261022213000_AddCareHomeRefundPolicySnapshots`, Notifications
`20261010010618_AddEmailHtmlSnapshots`, Community `20261023150000_AddPostDeletionAudit`,
Finance `20261024133842_AddDuplicateChargeClaims`, Chat `20261013171755_AddAttachmentCleanupIndex`.
Archive `deploy/sanad-api-c1081620.tar.gz`, SHA-256
`fab43bc765e766ca3b087dfc32f841bb97720f507ebe12c8363b2e289f8c36de`
(12,481,586 bytes; 119 entries); no `.env`, no `appsettings.Development.json`, no
key/cert material; SHA-256 verified identical on the target after transfer.
`.env` and `deploy/publish-out` both prove ignored.

## Target preflight and backup

Production `72.62.92.144:8091`, service `sanad-api.service`, `/root/sanad-api`,
existing `/etc/sanad/sanad.env`. Before cutover the service was active
(NRestarts=0) at `e29bc2c…`, Production, database reachable. Database identity
verified privately on-host: `SanadDb` on localhost:5433, PostgreSQL 16.15, 18 MB;
Finance applied head was the predecessor
`20261022230000_AddComplaintReviewsAndSubscriptionRefundRequests`.

Backup in `/root/sanad-releases/portal-report-c1081620-20261024172423/`:
`database-before-portal-report-c1081620.dump` (custom format, 802,600 bytes,
**899 TOC entries**, validated with `pg_restore -l`), `app-before.tar.gz`
(12,434,702 bytes) and `config-fingerprints-before.txt`.

## Cutover and verification

Only application files were replaced; shipped `appsettings*.json` were deleted
from the staging copy so the host keeps its own configuration. Service unit,
environment file, storage and key ring preserved. Service
`active`/`running`, NRestarts=0. Config fingerprints before/after identical
(**CONFIG_UNCHANGED**). Startup log (28,725 lines, retained on host in the
release directory) records all nine context checks; only Finance had a pending
migration: `20261024133842_AddDuplicateChargeClaims` applied, the other eight at
0 pending.

`Verify-SanadDeployment.ps1` exit 0 over public HTTP: remote revision exactly
matches the manifest/pushed revision, Production environment, **9/9 required vs
applied heads**, startup-log 9-context evidence accepted, anonymous smoke
`GET /api/v1/lookups/services` HTTP 200.

Bounded feature smokes (anonymous, no credentials, no provider calls):

| Request | Result |
|---|---|
| `GET /api/v1/admin/finance/subscription-refunds/duplicate-charge` | 401 |
| `GET /api/v1/family/subscriptions/refunds/duplicate-charge` | 401 |
| `GET /api/v1/admin/finance/complaint-reviews` | 401 |
| `POST /api/v1/family/subscriptions/refunds/duplicate-charge` (legacy, anonymous) | 401 |
| `POST …/duplicate-charge` with `claimSubmission: true` (anonymous) | 401 |

The authenticated legacy `400 Subscriptions.Refund.MigratedToClaim` mapping was
verified locally in the W5 run; no Production user credential was used, by
design.

## Limitations

- Authenticated Production claim flows were not exercised with real user
  credentials; contract behaviour is covered by unit tests and the local W5
  runtime readbacks.
- The new Finance table starts empty in Production; no backfill is needed
  because claims are created by family submissions only.
- No provider call or real money movement occurred in this release.
