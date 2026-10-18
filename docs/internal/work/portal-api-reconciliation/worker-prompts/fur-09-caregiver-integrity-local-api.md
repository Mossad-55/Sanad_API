# FUR-09 whole-phase Worker brief — Local caregiver integrity API reproduction

## Authority and pinned baseline

The Owner approved the complete FUR-09 contract on 2026-10-18:
`docs/internal/work/portal-api-reconciliation/contracts/fur-09-caregiver-integrity-disposition.md`.
Pinned `main` HEAD: `caf9d8892eeef54728d7139460df32d21c55bf70` (FUR-08 release
receipt). The worktree has unrelated modified and untracked files. Preserve all
of them; do not reset, clean, stage, or overwrite them.
The Owner selected OpenCode Zen model `opencode/longcat-2.5-preview-free`
(LongCat 2.5 Preview Free). The Mastermind must reload OpenCode and verify the
effective `sanad-worker` model before dispatch. Do not start until that exact
model is loaded, and do not switch routes automatically.

Read and follow `AGENTS.md`, `docs/internal/governance/AGENTS.md`,
`docs/internal/Mastermind_Handoff.md`,
`docs/internal/work/portal-api-reconciliation/plan.md`, the complete FUR-09
contract above, `docs/internal/Project_Main_Goal.md`,
`docs/internal/operations/codex-workflow.md`, and
`.opencode/agents/sanad-worker.md`. Inspect current status/diff first.

## Context and evidence boundaries

The earlier PORTAL-04 delivery added and reviewed:

- `docs/internal/work/portal-api-reconciliation/diagnostics/caregiver-identity.sql`
- `docs/internal/work/portal-api-reconciliation/diagnostics/caregiver-identity-runbook.md`
- `docs/internal/work/portal-api-reconciliation/reports/phase-04-caregiver-integrity.md`
- `tests/Sanad.UnitTests/Caregivers/CaregiverIntegrityFixture.cs` and local
  relational/in-memory tests.

Those artifacts establish local LEFT JOIN/list-count behavior only. No query was
run on Production; the six reported rows and their cause remain unverified. The
new test must use only synthetic SQLite attached schemas and the existing
isolated `PortalApiMetadataHost`; it must never connect to PostgreSQL, Production,
or a shared local database. Do not run `dotnet run`, seed a persistent target,
use a real credential, or call a provider.

## Objective

Add one focused HTTP-level test using `WebApplicationFactory<Program>` that
exercises `GET /api/v1/admin/caregivers` through the real controller/policy,
handler, and production caregiver list/count SQL. Reuse `PortalApiMetadataHost`
and `CaregiverIntegrityFixture`; replace only the test host's caregiver context
with the fixture's open SQLite connection, including its attached `caregivers`
and `identity` schemas. Keep all other contexts test-only/InMemory, seeders and
migrations disabled, background services removed, and data entirely synthetic.

The HTTP test must verify:

1. Eligible SuperAdmin (or SupportAdmin) receives 200; anonymous receives 401;
   at least one non-eligible authenticated account receives 403.
2. A matched caregiver returns stored Identity name/phone enrichment.
3. A caregiver whose `user_id` has no Identity row remains in `items`, its
   enrichment fields are null, and `totalCount` still counts that row.
4. A present Identity row with an unexpected caregiver account type remains
   visible and is not rewritten/filtered by the list endpoint.
5. Status/type filter and paging behavior preserve expected rows and totals.

Do not use an API action to create fixtures. Seed the fixture before the HTTP
request and read it back before invoking the route. No actual user data or IDs.

## Owned files

- Add only `tests/Sanad.UnitTests/API/CaregiverAdminListApiTests.cs` for the
  in-process HTTP coverage. Reuse existing fixtures; if an additional helper
  file or change to existing shared host/fixture is materially required, stop
  and report the exact need instead of expanding the manifest.
- Add only
  `docs/internal/work/portal-api-reconciliation/reports/fur-09-caregiver-integrity-disposition.md`
  as the report.
- The FUR-09 contract, phase plan, Mastermind handoff, existing SQL/runbook,
  existing PORTAL-04 report, production code, and unrelated files are not owned.

## Acceptance and checks

- Report local behavior evidence separately from Production cause. State that
  the six Production rows remain unverified and that no repair/quarantine is
  recommended from synthetic evidence.
- Run a strict solution build after final test changes with zero warnings/errors.
- Run the focused new API test and directly relevant existing caregiver
  integrity tests; report exact command and passed/failed/skipped counts.
- Run `git diff --check` on owned files. No full suite, live API, Bruno,
  PostgreSQL, Production, migration, persistent database write, provider, commit,
  push, or release activity.

## Stop conditions

Do not invent a disposition for Production rows. Stop and report `BLOCKED` if the
test host cannot safely replace only the caregiver context with the existing
SQLite attached-schema fixture, if a shared/persistent target would be needed,
or if an out-of-manifest change is required. Do not start another agent or run
`opencode run`. Inspect exact file contents before every edit; do not repeat
no-op edits or unchanged failing commands. After the same unchanged failure
occurs twice, stop and report the exact error and evidence. Otherwise return one
concise `COMPLETE` report and stop. Do not commit/push/release or start another
phase.
