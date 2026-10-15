# PORTAL-01 report — admin user reactivation readiness failure

Status: Done (worker implementation complete; mastermind review follows).
Do not begin PORTAL-02 from this report.

## Intake

- Worker base: pushed `main` `44c0bebb887bb3e6145919f9928123a431e53f79` (launch-message
  revision). At intake `main == origin/main` with a clean tree; no reset to the
  older `955c33a` pin. No unrelated delta touched overlapping files.
- Brief: `docs/internal/work/portal-api-reconciliation/worker-prompts/01-reactivation.md`.
- No API startup, database reset, provider send, migration application, VPS
  access, deployment, or publish occurred. All persistence is isolated
  EF InMemory / SQLite `:memory:`.

## Root cause

`ReactivateAdminUserCommandHandler` loaded the target, checked status guards,
then called `User.Reactivate`, which calls `ValidateActivationReadiness`.
Admin-created users start `PendingVerification` with unverified phone/email, so
suspending/blocking one preserves the unverified flags and reactivation threw
an uncaught `DomainException`. The registered exception handler only handles
FluentValidation, so the request surfaced as HTTP 500. Existing positive tests
started from previously Active/verified users, so the path was uncovered.

## Fix (approved behavior only)

- `src/Modules/Identity/Application/.../Users/Admin/AdminUserStatusCommands.cs`:
  new `AdminUserErrors.ActivationNotReady`
  (`Identity.Admin.ActivationNotReady`, safe fixed detail) and a bounded guard
  `if (!user.IsActivationReady) return AdminUserErrors.ActivationNotReady;`
  after the existing status guards and before `user.Reactivate`. No
  exception-message matching, no blanket catch; persistence/notification
  failures still propagate and are never relabeled.
- `src/Modules/Identity/Domain/.../Users/User.cs`: added read-only
  `IsActivationReady` mirroring `ValidateActivationReadiness` (account present,
  phone verified; non-Elderly-only additionally requires email, verified email,
  password; Elderly-only never gains email/password requirements).
  `ValidateActivationReadiness` rules are unchanged.
- `src/API/Sanad.API/Controllers/AdminUsersController.cs`: mapped
  `Identity.Admin.ActivationNotReady` to 409 with title `Conflict`, the exact
  code, trace ID, and — only for this error — the safe fixed detail. Unrelated
  error payloads are unchanged.

Accepted result for an unready Suspended/Blocked target is 409 with the exact
code/detail from the brief; status/reason/timestamps/contact flags/sessions are
unchanged, with no Reactivated audit entry or success notice. Verified targets
still get 200/Active/cleared reason/one audit/one notice; repeat reactivation
stays 409 `Identity.Admin.NotSuspended`; 404/403/invalid-state outcomes are
preserved.

## Fixture readiness (mandatory gate, read-only first)

- Configuration: reused `AdminUserManagementTests.TestContexts.CreateAsync`
  (isolated EF InMemory per-GUID database or SQLite `Data Source=:memory:`),
  fake clocks (`FixedClock`), `NeverSeededProtection`/`EmailSeedProtection`,
  `RecordingNoticePublisher`, fake hasher/OTP/email/SMS senders. Controller
  tests use `DefaultHttpContext` plus a stub/delegating `ISender`. No
  PostgreSQL, no external provider, no hosted API, no startup seeding or
  migrations. The current Production VPS was never inspected, seeded, or reset.
- Seed inspection before reuse: `AddUser(..., Active)` verifies contacts and
  sets a password; `AddUser(..., PendingVerification)` leaves contacts
  unverified with no password. For the reported case the suite uses
  `CreateAdminUserCommandHandler` with the existing fake hasher/OTP/email/SMS
  helpers (producing the true pending admin state: account + password,
  unverified contacts) and, in the controller chain test, the explicitly
  constructed equivalent pending admin state with a password. No verified
  target was mistaken for an unready one.
- Actors/targets prepared before any dispatch: acting SuperAdmin (verified);
  never-verified SupportAdmin targets plus verified controls; Suspended/Blocked
  states entered through the suspend/block handlers and domain methods;
  sessions via `AddSession`. Saved flags/status/relations were read back before
  the reactivation dispatch. Each scenario keeps independent state; no
  consumed/re-activated target was reused as still-suspended.
- Synthetic labels only; no passwords, OTPs, tokens, or connection strings are
  recorded here.

## Regression evidence

- Pre-fix reproduction (before the guard/mapping change, with only the new
  error symbol declared): all 4 cases of
  `Reactivate_UnreadyPendingAdmin_ReturnsActivationNotReady` failed with
  `Sanad.BuildingBlocks.Domain.Exceptions.DomainException: Phone number must
  be verified`, thrown from `User.ValidateActivationReadiness` (User.cs) via
  `User.Reactivate` through `ReactivateAdminUserCommandHandler.Handle`
  (`AdminUserStatusCommands.cs`) — the unhandled path that becomes HTTP 500.
- Post-fix coverage (all in the owned test files, InMemory + SQLite variants
  where supported):
  - Unverified (created-flow and constructed) and partly verified
    (phone-only/email-only) non-Elderly targets in both Suspended and Blocked
    states: explicit `ActivationNotReady` failure, no thrown exception; re-read
    persistence shows no status/reason/timestamp/contact change, no new success
    audit/notice, no session restoration.
  - Previously verified Suspended and Blocked targets: 200/existing result
    shape, Active, cleared reason, exactly one new Reactivated audit and
    notice, revoked sessions unchanged; repeat reactivation keeps the old
    `NotSuspended` conflict.
  - Readiness edges: missing account/email/password each rejected; ready
    Elderly-only target reactivates (no email/password requirement acquired).
  - Seeded, missing, and invalid-source-state targets keep existing codes.
  - Controller boundary: 409/`Conflict`/exact code/safe detail/trace ID for
    the new failure; unchanged success/old-failure dispatch; the originally
    failing pending-create → suspend → reactivate path verified through the
    real seeded handler/controller chain (method-level dispatch, not a hosted
    HTTP run).
  - Persistence failure (throwing save decorator on a ready target) propagates
    instead of being relabeled `ActivationNotReady`.

## Checks (after final code change)

- `dotnet build Sanad.slnx --no-restore -m:1 --nologo
  /p:TreatWarningsAsErrors=true` — exit 0, 0 warnings, 0 errors.
- `dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj --no-build
  --no-restore --nologo -v minimal --filter
  "FullyQualifiedName~Sanad.UnitTests.Identity.AdminUserManagementTests|FullyQualifiedName~Sanad.UnitTests.Identity.Users.UserStatusTransitionTests|FullyQualifiedName~Sanad.UnitTests.API.AdminUsersReactivationControllerTests"`
  — exit 0; 53 passed, 0 failed, 0 skipped.
- `Sanad.Admin.postman_collection.json` parses as JSON (PowerShell
  `ConvertFrom-Json`); contract mapping gate: 556 controller signatures /
  620 Postman requests / 0 missing / 0 orphan; `git diff --check` clean.
- No full suite, PostgreSQL fixture, API startup, publish, or VPS operation;
  integrated full-suite verification belongs to PORTAL-07. Bruno is excluded.

## Remaining findings / handoff

- None blocking PORTAL-01: acceptance criteria pass within the owned manifest.
- Note for later phases: `AdminUsersController.ToCommandResult` still carries a
  duplicated `LastSuperAdmin` pattern and builds ProblemDetails without detail
  for older codes — left untouched per the minimal-scope instruction; any
  cleanup belongs to a future metadata pass, not PORTAL-01.
- Mastermind review follows; PORTAL-02 is not started.
