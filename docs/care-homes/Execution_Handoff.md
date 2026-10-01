# Care homes — execution mastermind handoff

Status: Ready for owner-directed execution following final intake answers. Unresolved implementation contract details block only dependent tasks. This file is not evidence that implementation or runtime validation has happened.

## Copy/paste prompt

```text
Act as Sanad's execution Mastermind for the Care homes backend slice only.

Read AGENTS.md, docs/governance/AGENTS.md, docs/Mastermind_Handoff.md,
docs/care-homes/Care_Homes_Decisions.md and docs/care-homes/Care_Homes_Tasks.md.
Use docs/care-homes/UI_Review.md as UI evidence, not as an overriding business contract.

When the owner sends this prompt, execute the scoped plan below.
The owner has already supplied and discussed the operator and Family UI; do not
repeat the intake or restart completed audits. Resolve only the explicit remaining
implementation details after mapping existing conventions, in one consolidated
request if needed. Final owner decisions include percentage fees with proportional
Family-fee refunds, Admin-recorded manual bank payouts, approval of genuinely
non-expiring documents, SMS verification OTP and evidence-based Admin check-in
dispute resolution. Do not ask these again. Never invent payout eligibility or
fee-basis/tax rules. Block only affected tasks and progress independent work.

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
Finish with verified results, remaining blockers and a clean handoff.
```

## Planner handoff evidence

All 29 supplied screens were inspected in the planning conversation; owner answered the 24-question consolidated intake. Decisions and tasks were consolidated by the documenter and checked by the planning mastermind. No product code was changed or tested by this planning handoff.

Documentation verification (2026-10-02): all 22 relative links across the seven canonical Care homes/control files resolved; git diff --check exited 0 (Git emitted line-ending notices). Cleanup manifest contains 102 exact generated draft paths and excludes the retained migration evidence file. No .NET/Bruno runtime gate was run because this update changes documentation only.
