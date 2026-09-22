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
- [x] Mastermind final validation and private handoff update: commit `30bcaa1` pushed to `origin/main`; VPS evidence remains owner action.

## Current blockers

- [x] Local migration application is complete and verified in `SanadDb`; no VPS/production action was taken and no local tax endpoint gate has run yet.
- [x] Independent review report received; its documentation finding was reconciled against live workspace evidence. No unresolved reviewer defect remains, with the stale-snapshot limitation recorded.
- [x] PostgreSQL concurrency regression is delivered and validated; the prior worker blocker is resolved through the explicitly authorized mastermind recovery action.
- [x] Documentation/Postman synchronization is complete through the explicitly authorized mastermind recovery; independent review remains unavailable and is not marked complete.

## Next action

Owner is deploying pushed revision `30bcaa1` at `72.62.92.144:8091`; migration/API/smoke evidence remains pending owner output. A repeatable/testable deployment mechanism is planned for the UI walkthrough phase.

---

# Active phase: subscription checkout quote

## Phase contract

- Add a server-owned, read-only quote for a new subscription purchase.
- Resolve only published, available plan versions and the currently active tax rule/coupon rules already present in the database.
- Return auditable base price, discount, tax, total, currency, cycle, and the renewal-anchor/recurring capability metadata needed by the next payment slice.
- Do not charge Paymob, activate a subscription, create an invoice, consume allowances, or implement renewals/proration in this slice.
- Do not reuse the booking-only Paymob merchant reference or webhook path.

## Ordered worker and gate checklist

- [x] Mastermind reconciles `CURRENT HANDOFF`, branch/HEAD/origin/status, and running API state; exactly one next action: pin and spawn the quote implementer.
- [x] `sanad_scout`: map existing subscription, tax/coupon, Paymob, persistence, controller, docs, and test contracts; worker report was unavailable, so mastermind recovery evidence is recorded.
- [x] Mastermind publishes this complete checklist and pins base SHA `30bcaa1fa1c65677310aa3c42eb7621a836438e2`.
- [x] `sanad_implementer` Harvey: no report or patch after bounded waits; worker was shut down and the unfulfilled report is recorded. No implementation files changed and no dependent gate started.
- [x] Replacement `sanad_implementer` Averroes: no report or patch after bounded waits; worker was shut down. No implementation files changed and no dependent gate started.
- [x] Implementer delivery recovered by explicitly owner-authorized mastermind path; quote implementation changed only the authorized application/controller scope and API build passed `0` warnings / `0` errors.
- [x] `sanad_test_author` Anscombe: no report or test files after bounded waits; worker was shut down. The owner-authorized mastermind recovery added the focused test artifact and recorded the worker non-delivery.
- [x] Focused quote tests: `dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj --no-restore --filter FullyQualifiedName~SubscriptionQuoteTests --nologo` passed `4/4`, `0` failed, `0` skipped.
- [x] Mastermind focused build, full build, and full test suite: API build `0/0`; `Sanad.slnx` build `0/0`; full tests `1749/1749` passed (`1` architecture + `1748` unit), `0` failed, `0` skipped.
- [x] `sanad_reviewer` Avicenna: verdict `corrections required`; high finding was future-effective tax rules applied immediately; medium findings were stale docs/collection and incomplete quote coverage. No payment, webhook, booking-reference, authorization, or client-price trust defect.
- [x] Mastermind correction: tax selection now requires `IsActive && EffectiveOnUtc <= UtcNow`; future-effective regression added and focused `5/5`, full build `0/0`, full suite `1750/1750` passed.
- [x] `sanad_reviewer` correction disposition: high tax-effective-time finding resolved and independently verified; remaining findings are medium docs/collection synchronization and low/medium controller-boundary test coverage. No payment, webhook, booking-reference, authorization, or client-pricing defect.
- [x] Mastermind recovery: added quote controller contract coverage; focused quote + controller tests pass `8/8` with no warning.
- [x] `sanad_documenter` Archimedes: no report or patch after bounded wait; worker was shut down. Owner-authorized mastermind recovery synchronized the exact five-file public manifest.
- [x] Documentation/collection validation: Postman JSON parses, quote request is present, and `git diff --check` is clean (line-ending warnings only).
- [x] Bruno/local API gate: `collections/Sanad/subscriptions` passed `5/5` requests and `5/5` status assertions, exit `0`; login `200`, plans `200`, current `200`, unavailable-plan quote `404 Subscriptions.Quote.PlanNotFound`, logout `204`; no mutation/payment request; API stopped and port `5235` has no listener.
- [x] Documenter scope closure: public docs, README, architecture, Postman, and Bruno subscription scope are synchronized through owner-authorized mastermind recovery.
- [x] Final build/test after docs, controller tests, and Bruno changes: `Sanad.slnx` build `0` warnings/`0` errors; full suite `1751/1751`, `0` failed, `0` skipped.
- [x] Owner-authorized logical commit/push completed as `7eb06311451f7f4a3b19f316a504531d341b81d5`; `HEAD` and `origin/main` are synchronized. No migration or remote deployment was performed.
- [x] Mastermind final local validation complete; exact gate results and cleanup are recorded above.

## Current blocker

- No local implementation blocker remains. The existing Paymob interface is booking-specific, so payment initiation/settlement remains a later slice with a subscription-specific merchant reference and webhook contract. Commit/push and any remote deployment remain owner actions.

## Next action

Owner reviews the pushed revision `7eb0631`; after owner deployment confirmation, the next bounded billing slice can define the subscription-specific payment intent/settlement contract.
