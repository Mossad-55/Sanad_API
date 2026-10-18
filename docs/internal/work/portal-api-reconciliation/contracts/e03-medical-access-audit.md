# E03 contract — caregiver medical-access decision audit

Approved by the Owner on 2026-10-18.

## Goal and boundary

Add an append-only, restricted audit of Family decisions to grant or revoke
medical access for a named caregiver, including authorization denials at those
grant-management operations. The audit must record only decisions made by the
existing grant-management flow.

The existing Family grant API is not currently consumed by a caregiver
medical-record read route. E03 therefore does **not** create a caregiver
medical-profile read route or claim to record caregiver reads of medical data.
It does not change how current or future consumers enforce grants.

## Event capture

Capture events only for the existing
`POST /api/v1/family/dependents/{dependentId}/medical-access-grants` and
`DELETE /api/v1/family/dependents/{dependentId}/medical-access-grants/{grantId}`
operations:

- Successful create: action `CreateGrant`, decision `Allowed`.
- Successful revoke: action `RevokeGrant`, decision `Allowed`.
- An authenticated Family request denied by the existing ownership/management
  authorization check (`Families.AccessDenied`): matching action, decision
  `Denied`, reason code `Families.AccessDenied`.
- Do not record list/detail reads, framework 401/403 rejections, malformed
  requests, invalid/inactive grantees, duplicate grants, unknown grant IDs, or
  other non-authorization business/validation failures as permission decisions.
- Do not change existing endpoint routes, authorization semantics, status codes,
  or error codes. In particular, do not make these Family routes a caregiver
  data-access path.

Persist successful decision rows atomically with the corresponding grant
create/revoke mutation. Denial rows are append-only records and must not change
the underlying grant. If the audit write fails, do not report a successful
grant/revoke; return safe `503 Families.AuditWriteFailed` without exposing
database details. Do not silently report an authorization denial as successfully
audited when its audit write failed. Rethrow request cancellation; never swallow
`OperationCanceledException`. Other existing status/error behavior is unchanged.

## Stored data and privacy

The new Families audit record may contain only:

- generated audit ID and server-generated `occurredOnUtc`;
- actor Family user ID and requested dependent ID;
- grantee caregiver user ID and medical-access grant ID when available;
- action and decision enums;
- stable reason code for `Denied` (never raw exception/message text);
- grant type, permission booleans, and expiry for successful decisions only;
- server-generated correlation/trace ID.

Denied events must leave grant details null; do not persist unvalidated attempted
grant details.

Do not store or return grant notes, medical profile/medication/history data,
names, email, phone, IP address, request/response bodies, tokens, or free text.
Do not backfill historical grant, family activity, or admin activity rows;
decision history begins when E03 is deployed.

Audit rows are immutable during retention. A system-only retention cleanup is
the sole deletion path: daily, in bounded batches, physically delete **only E03
audit rows** whose `occurredOnUtc` is earlier than current UTC minus 12 calendar
months. The cleanup path must work without weakening the normal Families
append-only guard; reads must also exclude expired rows if cleanup is delayed.
Do not change retention of `MedicalAccessGrant`, `ElderlyActivityLog`, or
existing admin-access audit records. No retention/configuration UI is included.

## Restricted Admin read

Add read-only `GET /api/v1/admin/caregivers/medical-access-audit` under a new
dedicated `MedicalAccessAuditAdmin` policy. The policy requires a Normal-access
JWT and permits **SuperAdmin and SupportAdmin only**, as selected by the Owner.
ContentAdmin and all app accounts remain denied. Do not broaden another policy.

The response is a paged projection of the audit row only. It must never expose
medical content or grant notes. Support optional filters `fromUtc`, `throughUtc`,
`actorUserId`, `dependentId`, `granteeUserId`, `action`, and `decision`. With no
time filters, return the newest 30 days; if one time bound is supplied, require
the other; both bounds must be UTC and `throughUtc` may not exceed
`fromUtc.AddMonths(12)`. Use an inclusive lower and exclusive upper bound.
Default `page=1`, `pageSize=50`, maximum page size 100; reject page offsets that
would overflow the paging implementation. Sort by `occurredOnUtc` descending,
then audit ID descending. Return the existing repository paging envelope style
with total count and total pages. Invalid filters use the standard 400
validation response; missing/invalid authentication is 401 and policy rejection
is 403.

## Explicit exclusions

- No caregiver medical-profile read/write endpoint, grant-consumption behavior,
  permission expansion, new caregiver consent UI, or frontend implementation.
- No capture of medical data or Family profile-view events as caregiver events.
- No changes to current Family grant semantics, grant records, Family activity
  history, existing admin access-audit behavior, or unrelated policies.
- No Production access/request, shared database fixture, migration application,
  provider call, Bruno run, commit, push, package, or deployment from the Worker.
  Migration authoring is in scope; applying it is a separate release operation.
- No later phase work.

## Acceptance and verification

- Focused tests cover successful create/revoke event contents and atomicity,
  authorization-denial events, excluded non-authorization failures, immutable
  retained audit rows, 12-month cutoff/purge boundaries, filter semantics,
  stable ordering/paging including invalid UTC/range/page bounds, 503 audit-store
  failure mapping, and SuperAdmin/SupportAdmin-only read authorization.
- Tests prove audit output excludes notes and medical/profile PII, existing
  Family grant status/error behavior is unchanged, and grant data is not exposed
  to caregivers by this phase.
- Generated OpenAPI documents the new endpoint and DTOs; Postman Admin examples
  match the route, permission, filters, paging, and response. The Family guide
  distinguishes current grants from this Admin decision audit and states that
  no caregiver medical-read endpoint is added.
- Run one strict Release solution build after final code changes (0 warnings and
  errors), focused Families/API/OpenAPI/retention tests, Postman JSON/route
  validation, and `git diff --check`. Do not run the full suite unless risk
  warrants it.
- Tests use isolated in-memory/synthetic data only. Do not start the API against
  shared or Production databases, apply migrations, seed persistent data, or
  perform real caregiver access.
- Report exact commands/results, counts, skips/warnings, changed files, and
  limitations. No commit/push/release is authorized by this contract.

## Worker-owned manifest

- Add `src/Modules/Families/Domain/Sanad.Modules.Families.Domain/Elderlies/CaregiverMedicalAccessAudit.cs`.
- `src/Modules/Families/Application/Sanad.Modules.Families.Application/MedicalAccess/CreateMedicalAccessGrantCommandHandler.cs`
- `src/Modules/Families/Application/Sanad.Modules.Families.Application/MedicalAccess/MedicalAccessGrantCommands.cs`
- `src/Modules/Families/Application/Sanad.Modules.Families.Application/Abstractions/Data/IFamiliesDbContext.cs`
- `src/Modules/Families/Infrastructure/Sanad.Modules.Families.Infrastructure/Persistence/FamiliesDbContext.cs`
- Add `src/Modules/Families/Infrastructure/Sanad.Modules.Families.Infrastructure/Persistence/Configurations/CaregiverMedicalAccessAuditConfiguration.cs`.
- Add one Families migration and its designer; update `FamiliesDbContextModelSnapshot.cs`.
- `src/API/Sanad.API/Authorization/AuthorizationPolicies.cs`
- `src/API/Sanad.API/ProblemDetail/ResultProblemDetailsMapper.cs`
- `src/API/Sanad.API/DependencyInjection.cs`
- `src/API/Sanad.API/Controllers/DependentMedicalAccessGrantsController.cs`
- Add `src/API/Sanad.API/Controllers/AdminCaregiverMedicalAccessAuditController.cs`.
- Add `src/API/Sanad.API/FamiliesIntegration/CaregiverMedicalAccessAuditRetentionService.cs`.
- `tests/Sanad.UnitTests/Families/MedicalAccessGrantHandlerTests.cs`
- Add `tests/Sanad.UnitTests/Families/CaregiverMedicalAccessAuditRetentionTests.cs`.
- `tests/Sanad.UnitTests/Support/FamiliesDbContextAdapter.cs`
- Add `tests/Sanad.UnitTests/API/AdminCaregiverMedicalAccessAuditTests.cs`.
- `docs/app/families/medical-access.md`
- Add `docs/admin/caregiver-medical-access-audit.md`; update `docs/admin/admin-overview.md` to link it.
- `docs/postman/app/Sanad.App.Family.Operations.postman_collection.json`
- `docs/postman/admins/Sanad.Admin.postman_collection.json`
- Add `docs/internal/work/portal-api-reconciliation/reports/e03-medical-access-audit-delivery.md`.

Do not edit files outside this manifest. If an additional shared file is
necessary, stop and report the exact path/reason before editing. The Mastermind
owns this contract, the Worker brief, phase status, review, and release
coordination.
