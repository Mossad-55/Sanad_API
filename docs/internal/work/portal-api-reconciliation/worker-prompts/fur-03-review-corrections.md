# FUR-03 bounded correction brief — finance PostgreSQL verification

Use High reasoning. This is a bounded correction round for your existing FUR-03 delivery, not a new phase. Preserve all owner files and unrelated changes. Do not commit, push, publish, deploy, mark Done, or start FUR-04.

## Source pin and current state

- Baseline/source pin: `30f6bcba3d4d91421827676b681f584d28ed9798`.
- The existing FUR-03 work is in the current uncommitted workspace. Inspect `git status`, `HEAD`, and diffs first; preserve all current changes.
- Read the approved contract, your delivery report, and `reports/fur-03-mastermind-review.md` before changing anything.
- No runtime defect was reproduced; expected runtime code delta remains none.

## Exact target authorization and safety boundary

The original contract did not make the phase-specific target name/creation prerequisite sufficiently explicit; that omission is Mastermind's responsibility. The owner explicitly authorized on 2026-10-16 creation/use of exactly the disposable local PostgreSQL database `SanadFur03VerifyDb` at `localhost:5432`, solely for this FUR-03 correction round.

- Use only `ConnectionStrings__Fur03VerificationDatabase`; fail closed unless host is local loopback and database name is exactly `SanadFur03VerifyDb`.
- Before writes, inspect that exact target read-only. If it exists but its ownership/disposability or contents are unclear, stop and report; do not drop, reset, truncate, or repurpose it. If absent, create only this named database and provision the existing committed CareHomes schema/migrations required to run the proof. Do not edit migration sources or apply uncommitted migrations.
- Never connect to `SanadBrunoTestDb`, any other database, a personal/development DB, Production/VPS, or an external host. Do not change configuration outside the test child process.
- Neutralize inherited integration targets in the actual dotnet test child process; prove that only the explicit FUR-03 target is present. No real provider calls.
- Do not put connection strings, credentials, OTPs, tokens, customer data, or raw production logs in the repository/report.
- If any prerequisite would exceed this exact authorization, stop and disclose it before acting.

## Required bounded corrections

1. **Target guard and proof:** change the PostgreSQL test's expected database name to `SanadFur03VerifyDb`, preserving fail-closed host/name checks and the opt-in key. Run the stateful PostgreSQL suite only against the authorized target. Preserve per-test fixture readbacks and cleanup by exact generated IDs; report database-level pre/post readbacks without exposing sensitive data.
2. **Execute the historical Admin revenue handler:** Admin revenue PostgreSQL tests must invoke `GetAdminCareHomeRevenueHandler.Handle`, including filtered, unfiltered, and unknown-filter cases. Do not test only `CareHomeRevenueProjection.Revenue`; the `Guid? facilityId` to `CareHomeId?` path is material to this regression.
3. **Complete the contract's PostgreSQL coverage:** add unfiltered Admin dashboard and Approved-owner dashboard proof. Strengthen Admin dashboard fixtures/assertions so the filter effect is independently observable for per-facility counters/resources as well as finance values where the handler returns them. Keep fixtures minimal and read prerequisites back before assertions.
4. **Reproducible evidence:** record exact commands (no ellipses), filters, exit codes and pass/fail/skip counts. Explain actual child-process environment sanitization and exact target validation. Do not claim broader coverage than the test cases prove.
5. **Align documentation:** update the worker report, both dated handoffs, and handoff README so each states exact verified coverage, target identity, no frontend repository inspection, no runtime change, and deployment N/A. Keep issue → approved contract → tests → deployment receipt/N/A traceability consistent. Correct any current overclaim identified by the Mastermind review.

## Gates and delivery

- Re-run the exact PostgreSQL proof after the test changes on only the authorized target.
- Run the strict solution build and focused relevant tests after code/test changes; run `git diff --check` and validate all changed handoff links. No full suite is required absent runtime changes, but disclose the exact gates performed.
- Report any migration/configuration requirement discovered. Existing committed schema provisioning for the explicitly authorized isolated test DB is in scope; app migration edits, other persistent targets, or production changes are not.
- Save the updated delivery report at `docs/internal/work/portal-api-reconciliation/reports/fur-03-worker-delivery.md`, including a dated correction record and changed-file manifest. Keep FUR-03 In progress awaiting Mastermind review.
- Return exact results and any blocker. Do not perform release operations; the owner will receive a separate Mastermind acceptance/release decision.
