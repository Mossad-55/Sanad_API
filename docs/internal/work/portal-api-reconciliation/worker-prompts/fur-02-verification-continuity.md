# FUR-02 worker brief — verification continuity

Prepared 2026-10-16 (Africa/Cairo). **Ready for the owner's one whole-phase
implementation worker.** The [approved contract](../contracts/fur-02-verification-continuity.md)
is binding. Complete code, tests, feature docs, Postman and the saved delivery
report together; then stop for Mastermind review. Do not start FUR-03.

## Intake and source pin

Repository `D:\Sanad_API`; preparation HEAD and `origin/main`:
`ba88f3c63120110dbf5e84744aaa46abbed01ad9`. At preparation, the only
local delta is the Mastermind's FUR-02 planning documentation: modified
`docs/internal/work/portal-api-reconciliation/plan.md` and
`docs/internal/Mastermind_Handoff.md`, plus this untracked contract and prompt.
Recheck `git status --short --branch`, HEAD, and relevant diffs on intake.
Read root `AGENTS.md`, governance, current handoff, active plan, approved
contract and the FUR-01 release receipt. You are not alone in this repository:
preserve owner/private/unrelated files and any later edits. Work on current
`main`; do not create a routine branch/worktree, reset files, or stage/commit.

Report a changed source baseline or overlap to the owner before editing those
files. The owner gives this prompt to one worker; do not dispatch other workers.

## Objective and exact behavior

Cover intake `FR-20261016-01` and `FR-20261016-12` within FUR-02.

1. Add nullable `emailVerificationRequestId`,
   `emailVerificationRequestExpiresOnUtc`, `phoneVerificationRequestId` and
   `phoneVerificationRequestExpiresOnUtc` to `LoginResponse`. On successful
   password login for `PendingVerification`, select each authenticated user's
   latest `Pending` or `Expired` signup verification request by purpose, then
   creation time and ID. Return its true expiry so resend can recover it,
   including when `verify` marked it `Expired`. Verified channels return null.
   Active and elderly OTP login return null. Preserve the existing wrapper
   serialization for response IDs and plain GUIDs in request bodies. Preserve
   the restricted token, flags, null refresh/session and normal-access policy.
   No other user's, password-reset or elderly-login request ID can appear.
2. In existing resend, accept `Pending` and `Expired` requests, preserving
   not-found, superseded and 60-second cooldown checks. `Verified` and
   `Invalidated` remain non-resendable. Send the generated code first. On provider
   rejection/exception/timeout, persist no verification change and return
   HTTP 502 `Identity.Verification.ProviderDeliveryFailed` with the public
   message "Verification code delivery is temporarily unavailable. Please try
   again." Propagate request cancellation while preserving state. On provider
   success, atomically invalidate the old `Pending` or `Expired` request,
   update its concurrency marker, save exactly one replacement
   with a five-minute expiry, and return the existing response shape.
3. Add a PostgreSQL-safe optimistic concurrency marker to
   `VerificationRequest`, EF configuration and **one Identity migration**.
   A losing simultaneous resend returns the existing HTTP 409
   `Identity.Verification.RequestSuperseded`; exactly one replacement persists.
   Apply the same atomic old-row update and replacement insert when the old
   request was already `Expired`. Supersession compares the latest `Pending`
   or `Expired` request for the same user, purpose and target.
   The losing request may already have sent an unusable code. Document that
   provider/database ambiguity. Do not use an in-process-only lock or claim
   exactly-once external delivery.
4. Preserve registration's persisted user and both pending requests if a
   post-save provider send fails; prove password-login recovery. Keep existing
   register/verify/resend requests, success response shapes, status codes,
   six-digit OTP, five attempts and account-type rules. No new endpoint,
   fixed OTP, force verification, CORS/provider/config change or production
   shortcut.

If the approved behavior cannot be implemented safely within this file scope,
stop and report the specific conflict. Do not invent a new policy.

## Owned files

Production changes are limited to:

- `src/Modules/Identity/Application/Sanad.Modules.Identity.Application/Authentication/Login/LoginResponse.cs`
- `src/Modules/Identity/Application/Sanad.Modules.Identity.Application/Authentication/Login/LoginCommandHandler.cs`
- `src/Modules/Identity/Application/Sanad.Modules.Identity.Application/Authentication/Verification/ResendOtpCommandHandler.cs`
- `src/Modules/Identity/Application/Sanad.Modules.Identity.Application/Authentication/Verification/ResendOtpErrors.cs`
- `src/Modules/Identity/Domain/Sanad.Modules.Identity.Domain/Authentication/VerificationRequests/VerificationRequest.cs`
- `src/Modules/Identity/Infrastructure/Sanad.Modules.Identity.Infrastructure/Persistence/Configurations/VerificationRequestConfiguration.cs`
- `src/Modules/Identity/Infrastructure/Sanad.Modules.Identity.Infrastructure/Persistence/Migrations/` — one new migration pair and model snapshot only
- `src/API/Sanad.API/ProblemDetail/ResultProblemDetailsMapper.cs`
- `src/API/Sanad.API/Controllers/AuthController.cs` — response metadata only if needed

If a narrowly necessary persistence abstraction or API wiring change is
required, disclose the exact additional file and reason in the saved report;
do not broaden the feature. Do not edit registration/provider adapters unless
a concrete defect in this contract requires it and the owner agrees to that
scope change.

Tests may edit/add only the focused Identity registration, login, verification,
model and API auth/contract test files under `tests/Sanad.UnitTests/Identity/`
and `tests/Sanad.UnitTests/API/`, plus a bounded disposable PostgreSQL test
file under the existing test project if safe. Reuse existing fake senders and
isolated hosts. Feature documentation scope:
`docs/auth/registration-and-verification.md`,
`docs/auth/email-password-login.md`, `docs/auth/errors.md`,
`docs/auth/claims-and-policies.md` only where behavior changes, and
`docs/postman/Sanad.Auth.postman_collection.json`.

Create the sanitized worker report at
`docs/internal/work/portal-api-reconciliation/reports/fur-02-worker-delivery.md`.
Create a dated Care Home Owner handoff under
`docs/frontend-handoffs/admin/care-home-owner/phase-02-verification-continuity/`
and a shared App auth handoff under
`docs/frontend-handoffs/app/shared/phase-02-verification-continuity/` if the
contract affects those clients; link each from `docs/frontend-handoffs/README.md`.
These handoffs are truthful local-delivery notices, not frontend code reviews
or deployment claims. The worker may update only FUR-02 status/evidence lines
in the active `plan.md` and current `Mastermind_Handoff.md` without rewriting
historical receipts.

## Fixture and environment gate before business tests

First identify the actors and request states: pending user with password,
email/phone requests, partly verified user, expired pending request, foreign
user, and concurrent resend callers. Prove the selected database target is
disposable, with its endpoint and database name validated before any write.
Neutralize inherited connection strings and external provider settings in
the **actual test child process**; confirm that SMS/email senders are fakes.
Do not start the Production-mode VPS API for tests, use its database as a test
target, reset a database, seed broad data, or contact real SMS/email providers.

Reuse or create minimal isolated fixtures, then read back ownership, purpose,
status, expiry and dependencies before running each stateful scenario. If a
disposable PostgreSQL target is unavailable, run the safe isolated tests and
report relational concurrency as **not run** and the phase unaccepted; do not
substitute InMemory or SQLite as PostgreSQL proof. No Bruno work is assigned.

## Required verification

- Reproduce the pre-change resend failure ordering with a throwing fake;
  distinguish a source-level reproduction from any frontend provider exception.
- Focused tests: registration success/send failure and login recovery; foreign
  and wrong-purpose exclusion; expired pending and `Expired` recovery/resend;
  verified/active null fields; resend provider rejection, timeout, cancellation, retry,
  cooldown, superseded and max-attempt regression; actual 502/409 mapping.
- Prove one-winner concurrency with separate contexts/connections against a
  verified disposable PostgreSQL target, including persisted-state readback.
  The migration must apply cleanly there. Report the exact migration ID and
  that no production migration was applied by the worker.
- Verify restricted access claims/policies and the literal serialized login,
  resend and verify request/response shapes. Use existing
  `PortalAuthExampleContractTests` to check guide and Postman examples against
  actual serialization; inspect affected OpenAPI metadata. Do not assume
  the mapping script's missing/orphan counts prove route coverage: compare
  normalized method/route sets independently if route mapping is claimed.
- After final code edits: strict solution build with zero warnings/errors;
  focused Identity/API tests and architecture test. Because this alters auth
  persistence and a migration, run the guarded full unit suite before delivery
  if its target isolation is verified. Record passed/failed/skipped counts and
  warning count. Parse Postman JSON, check Markdown links and `git diff --check`.
  Rerun only gates invalidated by later edits.

Do not claim a live provider or public HTTP pass. Do not put credentials,
tokens, OTPs, personal records or raw production logs in test output or docs.

## Delivery report and stop point

Report baseline HEAD/local delta, exact changed-file manifest, implementation
and migration ID, configuration/restart/package requirements (state `none` when
true), fixture target readiness without secrets, pre/post state readbacks,
all commands/results/skips/warnings, Postman/docs/OpenAPI checks, and remaining
risks. Explain the provider ambiguity and concurrent losing send. Link
`FR-20261016-01/-12` to the approved contract, implementation/tests and
later deployment receipt placeholder. Leave FUR-02 `In progress — worker
complete, awaiting Mastermind review`. Present the saved report to the owner;
do not commit, push, publish, deploy or mark Done. Mastermind reviews actual
source/tests/docs and sends bounded corrections through the owner if needed.
