# Admin portal — FUR-06 Eligible caregiver payouts — eligible booking selector

Date: 2026-10-18 (Africa/Cairo). Phase: FUR-06.
Purpose: backend delivery/deployed notice; client adoption unconfirmed.
Audience: Admin portal (Admin audience = Admin portal + Care Home Owner portal).
Communication state: backend phase accepted and deployed at `7339034`; client
adoption/forwarding is unconfirmed.

No frontend repository was inspected. No frontend code defect is claimed.

## Baseline and authority

- Original intake and issue IDs: frontend intake item #10 (missing eligible-payout
  selector) from the A2 requested-endpoints table of the
  [intake audit](../../../../internal/work/portal-api-reconciliation/frontend-report-audit-2026-10-16.md):
  current admin list/detail/record/failure actions existed, but no eligible
  booking selector; full existing eligibility must be reused and failed payout
  attempts must not permanently exclude a later eligible payout.
- Canonical plan/approved contract: [FUR-06 eligible caregiver payouts
  contract](../../../../internal/work/portal-api-reconciliation/contracts/fur-06-eligible-caregiver-payouts.md);
  [worker delivery](../../../../internal/work/portal-api-reconciliation/reports/fur-06-worker-delivery.md);
  [Mastermind review](../../../../internal/work/portal-api-reconciliation/reports/fur-06-mastermind-review.md).
- Implementation/deployed revision: `73390344fcd2350a7ce33e7532e68813d79ba495`,
  pushed to `origin/main` and deployed to Production.
- Deployment revision and evidence: [release
  receipt](../../../../internal/work/portal-api-reconciliation/reports/fur-06-release.md)
  — strict build 0 warnings/errors; focused caregiver payout/Admin
  authorization tests 21/21 before commit; fresh package with all nine source-
  derived migration heads; exact-target revision, database reachable, 9/9 applied
  heads matched, services smoke HTTP 200; host configuration fingerprints
  unchanged. No migration was added in this phase; the endpoint briefly
  returned HTTP 502 during startup, then recovered. The guarded full Unit and
  Architecture attempts failed to connect to `testhost` and were not repeated
  during publish/deploy (recorded limitation).
- Evidence limits: no live authenticated selector request or Bruno run; the
  Postman request and saved 200 example are an authored representative example,
  not runtime response evidence. Frontend repositories inspected: none.

## What is correct and should remain

| Issue | Correct behavior/usage | Evidence |
| --- | --- | --- |
| #10 | `GET /api/v1/admin/caregiver-payouts/eligible-bookings` is a read-only selector of completed-and-paid bookings an operator may record a manual payout for right now. It reuses the record command's full eligibility: `Completed` with both completion and payment timestamps, effective payout delay elapsed, `Verified` payout account, and no `Paid` payout for the booking. A `Failed` payout does not permanently exclude the booking. Missing effective policy fails closed (`400 Finance.PayoutPolicy.Missing`). | [FUR-06 contract](../../../../internal/work/portal-api-reconciliation/contracts/fur-06-eligible-caregiver-payouts.md); [worker delivery](../../../../internal/work/portal-api-reconciliation/reports/fur-06-worker-delivery.md) |
| #10 | The response item exposes only what is needed to identify and record a payout: booking ID, caregiver ID, existing safe Arabic/English display name, completion time, payout-eligible time, and the existing caregiver fee amount/currency. `CaregiverAmount`/`Currency` come from the booking price snapshot (`BaseCaregiverFee` + snapshot currency); the platform fee is never deducted. **No payout-account, bank or IBAN field is returned.** | [worker delivery](../../../../internal/work/portal-api-reconciliation/reports/fur-06-worker-delivery.md); [admin caregiver payout guide](../../../../app/admin/caregiver-payouts.md) |
| #10 | Query convention: optional `caregiverId` filter; `page` default 1 (minimum 1); `pageSize` default 20 (range 1–100). Ordering is oldest payout-eligible time first, then booking id for stable pages — matching the existing admin review-queue precedent. | [worker delivery](../../../../internal/work/portal-api-reconciliation/reports/fur-06-worker-delivery.md); [admin caregiver payout guide](../../../../app/admin/caregiver-payouts.md) |

| Route | Verb | Policy | Status behavior |
| --- | --- | --- | --- |
| `GET /api/v1/admin/caregiver-payouts/eligible-bookings` | GET | `PayoutOperationalAdmin` (SuperAdmin + FinanceAdmin) | 200 paged eligible bookings; missing effective policy → 400 `Finance.PayoutPolicy.Missing` (fail-closed) |

## What needs correction or backend work

| Issue | Classification | Current truth and expected frontend action | Backend responsibility / phase | Evidence |
| --- | --- | --- | --- | --- |
| #10 | Frontend assumption guidance (not a proven client defect) | Do not paste caregiver/booking GUIDs or client-computed eligibility into a request; the selector is the server's decision. Use the returned `eligibleOnUtc` and `caregiverAmount` as displayed truth; do not re-derive amounts or fees. | None — deployed truth (FUR-06) | [FUR-06 contract](../../../../internal/work/portal-api-reconciliation/contracts/fur-06-eligible-caregiver-payouts.md) |
| #10 | Frontend assumption guidance | A booking whose payout previously `Failed` legitimately reappears in the selector; this is retry eligibility, not an error or duplicate. | None — deployed truth (FUR-06) | [worker delivery](../../../../internal/work/portal-api-reconciliation/reports/fur-06-worker-delivery.md) |

## Implementation connection

| Issue | Contract | Implementation/revision | Verification | Deployment | Remaining action |
| --- | --- | --- | --- | --- | --- |
| #10 | [FUR-06 contract](../../../../internal/work/portal-api-reconciliation/contracts/fur-06-eligible-caregiver-payouts.md) | `GetEligibleBookings` action + `GetEligibleCaregiverPayoutBookingsQuery` handler; admin caregiver payout guide and admin Finance Postman collection updated | Strict build 0 warnings/errors; focused 21/21; Caregivers suite 840/840; architecture 1/1; Postman JSON parsed; route mapping exit 0 | Deployed `7339034` and verified; no migration; no live authenticated run | Consume the selector for the record-payout workflow; never request payout-account/IBAN data (not exposed) |

## Decisions and evidence requested

- Owner decisions: none for this topic; no new scope is requested. A
  concurrent duplicate-writer collision during the phase was disclosed and
  resolved to a single implementation (kept the `eligible-bookings` variant
  matching the guide/Postman/authorization test); see the
  [worker delivery](../../../../internal/work/portal-api-reconciliation/reports/fur-06-worker-delivery.md).
- Frontend evidence: confirm the admin portal consumes the selector response
  shape, its filter/paging convention and the fail-closed policy error against
  the deployed OpenAPI.
- Acceptance: additive read-only route only; existing admin payout
  list/detail/record/mark-failed operations unchanged; no writes, provider calls
  or schema change.
- Configuration/migrations: none (no migration or configuration change in this
  phase).
- Next action: owner forwards this notice at discretion; Mastermind review of
  the FUR-10 integration handoff. No frontend code defect is asserted.
