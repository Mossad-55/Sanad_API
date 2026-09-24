# Codex local workflow

The active instructions are in `AGENTS.md`. The mastermind coordinates one sequential pass through the five bounded roles for implementation goals: scout, implementer, test author, reviewer, and documenter. A role without applicable work is marked inapplicable briefly. The old 16-step release checklist is historical and is not an execution plan.

## Resuming interrupted work

1. Stop the previous CLI turn, inspect `git status` and `git diff`, and preserve unfinished edits.
2. Start a new session. State the unfinished objective and ask the mastermind to inspect only relevant files and resume at the unfinished role.
3. Complete the remaining task and run targeted validation. Reuse valid prior gate results where no relevant file has changed.

## Model and cost controls

The persisted project default is `gpt-5.6-luna` at `medium` reasoning effort for the mastermind and all five Sanad workers (scout, implementer, test author, reviewer, and documenter); one subagent runs at a time. A persisted project default does not change the model already selected by a running session. Verify the active runtime model and reasoning effort with `/status`; the persisted default takes effect when the session is reloaded or a new session starts. Explicit CLI settings may override the persisted default.

Avoid long-running autonomous loops, duplicate role passes, and repeated full-suite validation. Check the five-hour and weekly balances in Settings > Usage. Those percentages represent shared Work/Codex allowance, not a direct token counter.

## Release checks

For an API contract change, synchronize affected docs and Postman requests and run the relevant tests. Add a targeted Bruno gate if integration behavior requires it. For schema or production changes, verify the exact database and seek owner authorization. Choose wider review and validation according to the specific risk; do not schedule every worker or gate automatically.
