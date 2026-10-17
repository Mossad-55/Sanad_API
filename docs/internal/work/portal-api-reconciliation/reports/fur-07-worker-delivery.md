FINAL_REPORT_STATUS: COMPLETE

# FUR-07 Community moderation — Codex continuation delivery

**Worker delivery: COMPLETE; phase status: awaiting Mastermind review.** This
does not mark FUR-07 Done or authorize commit, push, publish, database
migration, or deployment. The previous OpenCode provider-429 checkpoint is
preserved below as history. The owner-approved contract and its narrow test
manifest amendment were followed; no OpenCode session was used for this
continuation.

## Mastermind race corrections

The follow-up review identified two concurrent-request races and a counter
definition mismatch. These are corrected in the approved manifest:

- Restriction creation now uses a serializable transaction for relational
  providers and, on PostgreSQL, a transaction-scoped advisory lock keyed by
  author before the active-row check. `ReadCommitted` is used with the PG
  advisory lock so the check after waiting sees the previous lock holder's
  committed row. The losing request returns the existing
  `RestrictionAlreadyActive` conflict. A deterministic two-context SQLite
  concurrency test verifies one row persists and the other request conflicts.
- Report intake now catches only PostgreSQL SQLSTATE `23505` for
  `IX_CommunityPostReports_PostId_ReporterId`, or SQLite's unique violation for
  the same PostId/ReporterId columns. It detaches the failed entity, reloads
  the committed report, and returns it with `Created: false`. Other persistence
  exceptions are not swallowed. A save interceptor test inserts the winner
  between the read and insert to exercise this race.
- `ActiveRestrictionCount` now selects distinct author IDs before counting.
  The queue test includes duplicate active rows for one author and confirms
  the counter remains one.

Correction verification after final edits:

```text
dotnet build Sanad.slnx --no-restore -m:1 --nologo
```

**Passed:** exit 0; 0 warnings; 0 errors; elapsed 14.42 seconds.

```text
dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj --no-build --no-restore --filter "FullyQualifiedName~Sanad.UnitTests.Community|FullyQualifiedName~Sanad.UnitTests.API.AdminCommunityPostsModerationTests|FullyQualifiedName~Sanad.UnitTests.API.CommunityPostReportsTests|FullyQualifiedName~Sanad.UnitTests.API.CommunityAuthorRestrictionsTests|FullyQualifiedName~Sanad.UnitTests.Persistence.CommunityAndFamilyModelReadinessTests" --logger "console;verbosity=minimal"
```

**Passed:** 153 passed, 0 failed, 0 skipped; exit 0. The test run covers
both race regressions and the distinct-author counter case.

## Delivered behavior

- Added a paged/searchable admin queue and post detail with report totals,
  persisted counters, retained report history, chronological publish/rejection
  history, and author ID/display-name-only readback.
- Added rejection reason/note persistence, authenticated published-post report
  intake with reporter/post deduplication, one-time report resolution, and
  retained timed Community-only author restrictions.
- Enforced active restrictions on new post, comment, and reply commands only;
  existing-content changes and other interactions remain outside the guard.
- Authored the additive Community migration and model snapshot. The migration
  was not applied.
- Updated the two owner-approved existing rejection call sites, added focused
  moderation/model tests, updated both Community Postman collections, and
  completed the App and Admin Community guides.

## Verification

Final strict build, after the final code/test edit:

```text
dotnet build Sanad.slnx --no-restore -m:1 --nologo
```

**Passed:** exit 0; 0 warnings; 0 errors; elapsed 51.05 seconds.

Focused Community and model verification:

```text
dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj --no-build --no-restore --filter "FullyQualifiedName~Sanad.UnitTests.Community|FullyQualifiedName~Sanad.UnitTests.API.AdminCommunityPostsModerationTests|FullyQualifiedName~Sanad.UnitTests.API.CommunityPostReportsTests|FullyQualifiedName~Sanad.UnitTests.API.CommunityAuthorRestrictionsTests|FullyQualifiedName~Sanad.UnitTests.Persistence.CommunityAndFamilyModelReadinessTests" --logger "console;verbosity=minimal"
```

**Passed:** 151 passed, 0 failed, 0 skipped; exit 0. The first test attempt
before the final strict build had 149 passed and 2 failed in newly added tests
because their SQLite query used the converted ID's `.Value` member, which EF
could not translate. The test predicates were corrected to compare the typed
ID directly; the final focused run above passed. No product-code test failure
was present.

Postman validation used a read-only Python script over both collection JSON
files: parsed both collections and all raw JSON request examples, enumerated
35 requests, and verified all 8 FUR-07 route/method pairs against the
controller route attributes. **Passed:** both collections valid, all example
bodies valid JSON, and all routes mapped.

`git diff --check` also passed (exit 0). Git printed only existing LF-to-CRLF
working-copy advisories; it found no whitespace errors.

## Changed files

The existing partial implementation from the prior checkpoint was retained.
FUR-07-owned files changed or added in the shared worktree:

- `src/API/Sanad.API/Controllers/AdminCommunityPostsController.cs`
- `src/API/Sanad.API/Controllers/CommunityPostsController.cs`
- `src/Modules/Community/Domain/Sanad.Modules.Community.Domain/Posts/Post.cs`
- `src/Modules/Community/Domain/Sanad.Modules.Community.Domain/Posts/CommunityPostReport.cs`
- `src/Modules/Community/Domain/Sanad.Modules.Community.Domain/Posts/CommunityAuthorRestriction.cs`
- `src/Modules/Community/Application/Sanad.Modules.Community.Application/Abstractions/Data/ICommunityDbContext.cs`
- `src/Modules/Community/Application/Sanad.Modules.Community.Application/Posts/ModeratePostCommands.cs`
- `src/Modules/Community/Application/Sanad.Modules.Community.Application/Posts/AdminCommunityPostsQueries.cs`
- `src/Modules/Community/Application/Sanad.Modules.Community.Application/Posts/CommunityPostReportCommands.cs`
- `src/Modules/Community/Application/Sanad.Modules.Community.Application/Posts/CommunityAuthorRestrictionCommands.cs`
- `src/Modules/Community/Application/Sanad.Modules.Community.Application/Posts/CreatePostCommandHandler.cs`
- `src/Modules/Community/Application/Sanad.Modules.Community.Application/Comments/CreatePostCommentCommandHandler.cs`
- `src/Modules/Community/Application/Sanad.Modules.Community.Application/Comments/CreateCommentCommandHandler.cs`
- `src/Modules/Community/Application/Sanad.Modules.Community.Application/Comments/CreateReplyCommandHandler.cs`
- `src/Modules/Community/Infrastructure/Sanad.Modules.Community.Infrastructure/Persistence/CommunityDbContext.cs`
- `src/Modules/Community/Infrastructure/Sanad.Modules.Community.Infrastructure/Persistence/Configurations/PostConfiguration.cs`
- `src/Modules/Community/Infrastructure/Sanad.Modules.Community.Infrastructure/Persistence/Configurations/CommunityPostReportConfiguration.cs`
- `src/Modules/Community/Infrastructure/Sanad.Modules.Community.Infrastructure/Persistence/Configurations/CommunityAuthorRestrictionConfiguration.cs`
- `src/Modules/Community/Infrastructure/Sanad.Modules.Community.Infrastructure/Persistence/Migrations/CommunityDbContextModelSnapshot.cs`
- `src/Modules/Community/Infrastructure/Sanad.Modules.Community.Infrastructure/Persistence/Migrations/20261017154400_AddCommunityModeration.cs`
- `src/Modules/Community/Infrastructure/Sanad.Modules.Community.Infrastructure/Persistence/Migrations/20261017154400_AddCommunityModeration.Designer.cs`
- `tests/Sanad.UnitTests/API/AdminCommunityPostsModerationTests.cs`
- `tests/Sanad.UnitTests/API/CommunityPostReportsTests.cs`
- `tests/Sanad.UnitTests/API/CommunityAuthorRestrictionsTests.cs`
- `tests/Sanad.UnitTests/Community/CommunityPostReportTests.cs`
- `tests/Sanad.UnitTests/Community/CommunityAuthorRestrictionTests.cs`
- `tests/Sanad.UnitTests/Community/CommunityModerationAndCheckInTests.cs`
- `tests/Sanad.UnitTests/Community/CommunityRecommendationsTests.cs`
- `tests/Sanad.UnitTests/Community/CommunityImageFileTests.cs`
- `tests/Sanad.UnitTests/Persistence/CommunityAndFamilyModelReadinessTests.cs`
- `docs/app/community/writes.md`
- `docs/admin/community-moderation.md`
- `docs/postman/admins/Sanad.Admin.Community.postman_collection.json`
- `docs/postman/app/Sanad.App.Community.postman_collection.json`
- `docs/internal/work/portal-api-reconciliation/plan.md`
- `docs/internal/Mastermind_Handoff.md`
- `docs/internal/work/portal-api-reconciliation/reports/fur-07-worker-delivery.md`

The approved contract and worker prompt were read-only. Pre-existing changes
to `docs/internal/operations/codex-workflow.md` and
`tools/opencode_bridge/opencode_bridge.py` were preserved and not modified as
part of this delivery.

## Remaining review points and limits

- The report and rejection reason sets are explicit in the contract.
  Restriction reason codes reuse the Community rejection reason set because
  the contract specifies a restriction reason code but no separate list. This
  interpretation, including `Other` requiring a note, is called out for
  Mastermind review.
- No live HTTP/Bruno or PostgreSQL-server proof was run; the contract does not
  require it. Unit tests used EF in-memory/SQLite contexts, including a
  temporary SQLite file for the concurrent restriction test; that file is
  deleted by the test. The model test generated PostgreSQL DDL without
  connecting. Thus the advisory-lock production path is verified by source
  inspection and compilation, not by a live PostgreSQL race test.
- Mastermind correctness/security/compatibility review remains outstanding.
  FUR-07 acceptance and release are therefore incomplete.

## Side-effect confirmation

- No migration was applied and no configured/shared database was written,
  reset, or seeded. Test data lived only in transient EF in-memory/SQLite
  contexts and the temporary SQLite race-test file, which the test deletes.
- No provider, live HTTP, Bruno, OpenCode, commit, push, package, publish, or
  deployment operation was performed.
- The solution build emitted zero compiler warnings and zero errors. The
  PowerShell profile separately emitted `Export-Clixml` access-denied notices
  for terminal-icon preference files; they did not affect build/test results.

## Historical checkpoint — previous OpenCode worker

**State: INCOMPLETE.** The owner requested a checkpoint and stop before the
contract was finished. The OpenCode run returned this report in the existing
FUR-07 session (`job_id`: `oc-f527e7bc6f24`, `session_id`:
`ses_eb59ab8f7ffebiXLP2MWmWVkNW`); no implementation continued afterward.
The worker run is closed. The session record is retained for history.

## Side-effect confirmation

- No commit, push, merge, tag, branch switch, worktree creation, or deployment.
- No database change: no migration application, database write/reset, fixture
  creation, provider call, or live/Bruno HTTP run.
- The worker reports only reads and one local compile (`git status`, file reads,
  and `dotnet build`).
- The approved contract, worker prompt, and pre-existing handoff edits were
  preserved. This report was recorded by the Mastermind from the worker's
  final response after the worker stopped.

## Worker changes

Modified files:

- `src/API/Sanad.API/Controllers/AdminCommunityPostsController.cs`
- `src/API/Sanad.API/Controllers/CommunityPostsController.cs`
- `src/Modules/Community/Domain/Sanad.Modules.Community.Domain/Posts/Post.cs`
- `src/Modules/Community/Application/Sanad.Modules.Community.Application/Abstractions/Data/ICommunityDbContext.cs`
- `src/Modules/Community/Application/Sanad.Modules.Community.Application/Posts/ModeratePostCommands.cs`
- `src/Modules/Community/Application/Sanad.Modules.Community.Application/Posts/CreatePostCommandHandler.cs`
- `src/Modules/Community/Application/Sanad.Modules.Community.Application/Comments/CreatePostCommentCommandHandler.cs`
- `src/Modules/Community/Application/Sanad.Modules.Community.Application/Comments/CreateCommentCommandHandler.cs`
- `src/Modules/Community/Application/Sanad.Modules.Community.Application/Comments/CreateReplyCommandHandler.cs`
- `src/Modules/Community/Infrastructure/Sanad.Modules.Community.Infrastructure/Persistence/CommunityDbContext.cs`
- `src/Modules/Community/Infrastructure/Sanad.Modules.Community.Infrastructure/Persistence/Configurations/PostConfiguration.cs`
- `tests/Sanad.UnitTests/Community/CommunityModerationAndCheckInTests.cs`

New files:

- `src/Modules/Community/Domain/Sanad.Modules.Community.Domain/Posts/CommunityPostReport.cs`
- `src/Modules/Community/Domain/Sanad.Modules.Community.Domain/Posts/CommunityAuthorRestriction.cs`
- `src/Modules/Community/Application/Sanad.Modules.Community.Application/Posts/AdminCommunityPostsQueries.cs`
- `src/Modules/Community/Application/Sanad.Modules.Community.Application/Posts/CommunityPostReportCommands.cs`
- `src/Modules/Community/Application/Sanad.Modules.Community.Application/Posts/CommunityAuthorRestrictionCommands.cs`
- `src/Modules/Community/Infrastructure/Sanad.Modules.Community.Infrastructure/Persistence/Configurations/CommunityPostReportConfiguration.cs`
- `src/Modules/Community/Infrastructure/Sanad.Modules.Community.Infrastructure/Persistence/Configurations/CommunityAuthorRestrictionConfiguration.cs`

These implement portions of the queue, reports, rejection reasons, counters,
and restrictions, but are not accepted or verified by this checkpoint.

## Validation

The worker ran exactly one command:

```text
dotnet build Sanad.slnx --no-restore -m:1 --nologo
```

Result: **FAILED — 2 errors, 3 warnings.** Community Application,
Infrastructure, and API projects compiled; `Sanad.UnitTests` failed.

- `CS7036`, `tests/Sanad.UnitTests/Community/CommunityRecommendationsTests.cs(193,14)`:
  existing one-argument `Post.Reject(Utc(day))` no longer matches the changed
  `Reject(UserId, string?, string?, DateTime)` signature.
- `CS7036`, `tests/Sanad.UnitTests/Community/CommunityImageFileTests.cs(77,14)`:
  same out-of-manifest call-site mismatch.
- `CS8604`, `CommunityAuthorRestrictionCommands.cs(69,58)`: possible null
  `reasonCode` passed to `RequiresNote(string)`.
- `CS8604`, `ModeratePostCommands.cs(117,56)`: same warning.
- `CS8604`, `CommunityPostReportCommands.cs(58,53)`: same warning.

No tests were run. The required zero-warning, zero-error build gate is unmet.

## Blocker and remaining work

The approved file manifest excludes the two existing test call sites that now
fail compilation. The worker stopped before editing either file. Owner approval
is required to expand the manifest or revise the in-scope API change so the
solution can compile without those edits.

**Subsequent owner decision (2026-10-17):** The owner authorized the next
worker to add the two failing test files to the approved manifest and update
their `Post.Reject` calls. The amended contract and continuation brief record
this narrow expansion; the previous worker remains stopped.

Remaining contract work:

1. Update the two newly approved `Post.Reject` test call sites and fix the
   three nullable warnings in-scope.
2. Author one Community EF migration and designer, and update the model
   snapshot. Do not apply the migration.
3. Add `CommunityPostReportTests.cs`, `CommunityAuthorRestrictionTests.cs`,
   `AdminCommunityPostsModerationTests.cs`, `CommunityPostReportsTests.cs`,
   and `CommunityAuthorRestrictionsTests.cs`; update
   `CommunityAndFamilyModelReadinessTests.cs` for entities, indexes, and model
   alignment.
4. Update `docs/app/community/writes.md`; add
   `docs/admin/community-moderation.md`; update both Community Postman
   collections and validate their JSON/examples and route mapping.
5. Update the phase plan and Mastermind handoff, then complete the strict build
   and focused Community tests after the remaining changes.

No acceptance item depending on a green build or focused tests is met. Report,
queue/detail behavior, rejection persistence, counters, report lifecycle, and
restriction enforcement remain unverified. No commit, push, publish, or deploy
is authorized by this checkpoint.
