# Care Home Owner portal — FUR-08 Owner disputes — owner check-in dispute readback

Date: 2026-10-18 (Africa/Cairo). Phase: FUR-08.
Purpose: backend delivery/deployed notice; client adoption unconfirmed.
Audience: Care Home Owner portal (Admin audience = Admin portal + Care Home Owner portal).
Communication state: backend phase accepted and deployed at `14757cc`; client
adoption/forwarding is unconfirmed.

No frontend repository was inspected. No frontend code defect is claimed.

## Baseline and authority

- Original intake and issue IDs: frontend intake item #11 (missing owner
  dispute list; add own-facility-only read access, do not grant admin resolution
  powers) from the A2 requested-endpoints table of the
  [intake audit](../../../../internal/work/portal-api-reconciliation/frontend-report-audit-2026-10-16.md),
  with display fields clarified together with intake item #27 (dispute display
  gap: resident name snapshots and labeled original booking amount) from the A3
  missing-fields table of the same audit.
- Canonical plan/approved contract: owner-approved FUR-08 contract (read-only
  `GET /api/v1/care-homes/check-in-disputes/mine`); [worker
  delivery](../../../../internal/work/portal-api-reconciliation/reports/fur-08-worker-delivery.md).
- Implementation/deployed revision: `14757ccce6ad1fb59482ca26d4df5e17ca8c56d9`,
  pushed to `origin/main` and deployed to Production.
- Deployment revision and evidence: [release
  receipt](../../../../internal/work/portal-api-reconciliation/reports/fur-08-release.md)
  — strict Release build 0 warnings/errors; focused worker tests 20/20; Postman
  JSON/route/defaults and `git diff --check` passed; zero migrations applied;
  all 9/9 migration heads matched; service active/`NRestarts=0`; protected host
  configuration fingerprints unchanged; standard verifier and anonymous smoke
  HTTP 200 passed through an SSH tunnel. **No live authenticated FUR-08 endpoint
  request or Bruno run was performed.**
- Evidence limits: no live authenticated owner request or Bruno verification;
  endpoint behavior is covered by the accepted focused tests (own-facility
  visibility, foreign-owner isolation, empty/no-facility results, filters,
  count/order/paging, nullable enrichment, field-exclusion and OpenAPI
  nullability). Direct public HTTP from the build machine remains unverified;
  the SSH tunnel is the deployment evidence. Frontend repositories inspected:
  none.

## What is correct and should remain

| Issue | Correct behavior/usage | Evidence |
| --- | --- | --- |
| #11 | `GET /api/v1/care-homes/check-in-disputes/mine` is a read-only, owner-scoped list of check-in disputes. The facility scope is derived and enforced server-side from the authenticated owner (`CareHomeOwnerAccess`); no caller-selected facility parameter is accepted. An owner with no facility deliberately receives `200` with a valid empty page (a documented exception to the owner finance reads' 404). | [FUR-08 worker delivery](../../../../internal/work/portal-api-reconciliation/reports/fur-08-worker-delivery.md); [booking lifecycle guide](../../../../care-homes/booking-lifecycle.md) |
| #11 | Query convention: `page` default 1 (minimum 1); `pageSize` default 25 (range 1–100); optional `status` accepts only `Open` or `Resolved` (case-insensitive names; numeric/undefined values are rejected with the standard API validation response). Response is `{ page, pageSize, totalCount, items }`; count is taken after owner and status filtering and before paging. Ordering is `openedOnUtc` descending, then case ID descending for ties. | [FUR-08 worker delivery](../../../../internal/work/portal-api-reconciliation/reports/fur-08-worker-delivery.md); [booking lifecycle guide](../../../../care-homes/booking-lifecycle.md) |
| #11/#27 | Each item exposes case ID, booking ID, facility ID, status, opened timestamp, nullable checkout timestamp (null for a Family-submitted dispute before checkout), nullable Family reason (null for a checkout-triggered case), nullable resident Arabic/English name snapshots, and nullable original booking amount/currency enrichment (amount is the booking's stored `TotalAmount`; currency is the current fixed `EGP` convention — the booking does not persist currency). `effectiveCheckInOnUtc` is the Admin's case outcome only and never rewrites the booking's recorded check-in/checkout timestamps; `resolvedOnUtc` is null while open. | [FUR-08 worker delivery](../../../../internal/work/portal-api-reconciliation/reports/fur-08-worker-delivery.md) |
| #11 | The owner response exposes no Admin resolution evidence, rationale or resolver identity, no resident/family identifiers, and no payment/refund/payout details. Owners have no mutation or resolution powers; Sanad support handles cases. If the related booking is unavailable, resident snapshots and amount enrichment are null. | [FUR-08 worker delivery](../../../../internal/work/portal-api-reconciliation/reports/fur-08-worker-delivery.md); [booking lifecycle guide](../../../../care-homes/booking-lifecycle.md) |

| Route | Verb | Policy | Status behavior |
| --- | --- | --- | --- |
| `GET /api/v1/care-homes/check-in-disputes/mine` | GET | `CareHomeOwnerAccess` (facility scope enforced server-side) | 200 page `{ page, pageSize, totalCount, items }`; 200 empty page when the owner has no facility; invalid paging/status → standard API validation response |

## What needs correction or backend work

| Issue | Classification | Current truth and expected frontend action | Backend responsibility / phase | Evidence |
| --- | --- | --- | --- | --- |
| #11 | Frontend assumption guidance (not a proven client defect) | Do not give owners resolve/reject/admin actions on disputes; the endpoint is read-only and support handles cases. Never present `effectiveCheckInOnUtc` as the booking's recorded check-in time — it is the Admin case outcome only. | None — deployed truth (FUR-08) | [FUR-08 worker delivery](../../../../internal/work/portal-api-reconciliation/reports/fur-08-worker-delivery.md) |
| #27 | Frontend assumption guidance | Display the original booking amount with its labeled meaning (stored `TotalAmount`, fixed `EGP` currency convention); do not substitute refund, payout or net figures, and render null-safe rows when the booking is unavailable. | None — deployed truth (FUR-08, building on FUR-04 #27) | [FUR-08 worker delivery](../../../../internal/work/portal-api-reconciliation/reports/fur-08-worker-delivery.md) |

## Implementation connection

| Issue | Contract | Implementation/revision | Verification | Deployment | Remaining action |
| --- | --- | --- | --- | --- | --- |
| #11/#27 | Owner-approved FUR-08 contract (worker prompt: [fur-08 brief](../../../../internal/work/portal-api-reconciliation/worker-prompts/fur-08-owner-disputes.md)) | Owner GET action in `CareHomeBookingRequestsController.cs` + paged owner-scoped read query in `CareHomeStayCommands.cs`; owner guide and Owner Care Homes Postman collection updated | Strict Release build 0 warnings/errors; focused tests 20/20; OpenAPI envelope/nullability contract tests incl. equal-opened-time tie-break; Postman JSON/route/defaults; `git diff --check` | Deployed `14757cc` and verified; zero migrations applied; no live authenticated feature request or Bruno run | Render the open/resolved list with status filter and null-safe fields; route disputes to Sanad support |

## Decisions and evidence requested

- Owner decisions: none for this topic; no new scope is requested.
- Frontend evidence: confirm the owner portal renders the dispute list, its
  status filter, paging and nullable fields against the deployed OpenAPI, and
  labels the original booking amount correctly. No mobile-client behavior is
  asserted.
- Acceptance: additive read-only owner route only; existing Admin and Family
  dispute routes, DTO wire shapes and behavior unchanged.
- Configuration/migrations: none (no migration or configuration change in this
  phase).
- Next action: owner forwards this notice at discretion; Mastermind review of
  the FUR-10 integration handoff. No frontend code defect is asserted.
