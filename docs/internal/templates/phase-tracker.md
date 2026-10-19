# <Phase ID> — phase tracker and handoff

This file is the single status source for this approved phase. Copy it into the
phase directory only after the Owner approves that phase. Contracts define
scope; reports and release receipts preserve evidence. Do not duplicate their
history here.

## Phase checkpoint

- **Phase / contract:** <ID and link>
- **Owner approval:** <date/link>
- **Phase status:** pending | in_progress | blocked | awaiting_owner | completed
- **Active task:** <stable task ID, owner, exact next action>
- **Last verified checkpoint:** <timestamp/timezone; task ID; worker/session or unavailable>
- **Base / current revision:** <SHA> / <SHA or unknown>
- **Changed files:** <verified list, or not yet verified>
- **Checks:** <command — PASS/FAIL/NOT RUN, result or reason>
- **Blockers / owner gates:** <none or exact decision awaited>

## Tasks

Statuses: `pending` → `in_progress` → `blocked` or `ready_for_review` →
`completed`. Only the Mastermind marks a task completed, after verifying its
acceptance criteria and required evidence. Record unrun checks as `NOT RUN`;
never imply they passed. The next Worker task is the earliest pending Worker
task whose dependencies are all completed.

| ID | Task | Owner | Depends on | Acceptance criteria | Status | Evidence |
|---|---|---|---|---|---|---|
| <ID-M01> | Prepare and lock phase contract | Mastermind | — | Scope, exclusions, owners, files, acceptance and checks are explicit | pending | <link> |
| <ID-O01> | [OWNER GATE] Approve exact phase contract | Owner | <ID-M01> | Explicit approval recorded | pending | <link> |
| <ID-M02> | Break down tasks, assign Worker, pin base and brief | Mastermind | <ID-O01> | Task owners, dependencies, file boundary and kickoff are recorded | pending | <link> |
| <ID-W01> | <First bounded implementation task> | Worker | <ID-M02> | <Observable criteria and relevant checks> | pending | — |
| <ID-W02> | <Remaining bounded implementation task> | Worker | <ID-W01> | <Observable criteria and relevant checks> | pending | — |
| <ID-M03> | Review delivery and resolve findings | Mastermind | <ID-W01>, <ID-W02> | Actual diff/evidence reviewed; bounded corrections recorded | pending | <link> |
| <ID-M04> | Integrate and run required verification | Mastermind | <ID-M03> | Contract-required checks recorded with actual results and limitations | pending | <link> |
| <ID-M05> | Release preflight and present evidence | Mastermind | <ID-M04> | Exact revision, target, migration/backup/config preflight and release plan recorded | pending | <link> |
| <ID-O02> | [OWNER GATE] Authorize applicable commit/push/release/deployment | Owner | <ID-M05> | Separate explicit decision recorded; tests are not authorization | pending | <link> |
| <ID-M06> | [OWNER GATE] Release only if authorized; verify and record receipt | Mastermind | <ID-O02> | Authorized operation verified; receipt linked; otherwise remains waiting | pending | <link> |

Mark inapplicable release/deployment tasks `completed` only with the reason
`N/A — <why no release delta/operation applies>`. A deployment task must remain
owner-gated until explicit authorization is recorded.

## Checkpoint log

Append a checkpoint before work, after each task, when blocked, and at handback.
Each entry records timestamp/timezone, task ID, Worker/session if available,
base/current revision, changed files, each check and `PASS`/`FAIL`/`NOT RUN`,
blockers, and the next exact action. A checkpoint is **verified** only after the
Mastermind compares it with available revision/worktree evidence; it says
nothing about unrecorded later work.

| Timestamp | Task / worker-session | Base / current revision | Changed files | Checks and states | Blockers | Next exact action | Mastermind verification |
|---|---|---|---|---|---|---|---|
| <ISO timestamp + timezone> | <task / ID or unavailable> | <base / current> | <verified list or unverified> | <command — PASS/FAIL/NOT RUN> | <none or exact blocker> | <one exact action> | <verified / pending, by whom> |

## Compact handoff

Phase: <ID> | Status: <status> | Active task: <ID, owner, status>
Tracker: <this file> | Contract: <link>
Last verified checkpoint: <timestamp/timezone> | Worker/session: <ID or unavailable>
Base/current: <SHA>/<SHA or unknown> | Changed files: <verified list or pending>
Checks: <PASS/FAIL/NOT RUN with result/reason> | Blockers/owner gates: <exact state>
Next exact action: <one action and owner>
