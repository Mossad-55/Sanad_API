# Codex local workspace workflow

This repository uses a three-role workflow for local API development:

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

## B1-B3 closeout

B1-B3 is complete on `main`: caregiver confirmed-cancellation API, caregiver rejection facts, shared persistence guard, unit coverage, and Bruno coverage. Current evidence includes 31/31 focused caregiver cancel/decline unit tests and 16/16 caregiver Bruno requests with 23/23 assertions. The development seed timestamp correction is committed as `2faed38`, and the clean T0 regression is 81/81 requests with 106/106 assertions.

The next slice must begin with a current-state audit and a bounded worker brief. Do not reuse historical Arena prompts or treat old ledger entries as current instructions.
