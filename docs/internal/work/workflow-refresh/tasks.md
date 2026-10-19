# Workflow documentation refresh — task tracker

This is the canonical checklist for the Owner-approved workflow/documentation
cleanup task. It is not a product phase and does not authorize E04 or any other
roadmap work. Checkpoint claims below identify only the last recorded state.

## Current checkpoint

- **Status:** completed locally; changes remain uncommitted.
- **Active task:** none. The cleanup is complete; owner-gated commit/push is still pending.
- **Worker/session:** Mastermind direct work; no Worker dispatched.
- **Base/current revision:** `a16094b5786b93483635c488f808c3dcf5cebf4e` / `a16094b5786b93483635c488f808c3dcf5cebf4e` at task start.
- **Changed files:** `.opencode/agents/sanad-worker.md`, `.opencode/skills/sanad-docs/SKILL.md`, `docs/internal/Mastermind_Handoff.md`, `docs/internal/Project_Main_Goal.md`, `docs/internal/governance/AGENTS.md`, `docs/internal/operations/codex-workflow.md`, `docs/internal/templates/phase-tracker.md`, `docs/internal/work/portal-api-reconciliation/plan.md`, `docs/internal/work/workflow-refresh/tasks.md`.
- **Checks:** status/relevant diffs, workflow audit, E03 receipt, and OpenCode V2 skills guide inspected — PASS; relative Markdown links in 8 changed docs — PASS; `git diff --check` — PASS (pre-existing LF/CRLF notices only); whitespace scan of 3 new docs — PASS. Product build/test suite/Bruno — NOT RUN (documentation-only task).
- **Blockers:** no blocker to publishing the isolated cleanup files. Pre-existing overlapping and unrelated edits remain excluded from staging. Ongoing cross-machine task-state policy beyond this pushed cleanup remains undecided.
- **Next exact action:** finish staging only cleanup-owned files/hunks, inspect the staged diff, then commit and push this bounded documentation change.

## Tasks

| ID | Task | Owner | Depends on | Acceptance criteria | Status | Evidence |
|---|---|---|---|---|---|---|
| WF-M01 | Reconcile audit claims against current handoff, plan, and E03 receipt | Mastermind | — | E03 release truth and pending roadmap phases identified; stale claims separated from history | completed | E03 receipt and current handoff reviewed |
| WF-M02 | Update compact operational handoff and roadmap/index wording | Mastermind | WF-M01 | Current operational guidance is short; E04–E08 remain visible as not started/uncontracted; no product phase implied | completed | Mastermind handoff and project/portal index reviewed; E03 receipt retained |
| WF-M03 | Add one reusable phase tracker/handoff template and safe stop/resume rules | Mastermind | WF-M01 | Stable IDs, owner, criteria, dependencies, lifecycle, Mastermind tasks, owner gates, checkpoint fields, exact next action included | completed | [Phase tracker template](../../templates/phase-tracker.md); governance/workflow rules synchronized |
| WF-M04 | Add concise reusable Sanad documentation skill | Mastermind | WF-M01 | App/Admin/Care Homes audience clarity and verified-contract rules; no duplicate mandatory review workload | completed | [Sanad docs skill](../../../../.opencode/skills/sanad-docs/SKILL.md); optional-only usage documented |
| WF-M05 | Validate changed documentation and report checkpoint | Mastermind | WF-M02, WF-M03, WF-M04 | Relative links and diff checked; unrun checks stated; unrelated changes preserved | completed | Relative links resolve in 8 docs; whitespace scan clean; `git diff --check` exit 0 with only line-ending notices; product build/tests/Bruno NOT RUN for docs-only scope |
| WF-O01 | [OWNER GATE] Authorize commit/push, if desired | Owner | WF-M05 | Separate explicit decision recorded | completed | Owner explicitly authorized commit/push of cleanup; pre-existing edits excluded |
| WF-M06 | Commit and push the isolated workflow cleanup | Mastermind | WF-O01 | Only cleanup-owned changes are committed/pushed; unrelated work remains untouched | in_progress | Inspect staged diff before release |

## Checkpoint log

| Timestamp | Task / worker-session | Base / current revision | Changed files | Checks and states | Blockers | Next exact action | Mastermind verification |
|---|---|---|---|---|---|---|---|
| 2026-10-19T05:28:17.7154133+03:00 | WF-M01 / Mastermind direct | `a16094b5786b93483635c488f808c3dcf5cebf4e` / same | None by this task at checkpoint start | status/diff inspection PASS; audit/receipt reconciliation PASS; OpenCode V2 skills guide PASS | Cross-machine sync policy undecided | Apply approved bounded docs updates; then verify worktree and links | Verified by Mastermind from read-only repository inspection |
| 2026-10-19T05:36:41.9255980+03:00 | WF-M05 / Mastermind direct | `a16094b5786b93483635c488f808c3dcf5cebf4e` / same | `.opencode/agents/sanad-worker.md`; `.opencode/skills/sanad-docs/SKILL.md`; `docs/internal/Mastermind_Handoff.md`; `docs/internal/Project_Main_Goal.md`; `docs/internal/governance/AGENTS.md`; `docs/internal/operations/codex-workflow.md`; `docs/internal/templates/phase-tracker.md`; `docs/internal/work/portal-api-reconciliation/plan.md`; `docs/internal/work/workflow-refresh/tasks.md` | Relative Markdown links: PASS; `git diff --check`: PASS (line-ending notices); new-doc whitespace scan: PASS; build/tests/Bruno: NOT RUN, docs-only | Cross-machine sync policy undecided; commit/push not authorized | Review this checkpoint and report; do not commit/push | Diff/status and link/whitespace results checked by Mastermind |
| 2026-10-19T05:39:15.9754086+03:00 | WF-M05 / Mastermind direct | `a16094b5786b93483635c488f808c3dcf5cebf4e` / same | `.opencode/agents/sanad-worker.md`; `.opencode/skills/sanad-docs/SKILL.md`; `docs/internal/Mastermind_Handoff.md`; `docs/internal/Project_Main_Goal.md`; `docs/internal/governance/AGENTS.md`; `docs/internal/operations/codex-workflow.md`; `docs/internal/templates/phase-tracker.md`; `docs/internal/work/portal-api-reconciliation/plan.md`; `docs/internal/work/workflow-refresh/tasks.md` | Relative Markdown links across 8 docs: PASS; whitespace scan: PASS; `git diff --check`: exit 0 (LF/CRLF notices); build/tests/Bruno: NOT RUN, docs-only task | Cross-machine sync policy undecided; commit/push not authorized | Owner reviews local changes and decides sync requirement and whether to authorize commit/push | Verified by Mastermind against worktree/status and final link/whitespace checks |
| 2026-10-19T05:46:58.6621362+03:00 | WF-O01 / Owner direction received | `a16094b5786b93483635c488f808c3dcf5cebf4e` / same | No file changes at this checkpoint | Owner selected pause for commit/push; E04 option brief requested | Pre-existing overlapping/untracked work makes safe staging unresolved; task-state sync policy undecided | Await safe commit-scope direction; collect E04 current-app input and policy choices | Owner response recorded; no commit/push performed |
| 2026-10-19T05:48:20.7907322+03:00 | WF-O01 / Mastermind direct | `a16094b5786b93483635c488f808c3dcf5cebf4e` / same | `docs/internal/Mastermind_Handoff.md`; `docs/internal/work/workflow-refresh/tasks.md` | Relative links and whitespace in both files: PASS; `git diff --check`: exit 0 (existing line-ending notices) | Commit/push paused by Owner; E04 current-app/policy intake pending | Wait for Owner's E04 details and safe commit-scope direction | Verified by Mastermind against current files/check output |
| 2026-10-19T05:53:52.2994293+03:00 | WF-M06 / Mastermind direct | `a16094b5786b93483635c488f808c3dcf5cebf4e` / same | Staged scope: five cleanup files plus selected roadmap pointer hunks; exact stage to verify before commit | Owner authorization: PASS; staging excludes pre-existing diffs and untracked runtime/prompt/audit artifacts; staged-diff review: pending | None for bounded cleanup commit; broader E04 intake and future sync policy unresolved | Review exact cached diff; commit and push if scoped correctly | Pending Mastermind verification |

No commit, push, installation, runtime operation, product implementation, or
deployment is part of this cleanup task. E04–E08 remain separately unapproved.
