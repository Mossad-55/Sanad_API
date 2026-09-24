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
- Mandatory slice order: owner authorization/audit -> scout maps current state and manifest -> implementer changes one bounded slice -> test author works after the implementation revision is pinned -> authorized owner local migration creation/verification against the exact local database target when required -> mastermind runs focused tests, full build, and full test suite -> if a defect is found, re-engage the implementer with a new pinned correction brief and repeat affected validation -> preliminary independent reviewer review -> documenter synchronizes all affected docs, README references, and Postman collections -> mechanical controller-route/Postman check in both directions and contract-completeness reconciliation -> final reviewer sign-off -> mastermind performs final validation and the applicable Bruno gate -> owner performs authorized commit, push, and publication/merge actions -> after the owner-led UI walkthrough, owner deploys the verified revision -> after deployment, owner applies/verifies the migration against the exact production database target and runs safe smoke checks -> final handoff. Do not skip scout, either reviewer stage, or documenter because a slice appears small.

## Mastermind phase checklist and worker-progress reporting (mandatory)

- Before starting every phase or bounded slice, the mastermind must publish this complete ordered todo checklist. Each item names its owner, exact deliverable, dependency, and status (`pending`, `running`, `complete`, `blocked`, or `owner action`):
  1. Owner authorization and mastermind audit.
  2. `sanad_scout` maps current state and exact manifest.
  3. `sanad_implementer` completes the bounded change.
  4. `sanad_test_author` completes tests against the pinned implementation.
  5. Owner creates/verifies any required local migration against the exact verified local database target; this prerequisite must finish before validation and Bruno.
  6. Mastermind runs focused tests, full build, and full suite.
  7. If defects are found, implementer correction and affected validation gates.
  8. Preliminary reviewer correctness/security/compatibility review.
  9. Documenter completes all affected docs, README, and Postman synchronization.
  10. Mastermind runs the mechanical controller-route/Postman check in both directions and reconciles contract-completeness findings after synchronization.
  11. Final reviewer sign-off, including contract-completeness counts.
  12. Mastermind runs final validation and the applicable Bruno gate.
  13. Owner performs authorized commit, push, and publication/merge actions.
  14. After the owner-led UI walkthrough, owner deploys the verified revision.
  15. After deployment, owner applies/verifies migrations against the exact production database target and runs safe smoke checks.
  16. Mastermind completes final handoff.
- Before spawning any worker, the mastermind must report to the owner what that worker is doing now, the exact bounded output expected, the files or read-only scope it owns, the acceptance check, and which checklist item it changes. The owner must not be left waiting without knowing the active worker and immediate next milestone.
- After every worker spawn, delivery, interruption, blocker, correction, and gate result, the mastermind must update the phase checklist and immediately report the changed status, plain-language outcome, validation evidence, findings, blockers, and todo impact to the owner before starting another worker or gate.
- A worker may not be silently replaced, skipped, or allowed to run indefinitely. If it fails to deliver its report or reaches a blocker, the mastermind records the unfulfilled checks, stops dependent work, and reports the recovery action to the owner.
- The checklist is recorded in the current phase workspace artifact and summarized in the private `CURRENT HANDOFF`; it is not a third control authority. The checklist must never be hidden in worker prompts alone.

## Worker contract

### Permanent mastermind model routing and context quarantine

These are permanent repository workflow rules, not session-only preferences. Route workers as follows: `sanad_scout` -> `gpt-6-luna`; `sanad_implementer` -> `gpt-6-sol`; `sanad_reviewer` -> `gpt-6-sol`; `sanad_test_author` -> `gpt-6-luna`; and `sanad_documenter` -> `gpt-6-luna`. If the implementer has two consecutive compile/test failures, assign the correction to `gpt-5.6-sol`, then return subsequent implementer work to `gpt-6-sol`.

Do not pass workers raw repository files or full conversation histories. Give them only the immediate paths and discrete snippets needed for their bounded task. After each completed asset, flush conversational context before starting the next worker. Require concise, structural worker reports.

Every worker brief must include the pinned base SHA, exact allowed file manifest, numbered task ladder, acceptance checks, exclusions, stop conditions, and final-report fields. Workers never merge, push, deploy, create migrations, or edit `Sanad_Master_Context.md`, `Sanad_Operations.md`, or other control files.

The final report must list changed files, validation commands and results, assumptions, blockers, and unfulfilled checks. A worker must stop when the pinned scope or acceptance condition cannot be met.
- Every worker final report must also include: a plain-language explanation of what happened, evidence of the worker's exact scope and outcome, the current phase todo items affected, and one recommended next action. After every worker finishes, the mastermind must immediately report that explanation, validation result, findings, blockers, and todo impact to the owner before continuing.

## Repository rules

- Preserve unrelated user changes. Inspect status before editing.
- Use `apply_patch` for source and documentation edits.
- Treat `Sanad_Master_Context.md` and `Sanad_Operations.md` as private, untracked control files; never stage them.
- Read the relevant product rules and affected public docs before changing behavior.
- Documentation and collection synchronization is mandatory for every business-rule or API change: update every affected `docs/` page, the relevant Postman collection(s) under `docs/postman/`, and `README.md` when the implemented HTTP surface, setup, roadmap status, or workflow changes. A slice is not ready for commit, push, deployment, or handoff until these artifacts reflect the final behavior and examples.
- Run focused tests first, then the smallest relevant build/API gate. Record exact results.
- Run the applicable Bruno gate after implementation and build/test validation, and after docs/Postman synchronization, the mechanical route check, and final reviewer sign-off. Complete any required owner-authorized local migration creation/verification against the exact local target before focused tests/build and Bruno so migration-dependent gates are executable. For endpoint or contract changes, record exact requests/assertions and exit code; if the gate cannot run, record the blocker and precise next-phase task.
- Database resets, migrations, remote writes, commits, pushes, and deployments require owner authorization and exact target verification.
- T0 Bruno is the idempotent standard gate. Lifecycle Bruno tiers are one-shot and require a fresh local seed before rerun.
- Validation and deployment order: when required, owner-authorized local migration creation/verification against the exact verified local database target precedes focused tests/build and Bruno. Complete docs/Postman synchronization, the mechanical controller-route/Postman check, final reviewer sign-off, and the applicable Bruno gate in that order. VPS deployment and production migration application/verification remain after the owner-led UI walkthrough; the owner deploys the verified revision, then applies/verifies migrations against the exact production database target, then runs safe smoke checks.
- Stage validated changes by respected logical commit scope. Never stage the private control files, never start a new phase while required current-phase todo work is open or explicitly owner-deferred, and never commit/push/deploy without owner authorization.

## Contract-completeness gate (mandatory)

- Every API slice must maintain a requirements-to-contract matrix before implementation begins. Each requirement row must identify the owner role, route(s), authorization policy, source handler, persistence/data source, focused tests, Bruno coverage, public documentation page, Postman collection/request, and status.
- A route census is necessary but not sufficient: the matrix must also prove that each admin-managed resource has safe list/detail visibility in addition to create/update/delete and active-state transitions. “Create/activate/deactivate” without a way to read the resulting state is an incomplete contract.
- Before a phase can be marked complete, run a mechanical controller-route-to-Postman check in both directions, then reconcile intentional exceptions explicitly. An endpoint may not be silently excluded because it is a webhook, manual request, or mutation.
- The reviewer must provide explicit counts for missing requirements, missing source routes, missing tests, missing docs, missing Postman requests, and missing Bruno coverage. “Changed endpoints are documented” is not an acceptable completeness verdict.
- Any missing row blocks commit, push, deployment, and handoff until corrected or explicitly owner-deferred in the current checklist.

## Closeout

Each completed slice updates affected `docs/` pages, the private operations ledger, and the current next action. Do not claim a release or deployment from a worker result alone.
