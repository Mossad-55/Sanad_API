# FUR-08 Worker Delivery — Owner dispute readback

Status: COMPLETE (local phase implementation; no release authorized)
Worker session: same child continuation, `openai/gpt-6-luna#medium`
Base: `2bf92a0` plus pre-existing local delta. Prior persisted changes were retained; the earlier BLOCKED report incorrectly said no edits and is superseded here.

## Changed feature files
- `src/API/Sanad.API/Controllers/CareHomeBookingRequestsController.cs` — owner-authorized read-only GET and typed 200 response metadata.
- `src/Modules/CareHomes/Application/Sanad.Modules.CareHomes.Application/Bookings/CareHomeStayCommands.cs` — paged, owner-facility-scoped read query; post-filter count, deterministic opened-time/case-ID order, enrichment/null projection, validation. Status accepts only Open/Resolved names case-insensitively; numeric/undefined values rejected.
- `tests/Sanad.UnitTests/CareHomes/CareHomeReadbackTests.cs` — empty/no-facility, owner isolation, filters/count/order/paging, nullable enrichment/outcomes, timestamps, field exclusion, and validator cases.
- `tests/Sanad.UnitTests/API/CareHomeBookingControllerTests.cs` — owner route/policy/query/OpenAPI/read-only assertions and error mapping.
- `tests/Sanad.UnitTests/API/PortalRemainingResponseContractTests.cs` - OpenAPI route/schema projection contract.
- Generated OpenAPI assertions cover page/pageSize/totalCount/items envelope fields and nullable item semantics for every nullable contract field; a deterministic equal-opened-time test verifies case-ID-descending tie order.
- `docs/care-homes/booking-lifecycle.md` — owner list behavior, filters, nullability, no-facility behavior, no-mutation/support guidance.
- `docs/postman/care-homes/Sanad.CareHomes.Owner.postman_collection.json` — read-only owner list request, paging/status guidance, response notes.

## Verification
- Final correction verification: `dotnet build Sanad.slnx --configuration Release --no-restore -p:TreatWarningsAsErrors=true` - passed, 0 warnings, 0 errors.
- `dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj --configuration Release --no-build --no-restore --filter "FullyQualifiedName~OwnerDisputes|FullyQualifiedName~Owner_check_in_disputes|FullyQualifiedName~OwnerCheckInDisputes"` - passed 20/20, 0 failed, 0 skipped (includes handler, route and OpenAPI coverage).
- `python -c` JSON parse and route check for the Owner collection - passed; valid JSON, expected GET route, explicit paging defaults, and optional status disabled by default (no HTTP request made).
- `git diff --check` - passed; whitespace check clean. Git emitted line-ending conversion notices for existing/newline-normalized text files only.

## Assumptions, data effects, limitations
Currency enrichment uses the established fixed EGP convention; no currency is persisted on booking. No database/fixture writes, migration, provider call, Bruno run, production operation, commit, push, publish, or deploy occurred. No live endpoint runtime test was authorized or performed.

## Explicit no-release status
No release action authorized or performed. Mastermind review remains required.
