# Phase 1 report — list, detail, sessions

- Branch: `opencode/admin-users`
- Base: `b0ac0d040adb314e0bfaa140becc14dd00d7cd31` (`main`)
- Implementation began from branch tip `9bbcc3cb4294705522f3b02e45a62d9b88ff148b`; owner amendments `73f216e` and `ffa4832` were present.
- Final implementation revision: recorded in the phase closeout commit and worker report.

## Changed files

- `src/API/Sanad.API/Authorization/AuthorizationPolicies.cs`
- `src/API/Sanad.API/DependencyInjection.cs`
- `src/API/Sanad.API/CareHomesIntegration/ApiCareHomeOwnerFacilityLookup.cs`
- `src/API/Sanad.API/Controllers/AdminUsersController.cs`
- `src/API/Sanad.API/Controllers/Requests/AdminUserListRequest.cs`
- `src/API/Sanad.API/Controllers/Requests/AdminUserSessionsRequest.cs`
- `src/Modules/Identity/Application/Sanad.Modules.Identity.Application/Users/Admin/AdminUserQueries.cs`
- `tests/Sanad.UnitTests/Identity/AdminUserManagementTests.cs`
- `docs/admin/user-management.md`
- `docs/postman/admins/Sanad.Admin.postman_collection.json`
- `docs/internal/work/admin-users/reports/phase-1-list.md`

## Verification

- Build: `dotnet build Sanad.slnx --no-restore -m:1 --nologo /p:TreatWarningsAsErrors=true` — exit 0, 0 warnings, 0 errors.
- Focused tests: `dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj --no-build --no-restore --filter FullyQualifiedName~AdminUserManagementTests --nologo` — exit 0; 5 passed, 0 failed, 0 skipped. The list/detail/session query paths, including SQLite translation twins and name sorting, ran against both InMemory and SQLite providers.
- Postman collection JSON parse — passed; exactly 3 requests added.
- `docs/internal/tools/Verify-ApiContractMapping.ps1` — Postman: 522 controller signatures, 582 requests, 0 missing, 0 orphan. Existing Bruno census: 115 missing and 0 orphan; no Bruno files are in this phase's owned manifest.
- `git diff --check` — clean.
- Live API: pinned module connections to `localhost:5432/SanadBrunoTestDb`; Development; startup migrations and seeders disabled; existing SuperAdmin login 200; list 200; detail 200; sessions overview 200. Five temporary sessions created by verification retries were found by the verification device name and revoked with 204. API process stopped; port 5235 verified free.

## Notes and limitations

- Search and name ordering run in memory after SQL-translatable account/status filters because this module persists the searchable value objects through EF converters that cannot translate their string members. SQLite twins cover these paths; query results and sort semantics follow the frozen contract.
- Per explicit owner approval, API startup processed the one already-due care-home booking-notification outbox fixture row. The existing recipient query failed EF translation at `CareHomeBookingNotificationProcessor.ResolveRecipientsAsync`; the processor's normal retry handling marked the item failed. No new seed, migration, or API fixture account was created. This is separate from the successful Phase 1 API reads.
- Contract deviations: none. No push, merge, publish, deploy, or migration apply performed.
