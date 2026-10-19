# Sanad API working instructions

Repository-wide rules referenced by root AGENTS.md. Simplified by Owner direction on 2026-10-19.

## Start and resume

- Agree the product behavior and scope with the Owner before a new slice; reuse already supplied requirements and approvals.
- Mastermind owns planning, contract lock, worker handoff, review, integration, release coordination. Roadmap inclusion is not implementation/release approval.
- Read Mastermind_Handoff.md, then only active slice and linked contracts. Inspect git status/diff first; preserve unrelated edits/private files. Resume at unfinished role.
- One active slice; default one worker. Overlap only after explicit checks and owner approval; otherwise stay sequential.
- Single active brief owns scope, acceptance, current result. Handoff owns project next action/backlog. Short standing rules only.
- Replace current checkpoint instead of appending diary. One fact in one place.
- Resolve decisions for active slice; future decisions stay future. Block only dependent tasks.
- Legacy handoffs/logs/checklists are historical; do not treat as active instructions.

## Phase lifecycle and document ownership

1. **Prepare:** Mastermind puts scope, acceptance, file ownership, checks and any
   genuinely needed sequential tasks in one brief. Resolve material product
   decisions with the Owner. No implementation before scope approval.
2. **Start:** after approval and Owner direction, pin current revision/local delta,
   use the selected model, dispatch one Worker and record its session in the brief.
   A task is not `in_progress` merely because its plan exists.
3. **Deliver:** Worker updates only its assigned task and current result in the
   brief, including changed files, commands/results, limitations and next action.
   On a stage handback, stop until Mastermind accepts and resumes the next stage.
4. **Review:** Mastermind checks the actual diff and relevant evidence. Accept the
   task or send concrete corrections to the same Worker. Recheck only affected
   evidence. After two unsuccessful correction rounds, take over valid remaining
   work when Owner-authorized; never overwrite a running Worker.
5. **Accept:** when all implementation tasks, consumer docs/Postman and required
   checks pass review, mark the phase `accepted`. This means locally accepted,
   not committed, deployed or completed. Present results for release approval.
6. **Finish:** after explicit release approval, perform only authorized operations
   and verify them. Mark the phase `completed` only when required release work is
   verified (or deployment is explicitly N/A for non-runtime work). Record release
   revision, verification and limitations once; link an operational receipt only
   when needed. Report and stop; the next product phase needs its own direction.

Task states: `pending` → `in_progress` → `ready_for_review` → `completed`.
Worker may set `blocked` with a concrete reason; resume to `in_progress` when
resolved. Corrections return a task to `in_progress`. Only Mastermind sets
`completed`. Completed implementation tasks do not mean the phase is deployed.
Phase state: `approved`, `in_progress`, `ready_for_review`, `accepted`, `completed`;
record a blocker separately so the last verified state remains clear. Release
state is separate: `not_requested`, `awaiting_approval`, `authorized`, `verified`
or `not_applicable` with a reason. Never infer authorization from a status value.
Mastermind owns phase/release states: set `in_progress` when implementation starts
and `ready_for_review` when the complete delivery is available. Worker updates only
assigned task states/results. Required release verification that fails keeps the
phase `accepted` with the failure recorded; it does not become `completed`.

- **Brief:** owns task status, scope and evidence. Update on start, block, handback,
  review or release; replace current results, retain concise valid task evidence.
- **Mastermind handoff:** Mastermind alone updates the active brief link, blocker
  and next action at a meaningful stop or phase change. Do not copy task tables,
  test logs or release history into it.
- **Consumer guides/Postman:** Worker updates affected API behavior with the feature.
  No separate documentation relay, tracker, per-task report or routine index sync.
- Existing approved contracts may remain scope references without a second status
  ledger. For future phases put approval/scope in the brief itself. Use the
  [E04 brief](../work/portal-api-reconciliation/worker-prompts/e04-platform-controls.md)
  as the worked example; reuse its structure, not its product scope or old evidence.

## Worker and verification

- For bounded approved phase, use one native OpenCode Worker owning implementation, focused tests, feature docs/Postman. Mastermind reviews once.
- Brief pins revision, objective, owned files, acceptance, dependencies, output. Preserve others' edits.
- Phase sequence: contract approval, saved brief, short kickoff, dispatch only on owner direction. Do not duplicate full prompt in chat.
- One native child Worker per phase; record sessionID and pinned revision. Do not poll or replace while running. Resume same session where possible.
- Worker may move task to ready_for_review, never completed. Mastermind completes after validation.
- Implementer changes only approved behavior/owned files; reports ambiguity instead of inventing policy.
- Tests focused on changed behavior/regressions. Bruno optional and risk-based. Distinguish authored vs executed checks.
- Reviewer one bounded read-only review with concrete findings.
- Docs/Postman update with feature change; match verified behavior.
- Mastermind owns scope, decisions, integration, selected checks, final evidence, handoff. Build once after final code change; focused tests. Full suite only cross-cutting/high-risk or contract-required. Bruno only when selected/requested.
- Every dotnet build/test gate: successful exit and zero warnings. Reuse valid gates; rerun only invalidated checks. Never invent results.
- Stateful tests use verified disposable target and minimal writes. Never production. Resets need exact-target verification and owner authorization.

## Models and runtime

- Owner selects Mastermind model and reasoning effort. Worker profile default is free and never inherits Mastermind model.
- Use Owner-selected provider/model/effort; ask only when not already selected. Verify effective selection; never silently switch.
- Do not auto-switch providers; rate-limit/quota errors require owner decision.

## Additional standing rules

- Mastermind sends bounded corrections to the same worker; do not restart whole sequence.
- Docs-only changes use docs/script validation; no product build/tests required.
- No repeated intake of already supplied requirements.
- Protected production/new persistent targets/migration operations require explicit authority.
- No mandatory docs-only push before implementation.

## Safety and release

- Never expose credentials or stage private handoffs, protected fixtures, unrelated changes.
- Work directly on main; no worktree for routine task work.
- Contract approval is not release approval. Cleanup approval is not release approval. Commit/push/deploy only after explicit owner authorization, then verify and record receipt.
- Legacy deletion needs separate exact-list confirmation after replacement verification.
- Priority: Care homes + Chat + Notifications + full application UI reconciliation + remaining roadmap.
- At goal completion, report and hand off. Do not silently begin later product goal.
