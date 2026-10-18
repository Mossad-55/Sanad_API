# Care Home Owner portal — FUR-05 Owner settlements — owner settlement ledger readback

Date: 2026-10-18 (Africa/Cairo). Phase: FUR-05.
Purpose: backend delivery/deployed notice; client adoption unconfirmed.
Audience: Care Home Owner portal (Admin audience = Admin portal + Care Home Owner portal).
Communication state: backend phase accepted and deployed at `ca2ef2f`; client
adoption/forwarding is unconfirmed.

No frontend repository was inspected. No frontend code defect is claimed.

## Baseline and authority

- Original intake and issue IDs: frontend intake item #9 (missing owner
  settlement read) from the A2 requested-endpoints table of the
  [intake audit](../../../../internal/work/portal-api-reconciliation/frontend-report-audit-2026-10-16.md):
  Admin payout ledger and calculations already exist; `netCollected` is customer
  collection after completed refunds (including platform fee/tax), not owner
  income; commission must not be recomputed in the frontend.
- Canonical plan/approved contract: [FUR-05 owner settlements
  contract](../../../../internal/work/portal-api-reconciliation/contracts/fur-05-owner-settlements.md)
  (owner-approved 2026-10-17); [worker
  delivery](../../../../internal/work/portal-api-reconciliation/reports/fur-05-worker-delivery.md);
  [Mastermind review](../../../../internal/work/portal-api-reconciliation/reports/fur-05-mastermind-review.md).
- Implementation/deployed revision: `ca2ef2fbe16c395194301a88637aa93008e7066f`,
  pushed to `origin/main` and deployed to Production.
- Deployment revision and evidence: [release
  receipt](../../../../internal/work/portal-api-reconciliation/reports/fur-05-release.md)
  — strict build 0 warnings/errors; worker-focused tests 56/56; Mastermind guarded
  full Unit 3,482/0/31 and Architecture 1/1; zero migrations applied; all nine
  migration heads matched; service active/`NRestarts=0`; host configuration
  fingerprints unchanged; standard verifier and anonymous smoke HTTP 200 passed
  through an SSH tunnel. Post-deployment OpenAPI inspection confirms
  `GET /api/v1/care-homes/payouts/mine` is present and documents HTTP 200.
- Evidence limits: no live authenticated owner-ledger request was made (no
  authorized owner fixture/token for release); ledger behavior is covered by the
  passing isolated tests. Direct public HTTP from the build machine remains
  unverified; tunnel checks are the deployment evidence. Frontend repositories
  inspected: none.

## What is correct and should remain

| Issue | Correct behavior/usage | Evidence |
| --- | --- | --- |
| #9 | `GET /api/v1/care-homes/payouts/mine` returns the owner-facility settlement ledger: currently eligible unpaid balances (positive net payable) **and** already-recorded transfers, including a recorded payout followed by debt. Rows reuse the existing payout, refund, fee and debt truth; a non-null `payoutId` identifies a recorded transfer and a null `payoutId` identifies an eligible unpaid balance. Ordering stays checkout-time descending; no pagination or filters were added. | [FUR-05 contract](../../../../internal/work/portal-api-reconciliation/contracts/fur-05-owner-settlements.md); [release receipt](../../../../internal/work/portal-api-reconciliation/reports/fur-05-release.md) |
| #9 | Authorization is `CareHomeOwnerAccess`; the facility is resolved from the authenticated owner and no caller-selected facility scope is accepted. An owner with no facility gets the existing safe `404 CareHomes.Bookings.NotFound`; a facility with no rows gets `200` with an empty array. A missing effective Finance configuration fails closed with the existing `CareHomes.Payout.FinanceConfigurationMissing` behavior. | [FUR-05 contract](../../../../internal/work/portal-api-reconciliation/contracts/fur-05-owner-settlements.md); [Mastermind review](../../../../internal/work/portal-api-reconciliation/reports/fur-05-mastermind-review.md) |

| Route | Verb | Policy | Status behavior |
| --- | --- | --- | --- |
| `GET /api/v1/care-homes/payouts/mine` | GET | `CareHomeOwnerAccess` | 200 ledger rows (eligible unpaid + recorded transfers); 200 empty array when no rows; 404 `CareHomes.Bookings.NotFound` when the owner has no facility |

## What needs correction or backend work

| Issue | Classification | Current truth and expected frontend action | Backend responsibility / phase | Evidence |
| --- | --- | --- | --- | --- |
| #9 | Frontend assumption guidance (not a proven client defect) | Do not display `netCollected` (customer collection after refunds, including platform fee/tax) as owner income, and do not recompute commission/fee/debt in the frontend. Use the ledger row fields as returned: remaining customer amount, gross, fee rate/amount, net payable, facility debt, transfer reference and recorded timestamp. | None — deployed truth (FUR-05) | [FUR-05 contract](../../../../internal/work/portal-api-reconciliation/contracts/fur-05-owner-settlements.md) |
| #9 | Frontend assumption guidance | Treat a row with a non-null `payoutId` as an already-recorded transfer and a row with a null `payoutId` as an eligible unpaid balance; never infer payout timing from the client. | None — deployed truth (FUR-05) | [FUR-05 contract](../../../../internal/work/portal-api-reconciliation/contracts/fur-05-owner-settlements.md) |

## Implementation connection

| Issue | Contract | Implementation/revision | Verification | Deployment | Remaining action |
| --- | --- | --- | --- | --- | --- |
| #9 | [FUR-05 contract](../../../../internal/work/portal-api-reconciliation/contracts/fur-05-owner-settlements.md) | Read-only owner settlement query in `CareHomeStayCommands.cs`; Care Homes finance guide and Care Homes Finance Postman collection updated | Strict build 0 warnings/errors; focused 56/56; guarded full Unit 3,482/0/31; architecture 1/1; Postman JSON parsed; 557/557 route comparison zero gaps | Deployed `ca2ef2f` and verified; zero migrations applied; no live authenticated owner-ledger run | Render ledger rows using only the returned fields; label recorded transfers versus eligible unpaid balances |

## Decisions and evidence requested

- Owner decisions: none for this topic; no new scope is requested.
- Frontend evidence: confirm the owner portal renders the ledger with
  transfer/unpaid distinction, currency labels and null-safe rows against the
  deployed OpenAPI. No mobile-client behavior is asserted.
- Acceptance: existing route/policies unchanged; additive read-only owner
  projection only; no writes, audit mutations or provider calls.
- Configuration/migrations: none (no migration or configuration change in this
  phase).
- Next action: owner forwards this notice at discretion; Mastermind review of
  the FUR-10 integration handoff. No frontend code defect is asserted.
