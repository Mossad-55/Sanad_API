# Sanad API Codex workflow

## Authority and roles

- The owner is the final authority for product decisions, credentials, migrations, commits, pushes, merges, deployments, and production data.
- The mastermind coordinates the work: inspect the current state, create bounded briefs, pin revisions, review worker output, run proportionate local validation, and maintain the private operations ledger.
- Workers have one explicit role and one bounded task. Use `sanad_scout` for read-only mapping, `sanad_reviewer` for read-only correctness/security/test review, `sanad_implementer` for a single feature writer, and `sanad_test_author` for tests after the implementation revision is pinned.

## Worker contract

Every worker brief must include the pinned base SHA, exact allowed file manifest, numbered task ladder, acceptance checks, exclusions, stop conditions, and final-report fields. Workers do not merge, push, deploy, create migrations, or edit `Sanad_Master_Context.md` or `Sanad_Operations.md`.

The final report must list changed files, validation commands and results, assumptions, blockers, and unfulfilled checks. A worker must stop when the pinned scope or acceptance condition cannot be met.

## Repository rules

- Preserve unrelated user changes. Inspect status before editing.
- Use `apply_patch` for source and documentation edits.
- Treat `Sanad_Master_Context.md` and `Sanad_Operations.md` as private, untracked control files; never stage them.
- Read the relevant product rules and affected public docs before changing behavior.
- Run focused tests first, then the smallest relevant build/API gate. Record exact results.
- Database resets, migrations, remote writes, commits, pushes, and deployments require owner authorization and exact target verification.
- T0 Bruno is the idempotent standard gate. Lifecycle Bruno tiers are one-shot and require a fresh local seed before rerun.

## Closeout

Each completed slice updates affected `docs/` pages, the private operations ledger, and the current next action. Do not claim a release or deployment from a worker result alone.
