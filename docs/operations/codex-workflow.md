# Codex local workspace workflow

This repository uses an owner/mastermind workflow with specialized, bounded worker roles for local API development:

| Role | Responsibility | Authority and restrictions |
|---|---|---|
| Owner | Final authority for product decisions, credentials, migrations, commits, pushes, publication/merge, deployments, and production data | — |
| Mastermind | Current-state audit, task decomposition, worker briefs, review, local validation, ledger and documentation coordination | — |
| Worker | One bounded scout, review, implementation, test, or documentation task from a pinned revision | Never merge, push, deploy, create migrations, or edit `Sanad_Master_Context.md`, `Sanad_Operations.md`, or other control files |

## Local setup

Codex loads repository instructions from `AGENTS.md`. Project-scoped custom agents live in `.codex/agents/`:

- `sanad_scout`: read-only evidence and file mapping
- `sanad_reviewer`: read-only correctness, security, compatibility, and test review
- `sanad_implementer`: one bounded production implementation
- `sanad_test_author`: independent tests after the implementation SHA is pinned
- `sanad_documenter`: pinned documentation, README, and Postman synchronization audit

The private control files `Sanad_Master_Context.md` and `Sanad_Operations.md` remain untracked at the workspace root. They are coordination records, not product documentation and must never be staged.

## New-session handoff

The latest `CURRENT HANDOFF` block at the top of `Sanad_Operations.md` is the active checkpoint. A new mastermind must reconcile it with the live branch before doing work. The first response must state the verified `HEAD`, remote alignment, dirty/untracked state, completed gates, pending work, and one next action. Historical entries are evidence, not instructions. Completed Bruno gates, migrations, and worker tasks must not be repeated unless the live audit shows that their recorded result is invalid or the handoff explicitly requests a rerun.

## Standard slice lifecycle

1. The owner authorizes the objective and any state-changing authority, and the mastermind audits the current branch, SHA, status, product rules, affected code, tests, and docs.
2. `sanad_scout` maps current state and the exact file manifest.
3. `sanad_implementer` makes one bounded change from the pinned revision.
4. `sanad_test_author` adds tests after the implementation revision is stable and pinned.
5. When required, the owner creates/verifies the local migration against the exact verified local database target. This must happen before any migration-dependent focused test, build, or Bruno gate.
6. The mastermind runs focused tests, the full build, and the full test suite.
7. If the mastermind finds a defect, re-engage the implementer with a new pinned correction brief and repeat the affected validation gates.
8. A preliminary `sanad_reviewer` performs an independent correctness, security, compatibility, and test review.
9. `sanad_documenter` completes all affected documentation, README, and Postman synchronization, including examples, variables, descriptions, and response assertions.
10. After Postman synchronization, the mastermind runs the mechanical controller-route/Postman check in both directions and reconciles the requirements-to-contract matrix and all completeness findings.
11. A final `sanad_reviewer` provides sign-off after the mechanical check and contract-completeness reconciliation.
12. The mastermind performs final validation and the applicable Bruno gate, recording exact requests, assertions, and exit code.
13. The owner performs authorized commit, push, and publication/merge actions.
14. After the owner-led UI walkthrough, the owner deploys the verified revision.
15. After deployment, the owner applies/verifies migrations against the exact verified production database target, then runs safe smoke checks.
16. The mastermind completes the final handoff with verified SHA, gate results, cleanup state, and one next action.

### Permanent mastermind model routing and context quarantine

These are permanent repository workflow rules, not session-only preferences. Route workers as follows: `sanad_scout` -> `gpt-6-luna`; `sanad_implementer` -> `gpt-6-sol`; `sanad_reviewer` -> `gpt-6-sol`; `sanad_test_author` -> `gpt-6-luna`; and `sanad_documenter` -> `gpt-6-luna`. If the implementer has two consecutive compile/test failures, assign the correction to `gpt-5.6-sol`, then return subsequent implementer work to `gpt-6-sol`.

Do not pass workers raw repository files or full conversation histories. Give them only the immediate paths and discrete snippets needed for their bounded task. After each completed asset, flush conversational context before starting the next worker. Require concise, structural worker reports.

Before each phase, the mastermind must publish the numbered lifecycle checklist above as a complete ordered todo checklist. Every item names its owner, exact deliverable, dependency, and status. Before spawning a worker, the mastermind reports to the owner what the worker is doing now, its exact bounded output, owned files or read-only scope, acceptance check, and checklist item. After every spawn, worker report, interruption, blocker, correction, and gate result, the mastermind updates the checklist and immediately reports the plain-language outcome, evidence, blockers, and todo impact before starting the next worker or gate. A worker may not be silently skipped or replaced; missing reports and unfulfilled checks pause dependent work and are recorded.

The required execution order is: owner authorization and mastermind audit -> `sanad_scout` maps current state and exact manifest -> `sanad_implementer` makes one bounded change -> `sanad_test_author` adds tests against the pinned implementation -> owner creates/verifies any required local migration against the exact verified local database target -> mastermind runs focused tests, full build, and full suite -> implementer correction and affected validation gates if a defect is found -> preliminary reviewer review -> documenter completes all docs/README/Postman synchronization -> mastermind runs the mechanical controller-route/Postman check in both directions and reconciles contract completeness after synchronization -> final reviewer sign-off -> mastermind runs final validation and Bruno -> owner performs authorized commit, push, and publication/merge -> after the owner-led UI walkthrough, owner deploys the verified revision -> after deployment, owner applies/verifies the migration against the exact verified production database target and runs safe smoke checks -> mastermind completes final handoff. The implementer correction round is mandatory whenever a defect is found and cannot be silently patched around by the mastermind. Local migration verification precedes migration-dependent tests/build and Bruno; production migration application/verification remains after deployment.

After every worker completes, the mastermind must report in plain language what the worker did, what changed or was found, exact validation results, blockers, affected todo items, and the next action. This report is required before another worker or phase starts.

## Bruno gate (mandatory)

Every applicable API slice must include a Bruno gate after implementation validation. The mastermind must record the collection/tier, exact request count, exact assertion count, exit code, seed/reset state, API cleanup state, and whether the gate was idempotent or one-shot. T0 is the idempotent standard gate; lifecycle tiers require a fresh authorized seed before rerun. If Bruno cannot run because a migration, credential, environment, or deployment prerequisite is missing, the slice remains open and the blocker plus an explicit next-phase todo item must be recorded.

## Phase todo list (mandatory)

Each active phase owns an editable todo list at `docs/operations/current-phase-todo.md`. The mastermind updates it after every worker report, gate, correction, and owner decision. A new phase cannot start while required current-phase tasks, documentation/Postman synchronization, or Bruno status remain unresolved.

## Documentation and Postman synchronization (mandatory)

Any change that adds or changes a business rule, endpoint, request/response contract, authorization rule, error code, persistence behavior, migration, or user-visible workflow must update the complete affected documentation set before the slice can close:

- every affected page under `docs/`,
- every relevant collection and request under `docs/postman/`, including examples, variables, descriptions, and response assertions,
- `README.md` when the implemented HTTP surface, setup, roadmap status, or workflow changes, and
- the private `CURRENT HANDOFF` with the exact synchronization status.

For a broad audit, the mastermind must issue a `sanad_documenter` brief with the same pinned SHA, exact manifest, numbered task ladder, acceptance checks, exclusions, stop conditions, and final-report fields required of every worker. Missing or contradictory documentation or Postman updates pause commit, push, deployment, and handoff readiness.

## Contract-completeness gate

Every broad audit and every API slice must maintain a requirements-to-contract matrix, not just a changed-file list. Each row maps the product requirement to its role and authorization policy, implementation route and handler, persistence/data source, tests, Bruno scenario, public documentation, and Postman request. The matrix must explicitly verify admin resource visibility: list/detail reads must exist and be documented alongside create/update/delete and active-state transitions. A route census that only proves existing routes are present in Postman does not prove that required routes exist.

The closeout report must state exact counts for missing requirements, source routes, tests, documentation sections/files, Postman requests, and Bruno scenarios. A controller-to-Postman check runs in both directions; intentional exceptions such as external webhooks require a manual collection artifact or an explicit recorded owner decision. Any nonzero missing count blocks closeout until corrected or owner-deferred in the active checklist.

The repository check is `pwsh -File tools/Verify-ApiContractMapping.ps1`. It extracts controller routes and Postman requests, normalizes route parameters, checks both directions, prints exact counts, and exits nonzero on any mismatch. Run it after Postman synchronization and before final reviewer sign-off; rerun before commit/push if subsequent changes can affect the mapping. Contract-completeness counts and exceptions must be reconciled at this point, before final reviewer approval.

## Sanad validation rules

- Run focused unit tests before broader gates.
- T0 Bruno is the idempotent standard tier: run it against a fresh local seed and expect the current 81-request scope.
- Lifecycle Bruno tiers consume seeded bookings/accounts. Run them separately and reseed before rerunning.
- A green worker report is not a release verdict. The owner gate is authoritative for the final pinned revision.
- Database resets and migrations require exact target verification. Local migration creation/verification is an owner action before commit and targets only the verified local database. Never apply a migration to an unknown database.
- Local migration creation/verification, when required, is an owner action against the exact verified local database target and precedes migration-dependent focused tests/build and Bruno. Local Bruno has priority for the active phase. VPS deployment and production migration application/verification are deferred until after the owner-led UI walkthrough. Then the owner deploys the verified revision, applies/verifies the migration against the exact verified production database target, and runs safe smoke checks.
- Stage validated changes by logical commit scope, keep private control files unstaged, and do not start a new phase while required current-phase todo items remain open or explicitly owner-deferred.

## B1-B3 closeout

B1-B3 is complete on `main`: caregiver confirmed-cancellation API, caregiver rejection facts, shared persistence guard, unit coverage, and Bruno coverage. Current evidence includes 31/31 focused caregiver cancel/decline unit tests and 16/16 caregiver Bruno requests with 23/23 assertions. The development seed timestamp correction is committed as `2faed38`, and the clean T0 regression is 81/81 requests with 106/106 assertions.

The next slice must begin with a current-state audit and a bounded worker brief. Do not reuse historical Arena prompts or treat old ledger entries as current instructions.
