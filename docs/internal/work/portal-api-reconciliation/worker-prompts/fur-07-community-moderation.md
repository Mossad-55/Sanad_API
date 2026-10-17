# FUR-07 — Community moderation worker brief

**Contract:** `docs/internal/work/portal-api-reconciliation/contracts/fur-07-community-moderation.md`
**Contract status:** owner-approved 2026-10-17; manifest amendment approved for
two existing Community test files listed in the contract.
**Pinned base revision:** `119ba56adff9e954bb0a0348a7bf2fca3967c724`
**Implementation state:** this is a new worker session continuing a partial
FUR-07 implementation in the shared worktree. The previous worker session
ended at an owner-requested checkpoint; read its report before editing:
`docs/internal/work/portal-api-reconciliation/reports/fur-07-worker-delivery.md`.
Preserve all existing FUR-07 edits and continue from the current worktree; do
not reset, clean, or recreate the implementation.
**Report:** `docs/internal/work/portal-api-reconciliation/reports/fur-07-worker-delivery.md`

Implement the complete FUR-07 contract only. Read and follow repository
`AGENTS.md`, `docs/internal/governance/AGENTS.md`, the approved contract, and
the active slice plan. The contract is authoritative for behavior, routes,
roles, data policy, migration scope, tests, and documentation.

## Owned files

You may modify only the files enumerated in the contract's “Proposed exact
owned-file manifest” and add only its named files plus one generated timestamped
Community migration and designer. The contract and this prompt are read-only.
Do not edit other files. If another file is required, stop before editing it
and report the exact dependency and reason.

The owner approved the two additional test files in the contract manifest so
you can repair the existing `Post.Reject` call sites that failed the prior
build: `tests/Sanad.UnitTests/Community/CommunityRecommendationsTests.cs` and
`tests/Sanad.UnitTests/Community/CommunityImageFileTests.cs`. Do not edit any
other file outside the amended manifest.

## Required behavior

- Implement the admin Community post queue/detail, report intake and resolution,
  persisted rejection reason, counters, and timed Community-only author
  restrictions exactly as approved.
- Preserve the current Community moderation authorization policy. Do not grant
  ContentAdmin global suspension, change Identity status, hide/delete reported
  published posts, or extend report targets beyond posts.
- Enforce active restrictions on post/comment/reply creation only; preserve the
  contract's allowance for existing-content edits/deletes and other Community
  interactions.
- Keep migration authoring in scope, but do not apply migrations, write to or
  reset any database, create fixtures, call providers, commit, push, package,
  deploy, or change external systems.
- Update only the specified Community feature guides and Postman collections.

## Verification and report

Run the contract-required strict solution build once after the final code
change, plus focused tests for changed Community behavior and the Community
model/migration alignment. Validate the changed Postman JSON/examples and route
mapping. Do not run the full suite unless you find a concrete cross-cutting or
high-risk reason; report that reason before expanding verification. Use no
Bruno/live HTTP unless an approved disposable target and necessary stateful
boundary are established; this brief does not authorize any database writes.
Never use the Production-mode VPS.

Update the report at the path above, retaining the prior checkpoint as history
and adding this continuation's status, changed files, concise implementation
summary, each command and exact result/count/warnings, migration authored but
not applied, assumptions, limitations, and any acceptance check not met. Do
not mark the phase Done or continue into FUR-08.

The first line of your final report must be exactly one of:

`FINAL_REPORT_STATUS: COMPLETE`

or

`FINAL_REPORT_STATUS: BLOCKED`

Use no completion marker in interim progress. Stop after returning the report
or a specific blocker through this same OpenCode job. Do not message the
Mastermind separately or start another worker/session. The Mastermind will
review this delivery; if blocked or corrections are needed, wait for a bounded
continuation in this same session and then return an updated report.
