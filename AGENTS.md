# Sanad API Codex instructions

## Mastermind and workers

- The main Codex agent is the mastermind. It owns scope, decisions, worker briefs, integration, verification, and the final report. It must not restart a completed task just to fill a checklist.
- For an authorized implementation goal, run one sequential pass: `sanad_scout` maps relevant files -> `sanad_implementer` changes the bounded code -> `sanad_test_author` adds focused tests -> `sanad_reviewer` reviews the result -> `sanad_documenter` updates affected public docs and Postman requests. Then the mastermind resolves concrete findings and reports the outcome. If a role has no relevant work (for example, test author on a docs-only change), record that in one line and proceed. Do not run two review rounds or spawn duplicate workers.
- Inspect `git status` and the relevant diff first; preserve unrelated changes. Give each worker the immediate objective and file scope, not full repository files or conversation history. Keep reports short and structural. Run only one worker at a time.
- The mastermind runs focused validation for changed behavior. Run a full build, full suite, route census, or Bruno only when relevant to the actual change or explicitly requested. Never repeat a successful gate unless a later change invalidated its result. Stop stalled or repeating commands.
- Read the private `Sanad_Operations.md` handoff once if present; do not reconstruct missing private files or reread the full history at every step. Avoid publishing or maintaining a 16-item phase checklist. Give the owner short milestone updates and one final report.

## Model and runtime defaults

- The persisted project default is `gpt-5.6-luna` at `medium` reasoning effort. The mastermind and all five Sanad workers (scout, implementer, test author, reviewer, and documenter) use this default.
- A persisted default does not change the model already selected by a running session. Check the active runtime model and reasoning effort with `/status`; the persisted default takes effect when the session is reloaded or a new session starts.

## Safety and release actions

- Do not expose credentials or stage private `Sanad_Master_Context.md` / `Sanad_Operations.md` files.
- Check the exact target before migrations, database resets, or deployment. Obtain owner authorization for these and for commits, pushes, merges, remote writes, and production data changes.
- For a substantial API release, propose extra review, contract mapping, migration, build, test, and Bruno gates based on actual risks. Do not silently add the former 16-step release process.
- The historical detailed process in `docs/operations/current-phase-todo.md` is a record, not a standing instruction for new tasks.
