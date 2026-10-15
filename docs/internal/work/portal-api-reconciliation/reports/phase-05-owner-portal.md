# PORTAL-05 report — owner-portal endpoint verification with minimal fixtures

Status: Done — worker delivery reviewed and accepted with the bounded
mastermind test strengthening below. No production access,
deployment, Bruno, CORS, migration, or production-code change occurred.
Stop before PORTAL-06/07.

## Intake

- Worker base: pushed `main` `ddab615aa901c69a55d18f79949e1ee62ee6ff9b`
  (PORTAL-04 acceptance + PORTAL-05 brief). At intake `main == origin/main`
  with a clean tree.
- Owned manifest (exact files created/modified, nothing else):
  - new `tests/Sanad.UnitTests/API/CareHomeOwnerPortalFixture.cs`
    (private bounded harness: fresh InMemory Care Homes store per test,
    synthetic users/tokens, factory override, domain seed helpers, inline
    visit-guard passthrough)
  - new `tests/Sanad.UnitTests/API/CareHomeOwnerPortalTests.cs`
    (15 fixture-backed HTTP tests over real handlers)
  - `docs/postman/care-homes/Sanad.CareHomes.Owner.postman_collection.json`
    (selected owner read/PUT descriptions; PUT day example corrected
    `"Monday"` string to numeric `1`)
  - `docs/postman/care-homes/Sanad.CareHomes.Finance.postman_collection.json`
    (owner Dashboard/Revenue descriptions only)
  - `docs/care-homes/visits.md` (settings day format corrected to numeric
    `DayOfWeek` with rejection note; version/conflict evidence note)
  - new `docs/internal/work/portal-api-reconciliation/reports/phase-05-owner-portal.md`
    (this file)
  - `docs/internal/work/portal-api-reconciliation/plan.md` (PORTAL-05 status
    lines only)
- Reused unmodified: `tests/Sanad.UnitTests/API/PortalApiMetadataHost.cs`,
  `PortalHostIsolationTests.cs`, `PortalOpenApiContractTests.cs`,
  `CareHomeBookingControllerTests.cs`, `CareHomes/CareHomeFacilityTests.cs`,
  `CareHomeInventoryTests.cs`, `CareHomeVisitTests.cs`,
  `CareHomeFinanceReadModelTests.cs`, plus domain builders
  (`CareHomeFacility`, `CareHomeRoomType/Room/Bed`, `CareHomeBooking`,
  `CareHomeVisit`) and real handlers/DI/policies. No wholesale helper
  refactor; no unrelated expectation changed.
- Production controllers/handlers/domain/configuration are read-only in this
  task; none was modified.

## Mandatory readiness manifest (before host startup / requests)

1. Configuration verified: `PortalApiMetadataHost` — every module connection
   key points at the unreachable dummy target; `App:TestUserSeed:Enabled`,
   `Database:ApplyMigrationsOnStartup`, `FinanceMigrations:ApplyOnStartup`
   all false; production `IHostedService` registrations removed; storage
   under the test temp root; ephemeral DataProtection; test JWT
   issuer/audience/signing key. Each test additionally re-registers the
   Care Homes concrete context and `ICareHomesDbContext` on a fresh
   InMemory store (unique name per test), so no PostgreSQL connection is
   possible and no test touches a real persistent directory. No inherited
   connection string or secret was read; no passwords/tokens/PII in docs.
2. Existing builders inspected first: `CareHomeFacilityTests`,
   `CareHomeInventoryTests`, `CareHomeBookingLifecycleTests`,
   `CareHomeVisitTests`, `CareHomeFinanceReadModelTests`, `PortalApiMetadataHost`.
   No new seeding framework; no onboarding replay.
3. Seeded per test only what the scenarios need (synthetic actors, Draft
   facilities with location-text profiles, inventory, paid/pending bookings,
   visits): typical populated test holds 2 owners with facilities + 1
   empty-facility owner + 1 no-facility owner, 1 shared room type / 1 room /
   1–2 beds per facility, 1 accepted paid booking (1000/100/50, total 1150)
   per facility plus pending/awaiting-decision rows where counts are
   asserted, and 1 pending prospective visit per facility. Location uses
   profile text only; no hierarchy seeded.
4. Read back before starting the host: actor IDs/roles, owner-to-facility
   links, Draft lifecycle, inventory foreign keys, booking payment/status
   and amounts, visit status/kind/count, settings version (asserted in code
   via `CountAsync`/`SingleAsync` before `OverrideCareHomes`/HTTP).
5. Only then the factory/client is created and scenarios run. Domain time
   uses the real UTC clock at seed time with future start dates
   (holds/decision windows stay valid); JWTs are minted at real current
   time, so tokens remain valid independent of domain dates.

## Endpoint-by-state matrix (real handler / HTTP evidence)

Wire JSON is camelCase; enums are numeric (established contract, preserved).

| Route | State | HTTP / code | Payload assertions (real query) |
| --- | --- | --- | --- |
| GET `facilities/mine` | owned Draft w/ profile | 200 | `id` = facility, `status` = Draft, `version` matches, `draft.englishName`/`governorate` round-trip, `documents` array |
| GET `facilities/mine` | second owner | 200 own only | response `id` is caller's facility, never the foreign id |
| GET `facilities/mine` | no facility | 404 `CareHomes.Facility.NotFound` | — |
| GET `inventory/mine` | populated | 200 | `facilityId`, type id/`englishName`/`monthlyPriceEgp`/`allocationMode`=Shared, room `101` + resolved names, bed `A`, `maintenance` array; no foreign `201` leakage |
| GET `inventory/mine` | no facility | 404 | — |
| GET `booking-requests/mine` | populated | 200 | single row: booking/facility/roomType ids, `totalAmount` 1150, `EGP`, `Accepted`/`Paid`; pending holds excluded; no foreign booking id |
| GET `booking-requests/mine` | facility, no paid rows | 200 `[]` | empty array, not 404 |
| GET `booking-requests/mine` | no facility | 404 `CareHomes.Bookings.NotFound` | — |
| GET `visits/mine` | populated | 200 | single row: visit/facility ids, `visitorName`, count 2, `Prospective`/`Pending`; no foreign visitor name |
| GET `visits/mine` | facility, no visits | 200 `[]` | — |
| GET `visits/mine` | no facility | 404 `CareHomes.Visits.NotFound` | — |
| GET `dashboard/mine` | populated | 200 | `totalResources` 2, `pendingPaymentCount` 1, `awaitingDecisionCount` 1, `periodRevenue.grossCollected` 2300 / `paymentCount` 2 (real occupancy + revenue handlers) |
| GET `dashboard/mine` | empty Draft | 200 | zeros (`totalResources` 0, `occupancyPercentage` 0) |
| GET `dashboard/mine` | no facility | 404 `CareHomes.Bookings.NotFound` (code-derived from `GetOwnerCareHomeDashboardHandler`; not HTTP-exercised in this task) | — |
| GET `revenue/mine` | populated, unfiltered | 200 | `totals.netCollected` 1150, `paymentCount` 1, 1 transaction with booking id, `EGP`, `netMovement` 1150 |
| GET `revenue/mine` | `from`/`to` = payment day | 200 | 1 transaction (Cairo-inclusive bounds) |
| GET `revenue/mine` | future range | 200 | 0 transactions, `grossCollected` 0 |
| GET `revenue/mine` | reversed range | 400 `CareHomes.Revenue.InvalidRange` | — |
| GET `revenue/mine` | empty Draft | 200 | `netCollected` 0, 0 transactions |
| GET `facilities/mine/visit-settings` | no settings | 200 | `facilityId`, `version` 0 |
| PUT `facilities/mine/visit-settings` | `expectedVersion` 0 | 200 | `version` 1, capacity saved; GET readback confirms |
| PUT `facilities/mine/visit-settings` | stale version | 409 `CareHomes.Visits.Conflict` | target and foreign settings unchanged on readback |
| PUT `facilities/mine/visit-settings` | correct version | 200 | `version` 2 |
| PUT `facilities/mine/visit-settings` | capacity 0 | 400 `CareHomes.Visits.InvalidRequest` | version/capacity unchanged |
| PUT `facilities/mine/visit-settings` | `"day": "Monday"` | 400 framework validation (`errors` names `operatingHours`) | version stays 0; numeric `DayOfWeek` is the verified wire format |
| GET `facilities/mine/visit-settings` | no facility | 404 `CareHomes.Visits.NotFound` | — |
| All 7 GETs + settings PUT | anonymous | 401 | — (representative matrix, every route) |
| All 7 GETs + settings PUT | SuperAdmin / Family token | 403 | — (representative matrix, every route) |
| All 7 GETs | Draft facility | 200 | no approved-only gate exists on reads; settings save also allowed on Draft |

Handlers exercised for real: `GetMyCareHomeFacilityQuery`,
`GetMyInventoryQuery`, `OwnerListHandler` (`ListOwnerCareHomeBookingsQuery`),
`ListOwnerCareHomeVisitsQuery`, `GetOwnerCareHomeDashboardQuery` (with real
`CareHomeBookingOccupancyProvider`), `GetOwnerCareHomeRevenueQuery`,
`GetOwnerCareHomeVisitSettingsQuery` /
`UpdateOwnerCareHomeVisitSettingsCommand`. Fake external port: only
`ICareHomeVisitReservationGuard` inlined (PostgreSQL advisory-lock
implementation cannot run on InMemory; single-threaded verification makes no
concurrency claim). No email/SMS/payment/push provider is touched by these
reads/writes.

## Concrete defects found (reported before any production change; none made)

1. Authored owner PUT example and `docs/care-homes/visits.md` showed
   `"day": "Monday"` (string). Verified over HTTP: string day names are
   rejected with 400 model-binding validation (`errors` cites
   `operatingHours[...].day`) and leave settings version unchanged, while
   numeric `DayOfWeek` (1 = Monday) succeeds. This is an example/docs
   defect, not a production-code defect: numeric body enums are the
   established contract (plan C7) and are preserved. Corrected within the
   owned manifest: Postman PUT example body + description, `visits.md`
   settings section. No handler/policy/wire change.
2. No production-code defect was found in the eight routes: ownership
   scoping, populated/empty outcomes, Draft visibility, version conflicts,
   date-range validation, and 401/403 behavior all match their handlers.

## Limitations / unrun boundaries (honest, not silently mocked)

- Concurrency serialization of visit writes still relies on the PostgreSQL
  advisory-lock guard in production; the isolated host inlines it.
- Relational (PostgreSQL/SQLite) query translation was not re-proven here;
  InMemory is the supported isolated provider per the brief, and existing
  relational translation tests for nearby handlers were reused green.
- `PendingReview`/`Suspended` settings-write block
  (`CareHomes.Visits.InvalidState`) is code-derived, not HTTP-exercised
  (reaching those states needs full submission/review orchestration,
  outside this bounded task).
- CSV export, receipt, internal notes, decision/assignment/check-in/out,
  transfer, maintenance, availability, media, and Admin routes are
  explicitly outside PORTAL-05; untested nested/detail paths are listed
  here rather than claimed.

## Gates

- `dotnet build Sanad.slnx --no-restore -m:1 --nologo -v minimal`:
  exit 0, 0 warnings, 0 errors.
- `dotnet test ... --no-build --no-restore --filter
  FullyQualifiedName~Sanad.UnitTests.API.CareHomeOwnerPortalTests`:
  15/15 passed, 0 failed, 0 skipped.
- Reused: isolation + OpenAPI + facility + inventory + visit + finance-read
  + booking-controller focused sets: 107/107 passed, 0 failed, 0 skipped.
- Touched Postman JSON parses; contract mapping and diff-check are
  mastermind gates at integration (routes unchanged, so 0 missing/orphan
  is expected to hold).
- `git diff --check`: clean (verified before commit).

## Mastermind review and acceptance — 2026-10-15

Reviewed clean pushed delivery `6b3290646e6c79ad3928f9e199dfa570b7039031`
against the brief, fixture/HTTP assertions, real handlers and guide/Postman diff.
No production-code change was found or made. Numeric DayOfWeek correction is
an example fix, not a wire-format change. Finance Postman descriptions are
directly relevant documentation, not additional runtime scope.

Bounded review strengthening (test-only):

- Worker fixtures were all Draft; added the approved-facility case explicitly
  required by the brief. The helper uses the existing lifecycle-test pattern:
  real domain draft/profile/document submission, verification and approval,
  with no onboarding API replay/provider send. Readback precedes host startup.
  Both Draft and Approved now exercise all seven GETs with meaningful IDs,
  nested inventory and booking/visit/revenue assertions, and the full settings
  success/update/stale-version/invalid-capacity sequence.
- Added HTTP no-facility cases for dashboard and revenue: both return 404
  `CareHomes.Bookings.NotFound`. The earlier dashboard code-derived-only
  limitation in the worker matrix is superseded by these executed cases.
- Replaced raw-JSON substring exclusion of `201` (which could randomly occur
  inside a generated UUID) with exact foreign room-type/room/bed ID comparisons.
- Revenue date-range fixture now computes payment day in Cairo rather than
  UTC, matching the existing API contract across the UTC/Cairo midnight boundary.

After the last code change:

- `dotnet build Sanad.slnx --no-restore -m:1 --nologo
  /p:TreatWarningsAsErrors=true`: exit 0, **0 warnings, 0 errors**.
- Focused `dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj --no-build
  --no-restore --nologo -v minimal`, filter:
  `FullyQualifiedName~CareHomeOwnerPortalTests|FullyQualifiedName~PortalHostIsolationTests|FullyQualifiedName~PortalOpenApiContractTests|FullyQualifiedName~CareHomeFacilityTests|FullyQualifiedName~CareHomeInventoryTests|FullyQualifiedName~CareHomeVisitTests|FullyQualifiedName~CareHomeFinanceReadModelTests|FullyQualifiedName~CareHomeBookingControllerTests`:
  exit 0, **126 passed, 0 failed, 0 skipped** (19 owner + 107 related).
- Touched Owner/Finance Postman JSON parses; mapping gate exit 0:
  **556 signatures / 620 requests / 0 missing / 0 orphan**. Diff-check clean.
- Architecture boundary inputs unchanged; full unit/architecture integration
  remains PORTAL-07 rather than being claimed from this focused run.

No blocking Phase 05 finding remains in this bounded review. The fake visit
reservation guard still means no PostgreSQL lock/concurrency proof; unrelated
details/mobile routes and the reported caregiver data issue remain outside
this evidence. No Production access, PostgreSQL, providers, publish or deploy.
Final integration brief is prepared; its execution has not started.
