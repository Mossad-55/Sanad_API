# E01 contract — SuperAdmin user statistics and growth

Approved by the Owner on 2026-10-18.

## Goal and route

Provide the SuperAdmin home dashboard with truthful aggregate Identity-user
counts and a user-registration growth series. Add read-only
`GET /api/v1/admin/users/statistics`, protected by the existing
`SuperAdminOnly` policy. Do not change `/api/v1/admin/users` or broaden its
access.

## Response contract

Return an aggregate response with these fields:

- `totalUsers`: all distinct Identity user rows, across every current status.
- `usersByStatus`: an array of `{ status, count }` entries for each existing
  `UserStatus` value (`PendingVerification`, `Active`, `Suspended`, `Blocked`),
  including zero-count values. `status` is an enum-valued JSON number using the
  current enum mapping. These counts describe current status only, not historical
  status at a date.
- `usersByAccountType`: an array of `{ accountType, distinctUserCount }` entries
  for each currently defined `AccountType` value (`Family`, `MedicalCaregiver`,
  `CompanionCaregiver`, `Elderly`, `SuperAdmin`, `ContentAdmin`, `SupportAdmin`,
  `CareHomeOwner`, `FinanceAdmin`), including zero-count values. `accountType`
  is an enum-valued JSON number using the current enum mapping. A user can have
  multiple account types, so these group counts may overlap and must not be
  summed as `totalUsers`.
- `newRegistrationsByMonth`: new Identity user rows grouped by
  `User.CreatedOnUtc` in the 12 latest Cairo calendar months, including the
  current partial month. Return every month in chronological order as `yyyy-MM`,
  including months with zero registrations. Use Cairo-local month boundaries
  converted from UTC; do not use the server's local timezone. This is new user
  registration growth, not account additions or active-user growth.

Preserve the existing numeric enum JSON serialization by returning enum-valued
properties in array entries; do not use enum-keyed dictionaries/string enum
keys. Document numeric values and names in the user-management guide. Do not add
query parameters in this version. The screenshot's numbers are mockup content,
not expected data.

## Privacy, authorization, and screen boundary

- Only Normal-access `SuperAdmin` may read the endpoint, following the existing
  `SuperAdminOnly` policy. SupportAdmin, ContentAdmin, and FinanceAdmin remain
  denied as they are for the existing user list.
- Return aggregates only: no IDs, names, emails, phone numbers, row-level user
  data, search, drill-down or export.
- Do not add the screenshot's “services growth” series until the Owner defines
  its metric/source. The unclear account/category card from the screenshot does
  not authorize a new metric; return the defined account-type aggregates only.
- Financial monitoring, bookings/revenue, and unrelated dashboard panels are
  out of scope. No frontend implementation is authorized.

## Owned files / expected manifest

- `src/API/Sanad.API/Controllers/AdminUsersController.cs`
- Add `src/Modules/Identity/Application/Sanad.Modules.Identity.Application/Users/Admin/AdminUserStatisticsQueries.cs`
- Add `tests/Sanad.UnitTests/Identity/AdminUserStatisticsTests.cs`
- Add `tests/Sanad.UnitTests/API/AdminUserStatisticsApiTests.cs`
- `tests/Sanad.UnitTests/API/PortalRemainingResponseContractTests.cs`
- `docs/admin/user-management.md`
- `docs/postman/admins/Sanad.Admin.postman_collection.json`
- Add `docs/internal/work/portal-api-reconciliation/reports/e01-user-statistics-growth-delivery.md`

Mastermind owns the phase plan and handoff. Do not modify `plan.md`,
`Mastermind_Handoff.md`, or files outside this manifest. If another shared file
or a schema/migration change is materially required, stop and report the need.

## Acceptance and verification

- Tests cover total-user semantics, all statuses and account types (including
  zero counts), overlapping multi-account users, the fixed 12 Cairo-month
  buckets and boundary/current-month behavior, authorization, response JSON and
  generated OpenAPI. The HTTP test uses an isolated test host and synthetic data.
- Update the guide/Postman request to match the verified response and SuperAdmin
  authorization. Include the enum name/value mapping, Cairo bucket semantics,
  no-PII boundary, and explicit exclusion of the undefined services series.
- No migration, configuration change, persistent fixture, Production/shared
  database access, provider call, Bruno, or live API request.
- Run one strict solution build (zero warnings/errors) after final code changes,
  focused handler/API/OpenAPI tests, Postman JSON/route validation, and
  `git diff --check`. Do not run the full suite unless changed risk warrants it.
- Report exact commands/results, skips/warnings and limitations. No commit, push,
  package, deploy, or later E phase is authorized by this contract.
