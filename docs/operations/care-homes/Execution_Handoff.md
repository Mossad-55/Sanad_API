# Care homes — execution mastermind handoff

Status: Execution in progress under owner approval. Fee/tax and payout contracts are resolved. The caregiver review permission correction, Family caregiver ratings/top-10, owner submission, and initial Admin queue/document/review routes have implementation and test evidence. Owner onboarding Bruno passed (27 passed, 1 unrelated parser skip; 44/44 assertions); Admin Care Homes Bruno passed (40 passed, 1 unrelated parser skip; 69/69 assertions). The broader checklist remains active.

Current checkpoint: owner/facility routes remain under `docs/care-homes`; Admin operational routes are under `docs/admin`; Postman requests are synchronized in the corresponding collections. The owner dropped `localhost:5432/SanadBrunoTestDb`; guarded fixture startup rebuilt that exact local disposable target and applied project migrations. The owner API provisioned one disposable facility and inventory Bruno passed 30/30 assertions. The API and generated fixture env/image were cleaned; the DB and disposable fixture data remain. The user will deploy from their end after this authorized main commit/push.

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
