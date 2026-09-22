# Codex local workspace workflow

This repository uses an owner/mastermind workflow with specialized, bounded worker roles for local API development:

| Role | Responsibility | Cannot do without owner approval |
|---|---|---|
| Owner | Product decisions, credentials, migrations, commits, publication, merge, deployment | — |
| Mastermind | Current-state audit, task decomposition, worker briefs, review, local validation, ledger and documentation coordination | — |
| Worker | One bounded scout, review, implementation, or test task from a pinned revision | Merge, push, deploy, migration, control-file edits |

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

1. The owner authorizes the objective and any state-changing authority.
2. The mastermind audits the current branch, SHA, status, product rules, affected code, tests, and docs.
3. The mastermind issues a worker brief with a pinned base SHA, exact file manifest, acceptance ladder, exclusions, stop conditions, and final-report format.
4. A scout or reviewer maps/reviews first when the change is unfamiliar or high-risk.
5. One implementer changes only its manifest. A separate test author works only after the implementation revision is stable and pinned.
6. The mastermind reviews the complete bounded result and resolves any corrections before owner gates.
7. The owner runs the authoritative build/test/migration gate, approves publication, and controls commit/push/merge/deploy.
8. The mastermind runs or verifies the proportionate local API gate and updates public docs plus the private operations ledger.

Before each phase, the mastermind must publish one complete ordered todo checklist for every worker and gate, including owner actions and final handoff. Each row names the owner, deliverable, dependency, and status. Before spawning a worker, the mastermind reports to the owner what the worker is doing now, its exact bounded output, owned files or read-only scope, acceptance check, and checklist item. After every spawn, worker report, interruption, blocker, correction, and gate result, the mastermind updates the checklist and immediately reports the plain-language outcome, evidence, blockers, and todo impact before starting the next worker or gate. A worker may not be silently skipped or replaced; missing reports and unfulfilled checks pause dependent work and are recorded.

The required execution order is: `sanad_scout` mapping and manifest -> `sanad_implementer` bounded change -> mastermind focused tests, full build, and full test suite -> implementer correction round if any defect is found -> `sanad_reviewer` independent review -> `sanad_documenter` complete docs/README/Postman synchronization -> mastermind final validation and handoff update. The implementer correction round is mandatory whenever the mastermind finds a source, test, build, or contract defect; it receives a new pinned brief and cannot be silently patched around by the mastermind.

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

## Sanad validation rules

- Run focused unit tests before broader gates.
- T0 Bruno is the idempotent standard tier: run it against a fresh local seed and expect the current 81-request scope.
- Lifecycle Bruno tiers consume seeded bookings/accounts. Run them separately and reseed before rerunning.
- A green worker report is not a release verdict. The owner gate is authoritative for the final pinned revision.
- Database resets and migrations require exact local/target verification. Never apply a migration to an unknown database.
- Local Bruno has priority for the active phase. VPS deployment is deferred until after the owner-led UI walkthrough; then the mastermind must remind the owner to deploy the verified revision, apply/verify migrations, and run safe smoke checks.
- Stage validated changes by logical commit scope, keep private control files unstaged, and do not start a new phase while required current-phase todo items remain open or explicitly owner-deferred.

## B1-B3 closeout

B1-B3 is complete on `main`: caregiver confirmed-cancellation API, caregiver rejection facts, shared persistence guard, unit coverage, and Bruno coverage. Current evidence includes 31/31 focused caregiver cancel/decline unit tests and 16/16 caregiver Bruno requests with 23/23 assertions. The development seed timestamp correction is committed as `2faed38`, and the clean T0 regression is 81/81 requests with 106/106 assertions.

The next slice must begin with a current-state audit and a bounded worker brief. Do not reuse historical Arena prompts or treat old ledger entries as current instructions.
