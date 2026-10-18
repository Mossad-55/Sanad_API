# FUR-09 disposition — caregiver integrity local API reproduction

Status: COMPLETE (local phase evidence only; no release authorized)
Worker: Sanad phase Worker, `opencode/longcat-2.5-preview-free` (Owner-selected)
Base: pinned `main` `caf9d8892eeef54728d7139460df32d21c55bf70` plus pre-existing
local delta; all unrelated modified/untracked files were preserved untouched.

## What was added

- `tests/Sanad.UnitTests/API/CaregiverAdminListApiTests.cs` — 7 new focused
  HTTP-level tests for `GET /api/v1/admin/caregivers` through the real
  controller, `CaregiverReviewAdmin` policy, MediatR handler and the production
  caregiver list/count SQL.
- This report.

No production code, query, authorization, connection configuration, migration,
diagnostic SQL/runbook, PORTAL-04 report, or existing test/doc was modified.

## Test design (evidence boundary)

The tests reuse the existing isolated `PortalApiMetadataHost` (dummy module
connections, disabled seeders/migrations, no background jobs, ephemeral
DataProtection, test JWT options) and the existing
`CaregiverIntegrityFixture` synthetic SQLite attached schemas
(`caregivers.caregivers` + `identity.users` + `identity.user_accounts`) on a
kept-open `:memory:` connection. Only the test host's `CaregiversDbContext` /
`ICaregiversDbContext` registrations are replaced with the fixture's SQLite
connection; every other context stays InMemory. Fixtures are seeded and read
back on the fixture connection before any HTTP request. All data is synthetic
fixed GUIDs, names and phones; no real user data, credentials, providers,
PostgreSQL, Production/shared database, Bruno, or live API run.

## Verification (exact commands and results)

- Strict solution build:
  `dotnet build Sanad.slnx --configuration Release --no-restore -p:TreatWarningsAsErrors=true`
  — passed, 0 warnings, 0 errors.
- Focused tests:
  `dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj --configuration Release --no-build --no-restore --filter "FullyQualifiedName~CaregiverAdminListApiTests|FullyQualifiedName~CaregiverIdentityIntegrityTests|FullyQualifiedName~CaregiverAdminListIntegrityTests"`
  — passed 24/24 (7 new FUR-09 API tests + 10 existing
  `CaregiverIdentityIntegrityTests` + 7 existing
  `CaregiverAdminListIntegrityTests`), 0 failed, 0 skipped, ~45 s.
- `git diff --check` — passed (only pre-existing LF/CRLF notices for unrelated
  tracked files); new test file also verified free of trailing whitespace.

## What the local API evidence proves

Reproduced end-to-end through HTTP on synthetic SQLite attached schemas:

1. Authorization: eligible SuperAdmin receives 200; eligible SupportAdmin
   receives 200; anonymous receives 401; an authenticated non-eligible Family
   account receives 403. The `CaregiverReviewAdmin` policy behaves as
   documented at the HTTP boundary.
2. Matched enrichment: a caregiver whose `user_id` matches an `identity.users`
   row returns the stored Arabic/English name and phone
   (`Matched Medical` / `+10000000001`; `Matched Companion` / `+10000000005`).
3. Missing Identity user: caregivers whose `user_id` has no `identity.users`
   row remain in `items` with null `arabicFullName`/`englishFullName`/
   `phoneNumber`, and `totalCount` still counts them (5/5 rows returned and
   counted by default).
4. Wrong account type: a present Identity user holding a Family account
   behind a Companion caregiver stays visible with its stored enrichment and
   is neither filtered out nor rewritten by the list endpoint — confirming the
   deliberate LEFT JOIN semantics through the real HTTP response.
5. Filtering/paging: `?status=4` (Active) returns totalCount 2 with both rows
   Active; `?type=2` (Companion) returns totalCount 3 including the orphan
   row with null enrichment; paging (`page=1/2/3`, `pageSize=2`) returns the
   deterministic `updated_on_utc DESC, id` windows while `totalCount` stays 5,
   and an empty window still reports the full total.

This extends the PORTAL-04 local evidence (handler/SQL-level
`CaregiverIdentityIntegrityTests`, classification
`CaregiverAdminListIntegrityTests`, `CaregiverIntegrityFixture`) up through
the actual Admin HTTP endpoint, real controller/policy/handler/SQL stack.

## What it does not prove (Production boundary)

- It does not execute against PostgreSQL; SQLite attached-schema execution is
  not PostgreSQL proof.
- It does not connect to, read, or diagnose the Production database. The six
  reported Production rows and their cause remain **unverified** pending
  exact-ID, sanitized operator evidence via the existing read-only
  `diagnostics/caregiver-identity.sql` and
  `diagnostics/caregiver-identity-runbook.md` (unchanged by this phase).
- It does not establish which of the runbook's candidate explanations
  (absent Identity row visible from the Caregivers target, wrong id value, or
  stale copy) applies to any real row.

## Disposition recommendation

No row-level data action is recommended on the current evidence. The local
reproduction confirms the endpoint behaves exactly as designed (LEFT JOIN
enrichment with nullable fields; count includes every caregiver row) on
synthetic data; it neither identifies a defect nor implicates any Production
row. Any repair, quarantine, INNER JOIN change, or production target access
requires a separate exact reviewed contract and owner authorization.

## Fixture/data effects and limitations

- Each test creates its own in-memory SQLite connection, seeds 5 synthetic
  caregivers and 3 synthetic Identity users, reads them back, serves the HTTP
  scenario, then disposes the connection. No persistent fixture writes, no
  database/provider/migration/seed activity, no commit/push/publish/deploy.
- Anonymous/403/support-admin tests assert status codes only; they do not
  inspect response bodies.
- No full suite, live API, Bruno, PostgreSQL, or Production activity was run,
  per the approved contract.
