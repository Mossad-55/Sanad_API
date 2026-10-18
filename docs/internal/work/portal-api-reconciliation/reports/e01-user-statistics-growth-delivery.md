# E01 user statistics and growth — worker delivery

## Status
COMPLETE (Owner approved commit/push; deployment not performed).

## Changed files
- `src/API/Sanad.API/Controllers/AdminUsersController.cs` — added `GET statistics`.
- `src/Modules/Identity/Application/Sanad.Modules.Identity.Application/Users/Admin/AdminUserStatisticsQueries.cs` — new query/handler.
- `tests/Sanad.UnitTests/Identity/AdminUserStatisticsTests.cs` — new focused tests.
- `tests/Sanad.UnitTests/API/AdminUserStatisticsApiTests.cs` — new focused HTTP tests.
- `tests/Sanad.UnitTests/API/PortalRemainingResponseContractTests.cs` — registered the new route in SuccessOperations.
- `docs/admin/user-management.md` — new section.
- `docs/postman/admins/Sanad.Admin.postman_collection.json` — new request.

## Verification
- `dotnet build Sanad.slnx -c Release`: passed; 0 warnings, 0 errors.
- `dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj -c Release --no-build --filter "FullyQualifiedName~AdminUserStatistics|FullyQualifiedName~SuccessOperation_PublishesActualResponseSchema"`: passed; 67/67.
- The earlier `PortalRemainingResponseContractTests` class run was 39/40; the sole failure is the unrelated owner-dispute nullable-schema assertion at `PortalRemainingResponseContractTests.cs:214`. The E01 success-schema test passed in the focused 67-test run.
- Postman JSON parsed with PowerShell `ConvertFrom-Json`; request present.
- `git diff --check`: passed.

## Notes
- `usersByStatus` and `usersByAccountType` are zero-filled numeric enum-valued arrays (`{status, count}` and `{accountType, distinctUserCount}`).
- Cairo month boundaries computed in local time then converted to UTC; only `CreatedOnUtc` is projected (no PII rows).
- No migration, config, persistent fixture, provider, Bruno, or live API.

## Mastermind review
- Aligned the account-type response property with the approved contract: `distinctUserCount` (not `count`); API JSON assertions cover numeric enum values and the property name.
- Final strict Release solution build passed with zero warnings/errors; final focused endpoint/OpenAPI checks passed 67/67.
- E01 is reviewed and Owner-approved for commit/push. Deployment is paused until its target and procedure are identified; report back before deployment.
