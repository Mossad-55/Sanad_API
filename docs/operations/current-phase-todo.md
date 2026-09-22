# Current phase todo — subscription coupon and documentation synchronization

This is the editable task list for the active phase. The mastermind updates it after every worker report, validation gate, owner decision, and correction. Do not start the next subscription phase until the required open items are closed or explicitly owner-deferred.

## Complete

- [x] Implement Super Admin subscription plan authoring/publication/retirement.
- [x] Implement Super Admin coupon create/list/detail/hard-delete configuration.
- [x] Generate and inspect migration `20260921233541_AddSubscriptionCoupons`.
- [x] Focused subscription/controller tests: 62/62.
- [x] API build: 0 warnings, 0 errors.
- [x] Run scout endpoint census: 28 controllers, 228 actions; identify 13 missing Postman mappings and one misplaced route.
- [x] Correct docs/Postman mappings for the 13 endpoints and note-category route.
- [x] Correct authorization documentation for family activities and anonymous note categories.
- [x] Add and document the scout -> implementer -> mastermind gates -> correction -> reviewer -> documenter -> final mastermind workflow.
- [x] Correct object-level family ownership for notes and activities.
- [x] Add foreign-family and role-matrix regression coverage; focused role-matrix tests: 19/19.
- [x] Independent reviewer approved the ownership correction with no source/security findings.
- [x] Final documenter check synchronized notes/activity docs and Family Postman authorization wording.
- [x] Isolate authentication host tests from the Development subscription fixture; focused auth host tests: 5/5.
- [x] Full unit suite after isolation correction: 1723/1723 passed.
- [x] API full build after isolation correction: 0 warnings, 0 errors.
- [x] Local Bruno subscription gate: 4/4 requests, 4/4 assertions, exit code 0; fresh local seed used and API stopped after the run.
- [x] Owner-led UI walkthrough completed for the nine subscription/billing reference screens; mismatches and backend mappings recorded in the private handoff.

## Blocked or pending before phase close

- [ ] Apply and verify `20260921233541_AddSubscriptionCoupons` on the VPS after the UI walkthrough.
- [ ] Run safe deployed admin coupon smoke checks after VPS migration application.
- [ ] Add/run coupon-specific Bruno coverage if the next API contract requires it; the completed local gate currently covers the four read-only subscription requests.

## Next phase, not started

- [ ] Dynamic admin-configured VAT/tax rules and persistence.
- [ ] Re-run the full worker lifecycle and Bruno gate for that phase.
