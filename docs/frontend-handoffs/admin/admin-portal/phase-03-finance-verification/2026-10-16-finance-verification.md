# Admin portal — FUR-03 finance verification — admin facility-filtered reads

Date: 2026-10-16 (Africa/Cairo). Phase: FUR-03.
Purpose: backend contract verification notice; client adoption unconfirmed.
Audience: Admin portal (Admin audience = Admin portal + Care Home Owner portal).
Communication state: ready for owner forwarding; receipt not confirmed.

No frontend repository was inspected. No frontend code defect is claimed.
This notice communicates the verified backend contract and what admin-portal
clients should rely on. The backend contract is unchanged; no client code change
is required by this phase.

## Baseline and authority

- Original intake and issue IDs: `FR-20261016-02` (admin finance reads fail with a
  `facilityId` filter).
- Canonical plan/approved contract: [FUR-03 contract](../../../../internal/work/portal-api-reconciliation/contracts/fur-03-finance-verification.md) (owner-approved 2026-10-16).
- Source revision: `30f6bcba3d4d91421827676b681f584d28ed9798` matches `origin/main`; this
  FUR-03 delivery (test file and docs) remains uncommitted in the workspace.
- Delivery report: [fur-03-worker-delivery](../../../../internal/work/portal-api-reconciliation/reports/fur-03-worker-delivery.md), including the dated correction record.
- Deployment status: **N/A** — no runtime API change; no deployment performed.
- Evidence limits: handler-level and disposable-PostgreSQL proof (12/12 tests passed
  against `SanadFur03VerifyDb`); no frontend repository or real provider run.
  Frontend repository inspected: no.

## What is correct and should remain

| Issue | Correct behavior/usage | Evidence |
| --- | --- | --- |
| FR-20261016-02 | Historical October 11 typed-ID translation defect (`FacilityId.Value` untranslatable on Npgsql) was fixed in `5edd3419`; current source uses typed `CareHomeId` comparisons. Admin finance reads with no `facilityId` return all facilities; with a specific `facilityId` return only that facility; unknown `facilityId` returns valid empty result. Cairo-local inclusive date bounds; reversed range → 400 `CareHomes.Revenue.InvalidRange`. The `Guid? facilityId` → `CareHomeId?` conversion is proven through `GetAdminCareHomeRevenueHandler` on PostgreSQL. | Worker delivery §2.1, §3, §6; `CareHomeFinanceReadModels.cs`; `CareHomeFinancePostgresTests` (12/12 PostgreSQL passed) |

| Route | Verb | Policy | Query | Status behavior |
| --- | --- | --- | --- | --- |
| `GET /api/v1/admin/care-homes/dashboard` | GET | `CareHomesOperationalAdmin` (SuperAdmin + SupportAdmin, normal access) | `GetAdminCareHomeDashboardQuery(Guid? facilityId, ...)` | 200; reversed date range → 400 `CareHomes.Revenue.InvalidRange` |
| `GET /api/v1/admin/care-homes/revenue` | GET | `CareHomesOperationalAdmin` | `GetAdminCareHomeRevenueQuery(Guid? facilityId, ...)` | 200; reversed date range → 400 `CareHomes.Revenue.InvalidRange` |
| `GET /api/v1/admin/care-homes/revenue/export.csv` | GET | `CareHomesOperationalAdmin` | `GetAdminCareHomeRevenueQuery(...)` | CSV file or 400 on invalid range |

## What needs correction or backend work

| Issue | Classification | Current truth and expected frontend action | Backend responsibility / phase | Evidence |
| --- | --- | --- | --- | --- |
| FR-20261016-02 | Historical C5 — already resolved; not a current defect | No client change needed. The existing `facilityId` filter works on PostgreSQL; keep calling `GET /api/v1/admin/care-homes/dashboard?facilityId={guid}` and `GET /api/v1/admin/care-homes/revenue?facilityId={guid}` with the same shapes. An unknown facility ID returns an empty valid result, not an error. | FUR-03 — verification only; no runtime change | Worker delivery §2.1, §3, §6 |

## Implementation connection

| Issue | Contract | Implementation/revision | Verification | Deployment | Remaining action |
| --- | --- | --- | --- | --- | --- |
| FR-20261016-02 | [FUR-03 contract](../../../../internal/work/portal-api-reconciliation/contracts/fur-03-finance-verification.md) | No change needed — `CareHomeId` typed comparison already in `CareHomeFinanceReadModels.cs`; routes, policies, DTOs, date semantics, error codes unchanged | 12/12 disposable-PostgreSQL tests passed; 11/11 existing InMemory/SQLite tests; 25/25 route/policy mapping tests; strict build 0 warnings/errors | N/A — no runtime API delta | Admin portal continues using existing `facilityId` filter; confirm empty-result handling for unknown facility IDs |

The linked delivery report is backend evidence, not proof of client adoption.

## Decisions and evidence requested

- Owner decisions: none for this topic.
- Frontend evidence: confirm admin portal handles empty revenue/dashboard results for unknown `facilityId` as a valid empty response (not an error), and keeps using the existing `facilityId` query parameter shape.
- Acceptance: no new endpoint; existing route/query/DTO shapes unchanged; `CareHomesOperationalAdmin` authorization unchanged; no `FinanceAdmin` access added; no rate-limit change.
- Configuration/migrations: none required.
- Next action: owner forwards this notice; no frontend code defect is asserted.
