# FUR-03 worker delivery — finance read regression verification

Date: 2026-10-16 (Africa/Cairo).

Status: **In progress — corrections complete, awaiting Mastermind review.**
Whole-phase verification, tests, report, and audience handoffs are complete locally
and uncommitted. Do not commit, push, publish, deploy, or start FUR-04. No runtime
API change was made; no production database was used, reset, or seeded.

## 1. Baseline and intake

- Repository `D:\Sanad_API`, branch `main`, HEAD `30f6bcba3d4d91421827676b681f584d28ed9798`
  identical to `origin/main` at intake and at delivery (verified with
  `git rev-parse HEAD` and `git rev-parse origin/main`; `git status --short --branch`
  shows `## main...origin/main` with the pre-existing local planning delta preserved).
- The intake local delta matches the brief exactly: modified
  `docs/internal/governance/AGENTS.md`, `docs/internal/Mastermind_Handoff.md`, and
  `docs/internal/work/portal-api-reconciliation/plan.md` (Mastermind FUR-03 planning
  text), plus untracked approved contract `contracts/fur-03-finance-verification.md`,
  untracked worker brief `worker-prompts/fur-03-finance-verification.md`, and untracked
  correction brief `worker-prompts/fur-03-review-corrections.md`. No other worker,
  owner, or private overlap was found; unrelated files were preserved and no
  reset/stage/commit occurred.
- Read before editing: root `AGENTS.md`, governance, current handoff, active plan,
  the approved contract, and the FUR-02 deployment receipt
  (`reports/fur-02-deployment.md`, release `5cd3526` deployed).
- Binding contract: [FUR-03 finance verification](../contracts/fur-03-finance-verification.md),
  owner-approved 2026-10-16, covering intake `FR-20261016-02` (admin finance reads
  with `facilityId` filter) and `FR-20261016-03` (Draft owner dashboard/revenue reads).
  No new endpoint, DTO field, business rule, migration, CORS/provider/config change,
  rate-limit change, or `FinanceAdmin` access was introduced. No payouts, general
  booking filters, owner settlements, or frontend work.

## 2. Source findings (no defect reproduced)

### 2.1 `FR-20261016-02` — Admin finance reads with `facilityId` filter

**Classification: historical C5 (already resolved); not a current reproduced defect.**

Commit `5edd3419781cdc1d3b106b2debb60ea0295f0ff2` (October 11) replaced
untranslatable typed-ID `FacilityId.Value` predicates with typed `CareHomeId`
comparisons in `CareHomeFinanceReadModels.cs`. Current source contains that fix:

- `GetAdminCareHomeRevenueHandler.Handle` passes
  `q.FacilityId is Guid adminFacility ? new CareHomeId(adminFacility) : null` to
  `CareHomeRevenueProjection.Revenue` — this `Guid? facilityId` → `CareHomeId?`
  conversion is the path central to the historical translation fix and is now
  exercised directly through the handler in the PostgreSQL proof.
- `GetAdminCareHomeDashboardHandler.Handle` passes the same typed facility ID to
  `CareHomeDashboardProjection.Get`.
- The revenue projection filters `Where(x => facilityId == null || x.FacilityId == facilityId)`:
  null/unfiltered returns all facilities; a specific `Guid` returns only that facility.
  An unknown facility returns an empty valid result under current query semantics.
- Routes/verbs/policies unchanged:
  `GET /api/v1/admin/care-homes/dashboard` and `GET /api/v1/admin/care-homes/revenue`,
  `[Authorize(Policy = CareHomesOperationalAdmin)]` (SuperAdmin + SupportAdmin, normal access).
- Date semantics unchanged: Cairo-local inclusive via `ToUtcRange` (from inclusive,
  to exclusive via `AddDays(1)`); reversed range throws `ArgumentException` →
  `CareHomes.Revenue.InvalidRange` → HTTP 400.

### 2.2 `FR-20261016-03` — Draft owner dashboard/revenue reads

**Classification: historical C5 (already resolved); not a current reproduced defect.**

Owner resolution is by ownership and does not require `Approved` status:

- `GetOwnerCareHomeRevenueHandler.Handle` resolves
  `db.Facilities.SingleOrDefaultAsync(x => x.OwnerUserId == q.Actor)` then delegates to
  `CareHomeRevenueProjection.Revenue(db, facility.Id, ...)` — no status filter.
- `GetOwnerCareHomeDashboardHandler.Handle` resolves the same facility and delegates
  to `CareHomeDashboardProjection.Get` — no status filter.
- Draft facilities return successful empty or populated reads; `Approved` facilities
  behave identically for finance reads.
- Routes/verbs/policies unchanged:
  `GET /api/v1/care-homes/dashboard/mine` and `GET /api/v1/care-homes/revenue/mine`,
  `[Authorize(Policy = CareHomeOwnerAccess)]`.
- No facility → `CareHomes.Bookings.NotFound` → HTTP 404 (stable existing mapping).

### 2.3 Route, policy, and error mapping confirmation

- `CareHomeFinanceController`: `CareHomeOwnerAccess`, `api/v1/care-homes`,
  `dashboard/mine`, `revenue/mine`, `revenue/mine/export.csv` — all GET.
- `AdminCareHomeFinanceController`: `CareHomesOperationalAdmin`, `api/v1/admin/care-homes`,
  `dashboard`, `revenue`, `revenue/export.csv`, `bookings/{bookingId:guid}/receipt`,
  `bookings/{bookingId:guid}/internal-notes` — dashboard/revenue are GET.
- `ResultProblemDetailsMapper`: `CareHomes.Revenue.InvalidRange` → HTTP 400 (stable,
  verified by `CareHomeBookingControllerTests.Booking_error_codes_map_to_stable_http_statuses`).
- No `FinanceAdmin` grant; no policy expansion; no rate-limit addition; no new endpoint;
  no new DTO field; no provider call.

## 3. What existing tests prove and what the new PostgreSQL run proves

### 3.1 Existing tests (InMemory/SQLite; logic/mapping proof only, not PostgreSQL translation)

| File | Result | What it proves | What it does NOT prove |
| --- | --- | --- | --- |
| `tests/Sanad.UnitTests/CareHomes/CareHomeFinanceReadModelTests.cs` | 11 passed / 0 failed / 0 skipped | Revenue date bounds (Cairo-local inclusive), empty Draft owner reads, empty admin filtered reads, receipt/notest cross-facility isolation, historical unattributed rows, CSV escaping, SQLite relational translation (empty state only) | No PostgreSQL/Npgsql execution; no populated finance totals; no admin facility-filter isolation; no owner Draft+Approved populated reads |
| `tests/Sanad.UnitTests/API/CareHomeOwnerPortalTests.cs` | 19 passed / 0 failed / 0 skipped | Owner dashboard/revenue populated and empty via isolated HTTP host with real auth/policies/handlers; Draft and Approved facility states; invalid range 400; no facility 404; anonymous 401; non-owner 403 | No PostgreSQL/Npgsql execution; InMemory store only; admin filtered reads not covered through HTTP host |
| `tests/Sanad.UnitTests/API/CareHomeBookingControllerTests.cs` | 25 passed / 0 failed / 0 skipped | Route templates, verb/templates, policies (`CareHomeOwnerAccess` / `CareHomesOperationalAdmin`), error-code → status mapping including `CareHomes.Revenue.InvalidRange` → 400 | Reflection-only; no handler execution; no database |

**Limitation established:** existing InMemory/SQLite tests prove logic and mapping but
do not establish Npgsql/PostgreSQL execution of the exact production query paths.

### 3.2 New PostgreSQL run (exact production handlers through Npgsql)

`CareHomeFinancePostgresTests`: **12 passed / 0 failed / 0 skipped** against the
authorized disposable local PostgreSQL target (section 4):

1. `Admin_Revenue_Unfiltered_ReturnsAllFacilitiesData` — invokes
   `GetAdminCareHomeRevenueHandler.Handle` with `facilityId == null`; includes both
   test facilities' bookings (specific booking IDs found in results).
2. `Admin_Revenue_Filtered_ReturnsOnlySpecifiedFacility_ExcludesForeignFacility` —
   invokes `GetAdminCareHomeRevenueHandler.Handle` with each facility ID; facility A
   filter returns only facility A's 2 payments; facility B filter returns only facility
   B's 1 payment; foreign facility excluded; correct gross/net totals.
3. `Admin_Revenue_UnknownFacility_ReturnsEmptySuccess` — invokes
   `GetAdminCareHomeRevenueHandler.Handle` with an unknown `facilityId`; returns valid
   empty result (zero totals, no transactions).
4. `Admin_Dashboard_Filtered_DistinguishesFacilities_ResourcesAndRevenue` — invokes
   `GetAdminCareHomeDashboardHandler.Handle` per facility; facility A (2 shared beds)
   reports TotalResources=2, OccupiedResources=1, gross 1150; facility B (1 private
   room) reports TotalResources=1, OccupiedResources=0, gross 1150; each dashboard's
   revenue transactions contain only its own facility.
5. `Admin_Dashboard_Unfiltered_ReturnsAllFacilities` — invokes
   `GetAdminCareHomeDashboardHandler.Handle` with `facilityId == null`; reports
   TotalResources=3, OccupiedResources=1, gross 2300, with both facilities' revenue
   transactions present.
6. `Owner_Revenue_DraftFacility_ReturnsOwnData` — owner resolves Draft facility by
   ownership; revenue returns 1 payment, 1150 gross/net, correct facility ID.
7. `Owner_Revenue_ApprovedFacility_ReturnsOwnData` — owner resolves Approved facility
   (full approval lifecycle executed); revenue returns correct owned data.
8. `Owner_Dashboard_ApprovedFacility_ReturnsData` — owner Approved facility dashboard
   returns success, zero resources (no inventory), gross 1150 revenue, one transaction.
9. `Owner_Revenue_NoFacility_ReturnsNotFound` — owner without facility →
   `CareHomes.Bookings.NotFound` → HTTP 404 mapping.
10. `Owner_Dashboard_EmptyDraftFacility_ReturnsZeroTotals` — empty Draft facility
    dashboard returns zero resources, zero occupancy, zero revenue.
11. `Revenue_DateRange_FiltersByCairoLocalInclusiveDates` — invokes
    `GetAdminCareHomeRevenueHandler.Handle`; in-range Cairo-local date includes the
    payment; out-of-range date excludes it (inclusive from, exclusive to).
12. `Revenue_InvalidRange_ReturnsInvalidRangeError` — reversed `from`/`to` via handler
    returns `CareHomes.Revenue.InvalidRange` → HTTP 400.

Contract coverage now includes Admin filtered **and** unfiltered dashboard/revenue, plus
owner dashboard/revenue for Draft **and** Approved facilities, as the contract requires.

## 4. PostgreSQL target validation and fixture safety

### 4.1 Target identification (R1)

- PostgreSQL 18.4 service (`postgresql-x64-18`, Running) on `localhost:5432`.
- Target database: `SanadFur03VerifyDb` — created empty for this correction round and
  provisioned with the existing committed CareHomes schema/migrations (all 15 CareHomes
  migration heads applied, including `20261008141359_AddCareHomeFinanceReporting`). Not
  Production `SanadDb`, not the VPS database, not `SanadIntegrationDb`, and not
  `SanadBrunoTestDb` (which was never connected in this round).
- Sanitized database identifier in this report: `SanadFur03VerifyDb`.
- No secrets, connection strings, or credentials appear in this report.

### 4.2 Target validation (fail-closed, in test child process only)

- Opt-in connection key (FUR-03-specific, not FUR-02):
  `ConnectionStrings__Fur03VerificationDatabase`. The FUR-02 key and `sanad_fur02_verify`
  were not reused.
- Test attribute `Fur03PostgresFact` skips when the env var is absent (guarded suite
  leaves it absent).
- `GetValidatedConnectionString()` asserts: host is `localhost`/`127.0.0.1`/`::1`;
  database equals `SanadFur03VerifyDb` exactly. Any other value throws before any
  database write (fail-closed).
- The connection string existed only in the explicit `dotnet test` child process
  environment; it was not persisted and is not in the repository. Inherited integration
  targets were neutralized in the actual child process (the var was set only for that
  invocation). No Production-mode API was started; no existing database was reset or
  broadly seeded; only per-test fixture rows (deleted afterwards by ID in `finally`)
  were written.

### 4.3 Fixture prerequisites and readbacks

Minimal fixtures per scenario (two distinct facilities: one owner Draft facility,
one unrelated facility; empty and populated finance states):

- Facilities created via `CareHomeFacility.CreateDraft(owner, FixedUtcNow)`;
  Approved facility created through the full lifecycle workflow
  (`SaveDraft` → `UploadDocument` ×4 document types → `Submit` → `VerifyDocument` ×4
  → `Review(Approved)`).
- Bookings created via `CareHomeBooking.Create` + `MarkPaid` + `Accept` (Paid),
  or `MarkPaid` + `Accept` + `CancelByFamily` + `MarkRefundManuallyCompleted`
  (Paid + ManuallyCompleted refund).
- Dashboard fixtures include rooms/beds and per-facility `FixedOccupancyProvider`
  intervals so filtered and unfiltered results distinguish facilities in resource/counter
  fields as well as finance values.
- Prerequisites read back before each scenario: owner/facility relationship,
  Draft/Approved status, booking status/payment status, room/bed counts.
- No payment/provider calls; fake or avoided entirely.
- No broad seeder; no reset/drop/truncate; only per-test fixture rows inserted.

### 4.4 Cleanup result

- All fixture rows deleted by ID in `finally` blocks (`ExecuteDeleteAsync` on
  specific booking IDs and facility IDs only).
- Post-run database readback of `SanadFur03VerifyDb`: 0 facilities, 0 bookings,
  0 rooms, 0 beds — no fixture residue from this phase. No Production, VPS, or other
  database touched; `SanadBrunoTestDb` was not connected.
- No migration, config, provider, CORS, restart, or package change (state: none).

## 5. Exact commands, exit codes, counts, skips, warnings

| Step | Command | Exit | Result |
| --- | --- | --- | --- |
| Strict solution build | `dotnet build Sanad.slnx --no-restore -m:1 --nologo` | 0 | 0 warnings / 0 errors |
| Existing finance read model tests | `dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj --no-build --no-restore --nologo -v minimal --filter "FullyQualifiedName~Sanad.UnitTests.CareHomes.CareHomeFinanceReadModelTests"` | 0 | 11 passed / 0 failed / 0 skipped |
| Existing owner portal API tests | `dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj --no-build --no-restore --nologo -v minimal --filter "FullyQualifiedName~Sanad.UnitTests.API.CareHomeOwnerPortalTests"` | 0 | 19 passed / 0 failed / 0 skipped |
| Existing booking controller tests | `dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj --no-build --no-restore --nologo -v minimal --filter "FullyQualifiedName~Sanad.UnitTests.API.CareHomeBookingControllerTests"` | 0 | 25 passed / 0 failed / 0 skipped |
| New PostgreSQL proof tests | `dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj --no-build --no-restore --nologo -v minimal --filter "FullyQualifiedName~Sanad.UnitTests.CareHomes.CareHomeFinancePostgresTests"` (with `ConnectionStrings__Fur03VerificationDatabase` set only in the child process to `SanadFur03VerifyDb`) | 0 | 12 passed / 0 failed / 0 skipped |
| `git diff --check` | `git diff --check` | 0 | Clean |

Secrets were omitted from the command above (no connection string or secret appears
in this report or in any documentation; the value was supplied only in the explicit
test child process and was not persisted). No broad/full guarded suite was run because no runtime code changed
(not cross-cutting/high-risk); per the contract, no build or broad suite is launched
just for documentation when no runtime change occurs. The strict build above validates
the new test file compiles cleanly.

No Postman or OpenAPI files were touched (no contract mismatch found), so no JSON
parsing or example alignment check was needed.

## 6. Defect reproduction outcome

| Issue | Reproduced? | Exact behavior | Minimal fix |
| --- | --- | --- | --- |
| `FR-20261016-02` | **No** | Admin filtered/unfiltered dashboard/revenue execute correctly on PostgreSQL through the real handlers; facility filter isolates; unknown filter returns empty; Cairo date bounds inclusive; reversed range → 400 `InvalidRange` | None (already fixed in `5edd3419`) |
| `FR-20261016-03` | **No** | Owner Draft and Approved facility dashboard/revenue execute correctly on PostgreSQL; ownership resolution does not require `Approved` status; no facility → 404 `NotFound` | None (owner reads already status-agnostic) |

No runtime code change was made. The alleged defects are historically resolved and not
currently reproduced. This is a regression-verification phase, not a new feature.

## 7. Migration/configuration/provider/CORS/restart/package requirements

**None.** No migration, application configuration, provider, CORS, restart, or package
change is required or was made. The existing `CareHomes` schema (15 migrations, including
`AddCareHomeFinanceReporting`) was provisioned into the explicitly authorized disposable
test database `SanadFur03VerifyDb` solely for this proof; no migration sources were
edited and no other database was touched.

## 8. Deployment status

**N/A** — no runtime API delta exists for this verification-only phase; deployment is not
applicable. If a future phase makes runtime changes, they remain local until reviewed and
separately approved for release.

## 9. Limitations and remaining decisions

- PostgreSQL proof is opt-in: the 12/12 explicit pass is relational evidence, but the
  guarded full suite skips these tests when the target env var is absent (by design).
- `SanadFur03VerifyDb` is a dedicated disposable database for this correction round;
  per-test fixture rows were written and deleted by ID. Post-run readback confirms zero
  residue.
- Cairo-local inclusive date behavior was verified for one representative from/to day;
  boundary edge cases (DST transition, exact midnight) were not separately enumerated.
- Admin `CareHomesOperationalAdmin` policy membership (SuperAdmin + SupportAdmin) was
  confirmed in source/tests but not exercised end-to-end through the PostgreSQL path
  (handler-level test used directly, not through the auth pipeline).
- No frontend repository was inspected; no frontend code defect is claimed.
- Deployment receipt placeholder: a later Mastermind release step records the accepted
  phase and any deployment (or explicit N/A).

## 10. Traceability

| Item | Link |
| --- | --- |
| `FR-20261016-02` | [Audit §4#2](../frontend-report-audit-2026-10-16.md#2--admin-finance-reads-fail-with-a-facilityid-filter) → [approved contract](../contracts/fur-03-finance-verification.md) → `src/Modules/CareHomes/Application/Sanad.Modules.CareHomes.Application/Bookings/CareHomeFinanceReadModels.cs` (`GetAdminCareHomeRevenueHandler`, `GetAdminCareHomeDashboardHandler`, `CareHomeRevenueProjection.Revenue`) → `tests/Sanad.UnitTests/CareHomes/CareHomeFinancePostgresTests.cs` → handoffs below → deployment receipt placeholder (N/A) |
| `FR-20261016-03` | [Audit §4#3](../frontend-report-audit-2026-10-16.md#3--draft-owner-dashboard-revenue-fail) → [approved contract](../contracts/fur-03-finance-verification.md) → `src/Modules/CareHomes/Application/Sanad.Modules.CareHomes.Application/Bookings/CareHomeFinanceReadModels.cs` (`GetOwnerCareHomeRevenueHandler`, `GetOwnerCareHomeDashboardHandler`) → `tests/Sanad.UnitTests/CareHomes/CareHomeFinancePostgresTests.cs` → handoffs below → deployment receipt placeholder (N/A) |
| Worker delivery | This report (`docs/internal/work/portal-api-reconciliation/reports/fur-03-worker-delivery.md`) |
| Admin portal handoff | `docs/frontend-handoffs/admin/admin-portal/phase-03-finance-verification/2026-10-16-finance-verification.md` |
| Care Home Owner handoff | `docs/frontend-handoffs/admin/care-home-owner/phase-03-finance-verification/2026-10-16-finance-verification.md` |
| Deployment receipt | Placeholder (N/A — no runtime API delta) |
| FUR-02 deployment receipt | `docs/internal/work/portal-api-reconciliation/reports/fur-02-deployment.md` (release `5cd3526`; context for source pin) |

## 11. Changed-file manifest

**Runtime files changed: none.** (No defect reproduced; no conditional fix applied.)

**Test files added:**
- `tests/Sanad.UnitTests/CareHomes/CareHomeFinancePostgresTests.cs` (new; FUR-03-specific
  disposable-PostgreSQL proof with opt-in `ConnectionStrings__Fur03VerificationDatabase`
  key and `Fur03PostgresFact` guard; 12 tests against `SanadFur03VerifyDb`,
  including handler-level Admin revenue and unfiltered Admin dashboard / Approved
  owner dashboard coverage)

**Docs/handoffs added:**
- `docs/internal/work/portal-api-reconciliation/reports/fur-03-worker-delivery.md` (this report, updated correction pass)
- `docs/frontend-handoffs/admin/admin-portal/phase-03-finance-verification/2026-10-16-finance-verification.md` (updated)
- `docs/frontend-handoffs/admin/care-home-owner/phase-03-finance-verification/2026-10-16-finance-verification.md` (updated)
- `docs/frontend-handoffs/README.md` (two links added)

**Status/evidence lines updated (FUR-03 only):**
- `docs/internal/work/portal-api-reconciliation/plan.md`
- `docs/internal/Mastermind_Handoff.md`

No secrets, connection strings, tokens, customer records, raw production logs, or private
backup paths appear in this report or in any linked file.

## 12. Correction pass (Mastermind review R1–R4)

Mastermind's [review](fur-03-mastermind-review.md) required four bounded corrections;
all were applied with no runtime change:

- R1 — FUR-03-specific disposable database: test guard expected-database changed from
  `SanadBrunoTestDb` to `SanadFur03VerifyDb`; the database was created empty on local
  PostgreSQL `localhost:5432` and provisioned with the existing committed CareHomes
  schema/migrations; proof runs only against `SanadFur03VerifyDb`; fail-closed host/name
  checks preserved. `SanadBrunoTestDb` was not connected.
- R2 — Complete PostgreSQL query coverage: Admin revenue tests now invoke
  `GetAdminCareHomeRevenueHandler.Handle` (filtered, unfiltered, unknown-filter);
  added unfiltered Admin dashboard and Approved owner dashboard proofs; Admin dashboard
  fixtures strengthened with rooms/beds and per-facility resource/counter assertions so
  the facility predicate's effect is independently observable.
- R3 — Reproducible evidence: exact `dotnet test` command and filter recorded above
  (secrets omitted), with child-process target sanitization, target-name/host
  validation, exit codes, pass/fail/skip counts, and cleanup readback.
- R4 — Audience notices aligned: worker report, both dated handoffs, and the README
  now state exact verified coverage (12/12), target identity `SanadFur03VerifyDb`, no
  frontend repository inspection, no runtime change, and deployment N/A; issue →
  approved contract → tests → deployment receipt/N/A traceability preserved.

Mastermind review may return further bounded corrections through the owner. The worker
made no commit, push, publication, deployment, or Done marking, and did not start FUR-04.
Leave FUR-03 **In progress — corrections complete, awaiting Mastermind review**.
