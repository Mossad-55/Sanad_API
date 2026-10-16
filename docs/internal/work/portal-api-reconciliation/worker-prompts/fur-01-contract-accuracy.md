# FUR-01 worker brief — existing API contract accuracy

Prepared 2026-10-16. **Ready for the owner's implementation worker. The exact
[FUR-01 contract](../contracts/fur-01-contract-accuracy.md) is owner-approved.**
Complete the entire FUR-01 phase, including code metadata, tests, feature docs,
Postman and reports. Do not stop after one subtask or start another phase.
The prior internal worker was interrupted before production/test edits; no
implementation is delivered yet. Recheck the actual tree rather than assuming.

## Intake and authority

Repository: `D:\Sanad_API`. Pinned preparation HEAD:
`4b65369d760c8af6b2242ebd2022a6cb10057580`.
Read root AGENTS, its governance, current Mastermind handoff, the active
[plan](../plan.md#frontend-follow-up-sequence--2026-10-16) and the full FUR-01
contract before action. If contract approval is not recorded, stop and report
that fact without implementation. No new branch/worktree under current standing
main instruction. No commit/push/hooks bypass, publication or deployment.

You are not alone in the repository. Preserve all other edits. Preparation tree
contains modified documentation navigation/plan/handoffs plus untracked owner
`Frontend_Report.md`, audit, audience reports and approved contract. Recheck
status/HEAD/diff at intake and report any overlapping change; do not reset,
delete, stage wholesale or overwrite the user's files.

## Objective and owned scope

Implement only the approved FUR-01 contract: existing-response metadata for the
29-operation matrix, accurate existing guide/Postman examples and focused
isolated contract tests. No new routes or runtime changes. The contract's owned
file manifest is binding; it names seven permitted production controllers and
the permitted test/docs/Postman/delivery files. Production edits are annotations
and required imports only. No handler, DTO shape, serializer, DI, policy, mapper,
validation, migration, config or business-rule edits.

Admin audience includes Admin portal and Care Home Owner portal. App audience is
Family/Caregiver/Elderly only; shared contract notices are not mobile code audits.

## Execution sequence

1. Pin actual HEAD/local delta and confirm the 29 actions/results remain as
   contracted. Produce a bounded changed-file intention. If source invalidates
   the contract, report it; do not invent a replacement policy.
2. Inspect existing PortalApiMetadataHost and process-level integration guards
   before host/test launch. No real database, migration, seed startup or provider
   can be reachable through selected tests. Reuse that host; do not weaken it.
3. Author focused generated-OpenAPI failures for the exact metadata gaps and
   literal MVC request/response example checks. Distinguish unrun/authored from
   executed/pre-change failures. Metadata GETs need no business fixtures.
4. For representative business HTTP assertions, reuse deterministic dispatch
   fakes or existing isolated fixtures; seed only minimal missing data and read
   back prerequisites first. No production or disposable-DB reset is needed.
5. Add the contracted annotations and scoped documentation/Postman corrections.
   Preserve 200 DTO, 201 DTO/GUID and empty 204 distinctions; do not convert all
   operations to one status/envelope. Correct examples to current serialization.
6. Build once after final code edits with zero warnings/errors, run the specified
   focused tests and architecture check, parse affected Postman JSON and run the
   existing Postman-only mapping script. Run documentation link/whitespace and
   diff checks. No full suite or Bruno by default; no provider/live API testing.
7. Write exact test evidence, unresolved findings and dated audience reports.
   Link issue IDs to approved contract, actual implementation/test revisions and
   truthful deployment state. This task does not deploy; do not label metadata
   changes live. Leave phase status In progress with "worker complete, awaiting
   Mastermind review"; stop for review. Do not mark the whole phase Done.

If inherited integration variables exist, inspect safely and clear them only in
the same test child process; never print connection strings. The controlled
environment pattern in `docs/internal/tools/Test-SanadPortalGate.ps1` is reference
material, not permission to execute its full-suite workflow automatically.

## Required acceptance and report

Meet all six sections of the approved FUR-01 contract. Return:

- Intake SHA, final local diff/owned files and any scope deviation.
- Exact 29-operation status/schema matrix with nested enum/ID correctness.
- Actual build/test/check commands, exit codes, warnings, counts/skips, fixture
  preparation and pre/post evidence. Do not claim tests not run.
- Guide/Postman literal example coverage and links to dated Admin handoffs;
  shared App notice only where the contract is actually relevant.
- Remaining findings and their proper FUR phase; do not implement later slices.
- Explicit confirmation: no new routes/runtime/config/database changes, no
  providers/VPS/reset/migration actions, no commit/push or release operation.

Save the complete delivery report at
`docs/internal/work/portal-api-reconciliation/reports/fur-01-contract-accuracy.md`
and paste a concise summary in your final response for the owner to return to
the planning Mastermind. Include blockers explicitly; a known defect or failing
required gate is not completion. A missing live run is a limitation, never a
fabricated pass. Because you do not commit, report the intake SHA plus final
changed-file manifest/diff, not an invented implementation commit SHA.

Mastermind performs bounded review and requests specific corrections if needed.
If corrections are needed, the worker performs them and supplies updated gates.
Mastermind owns accepted-phase staging/commit/push and release operations under
the owner's phase-boundary workflow. Worker must not perform any of them, touch
the VPS, or mark a phase deployed. No hook bypasses, `git add .`, resets or bulk
cleanup. Preserve the existing user intake and unrelated local changes.
