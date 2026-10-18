# E03 whole-phase Worker brief — caregiver medical-access decision audit

## Pinned baseline and local delta

- Repository: `D:\Sanad_API`, branch `main`.
- Pinned `HEAD` and `origin/main`:
  `59a6582a065bd81b51d388fd984348b277f09d03`.
- Preserve all unrelated Owner changes; do not stage, alter, clean, or overwrite:
  - modified `.codex/agents/sanad_documenter.toml`,
    `.codex/agents/sanad_implementer.toml`, `.codex/agents/sanad_reviewer.toml`,
    `.codex/agents/sanad_scout.toml`, `.codex/agents/sanad_test_author.toml`,
    `.codex/config.toml`, `README.md`, `docs/internal/Project_Main_Goal.md`,
    `docs/internal/governance/AGENTS.md`,
    `docs/internal/operations/codex-workflow.md`,
    `tools/opencode_bridge/opencode_bridge.py`;
  - untracked `.opencode-audit-tmp/` and `.opencode/`.
- Effective existing `sanad-worker` profile is Owner-selected
  `opencode/beam-free#high`; do not modify profile/config files.
- Do not commit, push, package, publish, deploy, apply migrations, or start any
  phase after E03.

## Required reading

Read and follow repository `AGENTS.md`,
`docs/internal/governance/AGENTS.md`, the current
`docs/internal/Mastermind_Handoff.md`, this approved contract, and this entire
brief. Read only E03-linked slice material and source needed for the manifest.
The approved E03 contract is
`docs/internal/work/portal-api-reconciliation/contracts/e03-medical-access-audit.md`.

The Owner approved this contract on 2026-10-18. Do not reopen the approved
scope, infer a caregiver medical-read feature, or expand the grant semantics.
Stop and report `BLOCKED` if an ambiguity or required file outside the manifest
prevents safe implementation.

## Objective

Implement the approved E03 phase end-to-end: append-only audit records for
successful and authorization-denied Family medical-access grant/revoke
decisions, a SuperAdmin/SupportAdmin-only paged Admin read, and 12-calendar-month
system retention—without adding caregiver medical-record reads or changing the
current grant access behavior.

## Non-negotiable behavior

- Audit only `POST .../medical-access-grants` and `DELETE
  .../medical-access-grants/{grantId}` decisions:
  - successful create/revoke -> `Allowed` with action `CreateGrant`/`RevokeGrant`;
  - `Families.AccessDenied` from the existing Family ownership/manage checks
    -> `Denied` with stable reason code `Families.AccessDenied`;
  - do not record GET grant reads, framework auth failures, malformed requests,
    invalid/inactive caregivers, duplicate grants, unknown grant IDs, or other
    non-authorization failures.
- Preserve all existing routes, grant semantics, HTTP statuses, and errors.
  Successful grant/revoke mutation and its audit event must be atomic. A denied
  attempt must not mutate a grant. If an audit write fails, never report a
  successful grant/revoke; return safe `503 Families.AuditWriteFailed` without
  database details, and do not silently treat a denial as audited. Rethrow
  cancellation; do not swallow `OperationCanceledException`. Preserve all other
  existing errors/statuses.
- Store only the exact fields and nullability in the contract. Never persist or
  return grant notes, medical data, profile/history/medication content, names,
  email, phone, IP, request/response bodies, tokens, or free text. Denied events
  leave grant details null; do not persist unvalidated attempted grant details.
- No historical backfill. No caregiver medical-read endpoint or use of these
  grant records to authorize a new consumer. Do not claim this phase logs actual
  caregiver record reads.
- Add dedicated `MedicalAccessAuditAdmin` policy: Normal JWT, SuperAdmin and
  SupportAdmin only; ContentAdmin/app accounts denied. Do not broaden another
  policy.
- Implement bounded stable paging/filtering exactly as in the contract and a
  daily bounded cleanup that deletes only E03 audit events older than the
  12-calendar-month UTC cutoff. The system-only purge must work without
  weakening the normal Families append-only guard. Audit reads must exclude
  expired rows even if cleanup is delayed. Never purge MedicalAccessGrant or
  other audit tables.
- No frontend, shared database/Production, fixtures, migration application,
  provider, Bruno, release operation, or later phase.

## Owned files

Change only the approved manifest in the contract. The Mastermind owns the
contract, this brief, `plan.md`, `Mastermind_Handoff.md`, and current project
status. If an additional source/test/doc file is necessary, stop and report the
exact path and reason; do not edit it without a bounded Mastermind correction
instruction.

## Required coverage and delivery

Test atomic success event creation for grant/revoke, authorized-denial event
creation, exclusion of non-authorization failures, no medical/free-text data,
retention immutability and cutoff/purge, Admin filters/paging/order, exact
role boundaries, 503 audit-store failure mapping, cancellation propagation, UTC
and calendar-range validation, page-offset bounds, and unchanged existing grant
response/error behavior. Test the generated OpenAPI for the Admin endpoint/DTO
and update the Family guide, new Admin guide, Admin overview link, and both
Postman collections.

Run one strict Release solution build after final code changes (zero warnings
and errors), focused Families/API/OpenAPI/retention tests, Postman JSON/route
validation, and `git diff --check`. Full suite and Bruno are not required. Use
only in-memory/synthetic test data; do not launch the API against a shared DB or
apply any migration.

Update `docs/internal/work/portal-api-reconciliation/reports/e03-medical-access-audit-delivery.md`
with exact commands/results, counts, skips/warnings, changed files, and
limitations. Distinguish generated migration from migration application. Do not
claim unrun checks passed. Preserve unrelated edits. Do not commit, push,
publish, deploy, or begin another phase. Return one concise final report with
status (`COMPLETE` or `BLOCKED`), evidence, and any Owner decision needed, then
stop.
