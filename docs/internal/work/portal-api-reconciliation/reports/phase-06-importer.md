# PORTAL-06B — local geography importer closeout

Date: 2026-10-15. Status: implementation, isolated tests and disposable PostgreSQL rehearsal Done locally;
actual target provisioning remains In progress. No commit/push/import/deploy.
Base: `f932c46d826f758837398242ea370b6521c75e0e` on main, with the preserved
approved cleanup/governance/docs delta. Owner said continue after importer
development was identified as next; exact scope is in the
[execution brief](../worker-prompts/06b-geography-importer.md).

## Delivery and rules

Standalone .NET 10 CLI in [tools/Sanad.GeographyImport](../../../../../tools/Sanad.GeographyImport/README.md),
not API startup, seeding, hosted service, endpoint or migration. Its dedicated
context maps only the three existing caregivers lookup tables. Existing package
versions reused. No API/domain/handler/CORS/provider behavior changed.

Accepted candidate bytes are pinned to SHA-256
`E048767A00F5DE28B32E8F4E1E2B2BCBC4FA52C19BEA0C0D14D085DDB5AB8A8E`,
27 governorates / 347 Cities / 5712 Areas. Source keys are not database UUIDs.
Historical/provisional coverage limitations remain; labels are not certified
current national geography. Original approved display edits remain traceable.

- Preview is a read-only consistent transaction and new report file only.
- Exact bilingual match under the mapped parent preserves ID/state/timestamps.
  Inactive matches stay inactive; unmatched manual rows (including Smouha) stay.
- Partial/ambiguous names, inactive ancestors for new rows, orphans, stale or
  tampered plans fail closed. No fuzzy merge, rename, deletion or reparenting.
- Apply requires the reviewed report hash, exact target fingerprint and backup/
  maintenance references; plans expire after 24 hours. Credentials come only
  from an explicit process environment key, never appsettings discovery.
- Apply locks all three tables, rechecks/recomputes the whole plan, inserts
  parent-first inside one serializable transaction, verifies the full resulting
  snapshot and commits. Failure rolls back; no automatic retry. Timestamp
  precision is pinned to PostgreSQL microseconds.
- Existing API uniqueness checks are read-before-write without unique indexes.
  Stop/drain all relevant writers during apply; table locks alone cannot stop
  an earlier application read from inserting a duplicate after commit. Required
  labels are operator attestations, not proof of backup or stopped writers.

## Readiness before tests

Exact child-process environment prepared before dispatch: integration database
keys and importer connection removed; nine module connections replaced with
non-contact dummy targets; migration and fixture-seed flags disabled. No inherited
target values printed. Each relational fixture creates its own SQLite
`:memory:` database, seeds only its scenario prerequisites and reads them back
before actions. No EnsureDeleted, PostgreSQL connection, API host, provider send,
VPS access, persistent fixture or production configuration read was needed.

## Executed evidence

- `dotnet restore Sanad.slnx --disable-parallel --nologo`: exit 0.
- Final `dotnet build Sanad.slnx --no-restore -m:1 --nologo /p:TreatWarningsAsErrors=true`:
  exit 0, zero warnings/errors (43.37 s).
- Guarded `dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj --no-build
  --no-restore --nologo -v minimal` with filter GeographyImportTests,
  EgyptCatalogueCompatibilityTests and Governorate/City/Area lookup tests:
  **113 passed / 0 failed / 0 skipped**, exit 0, no warnings.
  Includes 23 new importer cases and 90 existing compatibility/regressions.
- Full accepted catalogue: 6086 initial inserts into SQLite memory, fresh repeat
  zero inserts, same IDs/snapshot. Pure planning covers the same full catalogue.
- Reviewed JSON serialization/deserialization exercised before actual memory
  apply; manual/inactive preservation, partial/ambiguous conflicts, stale/tampered/
  expired/future plans, authority guards, cancellation and injected second-save
  failure all tested. The latter reads back empty after rollback.
- Npgsql model/SQL generation verifies three existing tables without connecting;
  this is **not** a live PostgreSQL lock/transaction proof.
- Architecture `--no-build --no-restore`: **1/1**, exit 0, no warnings.
- CLI `--help`: exit 0 without connection. Candidate hash unchanged.
- Final diff whitespace check: clean. Full suite not rerun for this standalone
  tool; prior PORTAL-07 full-suite evidence remains historical, not a new gate.

## Manifest and remaining gates

New tool: project, Program, ImportModel, ImportPlanner, LocationDbContext,
ImportStore and README. Solution and unit-test project reference added; new
GeographyImportTests. Execution brief, this report and central/portal handoffs
updated. Previous approved cleanup changes preserved. Direct bounded review
performed; optional worker roles skipped for this exact standalone scope.

Next: owner review and explicit commit/push approval. No release action taken.
Disposable PostgreSQL rehearsal is now complete (appendix below). Exact target
read-only preview remains separate. Resolve any actual target conflicts by an approved
reconciliation, never silent overwrites; obtain backup/restore and maintenance
orders before actual apply. Production privilege/schema/trigger/lock behavior,
backup, target data and performance remain unverified. Local SQLite success is
not proof those operator gates passed. Follow the operator runbook for commands.

## Owner-requested shortest route — disposable PostgreSQL rehearsal

Owner explicitly authorized a random one-time disposable local database and no
additional research. Used installed PostgreSQL 18.4 binaries to initialize an
entirely new temporary cluster, bound only to `127.0.0.1:58861`, with a fresh
`geography_rehearsal` database. No inherited connection settings, existing local
database, API process, provider or VPS was used. Only three lookup tables matching
the production EF column mappings were created; this is not a full application
migration/startup rehearsal. Empty counts were read back before scenarios.

Executed the actual compiled CLI, not a test double:

1. Preview: 6086 inserts, zero conflicts, zero preserved extras.
2. Apply with that file's exact hash/target and synthetic disposable backup/
   maintenance attestations: committed 6086 inserts.
3. Fresh preview: zero inserts/conflicts; second apply committed zero inserts.
4. SQL readback: **27 governorates / 347 cities / 5712 areas**, zero orphan Cities
   or Areas. All commands exited 0; the PostgreSQL table-lock and timestamp paths
   were exercised. No live lock-contention/soak or production-backup claim.
5. Temporary server stopped successfully. Data and JSON reports retained locally
   at `C:\Users\mosad\AppData\Local\Temp\sanad-geography-4c8e7c7b0e3143898b6408438d3ad155`;
   no deletion/reset of existing data. No new code/build inputs changed, so the
   prior strict build and 113/113 tests remain valid.

Shortest remaining route: explicit commit/push/package order, owner deployment,
and one backed-up maintenance-window geography operation on the exact VPS target.
Deployment alone does not populate lookups. Replacing existing lookup rows is
not implemented or authorized by the disposable rehearsal; the current CLI is
insert-only. Existing Area-ID dependencies must be checked before any production
replacement. No reset or CASCADE command is supplied as an implicit shortcut.

## Release authorization

Owner subsequently approved commit/push, publication and deployment on
2026-10-15. Release preparation started. Existing noninteractive SSH access to
the named VPS failed authentication; no remote mutation. No new API runtime
configuration is introduced. The standalone import requires temporary
`SANAD_GEOGRAPHY_CONNECTION` for the actual Caregivers database, not an additional
API systemd setting. Publishing/deploying the API alone does not populate data.
Production lookup deletion/reset/replacement is not part of this release order.

Pre-publication gate: guarded full unit suite **3358 passed / 0 failed / 8
documented skips / 3366 total**, exit 0, no warnings. Skips: five guarded
Identity/Chat PostgreSQL integration tests and three existing Families cases.
Existing strict zero-warning build, architecture 1/1 and focused 113/113 remain
valid with unchanged compiled inputs. Postman mapping rerun 556/620/0/0,
diff check clean. No reset fixtures or real provider sends in the full gate.
