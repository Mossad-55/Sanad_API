# Phase 1 report — Rating unification

- **Phase:** 1. Rating unification
- **Branch:** `opencode/opus-work`
- **Base SHA:** `fe91d64d0f8b753d336f2fe8d3dc6b35e1a1e82b`
- **Final SHA (content commit):** `e8d9e6f86cc5ef02d22af7f05e42c7b2055d81bc`
- **Effective model:** `opencode/mimo-v2.6-flash-free` (Windows, dotnet build via PowerShell)

## Changed files

- `src/Modules/Community/Application/Sanad.Modules.Community.Application/Ratings/AddRatingCommand.cs` — `string RatingValue` → `int Value`.
- `src/Modules/Community/Application/Sanad.Modules.Community.Application/Ratings/AddRatingCommandHandler.cs` — integer range validation (1–5) returning `Community.Rating.Invalid`; parse/silent-fail path removed. Not-found/unpublished post semantics unchanged (`false`).
- `tests/Sanad.UnitTests/Community/CommunityRatingTests.cs` — new focused suite (valid values, out-of-range, update-in-place, unpublished/unknown post regression).
- `tests/Sanad.UnitTests/Community/CommunityModerationAndCheckInTests.cs` — one-line compile fix (`RatingValue = "5"` → `Value = 5`) in the existing rating test; see manifest note below.
- `docs/postman/app/Sanad.App.Community.postman_collection.json` — rating body `{"ratingValue": "5"}` → `{"value": 5}` plus description.
- `docs/app/community/writes.md` — rating contract touch-up (integer `value`, 400 `Community.Rating.Invalid`, no legacy shim).

`Sanad.API/Controllers/CommunityPostsController.cs` needed no edit: camelCase
JSON `value` binds to the renamed `Value` property under the existing
`[FromBody] AddRatingCommand` binding; the manifest-listed binding change was
already satisfied by the default binder.

## Verification (all on final content commit)

| Command | Exit | Result |
| --- | --- | --- |
| `dotnet build Sanad.slnx -warnaserror` | 0 | 0 warnings, 0 errors |
| `dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj --filter "FullyQualifiedName~Community" --no-build` | 0 | Passed 42 / Failed 0 / Skipped 0 |
| `ConvertFrom-Json` parse of `docs/postman/app/Sanad.App.Community.postman_collection.json` | 0 | valid JSON |
| `git diff --check` | 0 | no whitespace errors (one benign LF/CRLF notice on `docs/app/community/writes.md`) |

## Skipped / blocked checks

- Full unit + architecture suites: deferred to Phase 7 per tasks.md (run once at the end).
- Bruno stateful runs: no provider calls authorized this package; Bruno community rating bodies (`tests/Bruno/.../06-rate-unauthenticated.bru`, `16-rate-authenticated.bru`) still send legacy `ratingValue` — out of the Phase 1 manifest, scheduled for the Phase 7 collection sync.

## Manifest note

`tests/.../Community/*Rating*` is the exclusive test manifest; the pre-existing
rating test lived in `CommunityModerationAndCheckInTests.cs` and would not
compile after the command change. A one-line `Value = 5` fix was applied there
instead of leaving a broken build. No behavioral change to that test.

## Dependencies confirmed in main

- Community module, `CommunityDbContext` (InMemory test pattern), `Result`/`Error`
  + `ResultProblemDetailsMapper` (unmapped codes default to 400 — no shared
  contract change needed), Postman community collection, `writes.md` — all
  present at base `fe91d64`.

## Remaining owner decisions

None for this phase.
