# PORTAL-01 — fix admin user reactivation failures

Role: bounded implementation/test/documentation worker. The mastermind has
completed the scout/audit and the owner approved this phase. Implement this
phase only, then return the completed change for mastermind review.

## Intake and pin

Read root AGENTS.md, `docs/internal/governance/AGENTS.md`, the current
Mastermind_Handoff, and the PORTAL-01/rules/fixture sections of `../plan.md`.
Read the source files below. No whole-project audit is required.

Inspected clean pushed main before this handoff was
`955c33ab537ed365c94d00767351c53b5ec9f251`. The handoff commit adds only this
brief and updates the plan, Mastermind_Handoff and Project_Main_Goal. The
mastermind's launch message supplies that final pushed handoff revision: use
it as the exact worker base, record it, and verify `main == origin/main` with
a clean tree at intake. Do not reset to the older pin. If unrelated changes
have arrived, report the actual baseline/delta before touching overlapping files.

Work directly on main under current repository instructions; do not create or
switch branches/worktrees. You are not alone in the codebase. Preserve others'
edits, use explicit file staging, and do not revert unrelated work. Commit and
push the completed bounded task per standing governance. Do not deploy/publish,
merge another branch, change credentials, or apply database migrations.

## Problem and required behavior

`ReactivateAdminUserCommandHandler` loads the target and calls
`User.Reactivate`, which requires activation readiness. Admin-created users
start PendingVerification. Suspending/blocking one preserves its unverified
contact flags; reactivation then throws an uncaught DomainException and returns
500. Current positive tests start with a previously Active/verified user.

Fix the missing expected failure handling. Keep the existing domain rules:

- A user must have an account and a verified phone.
- A non-Elderly-only user also needs an email, verified email and password.
- Elderly-only readiness must not accidentally acquire email/password requirements.
- Read the domain state; the Admin list/detail `IsVerified` value is computed
  from status and is not proof that both contacts were verified.
- Never set verification flags or force a Pending/unready user Active.

Accepted result for an unready Suspended or Blocked target:

```json
{
  "status": 409,
  "title": "Conflict",
  "detail": "The user does not meet the activation requirements. Complete the required verification and account setup before reactivation.",
  "code": "Identity.Admin.ActivationNotReady",
  "traceId": "request-correlation-id"
}
```

The proposed detail above is safe fixed text, not an exception string. Keep
the target's status/reason/timestamps/contact flags and sessions unchanged on
this failure; create no Reactivated audit entry or success notice. A verified
Suspended/Blocked user still gets 200 with the existing AdminUserStatusResponse,
becomes Active, clears the status reason and writes one success audit/notice.
Previously revoked sessions remain revoked; new login is required. Repeating
reactivation against Active stays 409 Identity.Admin.NotSuspended. Preserve
existing 404 missing-user, 403 seeded protection and invalid-state outcomes.

Translate only the expected activation-readiness failure. Prefer reusing the
domain rule; a small domain readiness helper/refactor is allowed within the
manifest if needed without changing its rules. Do not match exception-message
strings, blanket-catch exceptions around persistence/notifications, or install
a global DomainException-to-400 handler. Only the new error requires the new
safe detail; do not change unrelated admin error payloads. No schema/enum
serialization redesign is part of this phase.

## Owned manifest

Production files (only these):

1. `src/Modules/Identity/Application/Sanad.Modules.Identity.Application/Users/Admin/AdminUserStatusCommands.cs`
   — new readiness error and bounded reactivation handling.
2. `src/API/Sanad.API/Controllers/AdminUsersController.cs`
   — map that error to 409 with safe detail, code and trace ID.
3. `src/Modules/Identity/Domain/Sanad.Modules.Identity.Domain/Users/User.cs`
   — only if a minimal readiness helper/refactor avoids duplicating domain rules;
   otherwise leave it untouched. No activation/verification policy change.

Tests/documentation:

4. `tests/Sanad.UnitTests/Identity/AdminUserManagementTests.cs` — reuse its
   fixture helpers and InMemory/SQLite theories, extending only as needed.
5. `tests/Sanad.UnitTests/API/AdminUsersReactivationControllerTests.cs` — new
   focused response/dispatch tests, or an equivalent bounded test file here.
6. `tests/Sanad.UnitTests/Identity/Users/UserStatusTransitionTests.cs` — only
   if the optional domain helper/refactor requires extra invariant coverage.
7. `docs/admin/user-management.md` — reactivation prerequisites and new 409,
   verified success and no session restoration; explain normal verification.
8. `docs/postman/admins/Sanad.Admin.postman_collection.json` — only the
   existing Reactivate user request description/examples. Keep other folders.
9. `docs/internal/work/portal-api-reconciliation/reports/phase-01-reactivation.md`
   — evidence, intake SHA, manifest, fixtures and limitations.
10. `docs/internal/work/portal-api-reconciliation/plan.md` — only PORTAL-01
    status/evidence link. Do not advance other phases or rewrite approval.

The mastermind owns the central handoff/index updates after review. No migration,
new package dependency, shared Error redesign, CORS, service configuration,
library, geography or caregiver-data repair is in this manifest. Stop only an
overlapping dependent change if a concrete extra file is essential; explain the
needed addition rather than widening scope silently.

## Mandatory configuration/fixture readiness BEFORE API tests

1. Inspect test configuration and available fixture helpers first. Use
   `AdminUserManagementTests.TestContexts.CreateAsync`: isolated EF InMemory
   databases or SQLite `Data Source=:memory:` with fake clocks and recording
   notices/OTP/email/SMS. No PostgreSQL or external provider is needed here.
2. Inspect fixture states before reuse. `AddUser(..., Active)` verifies contacts;
   `AddUser(..., PendingVerification)` does not set a password. For the reported
   case, use `CreateAdminUserCommandHandler` with the existing fake hasher, OTP,
   email and SMS helpers or explicitly construct the same pending admin state
   with a password. Do not mistakenly create a verified target in the negative test.
3. Prepare actors, target accounts and sessions before any controller/test-host
   request. Seed only missing test prerequisites. Add a never-verified
   SupportAdmin target and positive verified controls; create Suspended and
   Blocked states through the relevant commands/domain methods, respecting
   session/audit baselines. Read back the saved flags/status/relations first.
4. Record fixture readiness and provider/config isolation in your report with
   synthetic labels/counts. Do not print passwords, OTPs, tokens or connection
   strings. Never inspect/seed/reset the current Production VPS for this task.
5. Then run the selected handler/controller scenarios. A direct controller with
   a real seeded handler dispatch chain is sufficient; no hosted API is required.
   If choosing an in-process HTTP test host, prepare its fake/configuration and
   data state before starting it, suppress real migrations/hosted provider work,
   and keep all persistence isolated. Do not start an API to discover missing seeds.

Keep each scenario's state independent; do not reuse a consumed/re-activated
target as though it were still suspended. Preserve unrelated pre-existing rows
in any reused fixture. No database reset command is authorized.

## Required regression evidence

First add a failing regression for the reported pending-create -> suspend/block
-> reactivate path and capture the failure before implementing the fix. Use the
existing InMemory and SQLite variants for persistence assertions where supported.

After the fix, cover:

- Unverified and partly verified non-Elderly targets in both Suspended/Blocked
  states: explicit ActivationNotReady failure with no thrown exception.
- Re-read persistence: no status/reason/timestamp/contact changes, no new success
  audit/notice, and no session restoration when reactivation is rejected.
- Previously verified Suspended and Blocked targets: 200/existing result shape,
  Active, cleared reason, exactly one new Reactivated audit and notice,
  revoked sessions unchanged. Verify repeat reactivation remains the old conflict.
- Readiness edge cases: missing required email/password/account and a ready
  Elderly-only target, so the guard matches the existing domain rules.
- Seeded target, missing target and invalid source state keep existing codes.
- Controller boundary for new failure: 409, Conflict, exact stable code, useful
  safe detail, trace ID. Also cover normal success/old failure dispatch behavior.
  Where feasible use the real seeded handler/controller chain for the originally
  failing path; do not claim an HTTP run if only controller methods were tested.
- Unexpected persistence/notification failures are not relabeled ActivationNotReady.

Do not suspend/block real users or use the developer's anonymized residual
account. No live API/provider/production operation is needed for acceptance.

## Verification, closeout and return

After the final code change, run one strict build:

```powershell
dotnet build Sanad.slnx --no-restore -m:1 --nologo /p:TreatWarningsAsErrors=true
```

Then the focused tests (adapt the new class name only if you changed its filename):

```powershell
dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj --no-build --no-restore --nologo -v minimal --filter "FullyQualifiedName~Sanad.UnitTests.Identity.AdminUserManagementTests|FullyQualifiedName~Sanad.UnitTests.Identity.Users.UserStatusTransitionTests|FullyQualifiedName~Sanad.UnitTests.API.AdminUsersReactivationControllerTests"
```

Require exit 0 and zero warnings. Parse the touched Postman collection as JSON,
run the Postman-only mapping gate and `git diff --check`. Bruno is excluded.
No full suite, PostgreSQL fixture, API startup, publish or VPS operation is
required for this phase; integrated full-suite verification belongs to PORTAL-07.
Investigate relevant failures rather than suppressing warnings or broadening scope.

Record actual commands, counts, warnings/skips, pre-fix regression evidence,
readiness/seed reuse or creation, and exact limitations. Mark PORTAL-01 Done in
the plan only when all phase acceptance criteria pass, with a link to your report;
mastermind review still follows. Commit only owned files and push main per
standing instruction; do not record a commit's own SHA inside itself.

Return: base SHA, implementation/delivered SHA, owned changed files, root cause,
new behavior/error example, checks with counts/exit/warnings/skips, fixture
preparation evidence, and remaining findings/handoffs. State whether any API,
database/provider or deployment actions occurred. Do not begin PORTAL-02.
