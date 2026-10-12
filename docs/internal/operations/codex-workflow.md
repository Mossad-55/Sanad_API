# Codex local workflow

Follow [repository instructions](../governance/AGENTS.md). Start with [Mastermind handoff](../Mastermind_Handoff.md), then its active slice. [Project goal](../Project_Main_Goal.md) indexes the owner-approved priorities.

## One sequential pass

Before this implementation sequence, the planning mastermind must ask what already exists in the owner's application for the slice, review provided UI/instructions, discuss recommendations and missing decisions, and adjust tasks with the owner. Development begins only after explicit plan approval. The planning mastermind supplies the approved plan and exact handoff prompt to the executing mastermind. Only Care homes is currently in intake; other features remain unplanned.

For substantial tasks, use scout → implementer → optional test author → reviewer → optional documenter → mastermind integration, verification, and handoff. One worker at a time. Small, clear tasks may skip roles that add no value. Concrete corrections receive a bounded follow-up; do not restart the sequence or add another review round.

Every brief specifies the revision, objective, owned files, acceptance criteria, dependencies, and required output. Scout identifies affected endpoints and verification prerequisites, including test data/actors/states, before Bruno requests are written. The Mastermind checks existing fixture data read-only and seeds only the minimum missing records on the already-authorized disposable target; it verifies the state before running a request. Keep a separate fresh booking/entity for each state-changing scenario that consumes its prior state. Test author is optional. Documenter updates feature guides/Postman when the API contract or user-facing behavior changes.

## Keep owner asks and verification simple

When blocked, tell the owner in plain language what is missing, why it prevents the next step, and the one decision or input needed to continue. Name the exact fixture/scenario only when that is the blocker. Do not turn a simple ask into a long authorization checklist; include only the target and side effects that matter for the specific operation.

Keep tests proportional to the behavior being changed. Run focused automated tests for the changed rule/contract. Bruno is optional and risk-based: use a small smoke check only when live HTTP behavior adds needed evidence or the owner requests it. Do not create a Bruno case for every endpoint or a fixed matrix of success, validation, authentication, authorization, and conflicts. Reuse safe fixtures; seed only what is missing, verify it before the request, and read back state-changing results. Clean up only when supported and authorized. Avoid broad suites and fixture frameworks. If Bruno is blocked by unavailable data/provider/runtime, record why and the automated evidence; do not let that alone hold the task or commit.

## Required gates

When Bruno is selected, use the [failure-first runbook](bruno-failure-first.md). Pin the runtime/fixture, run only explicit requests, diagnose the first failure, fix the evidenced cause, and rerun minimal prerequisites. HTTP failures do not justify database resets or random ports. Reuse valid evidence and authorized fixtures; after two unchanged attempts or ten minutes without new diagnostic evidence, stop and report the limitation.

For implementation changes, the Mastermind builds once after the final code edit and runs focused tests. Run the full suite for cross-cutting/high-risk changes or release readiness, not every small task. Bruno is not a default gate and does not block a commit solely because its runtime/fixture is unavailable. Required automated checks must pass; document skipped/blocked checks honestly. Documentation/script-only changes receive relevant static/script validation. Do not add broad fixtures or tests without a contract need or observed defect.

Reuse successful gates unless later changes invalidate them. Record commands, scope, results, warnings/skips, cleanup, and limitations in the owning slice. Static request mapping is not an executed test. Owner-confirmed historical results remain labelled separately.

## Resume and closeout

1. Inspect the worktree and preserve unfinished and unrelated changes.
2. Read the active slice, current role, and next action; resume there.
3. Resolve only decisions needed now; block dependent tasks narrowly.
4. Complete verification, update the slice and short handoff, and report.
5. Update the task/handoff, commit completed task changes on `main`, and push `main` to keep the remote aligned under the owner's standing instruction. Do not create routine worktrees or merge another branch. Get separate authorization for production changes, destructive database resets, or deployment. Do not silently start the next product goal.

The persisted model/effort remains gpt-5.6-luna / medium. Root AGENTS.md is the instruction-discovery pointer. Consolidated logs are retired sources awaiting separately confirmed cleanup.
