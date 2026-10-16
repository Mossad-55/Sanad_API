# FUR-03 worker brief — finance read regression verification

Prepared 2026-10-16 (Africa/Cairo). **Owner-approved complete-phase scope. Recommended worker reasoning effort: High.** One worker should read this complete brief from the repository, complete the approved phase, save the delivery report and audience handoffs, then stop for Mastermind review. Do not start FUR-04.

The binding contract is [FUR-03 finance verification](../contracts/fur-03-finance-verification.md). It is intentionally verification-first: runtime code changes are allowed only if a current defect is reproduced and isolated.

## Repository, source pin and intake

Repository: `D:\Sanad_API`. Preparation HEAD and `origin/main` were both `30f6bcba3d4d91421827676b681f584d28ed9798` (`Record verified FUR-02 deployment and audience status`). The current local planning/workflow delta is limited to:

- modified `docs/internal/governance/AGENTS.md`
- modified `docs/internal/Mastermind_Handoff.md`
- modified `docs/internal/work/portal-api-reconciliation/plan.md`
- untracked approved contract `docs/internal/work/portal-api-reconciliation/contracts/fur-03-finance-verification.md`
- this untracked worker brief

Recheck `git status --short --branch`, HEAD, origin/main and relevant diffs before work. Read root `AGENTS.md`, governance instructions, current Mastermind handoff, active plan, approved FUR-03 contract, FUR-02 deployment receipt and the FUR-03 source audit sections. Preserve owner/private/unrelated files and all later edits. Work directly on current `main`; no routine branch/worktree, reset, commit or push. You are not alone in this repository. If source pin changed, the current delta overlaps your proposed files, or an owner change affects scope, report it before editing overlapping files. Do not dispatch another worker.

## Objective and exact scope

Trace and verify the existing finance reads implicated by `FR-20261016-02` and `FR-20261016-03`:

1. Admin Care Home dashboard and revenue queries with no `facilityId` and with an explicit facility filter, including isolation from another facility and empty-result semantics for an unknown filter.
2. Owner Care Home dashboard and revenue reads when the owner facility is Draft and when Approved, using only the facility resolved from the authenticated owner.
3. Exact current controller → handler → EF query execution on PostgreSQL/Npgsql for the filtered facility-ID predicates and representative populated finance state. Existing InMemory/SQLite tests can supplement this, but cannot replace PostgreSQL execution.
4. Existing authorization and request/response/error behavior for the routes in scope. Preserve `CareHomesOperationalAdmin` for the admin finance controller and `CareHomeOwnerAccess` for the owner controller. Do not add FinanceAdmin, alter restricted-token access or change rate limits.

No new endpoint, DTO, business rule, migration, configuration, CORS change, provider call, financial policy or frontend change is approved. Do not expand to payouts, general booking filters, owner settlements, commission/debt/refund policy changes or unrelated typed-ID reviews. The historical fix also touched neighboring queries; only include a neighboring query if a direct dependency is demonstrated and obtain owner agreement before expanding scope.

If an exact current defect is reproduced, make only its minimal correction and focused regression coverage. If not reproduced, make no runtime change and report the claim as not reproduced. Do not speculate from the historical report or claim a frontend bug without inspecting frontend source (frontend inspection is outside this brief).

## Owned files

You may edit these files only as needed for the approved behavior:

- Runtime, conditional on a reproduced defect: `src/Modules/CareHomes/Application/Sanad.Modules.CareHomes.Application/Bookings/CareHomeFinanceReadModels.cs`; any additional runtime file must be disclosed with the concrete dependency and approved before changing it.
- Focused tests: `tests/Sanad.UnitTests/CareHomes/CareHomeFinanceReadModelTests.cs`, `tests/Sanad.UnitTests/API/CareHomeOwnerPortalTests.cs`, `tests/Sanad.UnitTests/API/CareHomeBookingControllerTests.cs`.
- If required for real PostgreSQL execution, a bounded FUR-03-specific integration test and fixture support under `tests/Sanad.UnitTests/CareHomes/` and its existing test project. Add an opt-in target key that is specific to FUR-03; do not repurpose the FUR-02 verification key or its fixture guard.
- Contract guides, only if verification finds an actual mismatch: `docs/care-homes/finance-reporting.md`, `docs/admin/care-homes.md`.
- Postman, only if an existing relevant request/example is missing or inaccurate: `docs/postman/care-homes/Sanad.CareHomes.Finance.postman_collection.json`, `docs/postman/admins/Sanad.Admin.CareHomes.postman_collection.json`, and the corresponding `docs/postman/care-homes/README.md` only if its index/permission note needs correction.
- Required sanitized report: `docs/internal/work/portal-api-reconciliation/reports/fur-03-worker-delivery.md`.
- Required dated Admin audience notices: `docs/frontend-handoffs/admin/admin-portal/phase-03-finance-verification/2026-10-16-finance-verification.md` and `docs/frontend-handoffs/admin/care-home-owner/phase-03-finance-verification/2026-10-16-finance-verification.md`; add both links to `docs/frontend-handoffs/README.md`. No App handoff is in scope.
- You may update only FUR-03 status/evidence lines in `docs/internal/work/portal-api-reconciliation/plan.md` and the active FUR-03 portion of `docs/internal/Mastermind_Handoff.md`. Do not rewrite FUR-02 or historical receipts.

If another file becomes necessary, pause before editing it and report the exact path, reason and proposed change to the owner through the Mastermind. Do not silently broaden the manifest.

## Safe fixture gate — before database writes or business API tests

First inventory the required fixture actors and states: two distinct facilities (one owner-scoped Draft facility and one unrelated facility), Approved owner facility where needed, empty and populated finance states, and bookings sufficient to prove payment/refund event-date filters and totals without contacting payment providers. Identify the exact queries/requests that consume each fixture and any prerequisite persisted records.

Before any PostgreSQL test write:

1. Independently read and verify the exact target endpoint and database name. It must be a disposable **local test database** with a FUR-03-specific name. Do not rely on an environment variable name alone to prove disposability.
2. Confirm the exact FUR-03 connection value is the only enabled integration target in the actual test child process. Neutralize inherited production, VPS, personal, Development and unrelated integration connection variables in that child process.
3. Read the relevant fixture prerequisites back before the scenario and verify facility/owner relationship, Draft/Approved state, booking payment/refund state and timestamps. Use minimal isolated fixtures; no broad application seeder.
4. Fake or avoid all payment/provider operations. These are read scenarios; do not call Paymob or any external provider.

Never use or mutate the Production-mode VPS, its database, or any personal/ordinary development DB. Never reset, drop, truncate or broadly seed a database. If no already available safe disposable target exists, stop before database writes and report the blocker and safe checks completed. Creating a new database/cluster or changing persistent environment configuration needs separate owner approval first. Under that condition, do not claim PostgreSQL acceptance; mark the phase incomplete pending the prerequisite. SQLite/InMemory is not a substitute.

No Bruno requests are assigned. A stateful HTTP test is optional only if needed to prove the controller boundary; fixture readiness and readback still apply.

## Required verification

- Inspect source and existing tests for the two issue paths; report what each current test proves and its limitations.
- Use the exact production query handlers with a real Npgsql/PostgreSQL execution for filtered admin dashboard/revenue and owner Draft dashboard/revenue as scoped. Read back assertions proving filter isolation and expected totals. Include the facility-filter null/unfiltered control needed to catch predicate errors.
- Verify existing routes, response shapes, date-range/error mapping and authorization policy source/tests remain compatible. Record any actual endpoint/global rate limiting discovered; do not change it.
- Test relevant empty and populated cases, Draft and Approved owner access, wrong/foreign facility isolation, and Cairo-local inclusive date boundaries to the degree those boundary paths are under test. Do not require unrelated status matrices.
- If runtime changes occur, run a strict solution build after the final code edit with zero warnings/errors, focused affected tests, and architecture test. Run full guarded unit suite before delivery only if the change is cross-cutting/high-risk or needed by release risk; verify child-process isolation before any test execution. Record the exact exit codes, counts, skips and warnings. If no runtime changes occur, do not launch a build or broad suite just for documentation.
- Check relevant Postman JSON parse and example alignment if those files are touched; check Markdown links and `git diff --check`.
- Do not use production HTTP or authenticated VPS GETs as fixture setup. Do not claim deployed behavior, frontend adoption, or an original frontend failure reproduced.

Every test command must have successful exit status and zero warnings for a clean gate. Stop repeating a stalled testhost attempt; record it accurately and use only successful guarded execution as evidence.

## Documentation and delivery report

The Admin portal and Care Home Owner audience notices must preserve issue IDs, state the source-confirmed historical fix versus any newly reproduced result, distinguish backend evidence from frontend assumptions, link issue → approved contract → source/tests → deployment truth, and say explicitly that neither frontend repository was inspected. If no API runtime changed, record deployment as not applicable for this verification-only phase. If runtime changes are required, record them as local until the complete phase is reviewed and released.

Save `docs/internal/work/portal-api-reconciliation/reports/fur-03-worker-delivery.md`. Include:

- baseline HEAD, local delta and changed-file manifest;
- route, policy, handler and EF query findings for both issues;
- what existing tests prove and what the new PostgreSQL run proves;
- exact sanitized disposable database identifier, target validation, actual child-process target isolation, fixture prerequisites/readbacks and cleanup result;
- whether each report reproduces, exact assertion/behavior, and any minimal fix (or state no runtime fix made);
- exact commands, exit codes, counts, skips and warnings; docs/Postman/OpenAPI checks if applicable;
- migrations/configuration/provider/CORS/restart/package requirements (`none` if confirmed);
- deployment status (`N/A` only if no runtime API delta; otherwise not deployed), limitations and remaining decisions;
- trace links from `FR-20261016-02/-03` to the approved contract, implementation/tests, handoffs and eventual deployment receipt placeholder.

Do not include credentials, connection strings, tokens, customer records, raw production logs, or private backup paths. Leave FUR-03 **In progress — worker complete, awaiting Mastermind review**. Do not commit, push, publish, deploy, alter production, create a new database/cluster, or mark Done. Return the saved report to the owner and stop.
