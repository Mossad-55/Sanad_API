# Sanad API working instructions

These repository-wide rules are referenced by the root AGENTS.md. Owner-approved on 2026-10-01. The modular migration does not restart completed product work.

## Start and resume

- Mandatory owner intake BEFORE planning or developing ANY product slice: ask what the owner already has in the application, review supplied screens and instructions, and clarify missing behavior. Recommend options for discussion; do not infer endpoint or business-rule approval from screenshots. Add, remove, or modify tasks with the owner, then obtain explicit plan approval before development.
- The current mastermind is the planning mastermind. Prepare only the currently discussed slice plan and, after owner approval, an exact execution handoff/prompt for another mastermind. Care Homes is complete; Chat is planned with an approved execution package and no implementation started. Notifications and later slices remain unplanned until discussed. Previously generated project-wide task files are superseded drafts pending scoped cleanup, not an executable backlog.

- Read docs/Mastermind_Handoff.md, then only the active slice and linked contracts. docs/Project_Main_Goal.md owns project scope, priorities, and the phase-to-slice index.
- Inspect git status and the relevant diff first; preserve unrelated edits and private files. Resume at the unfinished role. Reopen completed work only for a concrete defect, changed requirement, or invalidated evidence.
- Keep one active slice and one worker at a time when workers add value. The slice checklist owns task status and evidence; other documents link to it. Statuses are Not started, In progress, Blocked, and Done. Identify historical/owner-confirmed evidence separately from local execution. Small, well-bounded tasks may be completed directly by the Mastermind without running every worker role.
- Update the slice whenever a task starts, finishes, or becomes blocked. Keep the handoff short: active goal/phase/slice/worker, completed milestone, blockers, and exactly one next action.
- Resolve decisions needed for the active slice; future decisions remain in their future slices. Block only dependent tasks and continue independent authorized work within the active slice.
- Legacy handoffs, progress logs, VAT briefs, and the former 16-item release checklist are historical sources awaiting separately confirmed deletion. Do not treat them as active instructions or routinely reread private history.

## Worker roles and usual sequence

For a substantial implementation: sanad_scout → sanad_implementer → optional sanad_test_author → sanad_reviewer → optional sanad_documenter. The Mastermind integrates, resolves concrete findings, runs selected verification, and hands off. Small tasks may skip roles that add no value. Mark skipped roles briefly; do not spawn duplicate workers or run another review round.

- Every brief pins the current `main` revision/local delta, objective, owned files, acceptance criteria, dependencies, and required output. Tell workers they are not alone and must preserve others' edits. Supply bounded context rather than repository dumps or full conversation history.
- Scout maps requirements, existing behavior, affected endpoints, permissions, files, and verification before implementation; reports missing inputs and proposes the exact file manifest. Before any Bruno authoring, Scout lists the actors and data states each proposed scenario needs, which operations consume/change them, and which fixtures can safely be reused.
- Implementer changes only the approved behavior and owned files; reports contract ambiguity instead of inventing policy.
- Test author is optional and is used when a change needs substantial or independent test design. Add focused automated tests for the changed business rule or contract. Do not require unit tests and Bruno for every endpoint or a fixed success/validation/authentication/authorization/conflict matrix. Cover only behavior changed by the task and directly relevant regressions. Bruno is optional and risk-based; use it when a live HTTP/provider boundary adds evidence unit tests cannot provide, or when the owner requests it. Distinguish authored from executed checks and report any material gap.
- Reviewer performs one bounded read-only correctness, security, compatibility, and test-coverage review with concrete findings, paths, and corrections.
- Documenter keeps each feature guide separate and linked to its slice; documents routes, permissions, payloads, responses, errors, and lifecycle. Update the appropriate Postman collection with clear feature folders and existing naming conventions; create a separate collection when appropriate. Match examples to verified behavior. Update slice evidence and handoff references within the assigned manifest; do not duplicate checklists.
- Workers report changed files, results, findings, and blockers. Route concrete findings to the responsible worker with a bounded correction brief; do not restart the whole sequence.

## Mastermind verification

- When Bruno is selected, use [the Bruno runbook](../operations/bruno-failure-first.md) for stateful target checks and first-failure diagnosis. Pin the target and run only the explicit requests needed for the changed behavior. Do not run a full collection by default.
- Before writing/running a stateful Bruno scenario, the Mastermind checks fixture readiness read-only. If required data is missing, prepare the smallest deterministic fixture on the already-authorized disposable target, then read it back and verify the preconditions before the request. Use supported APIs or existing seeders; do not reuse a consumed booking/state for a test that needs the old state. No broad fixture framework or replay of onboarding. A new target, reset, migration, or unapproved persistent side effect still needs owner authorization.
- At the first unexpected Bruno failure, capture the actual request/response and relevant log, fix the evidenced cause, and rerun only that request with its prerequisites. After two unchanged attempts or ten minutes without new evidence, stop and report the specific limitation. Bruno being unavailable, lacking a fixture, or unsupported by a Development provider does not by itself block closeout when the required focused automated checks pass; record the gap and compensating evidence. Do not claim an unrun check passed.

- Mastermind owns scope, decisions, any needed sequential worker coordination, integration, the selected build/test checks, optional risk-based Bruno, final evidence, and handoff.
- For implementation changes: build once after the final code change and run focused tests for the affected behavior. Run the full suite for cross-cutting/high-risk changes or before a release, not automatically for every small slice. Run Bruno only when selected by the risk/contract or requested by the owner; it is not a default commit gate.
- Documentation/script-only changes use relevant documentation/script validation. Do not launch the API or full .NET suite merely for documentation relocation.
- Every dotnet build/test gate requires successful exit and ZERO warnings. Resolve failures/warnings; never use blanket suppression. If an unrelated/environmental warning cannot safely be resolved in scope, report the gate as not clean and block closeout.
- Reuse successful gates while their inputs remain valid; rerun only gates invalidated by later changes. Stop stalled or repeating commands. Record command, revision/scope, exit status, counts, skips, warnings, fixture changes, and limitations. Never invent execution results.
- Any stateful test data must use a verified disposable target and minimal fixture writes. Resets require exact-target verification and owner authorization. Never run stateful tests against production. Do not commit with a known code defect or failing required automated check; a Bruno check that was not selected or could not run is recorded as a limitation, not an automatic commit blocker.
- Define scope, permissions, observable acceptance criteria, and verification before implementation. Admin/CMS list/detail/history/inspection acceptance belongs to the same resource slice as its mutations.

## Models and runtime

The requested repository default is gpt-6-luna at medium reasoning for the mastermind and Sanad roles. Repository guidance cannot change a running session or a platform-pinned role runtime; the current Sanad role tools report gpt-5.6-luna as fixed and non-overridable. The owner can inspect the effective runtime with /status; reload/new sessions apply platform or persisted defaults unless overridden.

## Safety and release

- Never expose credentials or stage private handoffs, protected fixtures, or unrelated changes. New docs contain sanitized rules/evidence, not private environment details.
- Follow the owner's current standing branch/release instruction. Current standing instruction: work directly on `main`, commit completed task work, and push `main` so it remains aligned with `origin/main`; do not create or switch to a worktree for routine task work. This does not authorize production changes, destructive database resets, or deployment; those still require exact approval. Do not merge another branch unless separately requested.
- Legacy deletion needs a separate exact-list confirmation AFTER replacement verification. Blueprint/workflow approval is not deletion approval.
- Priority: Care homes → Chat → Notifications → full application UI reconciliation → remaining roadmap. Original phase IDs remain traceability metadata. Family and Elderly UI are supplied; Caregiver UI is outstanding. Do not ask for already supplied inventories or repeat completed audits.
- At goal completion, report and hand off. Do not silently begin a later product goal or restore the historical 16-item checklist.
