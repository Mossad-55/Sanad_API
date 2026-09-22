# Sanad API Codex workflow

## Session-start checkpoint (mandatory)

At the start of every new mastermind session, do not issue a worker brief or rerun a gate immediately. First reconcile the live workspace with the `CURRENT HANDOFF` block at the top of `Sanad_Operations.md`:

1. Read `AGENTS.md`, `Sanad_Master_Context.md`, `Sanad_Operations.md`, and the affected public docs.
2. Verify branch, `HEAD`, `origin/main`, working-tree status, and running API processes.
3. Treat only the latest `CURRENT HANDOFF` block and live verification as active; older ledger entries are history.
4. Report a compact status: completed, pending, blocked, and exactly one next action.
5. Do not rerun a completed Bruno gate, migration, or worker task unless the handoff explicitly marks it stale or the live audit disproves its result.

When a session ends or a bounded slice closes, update the `CURRENT HANDOFF` block with the verified SHA, exact gate results, cleanup state, and one next action. This block is the handoff contract between sessions.

## Authority and roles

- The owner is the final authority for product decisions, credentials, migrations, commits, pushes, merges, deployments, and production data.
- The mastermind coordinates the work: inspect the current state, create bounded briefs, pin revisions, review worker output, run proportionate local validation, and maintain the private operations ledger.
- Workers have one explicit role and one bounded task. Use `sanad_scout` for read-only mapping, `sanad_reviewer` for read-only correctness/security/test review, `sanad_implementer` for a single feature writer, `sanad_test_author` for tests after the implementation revision is pinned, and `sanad_documenter` for a cross-cutting documentation/README/Postman synchronization audit.
- Mandatory slice order: scout current state and manifest -> implementer changes one bounded slice -> mastermind runs focused tests, then the full build and full test suite -> if the mastermind finds a defect, re-engage the implementer with a new pinned correction brief and repeat the gates -> reviewer performs the independent correctness/security/compatibility review -> documenter synchronizes every affected docs page, README reference, and Postman collection -> mastermind performs final validation and updates the handoff. Do not skip scout, reviewer, or documenter because a slice appears small.

## Worker contract

Every worker brief must include the pinned base SHA, exact allowed file manifest, numbered task ladder, acceptance checks, exclusions, stop conditions, and final-report fields. Workers do not merge, push, deploy, create migrations, or edit `Sanad_Master_Context.md` or `Sanad_Operations.md`.

The final report must list changed files, validation commands and results, assumptions, blockers, and unfulfilled checks. A worker must stop when the pinned scope or acceptance condition cannot be met.
- Every worker final report must also include: a plain-language explanation of what happened, evidence of the worker's exact scope and outcome, the current phase todo items affected, and one recommended next action. After every worker finishes, the mastermind must immediately report that explanation, validation result, findings, blockers, and todo impact to the owner before continuing.

## Repository rules

- Preserve unrelated user changes. Inspect status before editing.
- Use `apply_patch` for source and documentation edits.
- Treat `Sanad_Master_Context.md` and `Sanad_Operations.md` as private, untracked control files; never stage them.
- Read the relevant product rules and affected public docs before changing behavior.
- Documentation and collection synchronization is mandatory for every business-rule or API change: update every affected `docs/` page, the relevant Postman collection(s) under `docs/postman/`, and `README.md` when the implemented HTTP surface, setup, roadmap status, or workflow changes. A slice is not ready for commit, push, deployment, or handoff until these artifacts reflect the final behavior and examples.
- Run focused tests first, then the smallest relevant build/API gate. Record exact results.
- Run the applicable Bruno gate after implementation and build/test validation. For endpoint or contract changes, the gate result must be recorded as exact requests/assertions and exit code; if it cannot run, record the blocker and the precise next-phase task. A slice is not fully closed until its Bruno status is documented.
- Database resets, migrations, remote writes, commits, pushes, and deployments require owner authorization and exact target verification.
- T0 Bruno is the idempotent standard gate. Lifecycle Bruno tiers are one-shot and require a fresh local seed before rerun.
- Validation and deployment order: complete the applicable local Bruno gate and record its exact evidence before closing the current phase. VPS deployment is intentionally deferred until the owner completes the UI walkthrough; then remind the owner to deploy the verified revision, apply/verify the migration, and run safe smoke checks.
- Stage validated changes by respected logical commit scope. Never stage the private control files, never start a new phase while required current-phase todo work is open or explicitly owner-deferred, and never commit/push/deploy without owner authorization.

## Closeout

Each completed slice updates affected `docs/` pages, the private operations ledger, and the current next action. Do not claim a release or deployment from a worker result alone.
