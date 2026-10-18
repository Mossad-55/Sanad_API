# FUR-09 contract — Local caregiver integrity API reproduction

Approved by the Owner on 2026-10-18.

## Goal

Extend the existing synthetic caregiver/Identity integrity evidence through the
actual Admin HTTP endpoint, then record what the local evidence proves. The six
reported Production rows remain unverified unless exact-ID, sanitized operator
evidence is later provided.

## Scope and behavior

- Add a focused in-process API integration test for
  `GET /api/v1/admin/caregivers` using the existing isolated
  `PortalApiMetadataHost`, synthetic test authentication, the real controller,
  authorization policy, handler, and caregiver list/count SQL.
- Reuse the existing `CaregiverIntegrityFixture` synthetic caregivers, Identity
  records, and attached SQLite schemas. Override only the test host's caregiver
  context as needed; all other contexts remain test-isolated and startup seeders,
  migrations, and background services remain disabled.
- Exercise a matched Identity row, a caregiver row with no matching Identity
  user, and a present Identity user with the wrong caregiver account type.
  Verify that the matched row returns its stored name/phone enrichment; a
  missing Identity row remains visible with nullable name/phone fields and is
  included in the list total; and a wrong account type does not silently filter
  or rewrite the caregiver-list row.
- Verify the eligible Admin can read the endpoint and anonymous/non-eligible
  callers are denied. Verify the relevant status/type filtering and paged list
  total through the HTTP response.
- Produce a sanitized disposition report that relates the local API evidence to
  the existing PORTAL-04 diagnostic/runbook and clearly distinguishes local
  reproduction from Production evidence. Recommend no row-level data action on
  the current evidence.

## Exclusions and safety

- No Production or shared-local database connection; no actual caregiver IDs,
  real credentials, persistent fixture writes, migration, seed job, provider
  call, Bruno run, deployment, or external API call.
- The test uses an in-process test server and synthetic SQLite attached schemas;
  it does not establish PostgreSQL execution or identify the cause of the six
  reported Production rows.
- No API behavior, query, authorization, connection configuration, or runtime
  code change. Do not change `INNER/LEFT JOIN` semantics, fabricate/recreate
  identities, delete/quarantine rows, or change production targets.
- Existing diagnostic SQL, runbook, and PORTAL-04 report remain unchanged.
  Any exact repair, quarantine, or production target access requires a separate
  contract/authorization.

## Owned files and deliverables

- Add `tests/Sanad.UnitTests/API/CaregiverAdminListApiTests.cs`; reuse
  `PortalApiMetadataHost` and `CaregiverIntegrityFixture` where possible. Keep
  any helper local to that new test file unless a material need requires Owner
  clarification.
- Add `docs/internal/work/portal-api-reconciliation/reports/fur-09-caregiver-integrity-disposition.md`.
- Do not modify production code, existing diagnostics, unrelated tests/docs,
  Mastermind-owned plan/handoff, worker profiles, or unrelated local changes.

## Acceptance and verification

- The HTTP test proves the authenticated Admin endpoint returns matched and
  unmatched synthetic rows as expected; unauthorized access is denied.
- Paging/filtering and count behavior match the existing contract.
- Report what the test proves and does not prove. Explicitly keep the Production
  six-row cause unverified and recommend no data repair/quarantine without exact
  operator evidence.
- Run one strict solution build (zero warnings/errors) after final test changes,
  plus focused API and relevant caregiver integrity tests. Do not run the full
  suite unless changed risk warrants it. No Bruno or live API run.
- Report exact commands/results and fixture effects. Stop at local acceptance;
  no commit/push/publish/deploy/database action is included.
