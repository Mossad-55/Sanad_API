# Codex local workflow

Follow [repository instructions](../governance/AGENTS.md). Start with [Mastermind handoff](../Mastermind_Handoff.md), then its active slice. [Project goal](../Project_Main_Goal.md) indexes the owner-approved priorities.

## One sequential pass

Before this implementation sequence, the planning mastermind must ask what already exists in the owner's application for the slice, review provided UI/instructions, discuss recommendations and missing decisions, and adjust tasks with the owner. Development begins only after explicit plan approval. The planning mastermind supplies the approved plan and exact handoff prompt to the executing mastermind. Only Care homes is currently in intake; other features remain unplanned.

Scout → implementer → test author → reviewer → documenter → mastermind integration, verification, and handoff. One worker at a time. Mark an inapplicable role briefly. Concrete corrections receive a bounded follow-up; do not restart the sequence or add another review round.

Every brief specifies the revision/worktree delta, objective, owned files, acceptance criteria, dependencies, and required output. Scout identifies every affected endpoint and test prerequisite. Test author supplies unit and Bruno coverage for each new or behaviorally changed endpoint. Documenter keeps feature guides separate and organizes matching Postman requests into clear feature folders or appropriate separate collections.

## Required gates

For every implementation slice, the mastermind builds the project, runs focused slice tests, then the full suite, and executes slice Bruno coverage against disposable local/test fixtures. Build/test gates require successful exit and zero warnings. No endpoint commit without successful required Bruno coverage. Setup, readback, cleanup, and untestable cases must be explicit. Documentation/script-only changes receive relevant static/script validation.

Reuse successful gates unless later changes invalidate them. Record commands, scope, results, warnings/skips, cleanup, and limitations in the owning slice. Static request mapping is not an executed test. Owner-confirmed historical results remain labelled separately.

## Resume and closeout

1. Inspect the worktree and preserve unfinished and unrelated changes.
2. Read the active slice, current role, and next action; resume there.
3. Resolve only decisions needed now; block dependent tasks narrowly.
4. Complete verification, update the slice and short handoff, and report.
5. Obtain separate authorization for release actions and exact-target database operations. Do not silently start the next product goal.

The persisted model/effort remains gpt-5.6-luna / medium. Root AGENTS.md is the instruction-discovery pointer. Consolidated logs are retired sources awaiting separately confirmed cleanup.
