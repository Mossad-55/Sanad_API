# Care homes — execution mastermind handoff

Status: Execution in progress under owner approval. Fee/tax and payout contracts are resolved. The caregiver review permission correction, Family caregiver ratings/top-10, owner submission, and initial Admin queue/document/review routes have implementation and test evidence. Owner onboarding Bruno passed (27 passed, 1 unrelated parser skip; 44/44 assertions); Admin Care Homes Bruno passed (40 passed, 1 unrelated parser skip; 69/69 assertions). The broader checklist remains active.

Current checkpoint: owner/facility routes remain under `docs/care-homes`; Admin operational routes are under `docs/admin`; Postman requests are synchronized in the corresponding collections. The owner authorized recreation of `localhost:5432/SanadBrunoTestDb`; both Bruno collections passed against that target with readback and session cleanup. The API was stopped, that disposable database was dropped, and only the generated Care Homes upload directory and local Bruno result reports were removed. The protected PNG and tracked fixture environment were preserved. Reviewer corrections, including the implementation race fix, are recorded below. No other database was changed.

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
Latest checkpoint: the owner onboarding module supports CareHomeOwner-only create/read/versioned bilingual draft-save routes, with unit/Bruno/Postman coverage. The initial `care_homes` onboarding migration was authorized for and applied to `localhost:5432/SanadBrunoTestDb` only. Continue from HC-TASK-010 through 014: private document upload, submission, Admin verification/review APIs and tests. Keep Admin docs in `docs/admin`; owner/Family/facility docs in `docs/care-homes`; synchronize the corresponding Admin or Care homes Postman collection. The caregiver and Care homes endpoint docs retain their established folders.

Mastermind verification checkpoint (2026-10-02): `dotnet build Sanad.slnx --no-restore -m:1 --nologo` exited 0 with 0 warnings/errors; focused Care Homes/storage tests passed 23/23; full suite passed Architecture 1 and Unit 2,119, with 0 failed/skipped. On the owner-authorized `localhost:5432/SanadBrunoTestDb`, owner onboarding Bruno passed 27 requests with 44/44 assertions; Admin Care Homes passed 40 requests with 69/69 assertions. Each collection had one skipped request due to the pre-existing invalid Wellness Tips parser file. Submitted-application, correction/resubmission, private-file, document-verification, approval/suspension/reactivation, role access/denial, and session-cleanup checks passed. The owner-save assertion was corrected to match the implemented `Draft` state during edits. The API was stopped and the disposable database/uploads/results were cleaned. The broad Care Homes checklist remains active.

Static contract mapping passed after the latest Bruno update: 325 controller route-method signatures, 371 Postman requests, 0 missing/orphan Postman mappings, 1,081 Bruno API requests, and 0 missing/orphan Bruno mappings. `git diff --check` passed with only CRLF replacement notices.

Next: continue only the remaining Care Homes backend checklist; do not repeat intake or the completed owner/Admin Bruno runs. Implement and verify the next dependent work, update checklist evidence, and obtain separate authorization before any future database reset, commit/push, deployment, or production/provider action.
```

## Planner handoff evidence

All 29 supplied screens were inspected in the planning conversation; owner answered the 24-question consolidated intake. Decisions and tasks were consolidated by the documenter and checked by the planning mastermind. No product code was changed or tested by this planning handoff.

Documentation verification (2026-10-02): all 22 relative links across the seven canonical Care homes/control files resolved; git diff --check exited 0 (Git emitted line-ending notices). Cleanup manifest contains 102 exact generated draft paths and excludes the retained migration evidence file. No .NET/Bruno runtime gate was run because this update changes documentation only.
