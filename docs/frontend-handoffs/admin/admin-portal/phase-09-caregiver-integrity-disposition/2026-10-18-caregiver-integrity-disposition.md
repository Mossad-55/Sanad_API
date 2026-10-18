# Admin portal — FUR-09 Caregiver integrity disposition — caregiver list enrichment evidence

Date: 2026-10-18 (Africa/Cairo). Phase: FUR-09.
Purpose: local synthetic API reproduction and disposition; **not a deployment
notice** — deployment is N/A.
Audience: Admin portal (Admin audience = Admin portal + Care Home Owner portal).
Communication state: local phase evidence complete; no release authorized or
performed. Client adoption/forwarding is unconfirmed and not claimed.

No frontend repository was inspected. No frontend code defect is claimed.

## Baseline and authority

- Original intake and issue IDs: audit finding **A2** (six reported caregiver
  Identity mismatches in the admin caregiver list; cause unproven) and the
  related intake row "six caregiver Identity mismatches — Data inconsistency
  reported; query behavior confirmed; cause unproven" from the
  [intake audit](../../../../internal/work/portal-api-reconciliation/frontend-report-audit-2026-10-16.md).
  The earlier PORTAL-04 diagnostic (read-only
  `diagnostics/caregiver-identity.sql` and
  `diagnostics/caregiver-identity-runbook.md`) remains unchanged and is still
  the path to operator evidence.
- Canonical plan/approved contract: [FUR-09 local caregiver integrity API
  reproduction contract](../../../../internal/work/portal-api-reconciliation/contracts/fur-09-caregiver-integrity-disposition.md)
  (owner-approved 2026-10-18); [disposition
  report](../../../../internal/work/portal-api-reconciliation/reports/fur-09-caregiver-integrity-disposition.md).
- Implementation/deployed revision: **none** — FUR-09 is a local test/report
  phase only. **Deployment N/A.**
- Evidence limits: the reproduction runs through an in-process API host with
  synthetic SQLite attached schemas; it is not PostgreSQL execution, not a
  Production/shared-database connection, and not a live API run. Frontend
  repositories inspected: none.

## What the local evidence proves

| Issue | Correct behavior/usage | Evidence |
| --- | --- | --- |
| A2 | `GET /api/v1/admin/caregivers` enriches each caregiver row from `identity.users` through the existing deliberate LEFT JOIN: a matched Identity user returns the stored Arabic/English name and phone; a caregiver with no matching Identity user **remains in `items`** with null `arabicFullName`/`englishFullName`/`phoneNumber` and is still counted in `totalCount`. This is the designed behavior, reproduced end-to-end through the real controller, `CaregiverReviewAdmin` policy, MediatR handler and production list/count SQL. | [disposition report](../../../../internal/work/portal-api-reconciliation/reports/fur-09-caregiver-integrity-disposition.md) |
| A2 | A present Identity user holding the wrong account type (e.g. a Family account behind a Companion caregiver) stays visible with its stored enrichment — the list neither filters nor rewrites the row. Authorization behaves as documented: eligible SuperAdmin/SupportAdmin receive 200; anonymous receives 401; an authenticated non-eligible Family account receives 403. Status/type filtering and paged totals match the existing contract. | [disposition report](../../../../internal/work/portal-api-reconciliation/reports/fur-09-caregiver-integrity-disposition.md) |

Verification evidence (worker-reported): strict solution build passed with
0 warnings/errors; focused caregiver API/integrity tests passed 24/24; no
production code, query, authorization, connection configuration, migration,
diagnostic SQL/runbook, PORTAL-04 report or existing test/doc was modified.

## What needs correction or backend work

| Issue | Classification | Current truth and expected frontend action | Backend responsibility / phase | Evidence |
| --- | --- | --- | --- | --- |
| A2 | Unverified Production observation (cause not established) | The six reported Production caregiver rows and their cause remain **unverified**. Do not treat the admin caregiver list as broken and do not build a client-side filter/hide around null names — null enrichment is the designed LEFT JOIN behavior. Any display decision for Production rows awaits exact-ID, sanitized operator evidence. | No repair/quarantine recommended or authorized; separate contract required for any data action | [disposition report](../../../../internal/work/portal-api-reconciliation/reports/fur-09-caregiver-integrity-disposition.md); [FUR-09 contract](../../../../internal/work/portal-api-reconciliation/contracts/fur-09-caregiver-integrity-disposition.md) |
| A2 | Backend disposition | No row-level data action, INNER JOIN change, fabricated/recreated identities or deletion is recommended on the current evidence. The local reproduction confirms designed behavior on synthetic data; it neither identifies a defect nor implicates any Production row. | FUR-09 complete (local scope); any repair needs a new exact reviewed contract and owner authorization | [disposition report](../../../../internal/work/portal-api-reconciliation/reports/fur-09-caregiver-integrity-disposition.md) |

## Implementation connection

| Issue | Contract | Implementation/revision | Verification | Deployment | Remaining action |
| --- | --- | --- | --- | --- | --- |
| A2 | [FUR-09 contract](../../../../internal/work/portal-api-reconciliation/contracts/fur-09-caregiver-integrity-disposition.md) | New focused tests `tests/Sanad.UnitTests/API/CaregiverAdminListApiTests.cs` only; no production code changed | Strict build 0 warnings/errors; focused tests 24/24; `git diff --check` passed | **N/A** — local test/report only; no release | Obtain exact-ID sanitized operator evidence via the existing read-only diagnostic/runbook before any display or data decision |

## Decisions and evidence requested

- Owner decisions: none for this topic; no repair, quarantine or production
  target access is recommended or authorized on current evidence.
- Frontend evidence: the admin portal's rendering of caregiver rows with null
  names is client-side behavior that was not inspected; this notice asserts no
  mobile/web client state.
- Acceptance: the local HTTP reproduction matches the existing contract for
  matched, missing-Identity and wrong-account-type rows, authorization and
  paging/filtering; the six Production rows remain explicitly unverified.
- Configuration/migrations: none (no runtime delta of any kind in this phase).
- Next action: owner decides whether to obtain operator evidence via the
  existing read-only diagnostic; Mastermind review of the FUR-10 integration
  handoff. No frontend code defect is asserted.
