# Care Home Owner portal — FUR-03 finance verification — Draft and Approved owner reads

Date: 2026-10-16 (Africa/Cairo). Phase: FUR-03.
Purpose: backend contract verification notice; client adoption unconfirmed.
Audience: Care Home Owner portal (Admin audience = Admin portal + Care Home Owner portal).
Communication state: ready for owner forwarding; receipt not confirmed.

No frontend repository was inspected. No frontend code defect is claimed.
This notice communicates the verified backend contract and what owner-portal
clients should rely on. The backend contract is unchanged; no client code change
is required by this phase.

## Baseline and authority

- Original intake and issue IDs: `FR-20261016-03` (Draft owner dashboard/revenue reads fail).
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
| FR-20261016-03 | Owner finance reads (dashboard and revenue) resolve the facility from the authenticated owner and do **not** require `Approved` status. A Draft facility returns successful populated or empty reads; an `Approved` facility behaves identically for finance reads. No facility → 404 `CareHomes.Bookings.NotFound`. Cairo-local inclusive date bounds; reversed range → 400 `CareHomes.Revenue.InvalidRange`. Owner dashboard and revenue for both Draft and Approved facilities are proven through `GetOwnerCareHomeDashboardHandler` and `GetOwnerCareHomeRevenueHandler` on PostgreSQL. | Worker delivery §2.2, §3, §6; `CareHomeFinanceReadModels.cs`; `CareHomeFinancePostgresTests` (12/12 PostgreSQL passed); existing `CareHomeOwnerPortalTests` (19/19) |

| Route | Verb | Policy | Query | Status behavior |
| --- | --- | --- | --- | --- |
| `GET /api/v1/care-homes/dashboard/mine` | GET | `CareHomeOwnerAccess` | `GetOwnerCareHomeDashboardQuery(UserId actor, ...)` | 200; no facility → 404 `NotFound`; reversed date range → 400 `InvalidRange` |
| `GET /api/v1/care-homes/revenue/mine` | GET | `CareHomeOwnerAccess` | `GetOwnerCareHomeRevenueQuery(UserId actor, ...)` | 200; no facility → 404 `NotFound`; reversed date range → 400 `InvalidRange` |
| `GET /api/v1/care-homes/revenue/mine/export.csv` | GET | `CareHomeOwnerAccess` | `GetOwnerCareHomeRevenueQuery(...)` | CSV file or 404/400 |

## What needs correction or backend work

| Issue | Classification | Current truth and expected frontend action | Backend responsibility / phase | Evidence |
| --- | --- | --- | --- | --- |
| FR-20261016-03 | Historical C5 — already resolved; not a current defect | No client change needed. Build navigation from each route's actual lifecycle capability: Draft dashboard/revenue access is intentional and works. Do not hide Draft owner reads; do not treat 404 as "undeployed" when it means "no facility owned by this caller." | FUR-03 — verification only; no runtime change | Worker delivery §2.2, §3, §6 |

## Implementation connection

| Issue | Contract | Implementation/revision | Verification | Deployment | Remaining action |
| --- | --- | --- | --- | --- | --- |
| FR-20261016-03 | [FUR-03 contract](../../../../internal/work/portal-api-reconciliation/contracts/fur-03-finance-verification.md) | No change needed — ownership resolution already status-agnostic in `CareHomeFinanceReadModels.cs`; routes, policies, DTOs, date semantics, error codes unchanged | 12/12 disposable-PostgreSQL tests passed; 19/19 existing owner-portal HTTP tests; 25/25 route/policy mapping tests; strict build 0 warnings/errors | N/A — no runtime API delta | Owner portal keeps Draft and Approved finance reads enabled; confirm no-facility → 404 handling |

The linked delivery report is backend evidence, not proof of client adoption.

## Decisions and evidence requested

- Owner decisions: none for this topic.
- Frontend evidence: confirm owner portal reads finance data for Draft facilities (not only Approved), handles no-facility 404 as "no facility owned" (not undeployed), and keeps using existing `dashboard/mine` and `revenue/mine` shapes.
- Acceptance: no new endpoint; existing route/query/DTO shapes unchanged; `CareHomeOwnerAccess` authorization unchanged; no `FinanceAdmin` or restricted-token access; no rate-limit change.
- Configuration/migrations: none required.
- Next action: owner forwards this notice; no frontend code defect is asserted.
