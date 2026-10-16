# FUR-01 — bounded Mastermind review corrections

Owner handoff to the same implementation worker. Date: 2026-10-16.
Intake remains main HEAD `4b65369d760c8af6b2242ebd2022a6cb10057580` plus
your existing local delivery and the preserved planning/report delta.

Read governance/current handoff, the approved
[contract](../contracts/fur-01-contract-accuracy.md), your
[delivery report](../reports/fur-01-contract-accuracy.md) and the complete
[Mastermind review](../reports/fur-01-mastermind-review.md).

You are not alone. Preserve all changes, including the new Mastermind review.
Do not create a branch/worktree, revert/reset, delete incidental files, stage,
commit, push, publish, contact the VPS or begin FUR-02. Mastermind owns review,
accepted-phase integration and release. This is one correction pass, not a
new full project audit or permission to redesign the contract.

## Owned corrections

1. **R1:** fix login/refresh response ID shapes in the two approved auth guides;
   keep request/header GUIDs plain and explain inner-value extraction.
2. **R2:** correct registration-guide activation ordering to the existing
   both-channels rule after either supported verification purpose. No code change.
3. **R3:** in the two existing new test files, add literal-example conformance
   checks against actual guide/Postman JSON (requests bind actual DTOs with MVC
   options; responses match serialized shape/nullability/wrappers). Cover the
   three auth guides and seven edited response bodies. Show old wrong examples
   fail the targeted guard. Strengthen actual visit kind/status ref + enum
   value/name checks. No new host, framework, production scope or provider calls.
4. **R4:** fix all eleven broken links in the three phase-01 audience reports;
   validate all local Markdown file links under docs/frontend-handoffs/.
5. **R5:** fix attribution and local/deployed wording. Backend missing-schema
   fixes are not frontend mistakes. Remove ambiguous `published, local` labels.
6. Update your report's exact manifest/counts/limitations. Preserve original
   results as historical where helpful; don't claim fresh pre-change proof from
   the old audit. Record actual literal-file checks and independent route-set
   comparison rather than relying on vacuous script 0/0 output. The script
   itself is out of scope and must not be edited.

Owned files: the three auth guides; PortalAuthExampleContractTests.cs and
PortalRemainingResponseContractTests.cs; your three phase-01 audience reports;
your phase report and existing plan/handoff evidence sections as needed. Correct
relevant existing auth Postman examples only if a literal check proves drift
within the approved seven bodies. Do not broaden auth/mutation endpoint scope.
The seven controller annotations appear correct and require no reimplementation.

## Required gates and return

Inspect inherited integration variables/isolation before selected test launch;
clear guarded integration targets in the same child process. Reuse existing
isolated host and fake providers. Literal docs/metadata checks need no live
database. No real provider, PostgreSQL, API startup outside the isolated host,
reset, migration or VPS operation.

After final test/code edits: strict zero-warning/error solution build; focused
contract/isolation/owner regression tests (include new checks and report actual
counts); architecture; affected Postman JSON parse; mapping-script output plus
actual normalized route HashSet differences; Markdown file-link/whitespace and
diff checks. No full suite. Preserve unchanged valid evidence with clear labels.

Save the revised complete phase report at its existing path, with an R1–R5
resolution table and exact evidence. Return a concise summary to the owner for
Mastermind re-review. Keep phase In progress, worker corrections complete /
awaiting acceptance. No final Done or release operations.
