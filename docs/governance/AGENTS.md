# Sanad API working instructions

These repository-wide rules are referenced by the root AGENTS.md. Owner-approved on 2026-10-01. The modular migration does not restart completed product work.

## Start and resume

- Mandatory owner intake BEFORE planning or developing ANY product slice: ask what the owner already has in the application, review supplied screens and instructions, and clarify missing behavior. Recommend options for discussion; do not infer endpoint or business-rule approval from screenshots. Add, remove, or modify tasks with the owner, then obtain explicit plan approval before development.
- The current mastermind is the planning mastermind. Prepare only the currently discussed Care homes plan and, after owner approval, an exact execution handoff/prompt for another mastermind. Other product slices remain unplanned until discussed. Previously generated project-wide task files are superseded drafts pending scoped cleanup, not an executable backlog.

- Read docs/Mastermind_Handoff.md, then only the active slice and linked contracts. docs/Project_Main_Goal.md owns project scope, priorities, and the phase-to-slice index.
- Inspect git status and the relevant diff first; preserve unrelated edits and private files. Resume at the unfinished role. Reopen completed work only for a concrete defect, changed requirement, or invalidated evidence.
- Keep one active slice and one worker at a time. The slice checklist owns task status and evidence; other documents link to it. Statuses are Not started, In progress, Blocked, and Done. Identify historical/owner-confirmed evidence separately from local execution.
- Update the slice whenever a task starts, finishes, or becomes blocked. Keep the handoff short: active goal/phase/slice/worker, completed milestone, blockers, and exactly one next action.
- Resolve decisions needed for the active slice; future decisions remain in their future slices. Block only dependent tasks and continue independent authorized work within the active slice.
- Legacy handoffs, progress logs, VAT briefs, and the former 16-item release checklist are historical sources awaiting separately confirmed deletion. Do not treat them as active instructions or routinely reread private history.

## Unchanged worker sequence

For implementation: sanad_scout → sanad_implementer → sanad_test_author → sanad_reviewer → sanad_documenter. The mastermind integrates, resolves concrete findings, verifies, and hands off. Mark a role with no relevant work inapplicable in one line. Do not spawn duplicate workers or run another review round.

- Every brief pins the revision/worktree delta, objective, owned files, acceptance criteria, dependencies, and required output. Tell workers they are not alone and must preserve others' edits. Supply bounded context rather than repository dumps or full conversation history.
- Scout maps requirements, existing behavior, every affected endpoint, permissions, files, tests, fixtures, and verification prerequisites before implementation; reports missing inputs and proposes the exact file manifest.
- Implementer changes only the approved behavior and owned files; reports contract ambiguity instead of inventing policy.
- Test author supplies unit tests for the relevant logic of every new or behaviorally changed endpoint AND Bruno coverage for every such endpoint. Cover success, validation, authentication, authorization, and applicable conflict/state cases. Stateful scenarios require disposable fixture setup, readback, and cleanup. Distinguish authored tests from executed tests; report exact missing prerequisites.
- Reviewer performs one bounded read-only correctness, security, compatibility, and test-coverage review with concrete findings, paths, and corrections.
- Documenter keeps each feature guide separate and linked to its slice; documents routes, permissions, payloads, responses, errors, and lifecycle. Update the appropriate Postman collection with clear feature folders and existing naming conventions; create a separate collection when appropriate. Match examples to verified behavior. Update slice evidence and handoff references within the assigned manifest; do not duplicate checklists.
- Workers report changed files, results, findings, and blockers. Route concrete findings to the responsible worker with a bounded correction brief; do not restart the whole sequence.

## Mastermind verification

- Bruno recovery MUST follow [the Bruno runbook](../operations/bruno-failure-first.md). Pin one worktree/API build/port/database/fixture identity. Use the offline preflight with explicit request paths before the first run or changed selection/tooling/target; reuse its result while inputs remain valid.
- At the first unexpected Bruno failure, capture expected/actual status, approved contract, response error and matching API exception/request encoding. Fix the evidenced request, assertion or code directly, then rerun the failed request with only required prerequisites. Never reset a database, rotate ports, replay all onboarding or restart worker phases merely for an HTTP/status/assertion failure.
- After two unchanged attempts or ten minutes without new diagnostic evidence, stop blind reruns and report the precise failure, next hypothesis and blocker while continuing useful authorized diagnosis. This checkpoint never waives required Bruno coverage. Once corrected, run the full affected endpoint manifest. Test author supplies ordered dependencies, replay rules and cleanup with the tests.
- Existing owner authorization remains valid within its exact scope. Do not repeatedly request it for the same authorized run. A reset or new startup side effect still needs its own applicable authority and evidence; different worktree ports do not isolate a shared database.

- Mastermind owns scope, decisions, sequential worker spawning, integration, project build, focused slice tests, full suite, slice Bruno execution, final evidence, and handoff.
- For EVERY implementation slice: build the project, run focused slice tests, then run the full test suite. Execute the slice's Bruno success/failure/auth scenarios against disposable local/test fixtures before closeout or commit.
- Documentation/script-only changes use relevant documentation/script validation. Do not launch the API or full .NET suite merely for documentation relocation.
- Every dotnet build/test gate requires successful exit and ZERO warnings. Resolve failures/warnings; never use blanket suppression. If an unrelated/environmental warning cannot safely be resolved in scope, report the gate as not clean and block closeout.
- Reuse successful gates while their inputs remain valid; rerun only gates invalidated by later changes. Stop stalled or repeating commands. Record command, revision/worktree scope, exit status, counts, skips, warnings, cleanup, and limitations. Never invent execution results.
- Test database resets require exact disposable-target verification and owner authorization. Never run stateful Bruno against production. Do not commit an endpoint change with missing, failing, or blocked required Bruno coverage.
- Define scope, permissions, observable acceptance criteria, and verification before implementation. Admin/CMS list/detail/history/inspection acceptance belongs to the same resource slice as its mutations.

## Models and runtime

The requested repository default is gpt-6-luna at medium reasoning for the mastermind and all five Sanad roles. Repository guidance cannot change a running session or a platform-pinned role runtime; the current Sanad role tools report gpt-5.6-luna as fixed and non-overridable. The owner can inspect the effective runtime with /status; reload/new sessions apply platform or persisted defaults unless overridden. Worker runtime configuration and role order are unchanged.

## Safety and release

- Never expose credentials or stage private handoffs, protected fixtures, or unrelated changes. New docs contain sanitized rules/evidence, not private environment details.
- Obtain owner authorization for exact-target migrations, database resets, deployment, commits, pushes, merges, remote writes, and production data changes. Existing authorization is valid only within its scope.
- Legacy deletion needs a separate exact-list confirmation AFTER replacement verification. Blueprint/workflow approval is not deletion approval.
- Priority: Care homes → Chat → Notifications → full application UI reconciliation → remaining roadmap. Original phase IDs remain traceability metadata. Family and Elderly UI are supplied; Caregiver UI is outstanding. Do not ask for already supplied inventories or repeat completed audits.
- At goal completion, report and hand off. Do not silently begin a later product goal or restore the historical 16-item checklist.
