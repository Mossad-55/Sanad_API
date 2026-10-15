# PORTAL-02 — library multipart parsing and actionable validation

Role: bounded implementation/test/documentation worker. Execute the approved
PORTAL-02 scope, then return for mastermind review. PORTAL-01 is complete;
do not reopen its Identity changes or begin PORTAL-03.

## Intake and baseline

Read root AGENTS.md, governance, Mastermind_Handoff and PORTAL-02 plus the
mandatory fixture-readiness section of `../plan.md`. Read current Library
controller/commands/domain/tests before implementing; no whole-project audit.

Reviewed worker delivery on main: `4f714931ffd87f806c0e11d1b889629c3f6c083f`.
The following mastermind commit strengthens only Phase 01 tests, records its
review, updates handoff/index/plan and adds this brief. The launch message pins
that final pushed revision: use it as your exact base, verify clean main equals
origin/main, and record actual intake SHA. Do not reset to the older delivery.

Follow current repository policy: work directly on main, one worker at a time,
preserve others' edits and stage explicit owned paths. You are not alone in the
codebase. Do not create/switch branches or worktrees, merge, publish or deploy.
Commit and push completed scoped work normally. Do **not** bypass hooks with
`--no-verify`; if a hook fails, diagnose its actual failure and report any
unresolved dependency rather than skipping it silently.

## Confirmed problem and accepted outcome

Endpoints: POST `/api/v1/admin/library/content` and PUT
`/api/v1/admin/library/content/{id}`, multipart form-data.

The embedded `sectionsJson` parser uses default case-sensitive System.Text.Json
options, while the guide/Postman show camelCase. A syntactically valid array
`[null]` also reaches an unchecked item dereference. Known domain validation
failures collapse into `Cms.LibraryContent.Invalid` with a generic detail.

Accept both existing PascalCase and documented camelCase keys. Use camelCase
in the canonical examples. Preserve successful payloads, business rules,
authorization (`CmsContent`), status codes and stable existing error codes.
Keep existing numeric coercion behavior: case-insensitive property names do
not require changing JSON number handling.
Give invalid parse/content inputs safe actionable field errors and a trace ID.
Example for a Video with invalid duration:

```json
{
  "status": 400,
  "title": "Bad Request",
  "detail": "One or more library content fields are invalid.",
  "code": "Cms.LibraryContent.Invalid",
  "errors": { "durationSeconds": ["A positive duration is required for Video content."] },
  "traceId": "request-correlation-id"
}
```

Preserve `Cms.Library.InvalidSections` for malformed/invalid sections JSON,
`Cms.Library.InvalidType` for invalid type and `Cms.Library.InvalidCategory`
for malformed/unknown category. Existing content-rule failures retain
`Cms.LibraryContent.Invalid`. Parse errors identify `sectionsJson`, `type` or
`categoryId`; indexed section errors identify paths such as
`sectionsJson[0].displayOrder` / `sectionsJson[0].arabicBullets[0]`.
Use fixed safe validation text, never raw DomainException/JSON-parser/database
messages. Unknown content stays 404; Published/Archived update stays 409;
inactive category stays 409; existing storage error codes remain unchanged.

## Rules and compatibility to preserve

- Article: at least one section, no nonempty Video URL, no duration. Video:
  absolute HTTP(S) URL up to 500 characters, duration >0, no nonempty Article
  sections. Type remains immutable after creation.
- On update the current controller parses `request.Type` but the handler uses
  the persisted content Type. Validate type-specific rules against that effective
  persisted type. Do not accidentally change the type or reject previously valid
  updates solely because the ignored, valid request Type differs. Existing
  invalid/unrecognized Type parsing behavior remains.
- On update, omitted sections preserve existing sections. Explicit `[]` clears
  the requested array and must fail for Article. Do not replace omission with
  an empty list. Validate the merged effective content, not just supplied fields.
- Sections: positive unique display order, at most 40, bilingual required
  titles <=200 characters. Content bilingual titles <=200 and summaries <=1000.
  Preserve existing trimming and meaningful required/blank checks.
- Bullets: at most 30 per language, nonblank entries <=500 characters. Empty
  arrays remain valid. Current `LibraryContentSectionInput.ToDraft` normalizes
  omitted/null bullet arrays to empty; preserve that request behavior.
- Handle malformed JSON, wrong root shape, numeric/type mismatches, null list
  elements and invalid nested entries as 400, never NullReferenceException.
  Do not enable permissive parsing that silently turns invalid display orders
  into defaults or relaxes domain invariants.
- Cover: required on create, optional on update; JPEG/PNG/WebP signatures and
  existing 5 MiB request cap unchanged. Omitted file keeps the old cover.
- Validate before uploading when possible. If validation fails after a new
  upload, delete only the newly uploaded uncommitted cover. Failed updates
  preserve persisted content/sections/old cover; clear tracking or use fresh
  readback to prove this. Do not delete an already-committed cover because an
  unrelated later operation fails. Do not turn infrastructure failures into
  input-validation errors.

Use a Library-scoped validation helper/output and controller mapping; retain
domain validation as the final invariant check. Do not redesign shared Error,
ApiControllerBase or the global exception mapper merely to carry these fields.
If adding FluentValidation handling, map only the Library request/command
validation here and preserve the agreed `Cms.Library*` codes. The implementation
may choose an equivalent small local design and must explain it in its report.

## Owned manifest

1. `src/API/Sanad.API/Controllers/AdminLibraryController.cs`.
2. Optional new Library-specific API helper/request-validation file(s), limited
   to `src/API/Sanad.API/Controllers/Requests/LibraryContentValidation*.cs`.
3. `src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Library/LibraryContentCommands.cs`.
4. Optional `LibraryContentValidation*.cs` under that same Application Library
   directory and bounded `LibraryErrors.cs` additions if needed.
5. `src/Modules/Cms/Domain/Sanad.Modules.Cms.Domain/Library/LibraryContent.cs`
   and `LibraryContentSection.cs` only if a small validation-rule reuse/refactor
   is essential; preserve invariants and persistence model. Otherwise untouched.
6. `tests/Sanad.UnitTests/Cms/LibraryTests.cs`.
7. New `tests/Sanad.UnitTests/API/AdminLibraryControllerTests.cs` and, if useful,
   one bounded `LibraryContentValidationTests.cs` under Cms tests.
8. `docs/app/library/README.md` and only the Library CMS folder of
   `docs/postman/admins/Sanad.Admin.postman_collection.json`.
9. `docs/internal/work/portal-api-reconciliation/reports/phase-02-library.md`.
10. `docs/internal/work/portal-api-reconciliation/plan.md`: only PORTAL-02
    status/evidence link. Central handoff/index belong to the mastermind.

No new package, migration, schema change, fixture credentials, provider setting,
global JSON converter, WellnessTips parser change, media cleanup job or unrelated
OpenAPI work. Explain any essential extra file before widening this manifest.

## Mandatory fixture readiness BEFORE API/controller tests

1. Inspect existing Library test helpers/configuration and reuse valid fixture
   builders. Use isolated EF InMemory; SQLite only if the selected mapping/test
   supports it. PostgreSQL and the Production VPS are unnecessary here.
2. Seed the smallest required active/inactive category, Draft Article/Video,
   Published/Archived controls and target sections before invoking tested
   controller/HTTP actions. Reuse suitable states; never reuse a mutated fixture
   as though it still held its old values.
3. Prepare a deterministic valid tiny image/form file and a recording fake
   IFileStorage with per-test keys and save/delete evidence. No real disk or
   provider writes are needed; verify any temporary storage paths before use.
4. Read back category/content Type/status/sections/cover and capture the
   synthetic readiness manifest before starting a host or sending requests.
   Creation scenarios need their actor/category/storage prerequisites ready;
   they do not need the content under test pre-created.
5. Then exercise real controller parsing and real handler/domain validation
   through a minimal dispatch adapter or isolated in-process HTTP host. If using
   a host, prepare its fake configuration and fixtures first and suppress real
   migrations/background/provider work. No live VPS calls, environment secret
   reads, database resets or Development seeding on Production.

## Required tests and documentation evidence

First capture failing local regressions for camelCase sections and `[null]`.
After implementing, require:

- Valid camelCase and legacy PascalCase Article requests produce equivalent
  stored sections; valid Video works. Verify create 201/update 200 shapes.
- Invalid cases cover missing/empty Article sections, zero/duplicate orders,
  null entry, malformed/wrong-shape JSON, required/overlong text, section/bullet
  limits and invalid Video URL/duration or mixed Article/Video fields. Assert
  actual controller status/code/errors paths/trace ID rather than handler-only
  failures. Test useful boundaries, not every permutation of the same rule.
- Update omission preserves sections/file; explicit empty Article sections
  rejects without changing persisted state. Verify effective persisted Type
  handling and normal Draft-only/missing-content/category/storage failures.
- Valid empty/null/omitted bullet arrays retain existing semantics. No raw
  parser or database details leak through returned errors.
- No rejected request leaves a new cover or content row; failed update keeps
  the old cover and sections. Re-read untracked/fresh state. Verify new upload
  cleanup separately from existing-blob preservation.
- Exercise the literal canonical multipart examples from the guide/Postman
  through local controller/HTTP tests. Populate useful form-data examples for
  Article and Video with synthetic file placeholders; description-only claims
  are insufficient. State clearly whether tests were HTTP or method-level.

Run one strict solution build after the final code change, then the relevant
Library/controller/validation tests with `--no-build --no-restore`. Require
successful exit, zero warnings/errors and zero failed tests. Run architecture
if production boundaries change, parse touched Postman JSON, run the Postman-
only mapping gate and `git diff --check`. Bruno is excluded. Full-suite release
testing remains for PORTAL-07; never include reset-based PG fixture classes.

Record intake SHA, exact owned files, configuration/fixture preparation, pre-fix
evidence, post-fix commands/counts/warnings/skips, examples actually exercised,
error payloads, compatibility and limitations. Commit/push normally and return
the exact final SHA/report. Do not begin PORTAL-03 or deploy these changes.
