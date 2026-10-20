# F4 — Community restriction discoverability (RELEASE VERIFIED)

**Mastermind acceptance (2026-10-20):** phase `accepted`; release `authorized`.
Actual source diff reviewed: policy preserved, typed-ID author filter, bounded
stable paging, one captured active evaluation and batched author lookup; detail
addition is nullable. Active predicate follows existing domain expiry/revocation
semantics (no scheduled-start change). Worker strict build 0 warnings/errors,
20/20 focused and 20/20 real local HTTP checks accepted with reported limitations.
F3's separate test-ordering failure does not alter F4 source/evidence; exclude all
F3 files from release. Release executor may stage ONLY F4 manifest, publish clean
pushed source, deploy/verify existing target per runbooks; no migration expected.
Mastermind alone closes after verified receipt. Earlier task labels are historical.

**Release verified (2026-10-20, delegated release executor).** Implementation commit
`4d3cab488bd6a57447352d265c9955378f1b243c` is on `main`/`origin/main`, published from a
clean detached checkout of that revision (F4-alone compilation green), and deployed to
the existing Production target `72.62.92.144:8091`, service `sanad-api.service`, directory
`/root/sanad-api`, environment `/etc/sanad/sanad.env`. Package:
`deploy/releases/f4-4d3cab4/sanad-api-f4-4d3cab4.tar.gz`, 11,893,957 bytes, SHA-256
`4a744c1859cece06fe351a080d8976cc5c93c9e75ce37b772cfcb3d30dabec17` (build and server
hashes matched; excludes all `appsettings*.json`, `.env`, keys/certs). Startup checked
all nine contexts and applied **zero** migrations; `Verify-SanadDeployment.ps1` passed
(revision match, Production, 9/9 applied heads, startup 9/9, anonymous lookup smoke HTTP
200). Service active with `NRestarts=0`; env/unit/`appsettings.json` fingerprints
unchanged; host `appsettings.Development.json` preserved. Anonymous live smoke: new
`GET /api/v1/admin/community/restrictions` returns 401, and `GET /openapi/v1.json`
publishes the route with `authorId/isActive/page/pageSize`, the
`AdminCommunityRestrictionsPage`/`AdminCommunityRestrictionListItem` shapes and the
post-detail nullable `activeRestriction` retaining `authorId`. No authenticated Production
GET was made (no preapproved credentials available); no fixture/data write, provider call,
migration or reset. Release is `verified`; the Mastermind closes the phase. Only the
nine-file F4 manifest was committed; the unchanged `CommunityAuthorRestrictionTests.cs`
was excluded, and all F3/unrelated worktree edits were left untouched.

Phase: `in_progress` (Owner authorized); task: `ready_for_review` (implementation and
runtime verification are green; handed back for Mastermind acceptance); release: authorized
conditionally after Mastermind acceptance, no release by Worker. Base:
`ea3c3bb6b50b58b39bbea954767621d70029e763`. Worker B:
`river/deepseek-ai/DeepSeek-V4.1-Flash#high`. Record dispatched session in Current result.

Read root/governance, handoff and [approved F4 scope](../contracts/frontend-follow-up-2026-10-20.md#f4--community-restriction-discoverability-a1).
F3 runs concurrently with disjoint CareHomes ownership. Preserve existing dirty
configs/README/bridge/docs/audits and F3 work. No subagents/commits/release/migrations.

## Behavior and acceptance

- Add `GET /api/v1/admin/community/restrictions` with optional authorId/isActive,
  page/pageSize. Use existing moderation paging conventions (default 25/max100),
  stable newest-first order and total count. Active means current start/expiry/
  revocation facts under one captured clock instant; false means not active.
- Return existing restriction ID, author ID/bilingual names, moderator, duration,
  reason/note, starts/expiry, active flag and available revocation facts. Reuse
  current wire enum conventions and identity enrichment; no N+1 per-row lookups.
- Post detail adds nullable current activeRestriction (ID/expiry) retaining authorId.
  SuperAdmin/ContentAdmin only via existing CommunityModeration policy. No new
  revoke action; existing reason limits, create/revoke behavior stay unchanged.
- Cover active/expired/revoked/future-start cases if domain supports them, filters,
  stable pagination/counts, absent author names, empty reads, role denials and a
  restriction discoverable by another moderator/session after creation. Do not
  fabricate unavailable persisted facts or change restriction duration policy.
- Docs/Postman/OpenAPI must match actual response. No UI or schema migrations.

## Exact writable manifest

- `src/API/Sanad.API/Controllers/AdminCommunityPostsController.cs`
- `src/Modules/Community/Application/Sanad.Modules.Community.Application/Posts/AdminCommunityPostsQueries.cs`
- New: `src/Modules/Community/Application/Sanad.Modules.Community.Application/Posts/AdminCommunityRestrictionQueries.cs`
- `tests/Sanad.UnitTests/Community/CommunityAuthorRestrictionTests.cs`
- New if needed: `tests/Sanad.UnitTests/Community/AdminCommunityRestrictionQueryTests.cs`
- New if needed: `tests/Sanad.UnitTests/API/AdminCommunityRestrictionReadTests.cs`
- `docs/admin/community-moderation.md`
- `docs/postman/admins/Sanad.Admin.Community.postman_collection.json`
- New if needed: `tools/Invoke-F4LocalHttpVerification.ps1` (small L1 adapter, not copied F2 harness).
- This brief: assigned task/current result only, no handoff/phase-state edits.

Other context/policy/identity/schema files are read-only. Request precise manifest
amendment if needed, not silent expansion. New endpoint can live in existing
controller using absolute route consistent with existing restriction actions.

## Parallel verification gate

F4 holds first shared dotnet/API/fixture slot; F3 has been told to stop before those
operations. However F3 may still be editing source: do not attribute a build failure
in F3 files to F4 or edit them. If concurrent source churn prevents trustworthy build,
hand back implementation and ask Mastermind to serialize the final gate instead of
looping. Focused Community tests, real local HTTP reads, role denial and current
OpenAPI proof; reuse tiny synthetic fixtures after all-context local target/non-live
preflight. No production/provider calls or resetting existing data. Seed at most a
few rows through supported existing flows; stop if prerequisites require broad setup.
Do not rerun full suites; successful affected gates/strict build must have zero
warnings. Validate changed docs links/JSON and diff. No polling other workers.

## Current result

Session `ses_ea5b2f5fdffeSdPYTavoUhRvtF` (Worker B,
`river/deepseek-ai/DeepSeek-V4.1-Flash#high`), base
`ea3c3bb6b50b58b39bbea954767621d70029e763` unchanged: no commit, push, migration,
release, provider contact or phase-state edit. The earlier verification blocker was
cleared when the Mastermind fixed F3's `CS0136`; the runtime slot then ran on the
frozen tree. Implementation, focused tests and the real localhost proof were all
green, so this was a `ready_for_review` handback; acceptance was with the
Mastermind, and the phase has since been released and is now `verified` (see the
release receipt at the top of this brief).

### Changed paths (all inside the writable manifest)

- `src/API/Sanad.API/Controllers/AdminCommunityPostsController.cs` (modified):
  `ListRestrictions` GET `/api/v1/admin/community/restrictions`, optional
  `authorId`/`isActive`/`page=1`/`pageSize=25`, 200/400 responses, controller-level
  `CommunityModeration` policy unchanged. No new action, route policy or revoke
  behavior.
- `src/Modules/Community/Application/.../Posts/AdminCommunityRestrictionQueries.cs`
  (new): page/item DTOs, one-captured-clock query handler, shared
  `ActiveAt`/`NotActiveAt` predicate mirroring the domain rule and write gate, and
  one batched `GetCommunityAuthorNamesAsync` call per page (no per-row lookup).
- `src/Modules/Community/Application/.../Posts/AdminCommunityPostsQueries.cs`
  (modified): post detail gains nullable `AdminCommunityActiveRestriction` and
  retains `authorId`; nothing else on the detail projection changed.
- `tests/Sanad.UnitTests/Community/AdminCommunityRestrictionQueryTests.cs` (new):
  paging/clamping, author + active/inactive filters, expired/revoked/future-start
  rows, revocation audit, absent names, empty read, shared-start stability, and a
  counting context proving exactly one enrichment call per page.
- `tests/Sanad.UnitTests/API/AdminCommunityRestrictionReadTests.cs` (new): route
  template/policy/binding/parameter forwarding, mapped problem, published OpenAPI
  page/item/parameter/nullable-`activeRestriction` shapes, unchanged create/revoke
  routes, detail 404.
- `tests/Sanad.UnitTests/Community/CommunityAuthorRestrictionTests.cs`: unchanged
  (write-side contract is untouched by F4).
- `docs/admin/community-moderation.md` and
  `docs/postman/admins/Sanad.Admin.Community.postman_collection.json`: restriction
  read documented (parameters, page shape, item fields, active semantics) and the
  post-detail wording corrected to the actual wire shape (`activeRestriction`
  serialized as `null`, not omitted).
- `tools/Invoke-F4LocalHttpVerification.ps1` (new): small L1 dot-source adapter; 16
  HTTP checks + 4 logout checks, fail-closed target/fixture guards, INSERT-only
  guarded fixtures, no reset/delete and no copied F2 harness.
- This brief (task state and Current result only). No other repository file was
  added or changed; the offline smoke harness used below was run from the OS temp
  directory and is deliberately not part of the repository.

### Checks

| Check | Result |
| --- | --- |
| All-context local target/non-live preflight | PASS — Development/PersistentLocal, nine contexts on `SanadDb`/localhost:5432 with 0 pending migrations, SMS/SMTP cleared, push `DevelopmentNoOp`, Paymob not live, storage root present; re-run inside the adapter as its fail-closed precondition (`targetVerification.passed=true`) |
| Strict solution build `dotnet build Sanad.slnx -c Release /p:TreatWarningsAsErrors=true` (final tree) | PASS — 0 warnings, 0 errors; `Sanad.API.dll` rebuilt from the unchanged sources |
| Focused tests `--filter "FullyQualifiedName~AdminCommunityRestriction"` | PASS — Failed 0, Passed 20, Skipped 0, Total 20 (no pre-existing failures inside this filter) |
| F3 diagnostic focused filter (`CareHomeFacilityTests\|CareHomeProfileCommandTests\|CareHomeProfileMediaTests\|CareHomeDocumentUploadTests\|CareHomeReadbackTests`) — separate diagnostic, **not** an F4 acceptance claim | 54 tests: 53 passed, 1 failed — `CareHomeProfileCommandTests.ApprovedFacility_PhoneOnlyEdit_CarriesForwardDocsAndMedia_AndSubmitSucceeds` (expected `private/care-home-media/cover.jpg`, actual `private/care-home-media/gallery-0.jpg`). No F3 file was edited; reported to the Mastermind as diagnostic only |
| `tools/Invoke-F4LocalHttpVerification.ps1` end-to-end over real localhost HTTP | PASS — exit 0, `passed=True`, 20/20 checks, `apiStarted=True`, `apiReused=False`, DB source `environment:ConnectionStrings__IdentityDatabase`, head `ea3c3bb6…`, fixture guard passed. Real checks: 401 unauthenticated list, 403 family viewer, 400 for `page` and `isActive` bindings, default page shape (`items=0 page=1 pageSize=25 totalCount=0`), OpenAPI publication (489 paths / 618 schemas), author post created through the Community write flow (201), restriction create (200, `01a15a72-f930-7c98-86e2-05780c4b73d3`, Days7/Spam), duplicate create 409 `Community.RestrictionAlreadyActive`, author+active filter (`items=1 totalCount=1` containing the created row), second-session discoverability, post-detail `activeRestriction` before revoke, revoke by the second moderator session (200, revoked facts echoed, reason "F4 local verification"), active filter excludes revoked (`items=0`), inactive filter includes revoked (`items=1`), post-detail `activeRestriction` present-and-`null` after revoke, and four 204 logouts |
| Post-run local DB inspection (read-only psql) | PASS — 2 fixture users, 2 accounts, exactly 1 author post, exactly 1 restriction row (the run's), 0 active restrictions left behind; the run's 4 sessions (2×`content.admin.f4`, 1×`community.author.f4`, 1×`family.viewer`) all `revoked_on_utc=20:54:07` from the adapter's own logouts; no fixture session left live and no other fixture data touched |
| Process/port cleanup | PASS — the adapter stopped its own API host; ports 5235 and 5237 closed after the run and the temp OpenAPI probe host was stopped |
| Script static validation (PS 5.1 `[Parser]::ParseFile`) + offline smoke of the assertion closures | PASS — 0 syntax errors, ASCII-only/BOM-less; the closure-captured ids/names/expiries were reproduced offline before the live run |
| Changed docs/Postman validation + `git diff --check` | PASS — community collection JSON parses, documented route/params match the controller and the real OpenAPI document, `community-moderation.md` has no link targets to resolve, no whitespace errors in the changed files |

### Limitations

- The unfiltered default page ran before the fixture restriction was created, so it
  proves empty-read shape, paging defaults and validation only; unfiltered content
  and newest-first ordering are covered by the unit tests (seeded rows) and the
  adapter's ordering comparator, not by a live unfiltered row set.
- Trace correlation came back `unavailable`/`no-trace-id` for most checks because the
  local API log lines did not carry the trace ids; each check result is still a real
  HTTP status/body read.
- The F3 diagnostic failure above is outside F4 ownership and was deliberately not
  fixed (F3 was frozen); it is reported to the Mastermind, not counted here.
- One synthetic author post and one revoked restriction row remain in the persistent
  local `SanadDb` as the run's minimal fixtures (INSERT-only, no reset/delete).

### Next action for the Mastermind

The F4 diff was reviewed and accepted, then released and verified (see the release
receipt at the top of this brief). No further Worker action is pending; the Mastermind
closes the phase/task. Earlier `ready_for_review` labels above are historical.
