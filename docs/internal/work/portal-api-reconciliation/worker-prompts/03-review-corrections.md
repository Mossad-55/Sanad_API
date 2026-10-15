# PORTAL-03 — bounded mastermind review corrections

Return to the same Phase 03 worker. Do not begin PORTAL-04 or repeat the full
project audit. Read governance, Mastermind_Handoff, the original Phase 03 brief,
the Phase 03 report's mastermind review appendix and these corrections.

## Intake / ownership

Reviewed implementation: `57f9d3ed1beccbcef21fdec5c90ec7a33d28b97f`.
The following mastermind documentation commit records the review and adds this
brief. The launch message pins its final pushed SHA; verify clean main equals
origin/main there and record it. Do not reset to the implementation revision.

Work directly on main, one worker at a time. You are not alone: preserve others'
edits and stage explicit owned paths. No branch/worktree, hook bypass, deployment,
publish, live VPS access, secret reads, PostgreSQL fixture/reset or provider send.
Do not run the current host before fixing its readiness/isolation problem.

Owned files:

- `tests/Sanad.UnitTests/API/PortalApiMetadataHost.cs`.
- `PortalOpenApiContractTests.cs` and `AdminCommunityCategoryRequestTests.cs`
  in that same directory, plus at most one new bounded host-isolation test file
  there if needed.
- `src/API/Sanad.API/Controllers/AdminFinanceController.cs`: response metadata
  only, after proving the empty-state HTTP outcome. No runtime behavior change.
- `src/API/Sanad.API/OpenApi/PortalApiSchemaTransformer.cs`: only an evidenced
  nullable-enum correction; keep existing string-enum/ID behavior.
- `docs/admin/finance-platform-charges.md`, `docs/admin/api-contract-conventions.md`,
  the existing Phase 03 report and only PORTAL-03 status/evidence in `plan.md`.
  Affected finance-current Postman description only if it makes the same claim.

No Program/DependencyInjection production redesign or global output formatter
change. Ask for a bounded manifest extension if essential. Mastermind owns the
central handoff/index.

## R1 — test host is not isolated as reported (required correction)

`PortalApiMetadataHost` overrides only `ConnectionStrings:IdentityDatabase`.
Other module-specific connection keys can still come from inherited configuration.
The host does not remove any production IHostedService registrations, despite
its summary claiming background work is disabled. API registration includes
Care Homes processors, Chat dispatch/attachment cleanup and email/push outboxes;
several query concrete DbContexts immediately without a Development guard.
Disabling startup migrations/seeding does not disable them. Replacing only
ICaregiversDbContext in a scenario does not isolate those concrete contexts.

Before any host launch:

1. Inspect configuration key names and actual registered jobs read-only. Override
   **every** module connection key and relevant startup/provider/storage setting
   in the test host with deterministic safe settings, without reading/reporting
   secret values. Disable all migration paths, including any module-specific
   finance flag. Keep test-user/other fixture seeders disabled. No real target.
2. Remove only production background job registrations in test services (or use
   a test-only equivalent that guarantees none run). Preserve TestServer/web
   host framework services and reuse production controllers/OpenAPI/MVC options.
   Do not change production startup to make these tests safe.
3. Ensure storage/DataProtection cannot inherit a real persistent directory:
   configure test-only safe temporary roots where necessary and an ephemeral
   provider; do not create keys/directories in inherited release/private roots.
   Do not blindly instantiate real external provider clients as proof of safety.
4. Add regression checks before host startup for registrations/configuration:
   no production background jobs; every module connection is the safe test
   target; migrations/seeders disabled; storage/key setup isolated. Include an
   adversarial in-memory inherited-config layer with synthetic non-test module
   values if feasible, proving the final override wins without any connection.
   Use no real environment secrets. Record exact isolation evidence, not a
   claim that catching connection failures is acceptable isolation.
5. Then prepare minimal existing runtime fixtures and read back the entire
   governorate/city/area chain and scenario state before requests. Run metadata
   GETs fixture-free after their configuration is ready. No API startup as a
   means of discovering/preparing missing fixtures.

## R2 — establish finance-current empty-state HTTP truth (required correction)

The delivered guide/report claim a verified first-run `200` with JSON `null`,
but the added tests only check the populated 200 schema. The action calls
`Ok((await sender.Send(...)).Value)`; default MVC HttpNoContentOutputFormatter
can turn null output into **204**, and no formatter override was found in
source. A method-level OkObjectResult is not HTTP evidence.

After R1, add a real HTTP regression using a synthetic authorized admin and a
minimal isolated Finance context or a deterministic fake query result prepared
before startup. Exercise both empty/null and populated results through the
**unchanged production MVC pipeline**. Assert status, actual body and content
type. Do not change runtime formatting to fit the documentation.

If the empty state is 204, add its no-content response metadata, correct the
guide/conventions/report and test that the generated 204 has no body schema;
retain the populated 200 DTO schema. If it really is 200-null, show why and
publish a response-level nullable schema without changing the shared populated
DTO component or inventing a DTO. Record pre-correction evidence honestly.

## R3 — nullable enum schema constraints (verify; correct if reproduced)

The transformer unwraps Nullable<T> and replaces `schema.Enum` with numeric
members only. On a schema with integer+null type, a numeric-only enum constraint
still rejects null; checking only the type's nullability is insufficient.
Existing tests cover nullable strings/coordinates but not this branch.

Inspect the actual generated schema for `MyProfileResponse.AccountType`
(`AccountType?`) and another nullable enum parameter/property if present.
Serialize a synthetic null and a defined value with actual MVC options. Resolve
refs/anyOf appropriately and assert that the **complete schema**, including
enum constraints, allows the sample null and the defined value, and rejects an
undefined number. Preserve non-nullable enum constraints and string-enum behavior.

If reproduced, make the smallest transformer correction. Keep numeric members
and x-enumNames aligned; do not pair a null entry with a fictional CLR member.
Use a nullable union around the constrained numeric enum or another valid schema
representation with explicit, documented extension semantics. Do not merely
weaken all enums or globally allow null. No runtime JSON converter change.
If not reproduced, retain the regression and document the actual generated
representation explaining why it is valid. No speculative production change.

## Verification / handoff

Capture failing evidence for the established gaps without launching an unsafe
host. Keep earlier worker evidence historical and correct the prior claims;
do not erase it or imply its isolation was already proven.

After the last code change, strict solution build exit 0 with **zero warnings/
errors**, then all Phase 03 API schema/serialization/runtime/community and host
isolation tests plus existing AdminResponseSchemaContractTests. Architecture
1/1, parse touched Postman JSON if any, Postman-only mapping zero missing/orphan,
and diff-check. Do not run reset-based PostgreSQL fixtures; full integration
suite remains PORTAL-07. Report exact commands, counts/skips, config/fixture
readiness, confirmed HTTP/schema outcomes and remaining limits.

Commit/push normally and return final SHA/report. Stop for mastermind acceptance
before PORTAL-04. Nothing is deployed or claimed deployed by this correction.
