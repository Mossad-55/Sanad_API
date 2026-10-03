# Care homes — execution mastermind handoff

Status: Execution in progress under owner approval. Fee/tax and payout contracts are resolved. The caregiver review permission correction, Family caregiver ratings/top-10, owner submission, and initial Admin queue/document/review routes have implementation and test evidence. Owner onboarding Bruno passed (27 passed, 1 unrelated parser skip; 44/44 assertions); Admin Care Homes Bruno passed (40 passed, 1 unrelated parser skip; 69/69 assertions). The broader checklist remains active.

## HC-TASK-033 final status checkpoint (2026-10-03)

- HC-TASK-033 is **Done** for its bounded scope: the shared Admin-configured percentage fee/tax implementation, immutable transaction snapshots, and existing caregiver and Family subscription consumers. Finance Admin Bruno passed 18/18 assertions; the explicit 24-request subscription lifecycle passed 24/24 assertions; the corrected caregiver quote/Family checkout sequence passed 11 requests with 22/22 assertions (one known unrelated Wellness Tips parser skip while scanning). Build passed with 0 warnings/errors; focused Finance + Families.Subscription tests passed 168 with 3 PostgreSQL-only skips; the full solution passed Architecture 1 and Unit 2,178 with 3 PostgreSQL-only skips. Documentation/Postman synchronization is complete.
- This completion does not claim Care Homes booking checkout: that consumer remains deferred to HC-TASK-032, and facility payout fee/ledger remains HC-TASK-038. No downstream booking, payout, or ledger behavior was implemented under HC-TASK-033. Attribution review found the current dirty worktree changes belong to this Finance/consumer slice and its required docs/tests/Bruno/migrations; unrelated work remains preserved. No commit, push, package, or deployment has occurred.
- Earlier HC-TASK-033 failure and pending-gate bullets below are retained as historical evidence only; the final status above supersedes their former “In Progress”/unverified wording.

Current action (2026-10-03): HC-TASK-033 is Done for the bounded shared Finance consumer slice. HC-TASK-021 remains blocked until HC-TASK-032/034 provide hold/stay persistence and assignment/check-in lifecycle; no independent HC-021 implementation is available. The shared Finance implementation, migrations, docs, and unit coverage are integrated locally. Finance Admin Bruno passed 18/18 assertions; the explicit subscription lifecycle passed 24/24 assertions; the corrected caregiver quote/Family checkout gate passed 22/22 assertions. Build and test evidence are recorded in the final checkpoint above. Care Homes booking checkout remains deferred to HC-TASK-032, and facility payout fee/ledger remains HC-TASK-038. No commit, push, package, or deployment has occurred. Next: mastermind final release attribution and owner-authorized commit/push/package workflow.

Safety update (2026-10-03): a later guarded API startup for a focused, read-only invoice check unexpectedly applied `20261002134931_AddEmailOutbox` to `localhost:5432/SanadBrunoTestDb`; the previous lifecycle startup had applied no migrations. This application was not intended or freshly authorized. Test-user seeding was disabled and no Bruno requests were sent. The API was stopped, port 55819 confirmed closed, and generated fixture files removed. No rollback/reset/further migration or database cleanup was attempted. Stop all DB-backed work pending owner review and fresh direction.

Owner follow-up: the owner dropped only `localhost:5432/SanadBrunoTestDb`, then explicitly authorized recreation/migration and one corrected lifecycle attempt. A first startup failed while seeding a renewal subscription; source review found FamilySubscription reused EF-owned plan-benefit instances. Fixed this by copying benefit values and added a tracked-plan persistence regression test. The focused test passed, Families.Subscriptions tests passed 147/150 (3 PostgreSQL-only skips), the full suite passed Architecture 1 + Unit 2,172 (3 PostgreSQL-only skips), and build passed with 0 warnings/errors.

The next guarded startup completed, but the one authorized corrected lifecycle run stopped at enrollment payment intent (HTTP 500; 5 passed, 1 failed, 18 skipped by bail). Logs identify SQLSTATE `42P01`, missing `finance.platform_charge_rules`; Finance migrations are opt-in and the guarded fixture launcher lacked that opt-in. The launcher now sets `FinanceMigrations__ApplyOnStartup=true` only for guarded local fixture startup and restores the prior environment value. This launcher correction has not been run. Do not retry the lifecycle sequence or perform more DB-backed work without fresh authorization. The target retains applied project migrations and partial fixtures; API stopped, port 55819 closed, generated files removed. No rollback/reset, provider/production, commit, push, package, or deployment action occurred.

Current checkpoint: owner/facility routes remain under `docs/care-homes`; Admin operational routes are under `docs/admin`; Postman requests are synchronized in the corresponding collections. The owner dropped `localhost:5432/SanadBrunoTestDb`; guarded fixture startup rebuilt that exact local disposable target and applied project migrations. The owner API provisioned one disposable facility and inventory Bruno passed 30/30 assertions. The API and generated fixture env/image were cleaned; the DB and disposable fixture data remain. The user will deploy from their end after this authorized main commit/push.

## HC-TASK-033 documenter checkpoint (2026-10-02)

- HC-TASK-033 remains In Progress. Documentation synchronization added `docs/admin/finance-platform-charges.md` and `docs/postman/admins/Sanad.Admin.Finance.postman_collection.json`, and synchronized the affected subscription, booking, README, and Postman descriptions.
- The documented Finance routes use `FinanceOperationalAdmin` (normal SuperAdmin/SupportAdmin). The legacy `/api/v1/admin/subscriptions/tax-rules` adapter remains under unchanged SuperAdmin-only `SubscriptionPlanAdmin` authorization.
- The guide records the exact Finance and Family migration identifiers and the opt-in `FinanceMigrations:ApplyOnStartup` default `false`. It does not authorize production auto-migration, provider calls, deployment, or database reset/drop.
- This role ran only documentation validation; Mastermind must perform JSON/link/search/whitespace checks and the remaining implementation gates. The current booking-detail contract gap is explicit: persisted tax/rule snapshot fields are not exposed by that response.

Exactly one next action: Mastermind validates and integrates this documentation checkpoint, then records any concrete blocker before release.

## HC-TASK-033 Mastermind integration checkpoint (2026-10-03)

- Finance and Families migrations were regenerated through EF tooling with discoverable metadata and snapshots: `20261002210606_AddPlatformChargeRulesEfMetadata` and `20261002210659_AddPlatformChargeSnapshotMetadata`. The old hand-authored, undiscoverable migrations were removed. EF migration listing discovered both contexts' migrations. The owner then dropped the target DB; guarded fixture startup rebuilt only that disposable DB and applied its migrations, including these two.
- Added repository `NuGet.Config` pointing audit traffic to NuGet.org's official vulnerability-only endpoint. Restore completed without warnings and `dotnet build Sanad.slnx --no-restore -m:1 --nologo` passed with 0 warnings/errors.
- Focused tests: Architecture not included; shared charging filter passed 1,277, failed 0, skipped 3. Full suite: Architecture 1 passed; Unit 2,171 passed, failed 0, skipped 3. The skipped tests require PostgreSQL concurrency/constraint execution.
- The Family API listener on port 5235 was left untouched. This Care Homes main worktree reserves 55819 as the guarded fixture launcher's default; other worktrees must select distinct ports. This worktree's task API on 55819 and its earlier verified API process on 52687 were stopped; generated fixture environment/image were cleaned. Finance Bruno's latest run passed all 18 assertions (15 passed; 1 unrelated parser skip). Subscription positive payment/renewal/plan-change/invoice/callback coverage remains incomplete; HC-TASK-020 Bruno remains valid and was not rerun.

Exactly one next action: add and execute the missing positive subscription lifecycle Bruno coverage using safe disposable fixtures; do not use live provider state. No commit, push, Release package, deployment, or production/provider action until all required gates pass.

## HC-TASK-033 fixture-provisioning checkpoint (2026-10-03)

- The reviewed correction is fixture-only: `TestPlatformChargeRuleSeeder` is called only by opt-in Development `TestUserDataSeeder`. It uses the Finance writer to provision an effective 1.00% fee / 0.00% tax rule only when no effective rule exists; it preserves effective and future-only rules, selects the next version, treats a concurrent effective-rule winner as benign, and throws clearly if no rule is established. Six focused tests passed; the earlier API build passed with 0 warnings/errors; reviewer approval is recorded.
- The preceding explicit 24-path lifecycle run stopped at the first actionable enrollment payment-intent HTTP 400 because the migrated disposable database had no effective Finance rule. The application correctly has no product fallback. The seeder change is not DB- or Bruno-runtime-verified, and no further DB run is authorized or performed. The API is stopped, port 55819 is closed, and `local-fixtures.bru` plus `service-icon-fixture.png` are absent. The exact sequence design remains `bru run` over the 24 explicit paths with contiguous `meta.seq` 0–23, `--env local-fixtures --insecure --bail --reporter-skip-body`, excluding `meta.bru` and unrelated requests; see the sequence evidence in `Care_Homes_Tasks.md`. No Postman update applies because no HTTP contract changed.
- Mastermind integration passed after review: solution build exited 0 with 0 warnings/errors; focused Finance + Families.Subscriptions tests passed 168 with 3 PostgreSQL-only skips; full solution tests passed Architecture 1 and Unit 2,178 with 3 PostgreSQL-only skips. Offline rendering of all seven actual callback templates with synthetic fixture variables and the fixture rule's expected amounts passed JSON-object and HMAC-SHA512 checks; enrollment, renewal failure, renewal retry, and plan-change ID pairs were distinct, duplicates reused their intended IDs. This is not Bruno-runtime evidence. The exact runnable 24-path PowerShell command is in the task checklist.

Historical next action at this checkpoint: obtain owner authorization for the lifecycle run. That authorized run passed as recorded below.

## HC-TASK-033 lifecycle verification update (2026-10-03)

- After fresh owner authorization, guarded startup targeted only `localhost:5432/SanadBrunoTestDb`; migrations were already current and the opt-in Finance test rule seeder ran. Before Bruno requests, all seven actual callback templates were rendered offline with actual fixture ID `365dd4c7a77d`, 1.00% fee / 0.00% tax test rates, the fixture HMAC secret, and actual flow amounts. All payloads parsed as objects with valid server-order HMAC-SHA512; enrollment 913824/813824, first renewal 923824/823824, retry 928824/828824, and plan change 933824/833824 IDs were distinct and duplicates reused their IDs.
- The exact 24-explicit-path CLI run completed: 24 requests passed, 24/24 assertions passed, and one unrelated Wellness Tips parser skip remained. Enrollment callback/idempotency/invoice, renewal failure/grace/retry/success/idempotency/readback, plan-change success/idempotency/readback, invoice detail, and logout passed. No request hit an external provider. The API was stopped; generated fixture environment/icon were removed; port 55819 is closed. Disposable database rows remain and were not manually deleted/reset.
- Build and test integration are recorded above. However, the changed caregiver quote and Family caregiver checkout endpoints still lack verified Bruno evidence in this worktree. HC-TASK-033 remains In Progress; no commit, push, package, or deployment occurred.

Exactly one next action: obtain fresh owner authorization for the remaining bounded caregiver quote/Family checkout Bruno coverage against the same guarded disposable database, including readback and exact cleanup. Do not repeat the completed lifecycle sequence.

## HC-TASK-033 pending caregiver quote/Family checkout design (2026-10-03)

- This is a design checkpoint only; it records no new Bruno runtime result. The test author independently inspected the sequence and cleanup behavior, and the reviewer approved the bounded gate. The exact request order is caregiver `00-login-caregiver` → `01-search-seeded-active` → `03-quote-home-visit-success` → `08-logout-caregiver`, followed by Family `00-login-owner` → `01-list-dependents` → `02-search-active-medical-caregiver` → `03-checkout-success-no-payment` → `04-family-booking-detail-success` → `05-cancel-checkout-booking` → existing `05-logout-owner` (sequence 6; filename unchanged). Login/search requests populate `caregiverDiscoveryJwt`, `caregiverDiscoverySessionId`, `caregiverDiscoveryTargetId`, `familyBookingJwt`, `familyBookingDeviceSessionId`, `familyBookingDate`, `familyBookingDependentId`, and `familyBookingCaregiverId`; checkout populates `familyCheckoutBookingId` for detail/cancel.
- The caregiver login pre-request guard must require `baseUrl` parsed as HTTP, hostname `localhost`, an explicit numeric port from 1 through 65535, port not `5235`, and `appEnvironment=Development`. This excludes the root Family listener on 5235. The quote request is the seeded active caregiver home-visit quote and must assert the selected caregiver ID, positive base fee, exactly 1.00% platform fee, positive fee amount, 0.00% tax, and a positive Finance rule version. Family detail must assert the immutable booking ID/address, positive base fee, exactly 1.00% platform fee, positive fee amount, EGP, positive total, and total equal to base plus fee in cents under the 0% test tax. Checkout is unpaid and must not call a provider; cancel must return 204. Since no successful transaction or Paymob transaction ID exists, cancellation has no Paymob transaction to refund. Cleanup is cancellation plus logout only; no reset, deletion, or manual row cleanup.
- Installed Bruno CLI 4.2.0 help confirms `bru run [paths...]` and a single-request positional path form. The planned command, run from `tests/Bruno`, is:

```powershell
bru.cmd run `
  "collections/Sanad/caregiver-discovery/00-login-caregiver.bru" `
  "collections/Sanad/caregiver-discovery/01-search-seeded-active.bru" `
  "collections/Sanad/caregiver-discovery/03-quote-home-visit-success.bru" `
  "collections/Sanad/caregiver-discovery/08-logout-caregiver.bru" `
  "collections/Sanad/family-booking-checkout-detail/00-login-owner.bru" `
  "collections/Sanad/family-booking-checkout-detail/01-list-dependents.bru" `
  "collections/Sanad/family-booking-checkout-detail/02-search-active-medical-caregiver.bru" `
  "collections/Sanad/family-booking-checkout-detail/03-checkout-success-no-payment.bru" `
  "collections/Sanad/family-booking-checkout-detail/04-family-booking-detail-success.bru" `
  "collections/Sanad/family-booking-checkout-detail/05-cancel-checkout-booking.bru" `
  "collections/Sanad/family-booking-checkout-detail/05-logout-owner.bru" `
  --env local-fixtures --insecure --bail --reporter-skip-body
```

The 11 explicit paths exclude both collection `meta.bru` files and all unrelated requests, so collection metadata cannot be treated as a request. Before this one-time stateful run, the implementer must provide offline/static validation of generated request payloads and computed fixture IDs/amounts; the exact guarded startup may use only `localhost:5432/SanadBrunoTestDb`, and the run must stop on its first actionable failure. The earlier 24-request lifecycle sequence must not be rerun. The existing build, focused tests, full suite, and lifecycle evidence remain valid because this pending gate changes only Bruno requests/docs; it does not claim the pending gate passed.

## HC-TASK-033 pending caregiver quote/Family checkout gate outcome (2026-10-03)

- The exact documented 11 explicit-path command was attempted once with `--env local-fixtures --insecure --bail --reporter-skip-body`. Bruno emitted the known unrelated Wellness Tips invalid-file parser warning while scanning, then the first selected caregiver login failed in its pre-request script before sending HTTP: Bruno CLI 4.2's QuickJS runtime did not provide a working `URL` constructor, so the otherwise valid guarded local URL check threw. Per the first-actionable-failure rule, execution stopped immediately: 0 HTTP requests and 0 assertions ran, and the remaining 10 explicit request paths were skipped by `--bail`.
- This is a harness compatibility failure, not an API or product failure. Do not rerun the sequence or alter the guard to bypass it. Before any newly authorized verification, replace only the URL parsing dependency with a sandbox-compatible static localhost/explicit-port check that still requires HTTP, localhost, a numeric port in 1–65535, `Development`, and excludes port 5235.
- Guarded startup used only port 55819 and the authorized `localhost:5432/SanadBrunoTestDb`; it reported all migrations up to date with no migrations applied, and the opt-in fixture startup completed. The API was stopped; port 55819 is closed; exact generated `local-fixtures.bru` and `service-icon-fixture.png` files are absent. Because the failure occurred before any request reached the API, no HTTP request or booking mutation occurred; startup fixture seeding was the only intended database activity. HC-TASK-033 remains In Progress; no commit, push, package, or deployment occurred.

## HC-TASK-033 corrected caregiver/checkout gate authorization (2026-10-03)

- The owner has expressly asked to finish HC-TASK-033 and authorized one corrected attempt for the remaining caregiver quote and Family caregiver-checkout Bruno gate. This follows the documented QuickJS fail-stop; the earlier attempt was not silently repeated. Its 0-request/0-assertion result remains historical, and no result is claimed for the corrected attempt yet.
- The approved correction replaces the unsupported QuickJS `URL` constructor with a sandbox-compatible strict static check requiring `http://localhost:<port>`, exact `localhost`, an explicit numeric port 1--65535, `Development`, and rejecting port `5235` so the root Family listener cannot be targeted. Quote assertions now include caregiver-ID readback, positive base/fee, exactly 1.00% platform fee, 0.00% tax, and a positive rule version. Family detail assertions include immutable booking/address readback, positive base/fee/total, exactly 1.00% platform fee, EGP, and total equal to base plus fee in cents under the 0% fixture tax.
- The independent test-author inspection of sequence and cancellation cleanup and the reviewer approval are complete. The planned single run uses the same exact 11 explicit request paths in order, with `--env local-fixtures --insecure --bail --reporter-skip-body`; explicit paths exclude collection metadata and unrelated requests. It cancels the unpaid disposable checkout before logout and stops on the first actionable failure. No commit, push, package, or deployment has occurred.

## HC-TASK-033 corrected caregiver/checkout gate result (2026-10-03)

- Following fresh owner authorization, the exact 11 explicit request paths ran once with the corrected QuickJS-compatible guard. Bruno reported 11 passed, 1 skipped for the known invalid Wellness Tips parser warning while scanning, 22/22 assertions, and overall exit 0. Caregiver quote identity and pricing/rule assertions passed; Family checkout/detail immutable snapshot assertions passed; the pending unpaid booking cancellation returned HTTP 204; both logouts passed.
- No provider call occurred. Guarded startup targeted only `localhost:5432/SanadBrunoTestDb` on port 55819; migrations were up to date and none were applied, while opt-in fixture seeding ran. The API was stopped and port 55819 closed. Generated `tests/Bruno/environments/local-fixtures.bru` and `tests/Bruno/service-icon-fixture.png` were removed. The disposable database retains seeded fixtures and the canceled booking; no reset or deletion occurred.
- The changed endpoint gate is now verified, including the corrected static localhost/port guard and added quote/detail assertions. HC-TASK-033 remains In Progress until Mastermind completes final attribution/build evidence and the commit/push/package decision. No commit, push, package, or deployment is claimed in this checkpoint.

## Copy/paste prompt

```text
Act as Sanad's execution Mastermind for the Care homes backend slice only.

Read AGENTS.md, docs/governance/AGENTS.md, docs/Mastermind_Handoff.md,
docs/operations/care-homes/Care_Homes_Decisions.md and docs/operations/care-homes/Care_Homes_Tasks.md.
Use docs/operations/care-homes/UI_Review.md as UI evidence, not as an overriding business contract.

Execute the scoped plan under existing owner approval. The owner has already
supplied and discussed the operator and Family UI; do not repeat intake or restart
completed audits. Final decisions include separate Admin-configured percentage fee
and tax rates, each on payment base price, shared across caregiver, Care homes and
Family subscription payments; proportional Family-fee refunds; manual bank payouts
only after completed stays, with later refunds recorded as facility balance owed;
approval of genuinely non-expiring documents; SMS verification OTP; and
evidence-based Admin check-in dispute resolution. Do not ask these again. Block
only affected tasks and progress independent work.

Inspect git status and relevant diffs. Preserve unrelated local changes, private
files, UI and fixtures. Ignore the superseded product task files under docs/slices.
Map existing behavior before adding endpoints; do not reimplement completed work.

Keep the established sequential worker flow:
sanad_scout -> sanad_implementer -> sanad_test_author -> sanad_reviewer ->
sanad_documenter, then Mastermind integration, verification and handoff.
One worker at a time; bounded briefs with owned files, revision/worktree delta,
acceptance, dependencies and exact output. Do not add execution phases or repeat
the whole workflow for a concrete correction.

Backend only. Include the caregiver review permission correction to
SuperAdmin/SupportAdmin; ContentAdmin stays CMS-only. Include approved Family
caregiver ratings/top-10 integration, not unrelated caregiver booking expansion.
Notifications are in-app and email; no ordinary SMS notifications. SMS is explicitly
allowed for verification OTP only; login remains password-based.

Every new/changed endpoint needs unit and Bruno success/failure/auth/authorization
coverage. Run stateful Bruno only on authorized disposable local/test fixtures,
with readback and cleanup. Documenter synchronizes separate feature docs and
organized Postman requests, including the changed caregiver permissions.

Mastermind owns project build -> focused slice tests -> full suite, all with
successful exit and zero warnings, plus changed-endpoint Bruno coverage.
Reuse valid gates; report exact blockers, never claim an unrun gate passed.
Update the canonical task statuses/evidence and keep the handoff short.

Obtain separate authorization for exact-target database migrations/resets,
commits, pushes, deployment and external production/provider actions.
Do not delete legacy files without exact-list confirmation. Do not start Chat,
Notifications as a whole, UI audits or other product slices after this one.
Latest checkpoint: the owner confirms the pushed HC-TASK-020 checkpoint is deployed. HC-TASK-021 is Blocked pending booking/hold persistence from HC-TASK-032 and room/bed assignment plus check-in/out lifecycle from HC-TASK-034; see the checklist dependency map. HC-TASK-022 is complete: the pure calendar-month helper, seven authored boundary cases, documentation, build, focused tests, and full suite are verified. Existing HC-020 Bruno evidence remains valid; this change adds no HTTP contract, so no Postman or Bruno update applies. The first package at `a7cd31ae3a6abf63f038227d04109d9d67deba29` was rejected because it contained `appsettings.Development.json`; do not upload it. The publish project now excludes the file and the publisher checks for it. The corrected Release package was verified with the exact pushed manifest and without development settings. Exactly one next action: owner uploads and deploys the verified SHA-specific package reported in the handoff. Keep Admin docs in `docs/admin`; owner/facility docs in `docs/care-homes`.

Mastermind verification checkpoint (2026-10-02): `dotnet build Sanad.slnx --no-restore -m:1 --nologo` exited 0 with 0 warnings/errors; focused Care Homes/storage tests passed 23/23; full suite passed Architecture 1 and Unit 2,119, with 0 failed/skipped. On the owner-authorized `localhost:5432/SanadBrunoTestDb`, owner onboarding Bruno passed 27 requests with 44/44 assertions; Admin Care Homes passed 40 requests with 69/69 assertions. Each collection had one skipped request due to the pre-existing invalid Wellness Tips parser file. Submitted-application, correction/resubmission, private-file, document-verification, approval/suspension/reactivation, role access/denial, and session-cleanup checks passed. The owner-save assertion was corrected to match the implemented `Draft` state during edits. The API was stopped and the disposable database/uploads/results were cleaned. The broad Care Homes checklist remains active.

Static contract mapping passed after the latest Bruno update: 339 controller route-method signatures, 385 Postman API requests, 0 missing/orphan Postman mappings, 1,111 Bruno API requests, and 0 missing/orphan Bruno mappings. `git diff --check` passed with only CRLF replacement notices.

HC-TASK-014 remains In Progress: `CareHomeLicenseExpiryMonitor` performs a daily UTC-midnight scan and creates idempotent in-app alerts plus durable email-outbox messages for active SuperAdmin/SupportAdmin recipients. It considers only approved facilities whose latest approved-revision operating license is verified, dated, and expired before the scan date; Admin-verified non-expiring, unverified, and rejected/null-expiry documents are excluded. Replacement uploads remain pending review. SMTP Message-ID is a stable SHA-256 digest of the email idempotency key; outbox delivery is at least once. The owner later dropped the authorized disposable DB; guarded fixture startup rebuilt it and applied project migrations. No booking block applies to existing stays; integration remains deferred to HC-TASK-020/032.

Current task state: HC-TASK-020 remains In Progress because `ICareHomeOccupancyProvider` awaits booking integration. HC-TASK-021 is Blocked pending HC-TASK-032/034. HC-TASK-022 is Done with build, focused tests, and full suite passing; no HTTP contract changed. Booking/payment boundary semantics and renewal remain excluded. The owner confirms the latest pushed checkpoint is deployed. The first package failed its development-settings check and must not be uploaded; the corrected Release package was verified after the packaging fix. The single next action is for the owner to upload and deploy the SHA-specific package reported in the handoff.

Worker evidence (2026-10-02): the owner authorized additive migration and stateful Bruno execution only on `localhost:5432/SanadBrunoTestDb`. `20261002150725_AddCareHomeInventory` was generated, inspected, and applied to that exact target; it creates only `room_types`, `rooms`, `beds`, and `maintenance_blocks` with the approved FKs/indexes, and its `Down` drops only those tables. At the initial migration checkpoint, other module histories were current. After the owner dropped the disposable DB, guarded fixture startup rebuilt the exact target and applied project migrations. The fixture API ran with SMS/SMTP disabled; one disposable facility was created through the owner API.

The initial run’s mapper/facility findings were corrected and reviewer-approved. In linked main worktree `.codex/worktrees/care-homes-main` based on `main` commit `3eae51c`, focused tests passed 57/57 and the full suite passed Architecture 1 plus Unit 2,145. An initial build had 20 `NU1900` NuGet audit-feed connectivity warnings; after network-enabled restore, the final solution build passed with 0 warnings and 0 errors.

Current Bruno result: the reviewer-approved collection has unique contiguous sequence 1-30, with validations/conflicts before archival. Following the owner's DB drop, guarded startup rebuilt `localhost:5432/SanadBrunoTestDb` and applied project migrations. Owner login succeeded; the owner API created one disposable facility (201). Inventory Bruno passed: 31 requests (30 passed, 1 unrelated Wellness Tips parser skip), 30/30 assertions, 0 failed. It covers owner mutations, duplicate/invalid cases, maintenance/availability, Admin inspection, and role denials. The collection archives its room type/room and restores its bed. Generated fixture env/image and API process were cleaned up. HC-TASK-020 remains In Progress because `ICareHomeOccupancyProvider` awaits HC-TASK-032/034 booking integration. Latest static mapping is recorded above. The owner confirms this pushed checkpoint is deployed.
```

## Planner handoff evidence

All 29 supplied screens were inspected in the planning conversation; owner answered the 24-question consolidated intake. Decisions and tasks were consolidated by the documenter and checked by the planning mastermind. No product code was changed or tested by this planning handoff.

Documentation verification (2026-10-02): all 22 relative links across the seven canonical Care homes/control files resolved; `git diff --check` exited 0 (Git emitted line-ending notices). Cleanup manifest contains 102 exact generated draft paths and excludes the retained migration evidence file. The latest build, .NET tests, and passing inventory Bruno evidence is recorded above.
- HC-TASK-033 latest lifecycle evidence: with owner approval, guarded startup applied the Finance migration to the exact disposable `localhost:5432/SanadBrunoTestDb`; offline payload/ID/HMAC checks passed for all seven webhook variants. The next explicit-path Bruno lifecycle run passed 5 requests and stopped at enrollment payment intent with HTTP 400; `--bail` skipped the remaining 18 requests. API logs show the Finance query ran successfully, but no effective `platform_charge_rules` row exists. The application correctly fails closed when the required Admin-configured rule is absent; do not add a product default. The remaining likely harness gap is provisioning an effective shared Finance rule in the disposable fixture before subscription requests. Port 55819 is closed and only the exact generated `local-fixtures.bru` and `service-icon-fixture.png` artifacts were removed. The disposable DB remains migrated with partial fixture state; no reset, rollback, record cleanup, or further DB-backed run was performed. No provider/production action, commit, push, package, or deployment occurred. HC-TASK-033 remains In Progress; lifecycle coverage is incomplete. Next: design and test bounded fixture provisioning, then obtain explicit authorization before another stateful lifecycle run.
