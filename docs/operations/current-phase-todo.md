# Current phase todo — subscription billing invoices

Subscription billing items 15–18 are complete and owner-confirmed deployed green
at revision `02d7101`. This record now hands off to the full-application UI
walkthrough and gap analysis.

The plan-change slice is implemented and published as `e8cdfa1` + `fdd7a00`; the invoice/PDF slice is pushed as `9366d99`; allowance enforcement and the repeatable deployment verifier are pushed through `02d7101`. The owner confirms the exact revision is deployed green with migration, health, and smoke verification. The approved sequence is now full-application UI walkthrough/gap analysis, approved UI gaps, notifications/email, then later product phases.

## Current status

- Implementation and focused contract tests are complete. The accounting review finding was corrected so paid and zero-charge upgrades retain the target remaining-period gross for future credit calculations. The full suite also exposed a duplicate-webhook regression for initial subscription purchases and renewals; successful settlement now marks those payment attempts succeeded after validation.
- Validation is green: full API Release package and smoke verifier passed; full unit suite `1823/1823`; focused attendance `5/5`; architecture `1/1`; route-to-Postman check `245` controller actions / `281` requests / `0` missing / `0` orphan; Postman JSON parses; migration is applied locally and EF reports no pending model changes.
- Migration `20260924120349_AddSubscriptionPlanChanges` was generated, inspected, applied to the authorized local target `localhost:5432/SanadDb`, and confirmed applied. `dotnet ef migrations has-pending-model-changes` reports no model changes since the migration.
- Public family/admin guides, README, architecture, endpoint matrix, and Family Postman are synchronized. No dedicated plan-change Bruno run has been performed; the local development seed has only a Free current subscription, so it does not provide a paid Card subscription for safe end-to-end upgrade/downgrade coverage. Unit tests cover provider ordering and failure behavior.
- Owner confirms pushed revision `02d7101` is deployed green, including migration, health, and safe smoke verification.
- The plan-change slice is committed and pushed: `e8cdfa1` adds prorated upgrades and pending downgrades; `fdd7a00` standardizes project/worker routing on GPT-5.6 Luna with medium reasoning. `main` and `origin/main` match. Private handoffs and `subscription-vat-tax/` remain untracked and unstaged.

## Remaining phase work

- Completed bounded billing slice: branded invoices/PDFs for initial purchases and successful renewals.
- Allowance slice implemented: finite allowance is consumed only when a caregiver completion succeeds; cancelled, declined, expired, unpaid, duplicate, and failed completion attempts consume none; renewal resets usage and unlimited plans do not increment it.
- Validated and pushed through `02d7101`: migration `20260924141816_AddSubscriptionBookingAllowance`, complete billing revision, repeatable package script, and manifest/smoke verifier. The owner confirms deployment green; subscription billing is closed.
- Next phase: conduct the full-application UI walkthrough and gap analysis; implement only owner-approved gaps.
- Notifications/email follow core billing and approved UI gaps.

## Roadmap after subscription billing

1. Full-application UI walkthrough: inspect every family, caregiver, elderly, admin, support, booking, and subscription screen against the deployed API; record missing, stale, or contradictory behavior.
2. Approved UI gap slices: implement only confirmed gaps, with focused tests, docs, Postman/Bruno coverage, validation, and deployment per slice.
3. Notifications and email: implement the locked email + in-app channels and unified notification-preferences superset; SMS remains limited to security OTPs.
4. Phase G: chat/calls and notification delivery integrations.
5. Phase H: Care Homes only; Marketplace remains a future reconsideration and is not scheduled.
6. Phase I: earnings ledger/payouts, ratings, emergency-call/contact-card evolution, and remaining financial/operational hardening.

Deferred or unresolved product items to resolve during the UI/product review: payment-method replacement, trials, coupon redemption/marketing delivery, provider/store-policy compatibility, and other explicitly approved billing enhancements. These are not silently included in the completed subscription phase.

## Pinned plan-change contract

- Dedicated Owner-only routes: `POST /api/v1/family/subscriptions/plan-change/quote`, `POST /api/v1/family/subscriptions/plan-change/payment-intent` for immediate upgrades, `PUT /api/v1/family/subscriptions/pending-downgrade`, and `DELETE /api/v1/family/subscriptions/pending-downgrade`.
- Coupons do not apply to plan changes. The upgrade quote calculates the target plan's remaining-period gross amount using the effective tax rule, subtracts a time-prorated credit from the actually settled current-period gross amount, and floors the final charge at zero. Unpaid time earns no credit. Persist exact quoted gross, credit, tax, source/target terms, and provider-verification values.
- Card upgrades collect the immediate delta through a one-time provider payment, then update the existing provider subscription for future recurring billing; do not create a parallel provider subscription or re-enroll the card. Wallet upgrades remain one-time/manual and future Wallet renewals remain manual.
- A pending downgrade is effective only after a successful renewal at the original period boundary. `PUT` replaces an existing pending downgrade; `DELETE` cancels it; a successful upgrade clears it. Reject both plan-change operations during failed-renewal grace and when cancel-renewal is requested. The family current-subscription response exposes the pending downgrade.
- Paymob provider update is `PUT /api/acceptance/subscriptions/{subscriptionId}` with bearer authorization and JSON `{ "amount_cents": <target recurring gross cents> }`. It is invoked only after the immediate upgrade payment has been successfully settled and before local upgrade finalization. The request sets an exact value, so retrying the same target amount is safe. Do not send `next_billing`, `ends_at`, or a new provider enrollment; preserve the original provider subscription and renewal anchor. A failed update must not finalize the local plan snapshot; it must remain safely retryable through the existing idempotent callback/settlement path.
- A zero-charge upgrade is confirmed by the existing `POST /plan-change/payment-intent` route: it performs no provider charge, performs the Card future-billing amount update when applicable, and then finalizes the local target snapshot. For Card, scheduling/replacing/cancelling a pending downgrade synchronizes the existing provider subscription's exact future recurring amount immediately (target amount on schedule/replace; current amount on cancel). On any provider-update failure, the local pending state remains unchanged. Wallet has no provider-subscription update and remains manual.

# Historical phase todo — subscription docs and worker model routing closeout

The owner authorized one commit and push for the completed subscription documentation correction plus a model-only workflow change. Worker routing requested: Implementer and Reviewer always `gpt-5.6-terra`; Scout, Test Author, and Documenter always `gpt-5.6-luna`. The old two-failure model fallback must be removed. Only `AGENTS.md` and `docs/operations/codex-workflow.md` need routing text changes; the five agent TOMLs have no model field.

## Model-routing change checklist

1. **Owner/mastermind — authorization and live audit:** complete; owner authorized one combined commit/push, and `main`, `HEAD`, `origin/main`, and status were verified. Dependency: current handoff.
2. **`sanad_scout` — model-rule manifest:** complete; two routing paragraphs and no TOML model fields. Dependency: item 1.
3. **`sanad_implementer` — two-paragraph model change:** complete; requested assignments are identical in both files, Sol fallback removed, `git diff --check` passed. Dependency: item 2.
4. **`sanad_test_author` — tests:** complete applicability review; no source/API behavior changed, so no new test is needed. Dependency: item 3.
5. **Owner — local migration:** complete applicability review; no schema change. Dependency: item 3.
6. **Mastermind — focused tests, full build, full suite:** complete applicability review; two routing paragraphs changed only, so prior API gates remain valid and are not rerun. Dependency: items 3–5.
7. **Implementer — correction round:** pending only if review finds a defect. Dependency: item 6.
8. **`sanad_reviewer` — preliminary review:** complete after private handoff correction; fixed routing and no fallback approved, stale-reference search clear, `git diff --check` passed. Dependency: item 3.
9. **`sanad_documenter` — public workflow sync:** complete; README has no model-routing rule, agent TOMLs have no model fields, and no public workflow edit beyond the two routing paragraphs is required. Dependency: item 8.
10. **Mastermind — route/Postman and completeness:** complete for unchanged API surface at 238/274/0/0; recheck if Postman routes change. Dependency: documentation sync.
11. **`sanad_reviewer` — final sign-off:** complete; seven-file diff approved with zero scoped missing counts, correct routing, valid Premium examples, and private/unrelated artifacts excluded. Dependency: item 9.
12. **Mastermind — final validation/Bruno:** complete applicability review; `git diff --check`, Postman parsing, and 238/274/0/0 route mapping are green. No API behavior changed, so build/test/Bruno are not rerun. Dependency: item 11.
13. **Owner-authorized commit and push:** running; one logical commit excludes private/unrelated files. Dependency: item 12.
14. **Owner — UI walkthrough/deployment:** complete applicability review; no deployment for documentation-only change. Dependency: item 13 for publication.
15. **Owner — production migration/smoke:** complete applicability review; none required. Dependency: item 14.
16. **Mastermind — final handoff:** pending pushed SHA and clean tracked status. Dependency: item 13.

## Subscription documentation correction checklist

This bounded correction is pinned to `c620995a3d1400360302397ba7ab5f73dbc1975d`. It clarifies the existing admin plan-version contract and fixes the invalid Postman benefits example. It does not change an HTTP route or business rule. The historical phase records below are retained as history.

1. **Mastermind/owner — audit and scope:** complete; owner raised the documentation gap and the live source was inspected. Dependency: current handoff.
2. **`sanad_scout` — exact source/manifest map:** complete; enum values, routes, and three affected public artifacts identified. Dependency: item 1.
3. **`sanad_implementer` — production change:** complete; scout/reviewer confirmed source route and validation are correct; no source edit is needed. Dependency: item 2.
4. **`sanad_test_author` — contract tests:** complete; existing valid-create and benefit-invariant tests cover the unchanged behavior. Dependency: item 3.
5. **Owner — local migration:** complete; this documentation correction changes no schema or model. Dependency: item 3.
6. **Mastermind — focused tests, full build, full suite:** complete applicability review; no source or test change, so prior pinned gates remain valid and are not rerun. Dependency: items 3–5.
7. **`sanad_implementer` — correction:** complete applicability review; no source defect was found. Dependency: item 6.
8. **`sanad_reviewer` — preliminary contract review:** complete; one invalid Postman body and one missing admin enum guide found, plus one uncovered positive Bruno create-plan scenario. Dependency: item 2.
9. **`sanad_documenter` — admin/family docs and Postman:** complete after correction; the three-file synchronization and the Premium key-8 correction parse as JSON, preserve all eight unique keys, and pass `git diff --check`. The first worker interruption is recorded above. Dependency: item 8.
10. **Mastermind — two-way route/Postman and completeness reconciliation:** complete after correction; checker exit 0: 238 controller actions, 274 Postman API requests, 0 missing, 0 orphan. Both example benefit arrays are identical, have eight unique keys, and set PremiumContent false. Scoped missing counts are 0. Dependency: item 9.
11. **`sanad_reviewer` — final sign-off:** complete after correction; Premium example values match source and explicit test, scoped missing counts are all 0, and one successful-create runtime scenario is recorded as a manual mutation exception. Dependency: item 10.
12. **Mastermind — final validation and applicable Bruno:** complete for this docs-only correction; both JSON examples parse and match, `git diff --check` passes, and controller/Postman checker exits 0 at 238/274/0/0. No source/API behavior changed, so completed build/test/Bruno gates are not rerun. Dependency: item 11.
13. **Owner — commit/push/publication:** owner action; five reviewed public documentation files remain uncommitted and unstaged. Dependency: item 12.
14. **Owner — UI walkthrough/deployment:** complete applicability review; no deployment is required to validate this local documentation correction. Dependency: item 13 for any publication decision.
15. **Owner — production migration/smoke:** complete applicability review; no migration or production smoke is required by this documentation-only correction. Dependency: item 14.
16. **Mastermind — final handoff:** pending owner publication decision; record exact SHA, validation, cleanup, and one next action. Dependency: items 1–15 as applicable.

# Historical phase todo — subscription VAT/tax configuration

## Roadmap status and next-mastermind handoff

- [x] Catalog, plan publication/retirement, administrative reads, coupons, and VAT/tax configuration are implemented and documented.
- [x] Owner quotes, subscription-specific Card/Wallet payment intents, `sub_` settlement, manual renewal, seven-day grace, original-anchor preservation, duplicate-pending protection, idempotent settlement, and manual payment retry are implemented.
- [x] Final verified source/deployment revision: `c654051ac005a6f5f2a0f1ad3bdce45fa79a088b`; owner reports the revision is deployed and working on the VPS.
- [x] Current route/collection census: `238` controller actions, `270` Postman API requests, `0` missing mappings, `0` orphan requests.
- [x] Paymob Card enrollment initiation is implemented conditionally through the existing owner payment-intent route; Wallet remains manual.
- [x] Focused tests: mastermind filter `38/38`; test-author broader filter `45/45`; `0` failed, `0` skipped.
- [x] Full build: `0` warnings, `0` errors.
- [x] Full suite: architecture `1/1` plus unit `1772/1772`, `0` failed, `0` skipped.
- [x] Bruno negative-first gate: `5/5` requests and `5/5` assertions, exit `0`; statuses `401/200/200/409/204`; no provider mutation; API stopped and no listener remains.
- [x] Provider renewal-event settlement and retry integration: subscription callbacks now preserve the existing seven-day grace and original period-end anchor.
- [ ] Following slice: owner applies and verifies the additive callback-state, ledger, and provider-identity migrations, then runs the approved safe/manual operational checks.
- [ ] Later billing slices: upgrades/proration, downgrades, invoices/PDFs, and allowance consumption.
- [ ] Later platform slice: notifications/email after core billing.

Planned provider routes, persistence fields, and event names are not implemented contracts until they appear in source, the requirements-to-contract matrix, public docs, Postman, Bruno, focused tests, and the two-way route checker.

## Completed phase override: Paymob Card enrollment initiation — 2026-09-23

The older pending provider-enrollment checklist below is historical. The live completion record is this section.

- [x] Scout and requirements-to-contract matrix: Paymob enrollment capability verified; `238` controller actions, `270` Postman requests, `0` missing, `0` orphan.
- [x] Implementer and correction: Card-only provider plan mapping and enrollment initiation; callback identity persistence and renewal-event settlement are covered by the subsequent provider-callback phase.
- [x] Test author: Card/Wallet, authorization, missing configuration, payload/secret, admin mapping, and migration tests delivered.
- [x] Focused tests: mastermind filter `38/38`; test-author broader filter `45/45`.
- [x] Full build: `0` warnings / `0` errors; architecture `1/1`; full unit suite `1772/1772`, `0` failed, `0` skipped.
- [x] Reviewer and documenter: fresh review found no source defect; docs, README, matrix, and Postman synchronized; completeness counts all zero.
- [x] Bruno: `5/5` requests, `5/5` assertions, exit `0`; `401/200/200/409/204`; no provider mutation; API stopped and no listener remains.
- [x] Migration: `20260923113242_AddPaymobSubscriptionPlanMapping` generated and inspected; additive/nullable/reversible; unapplied by this run.
- [x] Owner commit/push, deployment, and final handoff: `c654051` is pushed and owner-confirmed working on the VPS; private ledgers and unrelated `subscription-vat-tax/` artifacts excluded.

## Next bounded phase: Paymob enrollment and card recurrence

- [x] `sanad_scout` — worker did not return after two bounded waits and was shut down; mastermind read-only recovery completed at live SHA `c18025c144f9a024d8115b3f16188fb7f047f141`. Repository evidence was reconciled with authoritative Paymob documentation.
- [x] Mastermind — provider contract pinned — Paymob subscription callbacks contain `subscription_data.id`, `trigger_type`, and body `hmac`; HMAC is SHA-512 over `{trigger_type}for{subscription_data.id}`. Renewal triggers are `Successful Transaction`, `Failed Transaction`, and `Failed Overdue Transaction`; subscription ID is available from the callback or inquiry. Implementation must preserve the existing seven-day grace and original anchor and must not invent undocumented retry timing.
- [x] `sanad_implementer` — delivered the seven-file callback/persistence implementation; exact scope review passed and `git diff --check` passed.
- [x] `sanad_test_author` — added `SubscriptionProviderCallbackTests.cs`; focused test gate passed `13/13`.
- [x] Mastermind focused tests/build — focused `13/13`, API build `0 warnings/0 errors`, full solution build `0 warnings/0 errors`.
- [ ] Mastermind full suite — blocked by pending EF model changes for provider callback persistence: architecture `1/1`, unit `1780/1785`, five host-test startup failures; no migration exists yet.
- [x] Owner-authorized migration generation/inspection — `20260923223028_AddPaymobSubscriptionCallbackState`; additive/reversible six nullable provider-state columns only; not applied.
- [ ] Mastermind full suite rerun — pending after migration generation; owner application remains separate and unauthorized in this phase.
- [x] Reviewer correction implementation — durable callback ledger seams, `paymob_request_id` propagation, immutable provider identity checks, duplicate/concurrency handling, and callback conflict mapping delivered; static scope review passed.
- [x] Correction compile/focused validation — API build `0 warnings/0 errors`; callback tests `13/13` passed.
- [ ] Ledger migration — owner authorization required for new `families.paymob_subscription_callbacks` table and unique callback/request indexes.
- [x] Owner-authorized ledger migration generation/inspection — `20260923225003_AddPaymobSubscriptionCallbackLedger`; creates only the callback ledger table and two unique indexes; `Down` drops only that table; not applied.
- [ ] Full suite after ledger migration — held because API host startup applies migrations automatically; requires explicit owner authorization for the exact local test database target.
- [ ] `sanad_implementer` — pending — implement only provider plan mapping/enrollment and card recurrence state from the pinned manifest. Dependency: approved scout manifest.
- [ ] `sanad_test_author` — pending — add focused provider-boundary, authorization, idempotency, and persistence tests after implementation is pinned. Dependency: implementer revision.
- [ ] Mastermind gates — pending — focused tests, smallest relevant build, full build, and full test suite; corrections require a new pinned brief and repeated gates. Dependency: tests.
- [ ] `sanad_reviewer` — pending — review provider security, HMAC/event identity, authorization, duplicate settlement, compatibility, migration safety, and explicit missing counts. Dependency: green gates.
- [ ] `sanad_documenter` — pending — synchronize affected docs, README, endpoint matrix, and Postman; do not document planned routes as live. Dependency: reviewer disposition.
- [ ] Bruno — pending — add and run safe negative-first/idempotent coverage with exact requests, assertions, exit code, seed, and cleanup. Dependency: docs and gates.
- [ ] Owner migration — owner action — authorize the exact local/remote target only after migration review and return migration-history evidence. Dependency: reviewed migration.
- [ ] Owner commit/push — owner action — authorize only after every checklist item is complete and route mapping is green. Dependency: Bruno and final validation.
- [ ] Mastermind final handoff — pending — record final SHA, gate counts, mapping counts, cleanup, blockers, and exactly one next action. Dependency: all prior items.

## Active phase: subscription renewal and seven-day grace — mastermind recovery

- [x] Scout: mapped the existing subscription/payment boundary and confirmed the provider subscription path; the manual renewal boundary was kept separate from provider callback settlement.
- [x] Implementer/mastermind recovery: added renewal payment attempts, renewal/grace state, seven-day grace recovery, original-anchor preservation, duplicate-pending protection, idempotent settlement, and the owner renewal payment-intent endpoint.
- [x] Test author/mastermind recovery: added domain and application tests for renewal timing, failed renewal grace, successful retry, anchor preservation, expiry, and renewal attempt identity; focused subscription gate passed `13/13`.
- [x] Mastermind gates: final solution build passed `0` warnings / `0` errors; architecture tests passed `1/1`; full unit suite passed `1765/1765`, `0` failed / `0` skipped.
- [x] Reviewer/mastermind recovery: corrected a capability-claim defect so manual renewal never advertises recurring support before provider subscription enrollment exists; persistence guard was updated to allow only approved renewal lifecycle fields.
- [x] Documenter/mastermind recovery: synchronized family/admin subscription docs, README status, architecture/coverage references, Family Postman, and renewal Bruno contract.
- [x] Bruno/local API gate: `collections/Sanad/subscriptions` passed `7/7` requests and `8/8` assertions, exit `0`; renewal request safely returned `409 Subscriptions.Renewal.NotDue` against the seeded future subscription; no payment mutation ran; API stopped and no listener remains.
- [x] Migration/commit/push preparation: two additive migrations are generated and inspected but not applied remotely; the authorized logical commit scope is ready and excludes private control files, unrelated `subscription-vat-tax/`, and VPS changes.
- [x] Final handoff: feature commit `2a2590f` and operations closeout `de66dcd507bb5ede4cb16f4333fa812131d63d92` are pushed; the final closeout SHA is the handoff baseline and private handoff records the next provider-enrollment/card-recurrence slice.

Scope exclusions: automatic Paymob subscription enrollment/card recurring, upgrades/downgrades, proration, invoices/PDFs, allowance consumption, notifications, UI, and VPS deployment.

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

---

# Active phase: subscription payment intent/settlement boundary

## Phase contract

- Map and implement one bounded subscription-specific payment boundary after the deployed quote slice.
- Preserve server-owned quote totals; never trust client price, tax, discount, currency, plan, or renewal values.
- Keep card and wallet methods explicit. Treat wallet as one-time/manual renewal unless recurring wallet capability is verified from the configured provider.
- Use a subscription-specific merchant reference and webhook/settlement lookup; do not reuse booking `BookingId` references or booking confirmation handlers.
- Do not bundle renewals, seven-day grace, upgrades/downgrades/proration, invoices/PDFs, allowance consumption, notifications, or deployment mechanism work into this phase unless the scout proves the bounded contract already exists and the checklist is revised before implementation.

## Ordered worker and gate checklist

- [x] Owner reports deployed revision `74a9118` is green; this is owner-confirmed, not independently shell-audited by the mastermind.
- [x] Mastermind reconciles branch/HEAD/origin/status and confirms `HEAD == origin/main == 74a9118`; private handoff remains untracked by rule.
- [x] Mastermind publishes this complete ordered checklist before worker work; current next action is the read-only scout.
- [x] `sanad_scout` Ohm: no report after bounded waits; worker was shut down and the unfulfilled report is recorded. Mastermind recovery mapped the live payment boundary: all current payment entities/reference/webhook commands are booking-owned.
- [x] Mastermind pins one bounded implementation manifest at SHA `74a9118`: initial subscription purchase payment intent plus settlement/activation, with a new subscription-specific payment-attempt/reference path; owner migration generation remains separate.
- [x] `sanad_implementer` Curie: no report or patch after bounded waits; worker was shut down. No implementation files changed and no dependent gate started.
- [x] Mastermind recovery implementation: added subscription payment attempt/status snapshot, provider boundary, owner payment-intent endpoint, subscription-specific webhook dispatch, amount/currency checks, idempotent settlement, and initial activation; no migration generated.
- [x] Production build preflight: `dotnet build src/API/Sanad.API/Sanad.API.csproj --no-restore --nologo` passed `0` warnings / `0` errors.
- [x] `sanad_test_author` Nash: no report or tests after bounded waits; worker was shut down. No worker output was trusted.
- [x] Mastermind recovery tests: added `SubscriptionPaymentTests`; focused payment boundary tests pass `4/4`.
- [x] Mastermind focused validation: `SubscriptionPaymentTests` passed `4/4`; production API build passed `0` warnings / `0` errors.
- [x] Post-migration focused rerun: `SubscriptionPaymentTests` passed `4/4`, `0` failed, `0` skipped after generating the inspected migration.
- [blocked] Full suite: `1749/1754` passed with `5` API host failures caused by EF `PendingModelChangesWarning` for the new subscription payment-attempt model; no source test failure was observed.
- [x] Post-migration full build: `dotnet build Sanad.slnx --no-restore --nologo` passed with `0` warnings / `0` errors.
- [x] Post-migration full suite: architecture `1/1` and unit tests `1754/1754` passed, `0` failed, `0` skipped; the prior five EF pending-model host failures are resolved by the generated migration.
- [x] Owner authorizes generation and inspection of the Families migration for `subscription_payment_attempts`; database application remains a separate owner-controlled action.
- [x] Mastermind migration generation and inspection: generated `20260922211104_AddSubscriptionPaymentAttempts`; verified the migration adds only `families.subscription_payment_attempts`, its family/plan-version restrictive foreign keys, family/status and unique nullable Paymob-order indexes, and a `Down` that drops only that table. No database was updated.
- [x] Correction loop: Bruno exposed that the wire contract uses numeric enum values; updated docs/Postman/Bruno to `1=Card`, `2=Wallet`, then reran the full Bruno gate successfully.
- [blocked] `sanad_reviewer` Newton: no report after two bounded waits; worker was shut down. No worker verdict is trusted.
- [x] Mastermind reviewer recovery: read-only review passed the bounded payment boundary. No server-price trust, authorization, booking-reference collision, HMAC bypass, amount/currency acceptance, duplicate activation, or migration-safety defect found. Non-blocking follow-ups: add direct controller/webhook contract coverage and synchronize public docs/Postman/Bruno before closure.
- [blocked] `sanad_documenter` Ampere: no report after two bounded waits; worker was shut down. No worker output is trusted.
- [x] Mastermind documenter recovery: synchronized family subscription docs, admin tax/payment boundary notes, architecture status, README endpoint/status references, Family Postman payment-intent example, and a safe Bruno current-subscription conflict contract. No private control file or live payment mutation was added.
- [x] Documentation/collection validation: Postman JSON parses; public payment-intent/webhook docs, README, architecture, Postman, and safe Bruno contract were inspected; `git diff --check` is clean apart from line-ending warnings.
- [x] Bruno/local API gate: after correcting the numeric enum contract (`1=Card`, `2=Wallet`) in docs/Postman/Bruno, local HTTP execution passed `6/6` requests and `6/6` assertions, exit `0`: login `200`, plans `200`, current `200`, payment-intent current-subscription conflict `409 Subscriptions.Payment.CurrentExists`, unavailable-plan quote `404 Subscriptions.Quote.PlanNotFound`, logout `204`; no provider/payment/webhook mutation ran. API stopped; no listener remains (TIME_WAIT connections only).
- [x] Owner-authorized local migration verification: exact target `localhost:5432/SanadDb` / `families` schema reports `20260922211104_AddSubscriptionPaymentAttempts` up to date after rebuilt EF artifacts; no additional migration operation was required.
- [x] Owner-authorized logical commit completed as `624a48c` (`feat(subscriptions): add payment intent settlement boundary`); private control files and unrelated `subscription-vat-tax/` remain uncommitted.
- [running] Push closeout: push `624a48c`, then verify `HEAD == origin/main`, clean tracked worktree, and preserved private/unrelated files; dependency: validated commit above.
- [x] Mastermind final validation and handoff update: Postman parses, `git diff --check` is clean apart from line-ending warnings, exact gates and cleanup are recorded, and one next action is set below.

## Current blocker

- Full validation is green: focused payment tests `4/4`, full solution build `0` warnings / `0` errors, architecture `1/1`, and unit tests `1754/1754`; the exact local Families target reports the generated migration up to date. Recurring provider capability remains explicitly out of scope and must not be inferred.

## Pinned implementation manifest

- `src/Modules/Families/Domain/Sanad.Modules.Families.Domain/Subscriptions/SubscriptionPaymentAttempt.cs` (new)
- `src/Modules/Families/Domain/Sanad.Modules.Families.Domain/Subscriptions/SubscriptionPaymentMethod.cs` (new, if needed to avoid booking-domain coupling)
- `src/Modules/Families/Application/Sanad.Modules.Families.Application/Abstractions/Payments/IPaymobClient.cs`
- `src/Modules/Families/Application/Sanad.Modules.Families.Application/Subscriptions/SubscriptionPaymentCommands.cs` (new)
- `src/Modules/Families/Infrastructure/Sanad.Modules.Families.Infrastructure/Payments/PaymobClient.cs`
- `src/Modules/Families/Infrastructure/Sanad.Modules.Families.Infrastructure/Payments/DevelopmentPaymobClient.cs`
- `src/Modules/Families/Infrastructure/Sanad.Modules.Families.Infrastructure/Persistence/FamiliesDbContext.cs`
- `src/Modules/Families/Infrastructure/Sanad.Modules.Families.Infrastructure/Persistence/Configurations/SubscriptionPaymentAttemptConfiguration.cs` (new)
- `src/API/Sanad.API/Controllers/FamilySubscriptionsController.cs`
- `src/API/Sanad.API/Controllers/PaymobWebhookController.cs`

The implementer may add no migration, tests, docs, Postman, Bruno, private control-file, booking-domain, or unrelated files. The exact provider decision to preserve is initial card/wallet intent; recurring wallet support is not assumed, and the returned subscription reference must be distinct from booking references.

## Next action

Owner deploys pushed revision `1b6fca6`, verifies the Families migration and API health on the VPS, and returns exact smoke evidence; no further local implementation phase starts before that deployment handoff.

## Commit/push closeout checklist

- [x] `sanad_scout` / mastermind recovery: bounded payment manifest mapped and pinned at `74a9118`; no further scout work is required for this slice.
- [x] `sanad_implementer` / mastermind recovery: payment intent, settlement, persistence, provider boundary, and subscription-specific webhook path implemented within the pinned manifest.
- [x] `sanad_test_author` / mastermind recovery: focused payment tests added; `4/4` passed.
- [x] Mastermind validation: production/API build and full solution build passed with `0` warnings / `0` errors; architecture `1/1`; unit tests `1754/1754`.
- [x] `sanad_reviewer` / mastermind recovery: independent read-only review passed; no payment-integrity, authorization, booking-reference, HMAC, idempotency, or migration-safety blocker found.
- [x] `sanad_documenter` / mastermind recovery: family/admin/architecture docs, README, Postman, and safe Bruno contract synchronized; Postman parses and `git diff --check` is clean apart from line-ending warnings.
- [x] Bruno/local API gate: HTTP local gate passed `6/6` requests and `6/6` assertions, exit `0`; seeded state preserved, no provider/payment/webhook mutation; API stopped and no listener remains.
- [x] Mastermind commit preparation: inspected exact diff, staged only validated payment-boundary files and tracked closeout checklist; excluded `Sanad_Master_Context.md`, `Sanad_Operations.md`, and unrelated `subscription-vat-tax/`.
- [x] Owner-authorized logical commit and push: final revision `1b6fca60bc40b1423629e18bddf0ba418fb30065` pushed to `origin/main`.
- [x] Post-push verification: branch `main`, `HEAD == origin/main == 1b6fca60bc40b1423629e18bddf0ba418fb30065`, tracked worktree clean, and private/unrelated files preserved untracked.
- [owner action] Deployment owner action: deploy `1b6fca6`, apply/verify migration on the VPS, and run safe smoke checks; dependency: owner deployment decision.
- [x] Final handoff: pushed SHA, exact gates, cleanup, and one next action are recorded in the private operations handoff.

## Repository contract-completeness audit — active correction phase

- [x] Mastermind/scout recovery: controller census is complete at `237` actions; Postman census is complete at `269` requests with `0` missing and `0` orphan mappings; requirement-to-route-to-artifact matrix review is complete.
- [x] Requirement completeness review: corrected the missing admin subscription plan/family-subscription reads; caregiver review/cancellation visibility is present; admin-managed resource lifecycle rows are documented in the coverage matrix.
- [x] Implementer/mastermind recovery: added read-only admin plan list/detail and family-subscription list/detail contracts without a migration.
- [x] Test author/mastermind recovery: focused subscription controller/query tests pass `21/21`.
- [x] Mastermind gates: full solution build passes `0` warnings / `0` errors; architecture tests pass `1/1`; full unit suite passes `1760/1760`, `0` failed / `0` skipped; route-to-collection checker passes `237/237` with `0` missing and `0` orphan mappings.
- [x] Reviewer/mastermind recovery: read-only review found no authorization, route-collision, data-exposure, pagination/filtering, or migration issue in the correction; lifecycle coverage is recorded in the matrix.
- [x] Documenter/mastermind recovery: synchronized admin subscription docs, architecture, README, admin Postman, public webhook Postman, workflow rules, coverage matrix, and Bruno read collection; all seven Postman JSON files parse.
- [x] Bruno: local admin-read gate passed against `http://localhost:5236` using the existing approved local Super Admin fixture (`admin@gmail.com`); `6/6` requests passed, `20/20` assertions passed, exit code `0`: login `200`, plan list `200`, plan detail `200`, family-subscription list `200`, family-subscription detail `200`, logout `204`. The first rerun exposed and corrected the Bruno logout contract to send `X-Device-Session-Id`; no business mutation ran.
- [x] Commit/push: validated audit corrections were committed as `8dff56d` and pushed to `origin/main`; local `HEAD` and remote SHA match.
- [owner action] Final handoff/deployment phase: owner begins the approved post-push deployment/UI-walkthrough phase from `8dff56d`; VPS migration and smoke actions remain owner-controlled.

## Prevention controls required before this phase can close

- [x] Add maintained requirement-to-route matrix: `docs/operations/endpoint-coverage-matrix.md` records role/resource requirements and current evidence.
- [x] Add repeatable route/verb-to-Postman consistency check: `tools/Verify-ApiContractMapping.ps1` passes with `237` actions, `269` requests, `0` missing, `0` orphan.
- [x] Add lifecycle completeness rule: every admin-managed resource must expose and document safe list/detail reads alongside mutations and active-state transitions.
- [pending] Make reviewer acceptance require explicit “missing requirements / missing endpoints / missing docs / missing Postman / missing Bruno” verdicts, not only changed-file review.
- [x] Make the closeout gate refuse completion when any checklist row is owner-deferred, undocumented, untested, or unmapped.
