# FUR-03 — finance read regression verification

Date: 2026-10-16 (Africa/Cairo). **Status: owner-approved 2026-10-16; worker brief prepared.**

## Traceability and evidence

- Source intake: [frontend report audit #2 and #3](../frontend-report-audit-2026-10-16.md#2--admin-finance-reads-fail-with-a-facilityid-filter).
- Issue #2 (`FR-20261016-02`): reported admin finance reads fail with a `facilityId` filter. Audit evidence says the October 11 typed-ID translation defect was fixed in `5edd3419781cdc1d3b106b2debb60ea0295f0ff2`; current failure is unproven.
- Issue #3 (`FR-20261016-03`): reported Draft owner dashboard/revenue reads fail. Audit evidence says owner selection does not require Approved status; current failure is unproven.
- Current phase/source pin for a future execution brief: `30f6bcba3d4d91421827676b681f584d28ed9798` (FUR-02 documentation closeout; deployed API package remains `5cd3526`).
- Relevant implementation: `src/Modules/CareHomes/Application/Sanad.Modules.CareHomes.Application/Bookings/CareHomeFinanceReadModels.cs`; owner/admin controllers in `src/API/Sanad.API/Controllers/CareHomeFinanceController.cs` and `AdminCareHomeFinanceController.cs`.
- Existing tests: `tests/Sanad.UnitTests/CareHomes/CareHomeFinanceReadModelTests.cs`, `tests/Sanad.UnitTests/API/CareHomeOwnerPortalTests.cs`, and `tests/Sanad.UnitTests/API/CareHomeBookingControllerTests.cs`.

## Proposed objective

Establish whether the already-implemented admin facility-filtered finance reads and owner Draft-facility reads execute correctly against PostgreSQL, using isolated deterministic fixtures and the exact current production query handlers. Change runtime code only if the current defect is reproduced and its cause is isolated. This is a regression-verification phase, not a new finance feature.

## Proposed observable behavior to preserve

1. Admin operational users can request the existing admin dashboard and revenue reads with no facility filter or a specific `facilityId`; a specific filter returns only that facility's finance/occupancy data. An unknown facility filter returns a valid empty result under current query semantics.
2. A Care Home Owner can read their own facility dashboard and revenue while the facility is Draft as well as after approval. Reads remain scoped to the facility resolved from the authenticated owner; no caller-supplied owner/facility override is added.
3. The existing routes, verbs, query names, response DTOs, date-range semantics, status codes and error codes remain unchanged. Invalid reversed date ranges retain the existing 400 mapping. No new endpoint or response field is proposed.
4. Admin finance controller authorization remains `CareHomesOperationalAdmin` (currently SuperAdmin and SupportAdmin with normal access). Owner controller authorization remains `CareHomeOwnerAccess`. No FinanceAdmin grant, policy expansion or restricted-token access is proposed.
5. Read operations remain read-only with respect to business records. Do not add or assume an endpoint-specific rate limit; record the actual existing controller/global limiter configuration if confirmed relevant. Do not change rate limits in this phase.

## Bounded verification and implementation scope

- Inspect the exact current route → controller → handler → EF expression path for admin filtered/unfiltered dashboard and revenue, and owner Draft/Approved dashboard and revenue. Confirm boundary/error behavior and authorization against current source and tests.
- Add or adjust focused tests only for gaps needed to prove this behavior. Keep current DTO and endpoint compatibility.
- Prove the facility-ID predicates and representative filtered finance query execute with Npgsql on PostgreSQL. SQLite and InMemory results are useful supplementary evidence, not substitutes for PostgreSQL execution.
- Use minimal fixtures with two distinct facilities, including a Draft owner facility and an unrelated facility; include empty and populated finance states sufficient to prove isolation, expected totals and status/date filtering. Read fixture prerequisites back before each stateful API scenario. Never seed/reset the existing Production-mode VPS.
- If an exact current defect is reproduced, implement only its minimal correction and regression test, then rerun the affected PostgreSQL proof. If the alleged issue does not reproduce, make no runtime change and report it as not reproduced.
- Do not expand into caregiver payouts, general admin booking filters, owner settlements, commissions/debts/refunds policy redesign, UI changes, new endpoints, data repair, or unrelated typed-ID audits. The historical fix touched neighboring query paths; this phase retests only the two intake findings unless a direct dependency is demonstrated.

## Fixture and environment safety

- No production, VPS, personal/development database, Bruno, real payment/provider call, broad seeder, reset, migration, or destructive cleanup.
- Before any PostgreSQL proof, independently identify the exact target and verify it is a disposable local test database with a phase-specific database name. Neutralize inherited integration connection strings in the actual test child process except for the explicitly selected FUR-03 target. Never infer disposable status from an environment variable name alone.
- If no safe target exists, the worker must stop before database writes and report the exact missing prerequisite. A fresh database/cluster, changed configuration, migration, or other persistent fixture setup must be disclosed for owner approval before it is performed.
- Prefer existing test infrastructure with a FUR-03-specific opt-in key and fail-closed target validation. Do not reuse the FUR-02 target name or fixture guard implicitly.
- Current source inspection shows no expected schema, migration, application configuration, provider, or CORS change. Any newly discovered requirement must be reported and approved before implementation proceeds.

## Required evidence and completion

The worker report must distinguish source/test evidence from deployed behavior, list changed files and exact commands/results, identify the verified PostgreSQL target only by a sanitized database identifier, and state whether a defect reproduced. Include fixture setup/readback and cleanup evidence, test counts/skips, warnings, route/permission compatibility, migrations/configuration, and remaining limitations. Do not include secrets, access tokens, customer data, or raw production logs.

Update the Admin and Care Home Owner handoffs with the conclusion and links from issue → approved contract → implementation/tests → deployment receipt. Do not claim a frontend code defect without inspecting that frontend. If no API code changes are warranted, record deployment as not applicable for this verification-only phase and link the evidence; if runtime code changes are required, the complete phase must pass review and the standing release workflow before it is marked Done.

Owner approval received 2026-10-16 for this verification-only scope and the fail-closed disposable-PostgreSQL prerequisite. Runtime changes remain conditional on reproducing a current defect. Approval does not authorize production fixture writes, a new test environment/cluster, destructive setup, or an unreviewed feature expansion.
