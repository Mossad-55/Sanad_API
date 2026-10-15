# PORTAL-06B — local importer implementation and tests

Owner said continue after the approved cleanup and identified importer/preview
as the next development. Base `f932c46d826f758837398242ea370b6521c75e0e` plus
the existing uncommitted approved cleanup/docs delta; preserve all of it.
Accepted candidate SHA-256:
`E048767A00F5DE28B32E8F4E1E2B2BCBC4FA52C19BEA0C0D14D085DDB5AB8A8E`.

Implement an explicitly invoked standalone .NET operator tool under
`tools/Sanad.GeographyImport/`; no API startup registration, endpoints, runtime
config, seeding, migration, package-version changes or automatic deployment.
Own that project; its solution/test-project references; focused importer tests
under `tests/Sanad.UnitTests/Caregivers/Lookups/`; importer runbook/report and
current plan/handoff/index status. Existing cleanup files remain intact.
One mastermind performs bounded mapping/implementation/tests/review directly;
no concurrent delegated worker or repeated app audit is needed.

Required behavior:

- Catalogue hash/schema/bilingual/parent/duplicate validation before connecting.
- Explicit single environment connection key only; no inherited app config.
- Read-only consistent preview: per-source insert / exact-bilingual keep /
  keep-inactive / conflict decisions and preserved manual-row inventory.
- Exact ordinal names in the same parent; partial or multiple matches conflict.
  No fuzzy aliases, automatic rename/reparent/delete/reactivate. New records use
  new UUIDs recorded in the plan, never source keys as UUIDs. No insertion under
  inactive ancestors. Retain manual/existing IDs, timestamps and inactive flags.
- Reviewed plan/candidate/target fingerprints, expiry, required backup reference
  and explicit apply command. Apply re-reads and recomputes the plan before any
  insert; fail on drift, conflict, plan tampering or catalogue mismatch.
- Transactional all-or-nothing insertion. PostgreSQL lookup-table writer locks
  before target snapshot read prevent races with normal admin writes; no schema
  mutations. Readers remain available. Separate SQLite-memory relational tests
  prove transactions/rollback only, never claim PostgreSQL lock execution.
- No credentials in arguments/reports/errors. Never overwrite existing reports.

Tests: minimal synthetic in-memory SQLite fixtures prepared/read back before
scenarios; real accepted catalogue initial/repeat plans; ID/manual preservation;
partial/ambiguous duplicates; inactive parents; invalid data; expiry/hash/target/
stale/tampered plans; injected save failure rollback; Npgsql SQL/model generation
without connecting. No inherited PostgreSQL fixture, EnsureDeleted, live DB,
provider or API startup. Strict build + focused tests + architecture if affected.
Do not run full app suite solely for an isolated tool; before release remains
separate. No real database inspection/write/import and no commit/push/publish/
deploy without explicit later owner approval. Stop with local evidence.
