# Phase 2 report — status, sessions, notices

> The continuation evidence below (2026-10-12) supersedes the earlier live-gate
> paragraph in this historical report.

- Branch: `opencode/admin-users`
- Base: `b0ac0d040adb314e0bfaa140becc14dd00d7cd31` (`main`)
- Implementation began at Phase 1 tip `c7cfd48d0ada72e5f3e7261595fb55d5d59293c2`.
- Implementation revision: `4e79c48ef1fd5630d7f66a610fcc12770f9e882d`.
- Phase status: implementation and automated/static gates complete; live-read gate remains pending. The task checklist remains In progress.

## Changed files

- `src/API/Sanad.API/Controllers/AdminUsersController.cs`
- `src/API/Sanad.API/Controllers/Requests/AdminUserStatusRequest.cs`
- `src/API/Sanad.API/DependencyInjection.cs`
- `src/Modules/Identity/Application/Sanad.Modules.Identity.Application/Users/Admin/AdminUserQueries.cs`
- `src/Modules/Identity/Application/Sanad.Modules.Identity.Application/Users/Admin/AdminUserStatusCommands.cs`
- `src/Modules/Identity/Domain/Sanad.Modules.Identity.Domain/Users/User.cs`
- `tests/Sanad.UnitTests/Identity/AdminUserManagementTests.cs`
- `docs/admin/user-management.md`
- `docs/postman/admins/Sanad.Admin.postman_collection.json`
- `docs/internal/work/admin-users/tasks.md`
- `docs/internal/work/admin-users/reports/phase-2-status.md`

## Verification

- Build: `dotnet build Sanad.slnx --no-restore -m:1 --nologo /p:TreatWarningsAsErrors=true` — exit 0, 0 warnings, 0 errors.
- Focused tests: `dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj --no-restore --filter FullyQualifiedName~AdminUserManagementTests -m:1 --nologo /p:TreatWarningsAsErrors=true` — exit 0; 13 passed, 0 failed, 0 skipped. New status/session query paths are covered with InMemory and SQLite cases.
- Postman Admin collection JSON parse — passed. Phase 2's suspend, reactivate, block, and session-revoke routes are represented; no missing or orphaned Admin user routes.
- Repository contract map — 2 missing Postman mappings are for unowned Landing routes present in concurrent workspace changes; 0 Postman orphans. No unrelated Postman/CMS edits were made. Existing Bruno gaps are outside this phase's owned manifest.
- `git diff --check` — clean.
- Live API read check — authorized and attempted on `SanadBrunoTestDb` at localhost:5235. API ran in Development with module migrations and seeders disabled and configured module connections pinned to that database. The required booking-notification outbox row was authorized for the first run; later read-only preflight found zero currently eligible booking, transfer, email, or push rows. The first login request returned 400 because `DevicePlatform=Unknown`; corrected `Web` login using the configured seed credentials returned 401 `Identity.Login.InvalidCredentials`. Read-only aggregates found exactly one SuperAdmin account; the email in the tracked local-fixture helper matches it, but that helper's configured password did not authenticate. Neither tracked Bruno local environment's admin email matches the existing SuperAdmin. A later API start served `GET /openapi/v1.json` with HTTP 200; that process also had migrations and seeders disabled. No access token/session was created, and list/detail/sessions/revoke were not run. All API processes were stopped; port 5235 was verified closed. Startup logged the existing `CareHomeReviewNotificationProcessor` LINQ translation failure while querying Identity users; no correction was made because it is outside this phase's owned scope. Live gate remains incomplete pending valid credentials for the existing fixture account.
- Mutating live status checks — not run; require separate per-run approval.

## Notes and limitations

- Suspend and block revoke every active device session; reactivate supports Suspended and Blocked as frozen. The last active SuperAdmin, seeded SuperAdmin, and self-action guards are covered by automated tests.
- Status-change notification enqueue uses the existing Notifications DbContext/outbox seams and logs enqueue failures without undoing the status mutation.
- Phase 2 actions are not yet audited. The frozen plan assigns the audit table and integration to Phase 4; Phase 4 must add audit coverage to these handlers before the overall goal is complete.
- No migration apply, seed creation, push, merge, publish, or deploy was performed.

## Continuation — 2026-10-12

- Branch: `opencode/admin-users`; current `HEAD` is `67e18db1e2d634991d4bfd773b1f57ba114783ce`.
  Phase 2–4 changes remain uncommitted in the worktree; unrelated CMS/Landing edits
  remain untouched.
- Full build remains valid for the current code: `dotnet build Sanad.slnx --no-restore -m:1 --nologo /p:TreatWarningsAsErrors=true` — exit 0, 0 warnings, 0 errors.
- Focused tests: `dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj --no-restore --filter FullyQualifiedName~AdminUserManagementTests -m:1 --nologo /p:TreatWarningsAsErrors=true` — host-context retry exit 0; 17 passed, 0 failed, 0 skipped. Earlier sandbox attempts failed before test execution when testhost could not connect.
- `git diff --check` — exit 0; Git emitted only an LF-to-CRLF working-copy notice. Admin and Public Postman collections parse as JSON. After adding the two public Landing GET requests, the contract map reports 533 controller signatures, 594 Postman requests, 0 missing Postman mappings, and 0 Postman orphans. It still reports 126 Bruno mappings missing and 0 Bruno orphans; Bruno gaps are explicitly excluded per the owner's latest direction.
- Live API on `localhost:5235`, pinned to `localhost:5432/SanadBrunoTestDb`, Development, migrations/seeders disabled: seeded-SuperAdmin login 200; list 200; detail 200; sessions overview 200. The API was stopped and port 5235 verified closed.
- Two user-authorized one-time host credential resets were needed: the first live login request was malformed (`devicePlatform` sent as a string) and returned 400; the corrected second login succeeded with the numeric `Web` enum value. Credentials stayed process-local and were cleared. Both resets revoked prior active sessions. The second login's session-revoke API call returned 500; its one exact verification session was revoked directly by unique device name (1 row). The audit table was confirmed absent. The API error log was not retained, so the table's absence is a strong dependency concern, not a proven root cause for that 500.
- API startup was within the user's previously authorized scope for the one due booking-notification outbox row. No migration was applied and no new account/seed was created. The row's post-startup state was not read back.
- Phase status remains **Blocked** for closeout: API session-revoke did not pass, and the audit table needed by the mutation/audit paths is absent. Unrun live mutations are not claimed as passes. No Phase 5 work was started.
- Follow-up: the owner asked that Bruno mapping be ignored while Postman mappings are complete. Postman now satisfies 0 missing/0 orphan. Source inspection confirmed session-revoke writes an `AdminUserAction`; the live database lacks that table. This explains the 500 dependency, though the API error log was not retained to prove the exact exception.

## Final continuation — 2026-10-12

- Final branch tip: `opencode/admin-users` at `67e18db1e2d634991d4bfd773b1f57ba114783ce`; base `b0ac0d040adb314e0bfaa140becc14dd00d7cd31`.
- Owner explicitly authorized applying only `20261012064508_AdminUserActions` to `localhost:5432/SanadBrunoTestDb`. EF confirmed it was the sole pending Identity migration; application completed successfully.
- The first post-migration reset committed, but its API launch failed before login because stdout and stderr were directed to the same file. No test session was created; that process-local password was cleared. After the owner authorized a corrective reset, the subsequent live sequence completed as recorded below.
- Following a corrective process-local SuperAdmin credential reset, login returned 200; list, detail, sessions overview, and audit each returned 200; targeted session revoke returned 204. Read-only DB confirmation found one recent audit row and zero active sessions with the verification device name. API stopped and port 5235 closed.
- Build passed with 0 warnings and 0 errors. Focused `AdminUserManagementTests`: 17 passed, 0 failed, 0 skipped (host-context run; sandbox testhost did not complete its IPC handshake).
- Postman contract map: 533 controller signatures, 594 Postman requests, 0 missing and 0 orphan Postman routes. The checker reports 126 Bruno gaps; owner explicitly excluded Bruno. Both scoped Postman collections parse; `git diff --check` is clean (LF/CRLF notices only).
- Live suspend/reactivate/block mutations were not run. No push, merge, deploy, or Phase 5 work occurred.
