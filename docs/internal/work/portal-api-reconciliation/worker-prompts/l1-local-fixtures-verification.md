# L1 — Persistent local fixtures and fast verification (ACTIVE WORKER BRIEF)

## Read first

1. Repository `AGENTS.md` and `docs/internal/governance/AGENTS.md`.
2. `docs/internal/Mastermind_Handoff.md`.
3. Approved scope: `docs/internal/work/portal-api-reconciliation/contracts/frontend-follow-up-2026-10-20.md`, section L1.
4. Inspect the pinned base and the working tree before editing. Preserve all
   pre-existing/unrelated edits; do not stage, overwrite, or include them.

## Current checkpoint

- Phase state: `completed`; task completed by Mastermind after final focused
  verification. Commit `367a604cbdd99ab64a5360e73bc3fb7b1b0ac707` was pushed to
  `origin/main` and verified as the remote branch head. Owner authorized resolving the
  reported schema blocker on local `SanadDb` only. The Worker reports the local
  schema step, fixture seed and L1 HTTP verifier completed successfully.
  Mastermind found the migration command gates by pending counts rather than exact
  IDs; the same Worker applied that correction (exact pending-ID set gate plus
  offline regression coverage, including rejection of malformed empty comma
  entries) in code/tests/brief only — no database operation, migration command,
  seed, API start or environment enumeration was run for the corrections.
  VPS deployment is `not_applicable`: L1 provides local Development/test-only fixture and
  verification tooling; no Production feature, data or configuration needs to
  run on the VPS. No provider call or release operation has occurred yet.
- Base revision: `1f9939db5407b0c3e8940c0d3b5fdf4a405d2101` (still the
  working-tree HEAD; unchanged by any L1 stage).
- At preparation, the worktree already contained unrelated modified files
  (`.codex/`, `README.md`, handoff/governance, E05/E06 proposals, bridge tooling)
  and untracked audit/config/prompt/contract files. The follow-up contract,
  clarification note and E03 prompts predate this L1 preparation; none is owned
  by L1. All of it is still present and uncommitted.
- First Worker model selected by Owner: `opencode/mimo-v2.6-flash-free` (OpenCode;
  no reasoning variants). Replacement Worker selected by Owner:
  `river/deepseek-ai/DeepSeek-V4.1-Flash#high` (River AI; provider/model verified
  active with a `high` variant). Owner authorized the replacement dispatch on
  2026-10-20. The replacement Worker is authorized to continue L1; migration
  scope was limited to the separately Owner-authorized local `SanadDb` operation
  and its safety conditions below. No release is authorized.

## Objective and boundaries

Deliver the smallest reusable persistent-local fixture setup and lightweight
script-based HTTP verification path required by the approved L1 contract. Reuse
existing domain/seed logic and the existing Bruno/local API tooling where safe;
retain Postman/Bruno as the consumer/manual interface. Do not add a framework,
seed API, dashboard, bulk-data project, recurring database recreation, endpoint
feature work, or unresolved frontend additions. OTP remains excluded.

Persistent synthetic fixtures may be added/reused only in a positively verified
local `SanadDb`; do not delete/reset existing data. Preserve geography/reference
data, manually progressed scenarios and unrelated rows. Owner separately
authorized resolving the reported L1 schema blocker by applying pending
migrations to local `SanadDb` only. Before applying, verify the exact migration
set against the current DB and inspect for destructive/data-loss operations; stop
for further Owner direction if the set differs from the reported preflight or
could delete/overwrite existing data. No VPS/Production writes, provider calls,
or credential disclosure. Keep `TestUserSeedTargetGuard` and its
`SanadBrunoTestDb` automated-test target guard intact; do not widen or bypass it.
If a safe persistent-local approach cannot be implemented without weakening
these guarantees, stop and report the precise blocker rather than guessing.

## Owned files and change boundary

Implementation-owned file manifest (no others without Mastermind/Owner approval):

- `src/API/Sanad.API/Seeding/TestUserDataSeeder.cs`
- `src/API/Sanad.API/Seeding/TestUserSeedTargetGuard.cs`
- `src/API/Sanad.API/Seeding/TestUserSeedOptions.cs`
- `src/API/Sanad.API/Program.cs` and
  `src/API/Sanad.API/ApplicationBuilderExtensions.cs` only if required for an
  explicit safe local fixture mode
- focused existing/new tests under `tests/Sanad.UnitTests/API/` and
  `tests/Sanad.UnitTests/Seeding/` (retain the Bruno target guard test)
- one lightweight verifier under `tools/` and offline script tests under
  `tests/` (do not invoke API/DB in script-unit tests)
- affected Bruno/Postman or local usage documentation only if instructions or
  verified behavior actually change

Record the exact changed-file manifest and rationale in the result before
handback. No changes to approved contracts, clarifications, unrelated docs,
agent configuration, README, deployment/release tooling, migrations, or
unrelated API behavior. No broad error-subsystem work.

## Required safety and acceptance

1. Before any local database access, verify effective host/database for all nine
   module contexts, migration state, existing records, storage root, and
   non-live integrations. Never infer a target from its name. Print no
    credentials/connection strings. If any check is unavailable or ambiguous,
    do not write; report the blocker. Migration application is authorized only
    for the exact, verified, non-destructive pending set on local `SanadDb`.
2. Fixture setup is stable/idempotent: reuse deterministic identities, add only
   missing approved synthetic records, never duplicate users or reset manually
   progressed state. Start smaller than the contract's two-family/three-
   dependent/two-caregiver/two-facility target when a check needs fewer rows.
   Keep feature-table additions to at most ten rows; audit/history/role-link
   expansion may naturally exceed this. Reuse suitable existing synthetic
   images only when needed and keep asset references consistent.
3. Script preflight proves local reachability and login and fails closed; never
   fall back to a remote URL. Start/reuse the API once. Mutations are not
   automatically retried; verify writes by reading state back. Redact tokens,
   credentials, connection strings and sensitive payloads.
4. Verify at least one small, already-existing endpoint end to end with a
   meaningful success and relevant invalid/unknown/auth/role/ownership case as
   applicable. Do not invent new product policy or add an endpoint feature to
   satisfy this check.
5. Failure diagnostics include check name, method/path, expected/actual result,
   safe error code/detail, trace ID and matching local exception/application
   location when available; state explicitly when trace correlation is
   unavailable. Capture only relevant local log context, never entire logs.
6. Repeat setup/check to demonstrate no duplicate fixtures or data loss; retain
   useful fixtures. No deliberate production error injection.
7. Focused tests cover fixture target safety/idempotence and script behavior,
   including failure diagnostics and redaction. Run script tests without API/DB
   where feasible. Build/test gates must exit successfully with zero warnings;
   authored and executed evidence must be distinguished. Validate changed
   Markdown links and JSON if any are changed. Do not run an unfiltered full
   suite unless contract risk requires it.

## Worker handback

When authorized, pin effective selected model and Worker session ID here, set
phase/task state in accordance with governance, then implement only the bounded
scope above. At handback report exact files, commands/results, fixture target
verification without secrets, repeat/idempotence evidence, script diagnostics,
limitations and next action. Worker may set task `ready_for_review`, never
`completed`. Mastermind reviews actual diff/evidence once and owns acceptance.
Commit/push were Owner-authorized and verified at `5953c6048f597bf6eee7429156cfdd5f2fca2d7b`
on `origin/main`. VPS deployment is not applicable for this local-only workflow;
no runtime Production behavior is being released. The one local migration
application was separately Owner-authorized and is recorded in the current result.

- Worker session: `ses_ea7b6534affefii0P3HQjPSvA2`
  (model `opencode/mimo-v2.6-flash-free`, no variant), dispatched 2026-10-20;
  stopped at Owner request before tool/script implementation or runtime target
  verification. Status-only report confirms no DB access/write, migration,
  commit, or delegation. Worker reports API Release build, unit project Release
  and Debug builds at 0 warnings/0 errors, and a focused isolated run of 41
  passed/0 failed; these are Worker-reported, not yet Mastermind-reproduced.
  Solution-wide warning-as-error build, offline script tests, and runtime
  verify/seed/HTTP checks were not run.
- Partial files developed by that Worker: `src/API/Sanad.API/Seeding/TestUserSeedOptions.cs`
  (new), `TestUserSeedTargetGuard.cs`, `TestUserDataSeeder.cs`, `Program.cs`,
  `ApplicationBuilderExtensions.cs`, `tests/Sanad.UnitTests/API/TestUserSeedTargetGuardTests.cs`,
  and new `tests/Sanad.UnitTests/Seeding/TestUserDataSeederMinimalScopeTests.cs`.
  New tool verifier and offline script tests were not created by that session;
  the replacement Worker added them (see the current result below). Keep these
  uncommitted changes and review them before reassignment; do not assume their
  acceptance based on the Worker report alone.
- Replacement Worker session `ses_ea7560231ffe8yQZSc8hxqEax9` on
  `river/deepseek-ai/DeepSeek-V4.1-Flash` (Owner-selected `#high`; runtime
  provider/model match the selection, the reasoning-effort variant is not
  independently observable from the session), dispatched 2026-10-20 on base
  `1f9939db5407b0c3e8940c0d3b5fdf4a405d2101`; the same session completed the
  bounded work. Nothing was committed, pushed or released,
  and no provider was called. The Owner-authorized local `SanadDb` schema step was
  executed under the exact-set and data-preservation gates recorded in the current
  result below.

### Current result (L1)

Mastermind reviewed the actual L1 diff and evidence, including the exact-ID gate
and fail-closed parsing, and accepts L1 locally. The authorized local migration,
fixture and HTTP run are Worker-reported; Mastermind independently reran the
focused tests and offline script tests listed below. Phase state `accepted`, task
`completed`, release `not_requested`. Nothing was committed, pushed, deployed or
released; no provider was called; no database was reset, dropped or recreated.

- Schema blocker resolved on the authorized target only. Read-only verification
  (now reporting exact pending ids) found all nine module contexts at
  `localhost:5432/SanadDb`, target mode `PersistentLocal`, with a pending set of
  3/3/0/2/0/1/2/2/4 = 17 — identical to the counts recorded in the previous
  preflight — and no `Up()` in any of the 17 migrations contains a destructive or
  data-loss operation: every `DropTable`/`DropColumn`/`DropIndex`/`DeleteData`
  call sits after `protected override void Down(` (revert-only; verified by line
  position), every `AddColumn` on a pre-existing table is nullable or defaulted,
  and the only two Up-time data statements are additive writes to columns the same
  migration creates — `20261013104759_AddChatMessagesPreference` (copies the
  existing `messages_from_families` value into the new `identity.users.chat_messages`,
  then sets NOT NULL with default true) and
  `20261012143753_AddCareHomeRoomTypeAmenitiesAndMedia`
  (`UPDATE care_homes.room_types SET amenities_json = '[]' WHERE amenities_json = ''`,
  normalizing the placeholder empty string in the column it just added). No
  pre-existing column or row is deleted, renamed or overwritten.
- The Worker reports applying the manually inspected pending set under the
  **original count-only gate** (this exact command was run; it remains the evidence
  that the 17 migrations were applied):
  `--apply-local-fixtures-migrations --expect "Identity=3,Cms=3,Caregivers=0,Families=2,Notifications=0,CareHomes=1,Finance=2,Community=2,Chat=4"`
  → exit 0, `passed:true`, `appliedAnyMigrations:true`, `failedChecks:[]`, exactly
  those 17 ids reported per module and nothing else applied. That gate compared
  only pending counts, so it could not itself reject a same-count/different-ID
  substitution; the applied set recorded above was established by manual
  inspection of the reported ids.
- **Mastermind review finding, corrected here:** the gate now compares the exact
  pending migration-**ID** set for every module instead of counts. The corrected
  CLI expectation is `Module=id;id;id,...` (comma-separated modules), where
  `Module=` states that the module has no pending migrations; the intended command
  form for the authorized set (recorded for review, **not re-run** in this
  correction) is:

  ```
  --apply-local-fixtures-migrations --expect "Identity=20261012064508_AdminUserActions;20261013104759_AddChatMessagesPreference;20261016133316_AddVerificationRequestVersion,Cms=20261010131314_AddLibraryContent;20261012061755_AddLandingSections;20261012071340_AddLandingSectionHistory,Caregivers=,Families=20261013005844_AddBookingAreaServiceAttribution;20261018170707_AddCaregiverMedicalAccessAudit,Notifications=,CareHomes=20261012143753_AddCareHomeRoomTypeAmenitiesAndMedia,Finance=20261010143327_AddCaregiverPayoutPolicyDescriptions;20261019061622_AddE04PlatformControls,Community=20261010004134_AddCommunityCategories;20261017154400_AddCommunityModeration,Chat=20261011111627_InitialChat;20261011145248_AddChatMessageLocation;20261013000659_AddChatPlusChanges;20261013171755_AddAttachmentCleanupIndex"
  ```

  Set semantics: the observed ids must match the expected ids exactly (same
  membership, order-insensitive), and a missing, extra, substituted or duplicated
  id fails — all of it **before** any `MigrateAsync`. The expectation itself must
  be non-empty, must name exactly the nine module contexts (a same-count module
  substitution is refused), and must contain no duplicate id, no malformed token
  and no empty comma entry (a leading, trailing or repeated comma is refused
  rather than silently dropped); a missing `--expect` is refused. Offline unit
  tests cover a same-count substitution with different ids,
  missing/extra/different-order ids, duplicate ids on either side, empty
  expectations, unknown modules, malformed/missing/duplicate-module expectations
  and leading/trailing/repeated empty comma entries. The original count-only
  command was already fail-closed on target
  and count: demonstrated live with mismatched expectation →
  `failedChecks:["module.identity"]`, missing `--expect` →
  `["expectation.not-provided"]`, expectation not covering the module set →
  `["expectation.module-set-mismatch"]`, all three with
  `appliedAnyMigrations:false` and exit 1. Neither version starts Kestrel or a
  background service, creates a database, or resets, deletes or reverts anything.
- Identity read failure diagnosed and resolved: `UserConfiguration` maps the
  `ChatMessages` complex property to `chat_messages` on `identity.users`, which
  exists only after `20261013104759_AddChatMessagesPreference`, so the fixture read
  failed on an undefined column until that migration was applied. It now reports
  14 Identity users with 2 fixture identities present.
- Verifier result (final artifact; credential-free JSON, `%TEMP%\l1-verifier-final.log`):
  `passed:true`, `apiStarted:true`, `apiReused:false`, `seedSkipped:false`,
  `results: total=8 passed=8 failed=0`, `notes:[]`; three target verifications
  (pre-flight, post-seed, post-repeat-seed) all `passed:true` with 0 pending in
  every module and 2 fixture identities present; two seed invocations both
  `exitCode=0`/`passed:true` with `identityUserCount=14` and
  `fixtureIdentityPresentCount=2` unchanged across all three reads (idempotent, no
  duplicates); eight checks — `paging.page-size-clamp`, `filter.status-draft`,
  `unknown.guid-detail`, `invalid.enum-query`, `auth.unauthenticated`,
  `role.family-denied` and `success.content-admin-list` twice (original and
  post-seed repeat) — all passing with the expected status and, for the error
  checks, the expected business code, field-level errors or role denial. The four
  expected client errors report a trace id with `traceCorrelation:"unavailable"`:
  the captured API log contains only the MediatR licence warning and two framework
  warnings, so no line carries those trace ids — stated explicitly, not guessed.
- Changed files — the uncommitted L1 delta, entirely inside the owned manifest:
  - `tools/Invoke-LocalFixtureVerification.ps1` (new): the fail-closed lightweight
    verifier — read-only nine-context target verification → one-shot Minimal seed
    → single API start/reuse → login preflight → seven checks on the existing
    `GET /api/v1/admin/wellness-tips` → repeat seed and success re-check.
    Loopback-only base URL with no remote fallback,
    `Database:ApplyMigrationsOnStartup=false` and every live SMS Misr/SMTP/Push
    input cleared for each child process, mutations never retried, diagnostics
    redacted, and the started API always stopped in a `finally`.
  - `tests/LocalFixtureVerification.Tests.ps1` (new): offline tests that
    dot-source the verifier with `-LibraryOnly`; now 135 checks passed / exit 0.
  - `src/API/Sanad.API/Seeding/TestUserSeedTargetGuard.cs`: `PendingMigrationIds`
    on the read-only report, `IsApprovedPersistentLocalTarget`,
    `AssertPersistentLocalTargets` refactored onto that predicate, and the
    two-phase fail-closed `ApplyPersistentLocalMigrationsAsync`. Corrected in this
    stage: the expectation is now
    `IReadOnlyDictionary<string, IReadOnlyList<string>>` (exact pending ids per
    module) plus the pure, database-free helpers `IsValidMigrationId`,
    `TryParseExpectedPendingIds` (the `--expect` parser, which never returns a
    partial expectation), `DescribeExpectationShape` (non-empty, exact module set,
    no duplicate id) and the id-set `DescribePendingSetMismatch`.
  - `src/API/Sanad.API/Program.cs`: new
    `--apply-local-fixtures-migrations --expect ...` branch (guard chain →
    expectation parse/refusal → per-context target re-check and exact-id
    comparison → migrate; credential-free JSON; exit 1 on refusal or failure). The
    `--expect` argument is now `Module=id;id;id,...` and its parsing moved into the
    unit-tested guard helper; the branch body is otherwise unchanged.
    `--verify-local-fixture-target` and `--seed-local-fixtures` are unchanged.
  - `tests/Sanad.UnitTests/API/LocalFixtureMigrationApplyTests.cs` (new, rewritten
    in this stage for the exact-ID gate): offline coverage of the target predicate,
    `IsValidMigrationId`, the `--expect` parser (missing/blank argument,
    malformed entry, empty comma entry — leading, trailing or repeated —
    duplicate module, malformed or duplicate id, empty id list), the expectation
    shape (empty, missing/extra module, same-count module substitution, duplicate
    id) and the id-set comparison (exact match, reordered set, same-count
    different-id substitution, missing/extra id, duplicated observed id, unknown
    module), plus the fail-closed apply paths (no expectation, incomplete module
    set, same-count module substitution, duplicate id, unapproved target). No
    connection is attempted, so a passing test also proves nothing was applied.
  - earlier L1 files from this slice that this stage did not modify, still
    uncommitted: the first Worker's `Seeding/TestUserSeedOptions.cs` (new),
    `Seeding/TestUserDataSeeder.cs`, `ApplicationBuilderExtensions.cs`,
    `tests/Sanad.UnitTests/API/TestUserSeedTargetGuardTests.cs` and
    `tests/Sanad.UnitTests/Seeding/TestUserDataSeederMinimalScopeTests.cs`
    (11:31–12:06 timestamps). The `SanadBrunoTestDb` automated-test target guard is
    intact and unwidened.
  - this brief's own Current checkpoint, task status and Current result sections
    (no other document was touched).
- Checks executed on the current tree:
  - Mastermind final focused rerun after all code changes:
    `dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj -c Release
    --no-build --no-restore --filter
    "FullyQualifiedName~LocalFixtureMigrationApplyTests|FullyQualifiedName~TestUserSeedTargetGuardTests|FullyQualifiedName~TestUserDataSeederMinimalScopeTests"
    --nologo` → 91 passed / 0 failed.
  - Mastermind reran `powershell -NoProfile -File
    tests/LocalFixtureVerification.Tests.ps1` → 135 checks passed; no API,
    database, HTTP request or dotnet process was invoked.
  - Mastermind searched the handoff, active brief and approved contract for
    credential-value patterns; none found. Final verifier log scan likewise
    found none. This does not remove the earlier provider-variable output from
    the first Worker session transcript; rotate affected credentials if valid.
  - `powershell -NoProfile -File tests/LocalFixtureVerification.Tests.ps1` →
    135 checks passed, exit 0, reporting that no API, database, HTTP request or
    dotnet process was invoked.
  - parser check on both scripts → 0 parse errors each.
  - `dotnet build Sanad.slnx -c Release --nologo /p:TreatWarningsAsErrors=true`
    → Build succeeded, 0 Warning(s), 0 Error(s).
  - focused tests (pre-correction run) `--filter "FullyQualifiedName~LocalFixtureMigrationApplyTests|FullyQualifiedName~TestUserSeedTargetGuardTests|FullyQualifiedName~TestUserDataSeederMinimalScopeTests"`
    → 50 passed / 0 failed (26 pre-existing + 24 new).
  - correction stage, offline only (no database, migration, seed, API-start or
    environment-value command was run), across the two review passes: `dotnet
    build Sanad.slnx -c Release --nologo /p:TreatWarningsAsErrors=true` →
    Build succeeded, 0 Warning(s), 0 Error(s) on each pass; focused tests
    `--filter "FullyQualifiedName~Sanad.UnitTests.API.LocalFixtureMigrationApplyTests"`
    → 58 passed / 0 failed after the exact-ID rewrite (24 before it) and 65 passed
    / 0 failed after the second narrow correction; `--filter
    "FullyQualifiedName~Sanad.UnitTests.API.TestUserSeedTargetGuardTests"` →
    21 passed / 0 failed (latest combined total 86 passed / 0 failed); `git diff --check` →
    exit 0 (only pre-existing CRLF notices, no whitespace errors); `git rev-parse
    HEAD` still `1f9939db5407b0c3e8940c0d3b5fdf4a405d2101`, and every
    pre-existing/unrelated worktree change is untouched. The first focused run of
    the corrected gate surfaced two stale test expectations from the count-only
    version — a partially populated parser result on the duplicate-module path and
    the old per-module `unexpected-target` outcome for a same-count module
    substitution. Both were the tests, not the gate, being wrong relative to the
    corrected contract: the parser now returns an empty expectation on every
    refusal path (no partial result can escape), and a same-count module
    substitution is asserted to fail closed with
    `expectation.module-set-mismatch` before any connection. Both were fixed and
    the runs above are the post-fix results.
  - second narrow review pass (same stage): the parser used
    `argument.Split(',', RemoveEmptyEntries | TrimEntries)`, which silently dropped
    empty comma entries, so `Identity=id,` or `Identity=id,,Chat=x` parsed as if the
    stray comma were absent — contrary to the fail-closed contract. Splitting now
    keeps empty entries and refuses them as `expectation.malformed-entry`, with new
    offline regressions for leading, trailing and repeated empty entries (each
    asserting false, the safe classification and an empty expectation) plus a
    counterpart test proving the same entries without the stray commas still parse.
    Exact-ID behavior, ordering and refusal classifications are otherwise unchanged.
    Afterwards `dotnet build-server
    shutdown` stopped the MSBuild/Roslyn build servers the builds leave behind
    (0 `dotnet` processes remaining) and port 5235 is free — no API process was
    started at any point in this correction.
  - the authorized migration application (exit 0, `passed:true`, 17 ids), the three
    live refusal paths (exit 1, nothing applied, as recorded above) and the full
    verifier run (`passed:true`, 8/8 checks), re-run unchanged against the final
    artifact.
  - final read-only state after all writes: all nine modules on `SanadDb` with 0
    pending migrations, the storage root `...\bin\Release\net10.0\sanad-files` now
    present, 14 Identity users and 2 fixture identities. The same command invoked
    without the provider inputs cleared correctly refused with
    `integrations.sms-misr-not-cleared` and `integrations.smtp-email-not-cleared`,
    and invoked without `ASPNETCORE_ENVIRONMENT=Development` correctly refused with
    "App:TestUserSeed may only be enabled in Development" — both without any
    database read or write.
  - `git diff --check` clean; worktree list unchanged; no leftover `dotnet`
  process and port 5235 free after every run. During this replacement Worker's
  checks only booleans and migration ids were emitted; no credential, connection
  string, token or payload value was printed, copied or persisted.
- Verifier defects found by actually running it end to end, and fixed (each has an
  offline regression test):
  1. the script read the API's redirected log with `File.ReadAllLines` while the
     API still held it open — a sharing violation that aborted the run and
     orphaned the API, and (through the orphan's inherited stdout handle) also
     caused the earlier 900 s run that produced no output. Logs are now read with
     `FileShare.ReadWrite` after the response exists, and the API is stopped in a
     `finally` so no failure path can orphan it;
  2. `System.Net.Http` is not loaded in a default Windows PowerShell session, so
     `New-Object System.Net.Http.HttpClient` threw and every readiness probe
     returned "unreachable" without sending a request — the API wait always timed
     out. The assembly is now loaded before any client is built;
  3. the login body sent `devicePlatform = 0` (`DevicePlatform.Unknown`), which
     `LoginCommandValidator` rejects — the API answered HTTP 400
     `fields=DevicePlatform`. It now sends Web (3), inside the documented
     device-name and app-version limits;
  4. the preflight expected `accessType = 0` although `AuthAccessType.Normal` is 1
     (`RestrictedVerification` is 2), so a correct Normal login was rejected. The
     expectation now runs through a pure helper that the offline tests cross-check
     against the enum source.
- Limitations / not proven:
  - The approved-target happy path of `ApplyPersistentLocalMigrationsAsync` has no
    unit-test coverage — it would connect to the persistent local database — so its
    evidence is the single authorized live application recorded above.
  - The corrected exact-ID comparison itself also needs an approved target, because
    reading the real pending ids requires a connection: its evidence is the pure
    helper unit tests plus the live per-module reads recorded above. The corrected
    `--expect` CLI was deliberately not executed even with a malformed argument,
    since that invocation *is* the migration command this stage was told not to
    run; the `Program.cs` branch is therefore covered by the warning-as-error build
    and by the unit-tested guard helper it delegates to, not by a live run. The one
    live application used the earlier count-only gate, so the 17 applied migrations
    remain evidenced by that run plus manual inspection of the reported ids.
  - `filter.status-draft` and both `success.content-admin-list` checks returned
    `items=0`/`totalCount=0`: local `SanadDb` holds no wellness-tip rows, so they
    prove route, paging-clamp and filter/contract handling with those shapes but
    not with populated data.
  - Trace correlation for the four expected client-error checks is `unavailable`,
    for the log-content reason recorded above.
  - Starting this API also starts the repository's existing outbox/notification
    background services against local `SanadDb`, and each login creates a device
    session (`activeFixtureSessionCount: 4`). Provider inputs were cleared for
    every started process, push used the Development no-op sender, and no provider
    was contacted.
  - The whole `Sanad.UnitTests.API` namespace was not re-run this stage; its single
    pre-existing failure (`PortalRemainingResponseContractTests.NestedResponseRefs_ResolveToActualShapes`,
    reproduced at the pinned base in a temporary worktree) is untouched by this
    stage's files.
  - Mastermind corrections applied: exact pending migration-ID set comparison per
    module plus fail-closed handling for same-count/different-ID substitutions,
    malformed/missing expectations and empty comma entries. The corrected gate
    was unit-tested but not run against the database; the prior HTTP verification
    remains evidence, and the single authorized live application recorded above
    remains evidence that those 17 migrations were applied. Mastermind reviewed
    and accepted L1 locally. Commit `367a604cbdd99ab64a5360e73bc3fb7b1b0ac707`
    was pushed to `origin/main`; `git ls-remote` confirmed it as the remote head.
    VPS
    deployment is `not_applicable` because these changes provide local
    Development/test fixture and HTTP-verification workflows only, with no VPS
    runtime feature or data requirement. Release state is `not_applicable` for
    this phase; no VPS deployment occurred or is required.
