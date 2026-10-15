# PORTAL-02 report — library multipart parsing and actionable validation

Status: Done (worker implementation complete; mastermind review follows).
Do not begin PORTAL-03 from this report. PORTAL-01 Identity changes untouched.

## Intake

- Worker base: pushed `main` `3f7c685686691aeab362bbb77ebc5463bf47a0cb`
  (launch-message revision: PORTAL-01 review + PORTAL-02 brief). At intake
  `main == origin/main` with a clean tree; no reset to the older `4f71493`
  delivery. No unrelated delta touched overlapping files.
- No API startup, database reset, provider send, migration application, VPS
  access, deployment, or publish occurred. All persistence is isolated EF
  InMemory; storage is a recording fake. No real disk or provider writes.

## Root cause

- `AdminLibraryController.TryParseSections` used default case-sensitive
  `System.Text.Json` options while the guide/Postman show camelCase, so
  documented payloads deserialized to entries with default/empty values.
- `sectionsJson=[null]` reached an unchecked `item.DisplayOrder` dereference
  outside the `JsonException` catch → `NullReferenceException`.
- Domain validation failures collapsed into `Cms.LibraryContent.Invalid` with
  a generic detail and no field information.

## Fix (approved behavior only)

- `src/API/Sanad.API/Controllers/Requests/LibraryContentValidation.cs` (new,
  Library-scoped): case-insensitive sections parser (default JSON number
  handling unchanged; malformed/wrong-shape JSON, null entries, and null roots
  handled without throwing) plus a request validator mirroring the domain
  rules. Fixed safe messages keyed by form field (`type`, `categoryId`,
  `videoUrl`, `durationSeconds`, titles, summaries) or indexed section path
  (`sectionsJson[0].displayOrder`, `sectionsJson[0].arabicBullets[0]`).
  Empty/null/omitted bullet arrays keep existing (empty) semantics; omitted
  `sectionsJson` on update resolves to the persisted sections; explicit `[]`
  stays an empty list. Domain validation remains the final invariant check in
  the unchanged command handlers.
- `src/API/Sanad.API/Controllers/AdminLibraryController.cs`: parse and
  content validation run before any upload; update pre-loads the persisted
  content (missing → existing 404; non-Draft → existing upload-then-dispatch
  order so file/lifecycle precedence is unchanged) and validates type-specific
  rules against the persisted type while the request `Type` stays
  parsed-but-ignored. Failures return 400 with the preserved codes
  (`Cms.Library.InvalidSections`, `Cms.Library.InvalidType`,
  `Cms.Library.InvalidCategory`, `Cms.LibraryContent.Invalid`), a fixed safe
  detail, an `errors` map, and the trace ID. New-cover-only cleanup is
  unchanged: rejected pre-upload requests never touch storage; handler
  failures delete only the newly uploaded key.
- `src/Modules/Cms/Application/.../Library/LibraryErrors.cs`: added the
  preserved `Cms.Library.InvalidType` / `Cms.Library.InvalidSections`
  errors. No domain rule, persistence-model, shared-Error, ApiControllerBase,
  or global-mapper change.
- Equivalent small local design as permitted: validation lives in the API
  request layer (it owns the form-field/indexed-path error shape) while the
  handlers keep final domain enforcement; no command response-type change was
  needed.

Accepted example (Video, invalid duration) returns exactly the brief payload:
400, `Bad Request`, detail `One or more library content fields are invalid.`,
code `Cms.LibraryContent.Invalid`, `errors: {durationSeconds: [...]}`,
plus `traceId`.

## Fixture readiness (mandatory gate, read-only first)

- Configuration: isolated EF InMemory `CmsDbContext` per test
  (`EnsureCreated`, GUID database names); recording fake `IFileStorage` with
  per-test keys (`library/test-<n>.png`), save/delete evidence, and an
  optional forced save failure; deterministic tiny PNG (8-byte signature,
  `image/png`); `DefaultHttpContext` with synthetic trace IDs. A minimal
  dispatch adapter delegates create/update/admin-detail to the real command
  handlers — method-level controller dispatch, no HTTP host, no migrations,
  no background/provider work.
- Seed inspection before reuse: smallest active/inactive categories, Draft
  Article/Video contents, Published/Archived controls, and target sections
  created through the real create handler (or domain calls for lifecycle
  states); category/content Type/status/sections/cover read back with
  `AsNoTracking` before the tested action. Creation scenarios prepare only
  actor/category/storage prerequisites. Mutated fixtures are never reused as
  though they held old values; failed-update assertions re-read fresh state.
- Synthetic labels/keys only; no secrets or Production data.

## Regression evidence

- Pre-fix run of the new `AdminLibraryControllerTests`: 33 failed / 6 passed
  of 39 — camelCase create lost its sections, `[null]` threw
  `NullReferenceException` at `AdminLibraryController.cs` (`item.DisplayOrder`
  via `TryParseSections` ← `TryParseContentRequest` ← `CreateContent`), and no
  failure carried an `errors` map. (One initial failure was a test-fixture
  bug — lifecycle target published through a detached entity — corrected in
  the test before the fix.)
- Post-fix coverage (39 controller tests, all passing): camelCase/PascalCase
  equivalence of stored sections; valid Video 201; `[null]`/malformed/wrong-
  shape/type-mismatch 400 `InvalidSections` without upload or row; invalid
  type/malformed category 400 with field errors; missing/empty/zero/duplicate/
  over-count orders, required/overlong text, bullet entry/count violations,
  Video URL/duration and mixed Article/Video fields with indexed error paths;
  exact example payload shape; update omission preserving sections/cover;
  explicit `[]` rejection without state change; persisted-type validation with
  ignored request Type; Draft-only/missing/unknown-category/inactive-category
  outcomes preserved; pre-upload validation leaving no cover/row; new-cover-
  only cleanup on handler failure for create and update; storage-error codes
  unchanged without dispatch; spoofed image rejected without upload; no raw
  parser/database text in errors.
- Canonical multipart examples from the guide/Postman (camelCase Article with
  bullets; Video with URL+duration) are exercised literally through
  controller dispatch with a synthetic file placeholder; Postman create/update
  requests now carry matching form-data examples.

## Checks (after final code change)

- `dotnet build Sanad.slnx --no-restore -m:1 --nologo
  /p:TreatWarningsAsErrors=true` — exit 0, 0 warnings, 0 errors.
- Unit focused: `... --filter
  "FullyQualifiedName~Sanad.UnitTests.API.AdminLibraryControllerTests"` —
  exit 0; 39 passed, 0 failed, 0 skipped. Existing
  `Sanad.UnitTests.Cms.LibraryTests` — 13 passed, 0 failed (handlers
  unchanged, still green).
- Architecture suite — 1 passed, 0 failed. No reset-based PG fixtures run.
- `Sanad.Admin.postman_collection.json` parses as JSON; contract mapping
  gate: 556 controller signatures / 620 Postman requests / 0 missing /
  0 orphan; `git diff --check` clean. Bruno excluded; full-suite release
  testing remains for PORTAL-07.

## Remaining findings / handoff

- None blocking PORTAL-02: acceptance criteria pass within the owned manifest.
- Pre-existing note (unchanged, out of scope): `GetContent` list/detail
  `InvalidStatus`/`InvalidSort` paths and the WellnessTips parser were not
  touched. The `LibraryContentSectionRequest` record moved with the parser
  into `Controllers/Requests` (same assembly); no other production caller
  existed.
- Mastermind review follows; PORTAL-03 is not started and nothing was
  deployed.
