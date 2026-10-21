# E08 — Delete only unreferenced landing assets (bounded brief)

Owner-approved minimum scope (follow-up contract, "Remaining roadmap"): delete only
unreferenced landing assets; references include retained history/revisions and Family-home
assets; reject referenced deletion; no automatic sweeper. Phase state: `approved` (queued).
Task state: `ready_for_review` — Mastermind-executed: adapter 26/26 on the frozen tree.
Release state: `not_requested`.

## Pinned behavior

- **One route, one key.** `DELETE /api/v1/admin/landing/media?key=<url-encoded key>` on the
  existing `AdminLandingController` (`CmsContent` policy: Normal SuperAdmin + ContentAdmin).
  Single asset per request. No bulk delete, no folder delete, no wildcard, no sweeper, no
  scheduled cleanup and no deletion triggered by section edits.
- **Public landing assets only.** The key must be a public media key in the existing landing
  upload folder: it must start with `landing/`, must not start with `private/`, must
  normalize to a relative path that stays inside the effective public storage root after
  `Path.GetFullPath` containment, and must resolve to a file that exists on disk.
  Anything else is rejected without touching the filesystem path or the storage interface.
- **Statuses.**
  - `204` — the asset existed and was unreferenced, so it was deleted.
  - `400` — missing, empty, malformed, traversal-escaping, `private/` or non-`landing/` key.
  - `404` — a well-formed landing key whose file does not exist.
  - `409` — the asset is still referenced by any retained reference; nothing is deleted.
- **Reference discovery (the safety core).** Before deleting, search every retained
  reference source for an exact key match and refuse if any exists. At minimum:
  - every `cms.landing_sections` row in every status (Draft, Published, Archived), because
    an archived or draft row still names the asset;
  - the retained `cms.landing_section_history` rows (the append-only history is never
    rewritten, so a historical reference is permanent);
  - the Family-home section and its retained history rows added by E05.
  Exact E05 table/column names are pinned in the dispatch amendment once E05 is accepted;
  the requirement does not change.
  Because the match must be on the stored value, compare the normalized key and fail closed
  on any row that normalizes to the same key.
- **Errors are mapped locally in the controller.** The shared `ResultProblemDetailsMapper`,
  `AuthorizationPolicies.cs`, `DependencyInjection.cs`, `IFileStorage` and
  `LocalDiskFileStorage` stay read-only. Use a bounded `Cms.Landing.Media.*` code prefix.
- **No schema change.** No migration is authored; this phase has no persistence of its own.
- **Accepted limitation to document:** reference discovery and physical deletion are not
  atomic with concurrent editorial writes; a reference created between the check and the
  delete can still leave a dangling path. There is no cross-module foreign key over a
  stored key and none may be invented; document the race and keep the window as small as
  the implementation allows (check and delete in one handler, no second query round trip).
- **No change** to upload behavior, public Landing reads, the landing section lifecycle,
  storage layout, or any existing media used by other modules.

## Exact writable manifest

- `src/API/Sanad.API/Controllers/AdminLandingController.cs` (the delete route + its local
  error mapping only; the existing upload/lifecycle routes keep their behavior)
- `src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Landing/LandingAdminCommands.cs`
  (the delete command/handler; add the reference-discovery query here or in
  `LandingAdminQueries.cs` if that file already owns that kind of read)
- `src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Landing/LandingAdminQueries.cs`
  (only if the reference lookup belongs there)
- **New:** `tests/Sanad.UnitTests/Cms/LandingMediaDeletionTests.cs`
- **New:** `tests/Sanad.UnitTests/API/AdminLandingMediaControllerTests.cs`
- `docs/admin/landing.md` (new section: route, role, statuses, retained-reference rule,
  race limitation)
- `docs/postman/admins/Sanad.Admin.postman_collection.json` (the delete request in the
  existing style; E08 owns this file in its wave — E07 uses its own dedicated collection)
- **New:** `tools/Invoke-E08LocalHttpVerification.ps1`
- This brief.

Additional files require a bounded Mastermind manifest amendment. `ICmsDbContext.cs` and
`CmsDbContext.cs` are expected to need **no** change (the lookup reads existing `DbSet`s);
if they do, request the amendment rather than editing them silently.

## Focused verification

- Handler/unit tests: an unreferenced landing key deletes; a key referenced by a Draft, a
  Published and an Archived landing section is rejected with 409 and the file survives; a
  key referenced only by retained history is rejected; a key referenced only by a
  Family-home section or its history is rejected; a key equal after normalization (slashes,
  casing, surrounding spaces) is still treated as a reference; a non-existent landing key
  is 404; missing/empty/`private/`/non-`landing/`/traversal keys are 400 and never reach the
  filesystem; the same request twice yields 204 then 404; no other section, history row or
  file is modified by a deletion.
- Controller tests: role matrix (SuperAdmin and ContentAdmin allowed, SupportAdmin, Family,
  Caregiver, Elderly and anonymous denied), exact status codes, and that the response body
  is empty on 204.
- Confirm no migration is created and no other context's snapshot changes.
- Strict Release solution build with zero warnings/errors, then only the affected focused
  classes. No whole-suite run.

## Local HTTP evidence (real, bounded)

`tools/Invoke-E08LocalHttpVerification.ps1` must follow the L1/F6/F7 adapter pattern:

- loopback-only, default `http://localhost:5257`; dot-source
  `tools/Invoke-LocalFixtureVerification.ps1 -LibraryOnly`; run the read-only target
  verification first and fail closed; refuse `SanadBrunoTestDb`.
- Provision only INSERT-only, guarded, idempotent synthetic fixtures: two dedicated admin
  identities (a ContentAdmin allowed, a SupportAdmin denied) with free phone numbers (taken
  already: +201000000001–009, 011, 012, 021–023, 051–054, 061, 062, 064, 065, 081–083 — take
  others, e.g. 091–095), and one landing section row whose `image_path` names the referenced
  asset. Never update, delete or reset an existing row.
- Create the real files through the real API: upload one valid tiny PNG twice through the
  existing `POST /api/v1/admin/landing/media` route (the upload must pass the route's real
  signature check), use one key for the referenced case and the other for the unreferenced
  case. This is a real HTTP write of the adapter's own fixtures, not an inserted database
  row pretending to be a producer.
- Real localhost HTTP checks, each reporting name, method, path, expected status, actual
  status, error code and trace id, secrets redacted: anonymous 401; SupportAdmin 403;
  ContentAdmin upload 201; delete of the unreferenced key 204; the file is gone afterwards
  (a second delete returns 404, proving the first really deleted); delete of the referenced
  key 409 and a byte/size re-check proving the file survives; delete of an unknown landing
  key 404; delete with a missing key, a `private/` key and a traversal key 400; unrelated
  landing reads still answer 200. Never prove a 400/404/409 by hand-writing invalid rows in
  shared tables. Every media check drives the route's real verb — the media route is
  DELETE-only and uploads are POST, so a GET answers 405 and proves nothing.
- Start or reuse one API instance on the pinned port, stop only the instance this run
  started, and log out every session it creates
  (`POST /api/v1/auth/sessions/logout`).
- Because the L1 `Invoke-PlannedChecks` has no per-check try/catch, structure checks so one
  failure still reports its name/method/path/expected/actual and does not discard the batch.
- No live provider call, no Production access, no authenticated Production request. The
  result JSON must separate HTTP-executed checks from unit-only checks and list honest
  limitations (including that only one referenced-status combination is exercised over HTTP
  and the rest are unit-tested, and the documented check-then-delete race).

## Dispatch amendment

- Base: `main` at `9436dbf09a66c338f05fdc65301a55c45bb9e18b` (F8 closeout). Recorded base SHA.
- Worker: `opencode/beam-free#high`; wave 3, paired with E07 (disjoint files).
- Runtime slot: Mastermind-held (single, serialized). Build and focused tests run; no API/HTTP/DB/migrations.
- E05 Family-home reference source: `cms.family_home_sections` (`items_json` stores ImageKey/IconKey) and `cms.family_home_section_history`.
- `AdminLandingController` was not edited by E05; E08 owns the delete route.
- Fixture phone numbers used: `+201000000100` and `+201000000101` (two INSERT-only dedicated
  identities), with the shared `content.admin@test.sanad.local` row supplying the password
  hash. No other row is written by SQL.

## Acceptance and release gate

Mastermind accepts only after: delivery present; focused tests and the adapter are real and
pass on the frozen tree; no migration was authored; the docs and Postman parse and match
source; `git diff --check` is clean. Commit, push and release require the Owner's standing
authority and a serialized release slot; record the deployed revision, migration counts,
9/9 heads, backup path and limitations in a release receipt before marking `completed`.

## Current result

- State: `ready_for_review` (delivery completed by the Mastermind after the worker
  handed back code without the two test artifacts or the adapter).
- Base: `main` at `9436dbf09a66c338f05fdc65301a55c45bb9e18b`.
- Files changed: `src/API/Sanad.API/Controllers/AdminLandingController.cs`,
  `src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Landing/LandingAdminCommands.cs`,
  `src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Landing/LandingErrors.cs`,
  `docs/admin/landing.md`, `docs/postman/admins/Sanad.Admin.postman_collection.json`, this brief.
- Files added: `tests/Sanad.UnitTests/Cms/LandingMediaDeletionTests.cs`,
  `tests/Sanad.UnitTests/API/AdminLandingMediaControllerTests.cs`,
  `tools/Invoke-E08LocalHttpVerification.ps1`.
- Commands run: `dotnet build Sanad.slnx -c Release /p:TreatWarningsAsErrors=true`
  -> 0 warnings, 0 errors. Focused run
  (`FullyQualifiedName~LandingMediaDeletionTests|FullyQualifiedName~AdminLandingMediaControllerTests`)
  -> 51 passed, 0 failed. Regression run (`FullyQualifiedName~Sanad.UnitTests.Cms|FullyQualifiedName~Sanad.UnitTests.API`)
  -> 1096 passed, 1 failed: the retained pre-existing baseline failure
  `PortalRemainingResponseContractTests.NestedResponseRefs_ResolveToActualShapes`,
  untouched by this phase. `git diff --check` -> only the repository-wide LF/CRLF
  warnings, no whitespace errors.
- HTTP adapter executed by the Mastermind in the single runtime slot on
  `http://localhost:5257`, once E07's single Caregivers migration had been applied to the
  local `SanadDb`: the shared nine-context read-only target verification and the F6 target
  guard both passed, nothing was written before them, and the adapter reports **26 checks,
  26 passed, 0 failed** (exit 0). The 26 cover the route shape, anonymous 401, SupportAdmin
  403, the unrelated landing read still answering 200, six invalid-key 400s carrying
  `Cms.Landing.Media.InvalidKey`, a well-formed missing key 404 carrying
  `Cms.Landing.Media.NotFound`, upload 201, unreferenced delete 204 plus a second delete
  404, a served landing draft blocking the delete 409 with `Cms.Landing.Media.Referenced`
  and 204 once the draft was removed, the same pair for a served family-home draft, and the
  two session logouts.
- Three defects in the Mastermind-authored adapter were found by actually running it and
  were fixed before acceptance. None of them touched product code:
  1. `"$media?key=..."` inside a double-quoted string made PowerShell read `$media?key` as
     one variable name, so the path collapsed to `=landing/<guid>.png` and every media URL
     became `http://localhost:5257=landing/...`, which throws
     `Invalid URI: Invalid port specified` before the first check runs. All 15 occurrences
     now use `"${media}?key=..."`. Parsing cleanly is not the same as running: the E07
     adapter had the same class of bug caught earlier by its offline smoke, and this one was
     only caught by execution because the failure aborted the run instead of corrupting a
     query string.
  2. the media checks drove `GET` on a DELETE-only route, so the API answered 405 and the
     checks proved nothing about the policy or the status mapping. They now drive `DELETE`
     (uploads stay `POST`), and the two well-formed-key checks were renamed
     `read.*` -> `delete.*` to match what they actually exercise.
  3. the family-home banner fixture supplied an English title without an Arabic one, which
     `FamilyHomeContentRules.ValidatePair` rejects with `Cms.FamilyHome.SectionInvalid`; the
     fixture now carries both languages, as the pinned rule requires.
- Delivery versus the brief's fixture instruction: the brief described a SQL-inserted landing
  section row naming the referenced asset. The delivered adapter instead creates every asset
  and every reference **through the API under verification** (upload, landing draft, then
  family-home draft) and uses SQL only for the two INSERT-only identities. The effect is the
  same or stronger — no row is hand-written that the product cannot write itself — and the
  app-audience 403s are covered by the controller policy matrix test instead of over HTTP.
- No migration was authored by this phase, and no other module's snapshot changed.
- Mastermind corrections to the handed-back code: the added Postman request was
  duplicated and the collection had lost its trailing newline (both fixed, 42 requests,
  valid JSON); the `docs/admin/landing.md` endpoint summary was missing the new route
  (added); the controller now reuses the inherited `BadRequestWithCode` and the
  `LandingErrors` constants instead of a duplicated local helper and hardcoded code
  strings; the unused `Microsoft.Extensions.Options` using was dropped from the handler
  file.
- Limitations: over HTTP, reference discovery is proved for a served landing draft and a
  served family-home draft; every other reference source (each section status, retained
  history, history details, normalization equivalence, unreadable stored payload) is
  proved by the focused unit suite against the real handler and the real local storage.
  The documented check-then-delete race is unchanged, and because
  `LocalDiskFileStorage.DeleteAsync` succeeds for an absent file, the 404 decision
  remains the controller's existence probe.
