# Codex local workflow

Follow [repository instructions](../governance/AGENTS.md). Start with [Mastermind handoff](../Mastermind_Handoff.md), then its active slice. [Project goal](../Project_Main_Goal.md) indexes the owner-approved priorities.

## One sequential pass

Before this implementation sequence, the planning mastermind must ask what already exists in the owner's application for the slice, review provided UI/instructions, discuss recommendations and missing decisions, and adjust tasks with the owner. Development begins only after explicit plan approval. The planning mastermind supplies the approved plan and exact handoff prompt to the executing mastermind. Only Care homes is currently in intake; other features remain unplanned.

Scout → implementer → test author → reviewer → documenter → mastermind integration, verification, and handoff. One worker at a time. Mark an inapplicable role briefly. Concrete corrections receive a bounded follow-up; do not restart the sequence or add another review round.

Every brief specifies the revision/worktree delta, objective, owned files, acceptance criteria, dependencies, and required output. Scout identifies every affected endpoint and test prerequisite. Test author supplies unit and Bruno coverage for each new or behaviorally changed endpoint. Documenter keeps feature guides separate and organizes matching Postman requests into clear feature folders or appropriate separate collections.

## Keep owner asks and verification simple

When blocked, tell the owner in plain language what is missing, why it prevents the next step, and the one decision or input needed to continue. Name the exact fixture/scenario only when that is the blocker. Do not turn a simple ask into a long authorization checklist; include only the target and side effects that matter for the specific operation.

Keep tests proportional to the behavior being changed. Add focused unit coverage for the key rule and concise Bruno coverage for the endpoint's main success path and the directly relevant failure/access case. Reuse existing fixtures, setup, requests, and passing evidence where valid. Create only the minimum fixture state needed for one meaningful scenario, read back the result, and clean up only when supported and authorized. Avoid duplicate cases, broad matrices, large manifests, speculative edge cases, and elaborate fixture systems unless the contract or a demonstrated defect requires them. Report any meaningful coverage gap plainly; do not inflate test counts to appear thorough.

## Required gates

Bruno uses the mandatory [failure-first runbook](bruno-failure-first.md) within the same worker sequence. Pin the worktree/runtime/fixture, run the offline preflight for the explicit request manifest, diagnose the first failure, fix the responsible code/request, and rerun its minimal prerequisites. After that passes, run the affected slice gate. HTTP failures do not justify database resets or random ports. Reuse existing in-scope authorization and valid evidence; apply the two-unchanged-attempt/ten-minute diagnostic checkpoint instead of repeating commands.

For every implementation slice, the mastermind builds the project, runs focused slice tests, then the full suite, and executes the minimum relevant Bruno coverage against disposable local/test fixtures. Keep the focused tests and Bruno scenarios on point; do not add redundant cases or overbuild fixtures. Build/test gates require successful exit and zero warnings. No endpoint commit without successful required Bruno coverage. State only the setup, readback, cleanup, and limitations needed to understand the evidence. Documentation/script-only changes receive relevant static/script validation.

Reuse successful gates unless later changes invalidate them. Record commands, scope, results, warnings/skips, cleanup, and limitations in the owning slice. Static request mapping is not an executed test. Owner-confirmed historical results remain labelled separately.

## Resume and closeout

1. Inspect the worktree and preserve unfinished and unrelated changes.
2. Read the active slice, current role, and next action; resume there.
3. Resolve only decisions needed now; block dependent tasks narrowly.
4. Complete verification, update the slice and short handoff, and report.
5. Obtain separate authorization for release actions and exact-target database operations. Do not silently start the next product goal.

The persisted model/effort remains gpt-5.6-luna / medium. Root AGENTS.md is the instruction-discovery pointer. Consolidated logs are retired sources awaiting separately confirmed cleanup.
