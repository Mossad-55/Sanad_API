# FUR-02 worker delivery — verification continuity

Date: 2026-10-16 (Africa/Cairo).

Status: **In progress — worker complete, awaiting Mastermind review.**
Whole-phase implementation, tests, feature docs, Postman and audience
handoffs are complete locally and uncommitted. Do not commit, push, publish,
deploy, or start FUR-03. No production migration was applied by the worker.

## 1. Baseline and intake

- Repository `D:\Sanad_API`, branch `main`, HEAD `ba88f3c63120110dbf5e84744aaa46abbed01ad9`
  identical to `origin/main` at intake and at delivery (verified with
  `git rev-parse HEAD` and `git rev-parse origin/main`; `git status --short
  --branch` shows `## main...origin/main`).
- The intake local delta matches the brief exactly: modified
  `docs/internal/work/portal-api-reconciliation/plan.md` and
  `docs/internal/Mastermind_Handoff.md` (Mastermind FUR-02 planning text),
  plus untracked `contracts/fur-02-verification-continuity.md` and
  `worker-prompts/fur-02-verification-continuity.md`. No other worker,
  owner, or private overlap was found; unrelated files were preserved and no
  reset/stage/commit occurred.
- Read before editing: root `AGENTS.md`, governance, current handoff, active
  plan, the approved contract, and the FUR-01 release receipt
  (`reports/fur-01-deployment.md`, release `f9231fb` deployed).
- Binding contract: [FUR-02 verification continuity](../contracts/fur-02-verification-continuity.md),
  owner-approved 2026-10-16, covering intake `FR-20261016-01` and
  `FR-20261016-12`. No new endpoint, fixed OTP, force verification,
  CORS/provider/config change, or production shortcut was introduced.

## 2. What was implemented

### 2.1 Additive restricted-login recovery (`FR-20261016-12`)

- `LoginResponse` gains four nullable trailing fields with `null` defaults,
  so existing constructions (including elderly OTP login and Active login)
  compile and serialize unchanged:
  `EmailVerificationRequestId`, `EmailVerificationRequestExpiresOnUtc`,
  `PhoneVerificationRequestId`, `PhoneVerificationRequestExpiresOnUtc`.
  Response IDs keep the `{ "value": guid }` wrapper shape; request bodies are
  untouched plain GUIDs.
- `LoginCommandHandler`, for a password-authenticated `PendingVerification`
  user only, selects per purpose (`VerifyEmail` / `VerifyPhone`) the newest
  request of the same user with status `Pending` or `Expired`, ordered by
  creation time then ID, and returns its ID plus true expiry. A verified
  channel returns null; a channel with no resendable request returns null
  (no OTP is manufactured, no channel is marked verified). Foreign users'
  IDs and reset-password/elderly-login purposes can never match the filter.
  Restricted token, flags, null refresh/session, and the normal-access policy
  are unchanged. Active and elderly OTP logins return null for all four
  fields by construction.

### 2.2 Retry-safe resend ordering (`FR-20261016-01`)

- `ResendOtpCommandHandler` now accepts `Pending` **and** `Expired`
  requests; `Verified` and `Invalidated` still return `RequestNotPending`.
  Not-found, superseded (latest `Pending`/`Expired` for the same user,
  purpose, and target, ordered by creation time then ID), and the 60-second
  cooldown checks are preserved.
- The replacement code is generated in memory and the provider sender is
  called **before** any verification state is invalidated or saved.
  Provider rejection/exception/timeout persists nothing and returns the new
  `Identity.Verification.ProviderDeliveryFailed` error. Only an actually
  cancelled request token propagates (`OperationCanceledException` rethrown
  when `cancellationToken.IsCancellationRequested`); a provider-side
  cancellation/timeout (for example `TaskCanceledException` from `HttpClient`
  while the request token is still live) maps to the 502 error with no state
  change. Only after a successful send does the handler invalidate
  the old `Pending`/`Expired` request, insert exactly one five-minute
  replacement, and save both atomically, returning the existing
  `ResendOtpResponse` shape.
- `ResultProblemDetailsMapper` maps the new code to HTTP **502** with title
  `Bad Gateway` and the exact owner-approved public detail "Verification code
  delivery is temporarily unavailable. Please try again." (this code only;
  all other errors keep their existing safe wording). Raw provider bodies,
  OTPs, credentials, and exception text never appear on the wire.
  `AuthController.ResendOtp` declares the 502 response in metadata only
  (no route or wire change).

### 2.3 Optimistic concurrency marker and migration

- `VerificationRequest` gains an `int Version` marker (new rows start at 1;
  every state mutation bumps it exactly once, including the new
  `InvalidateForResend`, which is the only transition that accepts an
  `Expired` old request). EF configuration marks it `IsConcurrencyToken()`
  on column `version`.
- A losing simultaneous resend fails its atomic save with
  `DbUpdateConcurrencyException`, translated to the existing HTTP 409
  `Identity.Verification.RequestSuperseded`; exactly one replacement
  persists. No in-process-only lock is used.
- Migration **`20261016133316_AddVerificationRequestVersion`**: adds
  non-nullable `identity.verification_requests.version` (`integer`,
  default 1 for pre-existing rows). One migration pair plus model snapshot
  only; no other migration file was touched.

### 2.4 Registration failure compatibility

- Registration is intentionally unchanged (its files are outside the owned
  scope): user plus both pending requests are saved before either provider
  send, so a post-save send failure preserves them and password login
  recovers both IDs. A new test proves this end to end at handler level.

## 3. Pre-change reproduction (source-level)

Pre-change `ResendOtpCommandHandler` (HEAD revision, lines 99/123/126):
`currentRequest.Invalidate(...)` → `SaveChangesAsync(...)` →
`SendCodeAsync(...)`. A provider exception therefore left the old request
invalidated with no replacement ID delivered — the exact FUR-02 failure.
Post-change, a throwing fake sender yields HTTP-mapped 502 with the old
request still `Pending`, zero saves, and a retry with the same ID succeeding
(regression tests in section 5). This is a source-level reproduction against
the real handler; it is distinct from the frontend-reported provider
exception, whose provider-side cause was never reproduced and is not claimed
here.

## 4. Configuration, restart, and package disclosure

- Configuration changes: **none**. Restart requirements: **none** beyond the
  normal release process. New package/runtime dependencies: **none**.
- One additive Identity migration (`20261016133316_AddVerificationRequestVersion`)
  ships with the phase and applies at the normal release migration step.
  No production migration was applied by the worker (section 6).

## 5. Tests

### 5.1 Fixture and environment gate

- Actors/states identified first: pending user with password, email/phone
  requests, partly verified user, expired/`Expired` requests, foreign user,
  wrong-purpose requests, and concurrent resend callers.
- Isolated suites use InMemory contexts, fixed clocks, and fake senders
  (deterministic success / `InvalidOperationException` rejection /
  `TimeoutException` / `OperationCanceledException` / barrier-gated send);
  no real SMS/email provider is contacted anywhere.
- Relational proof uses a validated disposable target: local PostgreSQL 18.4
  at `localhost:5432`, database `sanad_fur02_verify`, created empty for this
  phase. Endpoint (localhost) and database name (exact match) are asserted in
  test code before any write; any other value fails closed. The connection
  string lived only in the explicit test child process
  (`ConnectionStrings__Fur02VerificationDatabase`); the guarded suite leaves
  it absent so these tests skip there. No Production-mode API was started, no
  existing database was used as a target, reset, or seeded broadly, and only
  per-run rows (deleted afterwards by ID in `finally`) were written.
- InMemory concurrency coverage is labeled logic-only by construction; the
  PostgreSQL run below is the relational proof. No Bruno work was assigned
  or performed.

### 5.2 Focused results (isolated child, sanitized module targets)

Strict solution build before tests: **zero warnings/errors**.

Focused filter (Identity verification/login/registration/request-domain/
model/elderly/password plus API auth/contract/mapper/OpenAPI tests):
**322 passed / 0 failed / 0 skipped.**

Coverage added or adjusted for this phase:

- Registration success/send-failure and login recovery: send failure throws,
  user plus both `Pending` requests persist, password login returns both
  true IDs with restricted token and null refresh/session.
- Pending login: both IDs/expiries; latest-wins across same-purpose
  requests; past-expiry and `verify`-marked `Expired` IDs returned with true
  (past) expiry; verified channel null; foreign-user and
  reset/elderly-purpose exclusion; Active login all-null.
- Resend: `Expired` accepted and atomically replaced (old `Invalidated`,
  one `Pending` replacement, version bumps asserted); `Verified` and
  `Invalidated` rejected; provider rejection/timeout/provider-side
  cancellation (live request token) → 502 error with `Pending` status, zero
  saves, unchanged version; genuinely cancelled request token propagates
  with state preserved; same-ID retry succeeds after a failure; per-user
  supersession (no cross-user leak); `Expired`-latest superseded;
  max-attempts-invalidated rejection; cooldown boundary preserved.
- Domain: version starts at 1; every mutation bumps exactly once;
  `InvalidateForResend` accepts `Pending`/`Expired` and rejects terminal
  states. Model: `version` is a concurrency token on the real
  `IdentityDbContext` model. Mapping: 502 for the new code, 409 for
  superseded/cooldown, with leak-free details.
- API/contract: restricted login serializes null recovery fields and
  wrapper shape when populated; guide and Postman literals match serialized
  shapes; resend failure mapping at controller level (502/409); generated
  OpenAPI publishes the resend 502 response and the four `LoginResponse`
  schema properties.

### 5.3 PostgreSQL proof (explicit child only)

`VerificationContinuityPostgresTests`: **4 passed / 0 failed** against
`localhost:5432/sanad_fur02_verify` (PostgreSQL 18.4):

1. `Migration_ShouldApplyCleanly_AndContainVerificationVersion` — full
   `MigrateAsync()` on the fresh database succeeds; the applied-migrations
   history contains `20261016133316_AddVerificationRequestVersion`; the
   `identity.verification_requests.version` column exists.
2. `ConcurrentResend_ShouldPersistExactlyOneReplacement` — two separate
   contexts/connections resend the same past-cooldown request with
   barrier-gated fake senders: exactly one success plus one
   `RequestSuperseded` failure; readback with a third context shows exactly
   two rows (old `Invalidated`, one `Pending` replacement matching the
   winner's ID/expiry). Both fake sends were recorded, evidencing the
   documented losing-send ambiguity.
3. `Resend_ShouldReplaceAlreadyExpiredRequest_Atomically` — an already
   `Expired` old row becomes `Invalidated` with exactly one five-minute
   replacement in one atomic save.
4. `Supersession_ShouldPreferHigherId_WhenCreatedTimesTie` — equal creation
   times resolve deterministically by request ID on PostgreSQL.

### 5.4 Release-level gates (guarded, via `Test-SanadPortalGate.ps1`)

- Guarded full unit suite: **3454 passed / 0 failed / 12 skipped**
  (5 pre-existing PostgreSQL-integration skips + 3 pre-declared Families
  legacy skips + 4 FUR-02 PostgreSQL proof skips, which require the explicit
  opt-in target above).
- Architecture tests: **1/1**.
- Postman collections parse: **22/22**. `git diff --check`: clean.
  Relative Markdown links in all touched/added docs resolve (the two report
  links in the audience handoffs target this file).
- Actual route sets were not re-compared with the mapping script because no
  route was added, removed, or renamed — only a `ProducesResponseType`
  metadata entry on the existing resend operation. OpenAPI inspection above
  covers the affected metadata.

## 6. Migration and target statement

- New migration ID: `20261016133316_AddVerificationRequestVersion`
  (pair plus updated `IdentityDbContextModelSnapshot`; `IsConcurrencyToken`
  present in both).
- Applied during testing **only** to the disposable `sanad_fur02_verify`
  database via `MigrateAsync()` in the proof tests. No migration was applied
  to Production, to `SanadDb`, to `SanadIntegrationDb`, or to any other
  existing database. The disposable database was dropped after evidence
  collection; the throwaway provisioner project lived outside the repository
  and was removed.
- No migration-history or configuration drift exists in the repository beyond
  the one new migration pair and snapshot.

## 7. Provider ambiguity and concurrent losing send

Retry-safe persisted state is **not** exactly-once external delivery, and the
guides now state this: the code is sent before any state change, so a
provider may accept a message and then fail before the API observes success
(the old request stays usable — the chosen tradeoff), and a losing
concurrent resend may already have sent a code that is never accepted (proven
by the dual-send readback in the concurrency test). Clients must always use
the latest request ID received (via resend response or password login) and
must treat 502 as "retry the same ID" and 409-superseded as
"recover the current ID via login". No in-process lock or exactly-once
delivery is claimed.

## 8. Docs, Postman, and audience handoffs

- `docs/auth/registration-and-verification.md`: send-failure preservation,
  `Expired` resendability, 502 retry rule, cooldown/supersession semantics,
  one-winner concurrency and the at-least-once boundary.
- `docs/auth/email-password-login.md`: pending-response recovery fields with
  a populated example, per-channel null rules, Active/elderly null rule.
- `docs/auth/errors.md`: `ProviderDeliveryFailed` → 502 row, 502 safe-detail
  row, resendability matrix, no-leak wording.
- `docs/auth/claims-and-policies.md`: restricted-vs-normal recovery-ID row.
- `docs/postman/Sanad.Auth.postman_collection.json`: null recovery fields
  on both login examples, resend description updated, new literal
  `502 Provider delivery failed` example. Collection still parses; the
  seven shape-checked bodies still match serialization.
- Audience handoffs (truthful local-delivery notices; no frontend code
  inspected; not deployed), linked from `docs/frontend-handoffs/README.md`:
  Care Home Owner
  (`admin/care-home-owner/phase-02-verification-continuity/2026-10-16-verification-continuity.md`)
  and shared App
  (`app/shared/phase-02-verification-continuity/2026-10-16-verification-continuity.md`).
  Both distinguish the former lost-signup-ID assumption from the backend
  resend reliability risk.

## 9. Exact changed-file manifest

Production (within the brief's owned list; no other production file changed,
and no registration/provider-adapter edit was needed):

- `src/Modules/Identity/Application/Sanad.Modules.Identity.Application/Authentication/Login/LoginResponse.cs`
- `src/Modules/Identity/Application/Sanad.Modules.Identity.Application/Authentication/Login/LoginCommandHandler.cs`
- `src/Modules/Identity/Application/Sanad.Modules.Identity.Application/Authentication/Verification/ResendOtpCommandHandler.cs`
- `src/Modules/Identity/Application/Sanad.Modules.Identity.Application/Authentication/Verification/ResendOtpErrors.cs`
- `src/Modules/Identity/Domain/Sanad.Modules.Identity.Domain/Authentication/VerificationRequests/VerificationRequest.cs`
- `src/Modules/Identity/Infrastructure/Sanad.Modules.Identity.Infrastructure/Persistence/Configurations/VerificationRequestConfiguration.cs`
- `src/Modules/Identity/Infrastructure/Sanad.Modules.Identity.Infrastructure/Persistence/Migrations/20261016133316_AddVerificationRequestVersion.cs` (new)
- `src/Modules/Identity/Infrastructure/Sanad.Modules.Identity.Infrastructure/Persistence/Migrations/20261016133316_AddVerificationRequestVersion.Designer.cs` (new)
- `src/Modules/Identity/Infrastructure/Sanad.Modules.Identity.Infrastructure/Persistence/Migrations/IdentityDbContextModelSnapshot.cs`
- `src/API/Sanad.API/ProblemDetail/ResultProblemDetailsMapper.cs`
- `src/API/Sanad.API/Controllers/AuthController.cs` (502 response metadata only)

Behavior-neutral side effect, disclosed: `VerificationRequest.Verify`,
`RegisterFailedAttempt`, `Invalidate`, and `MarkExpired` now bump the same
concurrency marker, so the untouched elderly-login and password-reset callers
of `Invalidate`/`MarkExpired`/`Verify` also participate in optimistic
concurrency with no file, wire, or policy change. No additional production
file or persistence abstraction was required.

Tests (focused Identity/API files plus one bounded disposable-PostgreSQL file):

- `tests/Sanad.UnitTests/Identity/Verification/ResendOtpCommandHandlerTests.cs`
- `tests/Sanad.UnitTests/Identity/Login/LoginCommandHandlerTests.cs`
- `tests/Sanad.UnitTests/Identity/Registration/RegisterUserCommandHandlerTests.cs`
- `tests/Sanad.UnitTests/Identity/Registration/IdentityTestDbContext.cs` (test mirror of the concurrency token)
- `tests/Sanad.UnitTests/Identity/Authentication/VerificationRequests/VerificationRequestTests.cs`
- `tests/Sanad.UnitTests/Identity/Infrastructure/IdentityDbContextModelTests.cs`
- `tests/Sanad.UnitTests/Identity/Infrastructure/VerificationContinuityPostgresTests.cs` (new)
- `tests/Sanad.UnitTests/API/PortalAuthExampleContractTests.cs`
- `tests/Sanad.UnitTests/API/PortalOpenApiContractTests.cs`
- `tests/Sanad.UnitTests/API/AuthControllerRegistrationTests.cs`
- `tests/Sanad.UnitTests/API/ResultProblemDetailsMapperTests.cs`

Docs/Postman/handoffs/status:

- `docs/auth/registration-and-verification.md`
- `docs/auth/email-password-login.md`
- `docs/auth/errors.md`
- `docs/auth/claims-and-policies.md`
- `docs/postman/Sanad.Auth.postman_collection.json`
- `docs/frontend-handoffs/admin/care-home-owner/phase-02-verification-continuity/2026-10-16-verification-continuity.md` (new)
- `docs/frontend-handoffs/app/shared/phase-02-verification-continuity/2026-10-16-verification-continuity.md` (new)
- `docs/frontend-handoffs/README.md`
- `docs/internal/work/portal-api-reconciliation/plan.md` (FUR-02 status/evidence line only)
- `docs/internal/Mastermind_Handoff.md` (FUR-02 status/evidence lines only)
- This report (new).

## 10. Traceability and next receipt

- `FR-20261016-12` → [approved contract](../contracts/fur-02-verification-continuity.md)
  §2.1 → `LoginResponse`/`LoginCommandHandler` → login recovery tests, guide,
  Postman, and handoffs above.
- `FR-20261016-01` → approved contract §2.2–§2.3 → resend handler/errors/
  mapper/metadata, concurrency marker plus migration
  `20261016133316_AddVerificationRequestVersion`, resend/model/mapper/
  PostgreSQL tests, guides, Postman 502 example, and handoffs above.
- Deployment receipt placeholder: a later Mastermind release step records the
  accepted-phase commit, publication, migration application, and verification
  (or explicit N/A). This worker performed no release operation.

## 11. Remaining risks and blockers

- **No blockers.** One honest limitation: the losing concurrent send and the
  provider-accept-then-fail window are inherent at-least-once boundaries
  (section 7), documented rather than hidden.
- The guarded full suite cannot execute the four PostgreSQL proof tests by
  design (target opt-in absent → skipped); their 4/4 explicit pass is the
  relational evidence, and rerunning them needs only the same disposable
  target variable.
- Live SMS/email providers, public HTTP paths, and deployment verification
  were out of scope and are not claimed.
- Mastermind review may return bounded corrections through the owner; the
  worker made no commit, push, publication, deployment, or Done marking, and
  did not start FUR-03.

## 12. Correction pass (Mastermind review R1–R3)

Mastermind's [review](fur-02-mastermind-review.md) requested three bounded
corrections inside the approved contract; all were applied with no contract
or migration change:

- R1 — cancellation classification: the resend handler now propagates only
  an actually cancelled request token (`catch (OperationCanceledException)
  when (cancellationToken.IsCancellationRequested)`); provider-side
  cancellation/timeout with a live token maps to 502 with no state change.
  The old test expecting propagation for an uncancelled
  `OperationCanceledException` was revised to expect 502, and a new test
  proves propagation plus preserved state for a genuinely cancelled token.
- R2 — wire truth: the 502 problem detail for
  `Identity.Verification.ProviderDeliveryFailed` is exactly "Verification
  code delivery is temporarily unavailable. Please try again." with title
  `Bad Gateway` (this code only; other errors unchanged). Mapper tests pin
  status/title/detail/code and assert raw provider text, OTPs, and
  credentials never appear; a controller test pins the exact 502 body;
  `docs/auth/errors.md` and the literal Postman 502 example were aligned.
- R3 — attribution: `FR-20261016-01` is the phone OTP resend failure and
  `FR-20261016-12` is lost signup verification ID recovery. Headings, table
  rows, and traceability in this report and both audience notices were
  corrected; combined references were left intact.

Re-verification after the corrections (same sanitized child-process
isolation; no provider or database writes): strict solution build zero
warnings/errors; focused resend/mapper/controller/API contract sets
**156 passed / 0 failed**; guarded full unit suite **3457 passed / 0 failed /
12 skipped** (5 pre-existing PostgreSQL-integration + 3 Families legacy + 4
FUR-02 PostgreSQL proof skips); architecture **1/1**; Postman **22/22**
parse; `git diff --check` clean; relative Markdown links resolve. No
persistence change was made, so the previously recorded disposable-PostgreSQL
proof (4/4, since-removed target) stands without a new database write. No
commit, push, or deployment.

## 13. Safety correction pass (S1–S2)

A second bounded brief asked for two verification-safety corrections; both
were applied with no contract, migration, or persistence change:

- S1 — guarded-suite isolation: `docs/internal/tools/Test-SanadPortalGate.ps1`
  now also removes `ConnectionStrings__Fur02VerificationDatabase` from the
  actual dotnet test child environment and asserts its absence fail-closed
  (the key never matched the legacy `*IntegrationDatabase` pattern). Guard
  behavior was proven by invoking the script with a harmless inherited
  sentinel value (`SENTINEL-NOT-A-REAL-CONNECTION-STRING`, never a real
  connection string): the gate still reported integration targets absent and
  all four FUR-02 PostgreSQL tests skipped, with no PostgreSQL contact or
  database write. The sentinel lived only in that proving invocation and was
  cleared afterwards. The explicit disposable-PostgreSQL opt-in proof test
  and its target validation were not modified.
- S2 — mid-send cancellation pin: new test
  `Handle_ShouldPropagateCancellationAndPreserveState_WhenRequestTokenCanceledDuringSend`
  uses a fake sender that is definitely entered, cancels the request token
  during send, then throws `OperationCanceledException` like a token-honoring
  sender. It asserts propagation, an entered sender, an unchanged `Pending`
  request, no replacement row, and zero saves. The pre-cancelled-token test
  and the live-token provider-side `TaskCanceledException` → 502 test are
  kept, so both provider-boundary branches stay pinned.

Re-verification after S1–S2 (exact results):

- `dotnet build Sanad.slnx --no-restore -m:1 --nologo -v minimal` → exit 0,
  **0 warnings / 0 errors**.
- Focused resend suite
  (`--filter FullyQualifiedName~Sanad.UnitTests.Identity.Verification.ResendOtpCommandHandlerTests`,
  sanitized child env, integration keys absent, module targets dummy) →
  **19 passed / 0 failed / 0 skipped** (18 prior + 1 new mid-send test).
- Guarded full unit gate
  (`pwsh Test-SanadPortalGate.ps1 -Suite Unit`, invoked with the inherited
  sentinel described above) → exit 0, **3458 passed / 0 failed / 12 skipped**
  (same 5 + 3 + 4 skip composition; the +1 pass versus the previous 3457 is
  the new mid-send test). The four FUR-02 PostgreSQL skip lines were
  observed in the output, proving the child guard.
- Guarded architecture gate (`-Suite Architecture`, no sentinel) → exit 0,
  **1 passed / 0 failed**.
- `git diff --check` → exit 0 (only pre-existing LF/CRLF normalization
  notices, no whitespace errors).
- Limitation: no new PostgreSQL write was made or needed (no persistence
  change); the 4/4 relational proof from the removed disposable target
  stands. No production target, real provider, commit, push, or deployment.
  FUR-02 remains In progress, awaiting Mastermind review; FUR-03 not started.
