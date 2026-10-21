# E07 — Append-only caregiver review notes (bounded brief)

Owner-approved minimum scope (follow-up contract, "Remaining roadmap"): append-only
caregiver review notes with author/time, for SuperAdmin/SupportAdmin under the existing
caregiver review policy. No edit, delete, workflow, assignment or notification.
Phase state: `completed`. Task state: `completed`. Release state: **`released`** —
revision `f66492d877ab61d7057d74551d27feb8767fe509`.
Base, worker and runtime slot are recorded in the dispatch amendment below.

## Pinned behavior

- **Append-only.** A note row is created once and never updated or deleted by any API in
  this phase. No `PUT`, `PATCH` or `DELETE` route is added, and the note text is immutable
  after creation.
- **Audience.** Reads and writes use the existing `CaregiverReviewAdmin` policy (Normal
  SuperAdmin + SupportAdmin, `access_type = Normal`). No new policy, and no edit to
  `AuthorizationPolicies.cs` or `DependencyInjection.cs`. Caregiver-facing, Family-facing
  and Care Home-facing surfaces are out of scope.
- **Ownership/routing.** Routes extend the existing admin caregiver group
  (`AdminCaregiversController`, `[Route("api/v1/admin/caregivers")]`, no leading slash):
  - `GET  /api/v1/admin/caregivers/{caregiverId:guid}/notes` — paged list, newest first,
    `page` (default 1) and `pageSize` (default 20, max 100) normalized by the existing
    paging convention. Unknown caregiver → 404. Empty list → `200` with an empty `items`
    array and `totalCount` 0.
  - `POST /api/v1/admin/caregivers/{caregiverId:guid}/notes` — create one note; `201` with
    the created note body (id, caregiverId, text, authorUserId, createdOnUtc) so the caller
    can read back exactly what was stored. Unknown caregiver → 404.
- **Text bounds.** Required, trimmed, non-whitespace; **≤ 2000 characters**. Empty or
  whitespace-only text is `400`; over-length is `400`. Store the trimmed text verbatim
  otherwise (no masking, no truncation, no markdown processing).
- **Attribution.** The author is the authenticated actor user id; the timestamp is UTC and
  single-sourced from the same clock the other Caregivers writes use. There is no
  client-supplied author or timestamp.
- **No side effects.** Creating a note does not change caregiver status, certificates,
  ratings, payouts or review decisions; it does not emit notifications, emails or domain
  events; it does not touch identity records beyond reading the actor id the request
  already carries.
- **Privacy.** The note is administrative text. Reads return only id, text, author id and
  created time — never caregiver identity documents, phone numbers, email addresses or
  medical data.
- **Errors.** New codes use the `Caregivers.ReviewNote.*` prefix and are mapped **locally in
  the controller** (the shared `ResultProblemDetailsMapper` defaults unknown codes to 400
  and is read-only): missing caregiver → 404, empty/over-length text → 400. No 409 path
  exists because nothing is concurrent or mutable.
- **Query shape.** Paging must be bounded and deterministic (order by `createdOnUtc`
  descending, then by id descending as a stable tiebreak), and counts must reflect the
  caregiver's full note set.

## Exact writable manifest

- **New:** `src/Modules/Caregivers/Domain/Sanad.Modules.Caregivers.Domain/Caregivers/CaregiverReviewNote.cs`
- **New:** `src/Modules/Caregivers/Infrastructure/Sanad.Modules.Caregivers.Infrastructure/Persistence/Configurations/CaregiverReviewNoteConfiguration.cs`
- **New:** `src/Modules/Caregivers/Application/Sanad.Modules.Caregivers.Application/Onboarding/AdminCaregiverReviewNotes.cs`
- **New, reserved migration pair:** `src/Modules/Caregivers/Infrastructure/Sanad.Modules.Caregivers.Infrastructure/Persistence/Migrations/20261021130000_AddCaregiverReviewNotes.cs`
- **New:** `src/Modules/Caregivers/Infrastructure/Sanad.Modules.Caregivers.Infrastructure/Persistence/Migrations/20261021130000_AddCaregiverReviewNotes.Designer.cs`
- `src/Modules/Caregivers/Application/Sanad.Modules.Caregivers.Application/Abstractions/Data/ICaregiversDbContext.cs` (one `DbSet` line)
- `src/Modules/Caregivers/Infrastructure/Sanad.Modules.Caregivers.Infrastructure/Persistence/CaregiversDbContext.cs` (matching `DbSet`)
- `src/Modules/Caregivers/Infrastructure/Sanad.Modules.Caregivers.Infrastructure/Persistence/Migrations/CaregiversDbContextModelSnapshot.cs`
- `src/API/Sanad.API/Controllers/AdminCaregiversController.cs` (the two routes only)
- **New:** `tests/Sanad.UnitTests/Caregivers/CaregiverReviewNoteTests.cs`
- **New:** `tests/Sanad.UnitTests/API/AdminCaregiverReviewNotesControllerTests.cs`
- `docs/admin/caregivers-review.md` (a new section: routes, roles, bounds, response shape,
  append-only guarantee, error codes)
- `docs/admin/admin-overview.md` (exactly one index row for the new Postman collection,
  next to the existing Postman index line; preserve the file's existing bytes elsewhere)
- **New:** `docs/postman/admins/Sanad.Admin.CaregiverNotes.postman_collection.json`
- **New:** `tools/Invoke-E07LocalHttpVerification.ps1`
- This brief.

Rationale for a dedicated Postman collection: E08 also consumes the general
`docs/postman/admins/Sanad.Admin.postman_collection.json` in the same wave, and F7 set the
precedent of a phase-local Admin collection (`Sanad.Admin.Ratings`) to avoid exactly that
race. Do not edit the general Admin collection or any other phase's collection.

Additional files require a bounded Mastermind manifest amendment. No other file may be
touched; in particular the shared result mapper, shared policies, storage, registration
and every other context's migration or snapshot stay read-only.

## Focused verification

- Domain/handler unit tests: create-then-read round trip; trimmed text stored verbatim;
  whitespace-only, empty and 2001-character text rejected; unknown caregiver rejected with
  the 404 code; author and UTC timestamp attributed from the actor/clock, not the request;
  newest-first order with the id tiebreak; paging and `totalCount` over more than one page;
  `pageSize` clamp; append-only invariant (no update/delete path exists and a second note
  never alters the first).
- Controller tests: both routes answer the pinned success status and body shape; role
  denial for ContentAdmin, Family, Caregiver and Elderly; anonymous 401; the local
  400/404 mapping; no 409 or 5xx path is reachable from request input.
- Confirm the new migration is **authored only** and is additive (`CreateTable` +
  `CreateIndex`, `Down` drops only) with no other context's snapshot changed.
- Strict Release solution build with zero warnings/errors, then only the affected focused
  classes. No whole-suite run.

## Local HTTP evidence (real, bounded)

`tools/Invoke-E07LocalHttpVerification.ps1` must follow the L1/F6/F7 adapter pattern:

- dot-source `tools/Invoke-LocalFixtureVerification.ps1 -LibraryOnly` (and may reuse the F6
  helper functions as a library); loopback-only default `http://localhost:5255`; assert
  loopback and refuse any other host.
- Run the read-only target verification first and fail closed; never target
  `SanadBrunoTestDb`; verify the resolved psql connection points at the same local
  development database before any write.
- Provision only INSERT-only, guarded, idempotent synthetic fixtures (a caregiver row or
  two plus a body-caregiver row, a SupportAdmin identity and a ContentAdmin identity used
  for the denial check). Never update, delete or reset any existing row, and never create a
  migration row.
- Start or reuse one API instance on the pinned port and stop only the instance this run
  started. Log out every session this run creates (`POST /api/v1/auth/sessions/logout`).
  Use free phone numbers (taken already: +201000000001–009, 011, 012, 021–023, 051–054,
  061, 062, 064, 065, 081–083 — take others, e.g. 091–095).
- Real localhost HTTP checks, each reporting name, method, path, expected status, actual
  status, error code and trace id, with secrets redacted: anonymous 401, SupportAdmin
  allowed, ContentAdmin denied, create 201 with readback of the stored text and author,
  unknown caregiver 404, empty text 400, whitespace text 400, 2001-character text 400,
  paged list 200 with the created note present and a correct `totalCount`, second page and
  `pageSize` clamp, and a 200 empty list for a caregiver with no notes. Never induce a
  400/404 by writing invalid rows to shared tables.
- Note: the L1 `Invoke-PlannedChecks` has no per-check try/catch, so the adapter must
  structure checks so a single failure still reports its name/method/path/expected/actual
  and does not silently discard the batch.
- No live provider call, no Production access and no authenticated Production request. The
  result JSON must state which checks are HTTP-executed and which are unit-only, and must
  list honest limitations.

## Dispatch amendment

- Base: `main` at `9436dbf09a66c338f05fdc65301a55c45bb9e18b` (F8 closeout), verified with
  `git rev-parse HEAD` before any edit. No local delta of mine existed at dispatch.
- Worker: `river/deepseek-ai/DeepSeek-V4.1-Flash#high` (provider `river`, high effort).
  Wave 3, paired with E08 on disjoint files.
- Worker session: `ses_ea3d32310ffeuGyMm3vt6X2mel`.
- Runtime slot: Mastermind-held (single, serialized). Build and focused tests run; no API
  start, no HTTP adapter execution, no fixture SQL, no migration application and no local
  database write.
- Caregivers head predecessor verified: `20261020210727_AddCaregiverRatingModeration`; the
  reserved identifier `20261021130000_AddCaregiverReviewNotes` was free at dispatch.
- Fixture phone numbers used: `+201000000099` (SupportAdmin) and `+201000000110`
  (ContentAdmin).

## Current result

- State: task 1 `completed`, task 2 `ready_for_review`; phase stays `approved` for the
  Mastermind. Release state unchanged (`not_requested`).
- Session/base: `ses_ea3d32310ffeuGyMm3vt6X2mel` on `main`
  `9436dbf09a66c338f05fdc65301a55c45bb9e18b` (working tree, uncommitted).
- Files changed (new): `CaregiverReviewNote.cs`, `CaregiverReviewNoteConfiguration.cs`,
  `AdminCaregiverReviewNotes.cs`, `20261021130000_AddCaregiverReviewNotes.cs` +
  `.Designer.cs`, `tests/Sanad.UnitTests/Caregivers/CaregiverReviewNoteTests.cs`,
  `tests/Sanad.UnitTests/API/AdminCaregiverReviewNotesControllerTests.cs`,
  `docs/postman/admins/Sanad.Admin.CaregiverNotes.postman_collection.json`,
  `tools/Invoke-E07LocalHttpVerification.ps1`.
- Files changed (modified, all in manifest): `ICaregiversDbContext.cs`,
  `CaregiversDbContext.cs` (one `DbSet` each), `CaregiversDbContextModelSnapshot.cs`,
  `AdminCaregiversController.cs` (two routes, request record, local status mapping),
  `docs/admin/caregivers-review.md` (new "Review notes (append-only)" section),
  `docs/admin/admin-overview.md` (one Postman index addition on the existing line),
  this brief.
- Outside the manifest (bounded amendment requested, see below):
  `tests/Sanad.UnitTests/Caregivers/CaregiverPayoutHandlerTests.cs` and
  `tests/Sanad.UnitTests/Caregivers/PayoutAccountReviewHandlerTests.cs`.
- Checks run — PASS: `dotnet build Sanad.slnx -c Release /p:TreatWarningsAsErrors=true`
  → Build succeeded, 0 Warning(s), 0 Error(s). `dotnet test
  tests/Sanad.UnitTests/Sanad.UnitTests.csproj -c Release --no-build --filter
  "FullyQualifiedName~CaregiverReviewNote|FullyQualifiedName~AdminCaregiverReviewNotesControllerTests"`
  → Failed 0, Passed 53, Skipped 0. Same command with
  `--filter "FullyQualifiedName~PayoutAccountReviewHandlerTests|FullyQualifiedName~CaregiverPayoutHandlerTests"`
  (the amendment regression) → Failed 0, Passed 32, Skipped 0. Offline library-only smoke
  of the adapter (dot-sourced with `-LibraryOnly`, no API/DB/HTTP) → 278 checks, PASS
  (harness: `C:\Users\mosad\AppData\Local\Temp\opencode\e07-library-smoke.ps1`, a temporary
  non-repo file; its only inputs are the adapter's pure helpers plus synthetic responses).
  Adapter parses with the PowerShell language parser → 0 parse errors. New Postman
  collection parses as JSON → 11 requests, 4 variables. `git diff --check` → exit 0
  (only pre-existing CRLF conversion warnings).
- The offline smoke found and fixed one real adapter defect before any HTTP run:
  `"$notesPath?page=1&pageSize=3"` parses `$notesPath?page` as a single variable name
  (`?` is a legal bare-variable character), so all four paged URLs were emitted as
  `=1&pageSize=3`. All four now use `${notesPath}?...` and the smoke pins the exact query
  strings of stages two and three.
- Migration: authored only, `20261021130000_AddCaregiverReviewNotes` (EF scaffolded
  `20261021020423` with `dotnet ef migrations add --configuration Release --no-build`, then
  renamed to the reserved id; no connection was opened). `Up` is `CreateTable` +
  `CreateIndex` only, `Down` drops only, FK to `caregivers.caregivers` is `Restrict`, index
  is `(caregiver_id, created_on_utc)`; the snapshot diff is 40 inserted lines confined to
  the new entity, its FK and its index. **NOT applied.** Caregivers head is therefore one
  behind: `20261021130000` pending on top of `20261020210727_AddCaregiverRatingModeration`
  until the Mastermind applies it in a serialized release slot.
- NOT RUN (not claimed): HTTP execution of `tools/Invoke-E07LocalHttpVerification.ps1` and
  its fixture SQL (runtime slot Mastermind-held; its fail-closed guard also requires
  `pendingMigrations -eq 0` in all nine contexts, so it exits at target verification until
  the Caregivers migration is applied locally); `dotnet ef database update`; any API start,
  loopback request, database read or write; the whole Unit suite;
  `Test-SanadPortalGate.ps1`; architecture tests; Bruno; live providers.
- Authored vs executed: all product code, tests, migration, adapter, docs and Postman are
  authored; the strict Release build, the two focused test filters, the offline adapter
  smoke, the adapter parse check, the Postman parse check and `git diff --check` were
  executed. The adapter's 22 reported checks (15 stage-one, 1 newest page, 3 stage-three,
  2 logout, 1 computed cross-check) are authored, not executed.
- Bounded manifest amendment requested: adding `DbSet<CaregiverReviewNote> ReviewNotes` to
  `ICaregiversDbContext` made three pre-existing `ICaregiversDbContext` test doubles
  uncompilable. Each got exactly one pass-through line
  (`public DbSet<CaregiverReviewNote> ReviewNotes => inner.ReviewNotes;`): twice in
  `tests/Sanad.UnitTests/Caregivers/CaregiverPayoutHandlerTests.cs` and once in
  `tests/Sanad.UnitTests/Caregivers/PayoutAccountReviewHandlerTests.cs`. No assertion or
  other content changed, the amendment regression passes (32/32) and the F7 precedent
  applies. Nothing else outside the manifest was touched.
- Test-design notes for review: unit tests seed directly through `CaregiverReviewNote.Write`
  and pin tied-note ids via `db.Entry(note).Property(entity => entity.Id).CurrentValue`, and
  the adapter runs the four rejected-text `400` checks **before** the appends so a stray
  stored row would push this run's oldest note out of the top four and fail the coverage
  proof rather than hide it.
- Limitations: the fixtures were never provisioned and no note row is ever pre-seeded, so
  every review-note row the adapter reads is one the POST route it calls just created; the
  adapter needs the Caregivers migration applied locally before it can run; the loopback
  port pinned at `http://localhost:5255` was free at check time but is chosen by the
  Mastermind-held slot; the migration's runtime apply behaviour is unverified; and this
  phase adds no edit, delete, assignment, notification or caregiver-facing surface.
- Next action: superseded by the Mastermind execution block below.

### Mastermind execution (post-handback, 2026-10-21)

The worker's "NOT RUN (not claimed)" and "Authored vs executed" boundaries above describe the
handback; this block supersedes them.

- Accepted the bounded manifest amendment: three pass-through `ReviewNotes` lines on
  pre-existing `ICaregiversDbContext` test doubles, no assertion touched. The amendment
  regression (`PayoutAccountReviewHandlerTests|CaregiverPayoutHandlerTests`) was re-run by the
  Mastermind → 32 passed, 0 failed.
- Applied the migration to the local Development database only:
  `dotnet ef database update --project
  src/Modules/Caregivers/Infrastructure/Sanad.Modules.Caregivers.Infrastructure --context
  CaregiversDbContext --configuration Release --no-build`, with
  `ConnectionStrings__CaregiversDatabase` pinned to `Host=localhost;Port=5432;Database=SanadDb`
  and `App__TestUserSeed__Enabled=false`. Caregivers head moved from
  `20261020210727_AddCaregiverRatingModeration` to `20261021130000_AddCaregiverReviewNotes`
  (exactly one pending before, none after); `caregivers.caregiver_review_notes` carries exactly
  `id, caregiver_id, author_user_id, text, created_on_utc`. `SanadBrunoTestDb` was never
  touched.
- Re-ran on the frozen tree: strict Release build → 0 warnings / 0 errors; focused filter
  (`FullyQualifiedName~CaregiverReviewNote|FullyQualifiedName~AdminCaregiverReviewNotesControllerTests`)
  → **53 passed, 0 failed, 0 skipped**.
- Executed the adapter in the single runtime slot on `http://localhost:5255`. **First run:
  21/22** — the computed `cross-check.total-count-and-append-only` failed with "the first note
  changed its creation instant after further appends", while `appendOnlyIdUnchanged=True`,
  `appendOnlyTextUnchanged=True`, `appendOnlyAuthorUnchanged=True` and the caregiver matched.
- Root cause (verified, not assumed): a verification-adapter defect, not a data change. The
  create route returns `ToItem(note)` from the **in-memory** entity, so `createdOnUtc` carries
  the caller's `DateTime.UtcNow` at 100-nanosecond tick resolution, while every later read
  returns the persisted `timestamp with time zone`, which keeps **microseconds**. The
  cross-check compared the two representations for exact equality, so an untouched row failed.
- Fix (adapter only — no product code changed): the cross-check now compares the two instants
  with a sub-microsecond tolerance (under 10 ticks = 1 microsecond) through a new pure helper
  `Get-E07InstantValue`, and reports the observed difference as
  `appendOnlyInstantDeltaTicks`. The `.DESCRIPTION`, the cross-check doc comment, the `expected`
  text and the limitation list now state the microsecond resolution and its cause. Product code
  is deliberately unchanged: no microsecond-truncation convention exists anywhere under
  `src/`, and every sibling caregiver handler returns the same tick-precision in-memory value,
  so this is a house-wide characteristic and the note id remains the stable key.
- Re-run after the fix: **22/22 passed, 0 failed**, exit code 0. Evidence:
  `appendOnlyInstantDeltaTicks=7` (0.7 microseconds) with `appendOnlyIdUnchanged=True`,
  `appendOnlyTextUnchanged=True`, `appendOnlyAuthorUnchanged=True`, `boundaryNoteLength=2000`,
  `totalCount=12`, and all four list reads agreeing on that total.
- Fixture footprint: three adapter runs appended 12 retained review notes for
  `019a2f9c-0003-7000-8000-000000000001`. Nothing was updated, deleted or reset; the
  note-free caregiver still holds zero notes.
- Released: committed `f66492d` (18 files) and deployed on 2026-10-21 as revision
  `f66492d877ab61d7057d74551d27feb8767fe509`. The one Caregivers migration
  `20261021130000_AddCaregiverReviewNotes` was applied by the production startup policy
  (exactly one `Applying migration`, 0 DI exceptions, 0 destructive statements), 9/9 heads
  verified two independent ways, OpenAPI 515 → 516 paths with the new
  `/api/v1/admin/caregivers/{caregiverId}/notes [get,post]` answering 401 anonymously, row
  counts unchanged, `service=active` with `NRestarts=0`. Full evidence:
  [e07-caregiver-review-notes-release.md](../reports/e07-caregiver-review-notes-release.md).

## Acceptance and release gate

Mastermind accepts only after: delivery present; the focused tests and the adapter are real
and pass on the frozen tree; the migration is additive and authored only; the docs and the
dedicated Postman collection parse and match source; `git diff --check` is clean. Commit,
push and any migration application require the Owner's standing release authority and a
serialized release slot; record the deployed revision, migration apply counts, 9/9 heads,
backup path and limitations in a release receipt before marking `completed`.
