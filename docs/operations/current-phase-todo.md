# Current phase todo — subscription VAT/tax configuration

This is the visible checklist for the active bounded phase. The mastermind updates it after every worker spawn/report, correction, gate, and owner decision. Dependent work may not start while an earlier required item is incomplete.

## Phase contract

- Super Admin-only global VAT/tax configuration.
- Percentage rate inclusive `0..100`, rounded to two decimals.
- Positive unique version and UTC effective timestamp.
- Exactly one active rule; prior versions remain immutable except deactivation.
- Subscription prices remain tax-exclusive.
- Configuration/read APIs only; checkout, invoices, Paymob, and customer charge calculation are out of scope.

## Ordered worker and gate checklist

- [x] Owner confirms the VAT/tax product contract.
- [x] `sanad_scout`: map current code, interfaces, authorization, docs, Postman, and Bruno state at `c19fae0`.
- [x] Mastermind publishes the implementation brief and exact five-file production manifest.
- [x] `sanad_implementer`: deliver domain/application/persistence implementation; report was not returned before worker shutdown and is recorded as an unfulfilled worker-report check.
- [x] Mastermind review finds the missing public controller scope.
- [x] Implementer correction 01: add the three Super Admin admin routes and error mapping; complete report received.
- [x] `sanad_test_author`: add focused tests and six negative-first tax Bruno requests; files delivered, but the worker report was not returned before shutdown and is recorded as an unfulfilled worker-report check.
- [x] `sanad_reviewer`: independent review completed; verdict = corrections required.
- [x] Test-author Bruno correction: move logout from sequence 16 to sequence 23; report received.
- [x] Mastermind recovery: add PostgreSQL two-writer concurrency regression proving one success, one `Subscriptions.Tax.ActiveConflict`, and one active rule; focused gate passed `1/1` against dedicated `SanadIntegrationDb`, exit code `0`.
- [x] Owner authorizes and mastermind generates `20260922130346_AddSubscriptionTaxRules`; API compile preflight passed with 0 warnings and 0 errors.
- [x] Mastermind reviews the generated migration: additive table, unique version index, filtered unique active index, and reversible table-only `Down`.
- [x] `sanad_reviewer`: independently audit the generated migration and model snapshot; corrections required because PostgreSQL lacks rate/version check constraints.
- [x] Implementer correction 02: add named PostgreSQL checks for rate `0..100` and positive version in the tax-rule configuration only; worker report received with no blockers.
- [x] Mastermind regenerates `20260922130855_AddSubscriptionTaxRules` after correction 02; API preflight remains 0 warnings/0 errors and the migration now includes both named checks.
- [x] `sanad_reviewer`: fresh read-only audit returned `corrections required` for stale documentation in its worker snapshot; live audit confirms those documentation/Postman corrections are already present. No code, authorization, persistence, concurrency, or test findings were reported.
- [x] Owner-authorized local application of `20260922130855_AddSubscriptionTaxRules` to verified `localhost:5432/SanadDb`; migration history confirms it is applied.
- [x] Focused VAT/domain/application/API/model tests pass locally: `29/29`.
- [x] API build passes with zero warnings and errors.
- [x] Full unit suite passes after recovery test: `1744/1744` passed, `0` failed, `0` skipped, exit code `0` (`dotnet test ... --no-build --logger "console;verbosity=minimal"`).
- [x] Mastermind recovery after documenter worker failure: synchronized architecture overview and admin Postman collection; admin subscription docs/README were verified already current. Postman JSON parses and contains 3 tax-rule requests; diff check is clean.
- [x] Bruno parser/inventory check: `22` request files and `22` status assertions in `subscription-admin-negative`; sequence order is `1..22` followed by logout at `23`.
- [x] Local negative-first Bruno gate passed against local HTTP API: 22 requests, 22 passed, 22/22 assertions, exit code `0`; dedicated local seed login succeeded, tax mutation requests were absent, and logout cleanup returned `204`. API process stopped after the gate.
- [ ] Mastermind final validation and private handoff update — local validation is complete; VPS evidence remains owner action.

## Current blockers

- [x] Local migration application is complete and verified in `SanadDb`; no VPS/production action was taken and no local tax endpoint gate has run yet.
- [x] Independent review report received; its documentation finding was reconciled against live workspace evidence. No unresolved reviewer defect remains, with the stale-snapshot limitation recorded.
- [x] PostgreSQL concurrency regression is delivered and validated; the prior worker blocker is resolved through the explicitly authorized mastermind recovery action.
- [x] Documentation/Postman synchronization is complete through the explicitly authorized mastermind recovery; independent review remains unavailable and is not marked complete.

## Next action

Owner supplies or executes the approved VPS deployment mechanism for `72.62.92.144:8091`; current read-only reachability check returned `Unable to connect to the remote server`, so migration/API/smoke evidence is still unavailable.
