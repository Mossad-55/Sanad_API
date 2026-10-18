# E01 whole-phase Worker brief — SuperAdmin user statistics and growth

## Authority and pinned baseline

The Owner explicitly approved the complete E01 contract on 2026-10-18:
`docs/internal/work/portal-api-reconciliation/contracts/e01-user-statistics-growth.md`.
Pinned `main` HEAD: `3af82f86a62749e7f2c684f5e56f96bbd5b7ac93` (FUR-10 docs
closeout). The worktree contains unrelated modified and untracked files. Preserve
them; do not reset, clean, stage, or overwrite them.
The Owner selected OpenCode Zen `opencode/longcat-2.5-preview-free` for the
first E01 attempt; that child hit a rate limit and is terminal. The Owner
authorized switching the same child session `ses_eb0cce48effeiIbvjsmV0itcR6` to
OpenCode Zen `opencode/beam-free#high`; the effective profile was reloaded and
verified. Do not create a replacement or switch routes automatically.

The partial E01 changes below persist and remain unaccepted. The Owner clarified
the contract: `usersByStatus` and `usersByAccountType` are arrays of records with
enum-valued numeric properties (`status`/`accountType`) and count values. Replace
the enum-keyed dictionaries and align the API test, OpenAPI schema test, docs,
and Postman examples with that approved array wire shape.

Read and follow `AGENTS.md`, `docs/internal/governance/AGENTS.md`,
`docs/internal/Mastermind_Handoff.md`,
`docs/internal/work/portal-api-reconciliation/plan.md`, the complete E01
contract, `docs/internal/Project_Main_Goal.md`,
`docs/internal/operations/codex-workflow.md`, and
`.opencode/agents/sanad-worker.md`. Inspect current status and relevant diff
before editing.

## Objective

Implement only the approved read-only aggregate endpoint:

`GET /api/v1/admin/users/statistics`

Current partial-diff review (recheck live files before editing):

- The controller route and query/handler/response were added, but no delivery
  report, guide, or Postman update exists yet.
- `AdminUserStatisticsResponse` currently uses enum-keyed dictionaries. Replace
  them with arrays of `{status,count}` and
  `{accountType,distinctUserCount}` records carrying numeric enum values, and
  align unit/API/OpenAPI tests and docs.
- `AdminUserStatisticsTests.SeedMixedUsers` attempts to add both
  `MedicalCaregiver` and `CompanionCaregiver` to one user, but the Identity domain
  explicitly prohibits combining those account types. The fixture creates
  eight users while asserting `TotalUsers == 6`. Correct the fixture using only
  domain-valid account combinations and reconcile all expected counts.
- `CairoMonthStart` currently returns Cairo-local month midnight marked as UTC
  without converting the local boundary to UTC. It is also called inside LINQ
  `Where`/`GroupBy`, which may not translate to production Npgsql. Correct the
  boundaries and ensure aggregates remain database-translatable without
  materializing user/PII rows; add exact timezone/month-boundary coverage.
- The E01 test currently creates a user with both caregiver account types,
  which `User.AddAccount` prohibits, and seeds eight users while asserting a
  total of six. Correct the fixture using only domain-valid account combinations
  and reconcile all expected totals.
- No successful E01 build/test evidence is available; the first selected route
  hit a rate limit. Do not treat authored checks as passed.

Use existing `SuperAdminOnly` authorization. The response aggregates distinct
Identity user rows by all current statuses, account types, and the latest 12
Cairo calendar months of new registrations from `User.CreatedOnUtc`. Preserve
numeric enum wire serialization; document the values. Do not add range filters,
PII, user listings, exports, or the undefined “services growth” metric. A user
with multiple account types contributes once to each matching role count, so
role counts are not additive to the distinct total.

The monthly series is chronological, zero-filled, and includes the current
partial month. Convert UTC creation timestamps using Cairo calendar boundaries,
not the host local timezone. Status counts are a current snapshot only.

## Owned files

- `src/API/Sanad.API/Controllers/AdminUsersController.cs`
- Add `src/Modules/Identity/Application/Sanad.Modules.Identity.Application/Users/Admin/AdminUserStatisticsQueries.cs`
- Add `tests/Sanad.UnitTests/Identity/AdminUserStatisticsTests.cs`
- Add `tests/Sanad.UnitTests/API/AdminUserStatisticsApiTests.cs`
- `tests/Sanad.UnitTests/API/PortalRemainingResponseContractTests.cs`
- `docs/admin/user-management.md`
- `docs/postman/admins/Sanad.Admin.postman_collection.json`
- Add `docs/internal/work/portal-api-reconciliation/reports/e01-user-statistics-growth-delivery.md`

Do not edit the Mastermind-owned phase plan/handoff, `Project_Main_Goal.md`,
screenshots, other phase docs, existing migrations, provider/configuration files,
or unrelated changes. If the API behavior cannot be implemented within this
manifest, report the exact blocker before expanding scope.

## Verification and stop conditions

- Use isolated synthetic test data and the existing test-host patterns. No
  `dotnet run`, Production/shared database, persistent fixture, provider, or
  Bruno operation.
- Run one strict solution build with zero warnings/errors, focused query/API/
  OpenAPI tests, Postman JSON/route validation, and `git diff --check`. The full
  suite is not a blanket gate.
- Record exact commands/results and distinguish executed from authored checks.
- No-op/repeated edits are forbidden. Inspect exact content before each edit;
  if the same unchanged failure occurs twice, stop and return `BLOCKED` with
  exact evidence. Do not launch `opencode run` or any other agent/session.
- Return one concise `COMPLETE` or `BLOCKED` report and stop. No commit, push,
  package, deployment, or E02–E08 work is authorized.
