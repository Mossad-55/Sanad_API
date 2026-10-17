# Care Home Owner portal — FUR-04 Care Home readback — profile revisions and booking snapshots

Date: 2026-10-16 (Africa/Cairo). Phase: FUR-04.
Purpose: backend contract delivery notice; client adoption unconfirmed.
Audience: Care Home Owner portal (Admin audience = Admin portal + Care Home Owner portal).
Communication state: backend phase accepted and deployed; client adoption/forwarding remains unconfirmed.

No frontend repository was inspected. No frontend code defect is claimed.
This notice communicates the delivered backend readback contract and what
owner-portal clients can rely on in deployed revision `31b13cc`. No new endpoint, route,
policy, or lifecycle change is included.

## Baseline and authority

- Original intake and issue IDs: audit items `#24` (owner profile readback)
  and `#26` (owner operational booking detail) from the A3 missing-fields audit.
- Canonical plan/approved contract: [FUR-04 contract](../../../../internal/work/portal-api-reconciliation/contracts/fur-04-care-home-readback.md) (owner-approved 2026-10-16, including the test-schema addendum).
- Source baseline: `14677d961813a94be602390e7801623c766ad53c` (`main == origin/main`, clean at phase start).
- Delivery report: [fur-04-worker-delivery](../../../../internal/work/portal-api-reconciliation/reports/fur-04-worker-delivery.md); [release receipt](../../../../internal/work/portal-api-reconciliation/reports/fur-04-release.md) records Mastermind acceptance and Production deployment of `31b13cc`.
- Deployment status: **deployed and verified**; frontend forwarding/adoption is unconfirmed.
- Evidence limits: focused unit proof plus disposable-PostgreSQL proof (7/7
  tests passed against `SanadFur04VerifyDb`); no frontend repository,
  provider, Bruno, or production run. Frontend repositories inspected: none.

## What is delivered and should be relied on

| Issue | Correct behavior/usage | Evidence |
| --- | --- | --- |
| #24 | `GET /api/v1/care-homes/facilities/mine` keeps the existing `draft` meaning and shape and adds two nullable revision-bound readbacks: `submittedProfile` (`revisionId` plus profile projection, populated only while a submitted revision is pending review) and `approvedProfile` (follows `approvedRevisionId`, remains available while a newer editable draft exists). The GET never creates, saves, or mutates a draft. Owner private contact fields stay on owner/Admin reads and are never returned through public discovery. | Worker delivery §§2–3, §6; `CareHomeProfileCommands.cs`; `CareHomeReadbackTests` + `CareHomeReadbackPostgresTests` (PostgreSQL passed) |
| #26 | `GET /api/v1/care-homes/booking-requests/mine/{bookingId}/operational` now returns the booking's stored resident `residentArabicName`/`residentEnglishName`/`residentAge` snapshots. These are immutable booking snapshots, never recalculated from current Elderly/Identity records, and no cross-module lookup was added. Own-facility scoping and existing 404 behavior for missing/foreign bookings are unchanged. | Worker delivery §§2–3; `CareHomeStayCommands.cs` |

| Route | Verb | Policy | Status behavior |
| --- | --- | --- | --- |
| `GET /api/v1/care-homes/facilities/mine` | GET | `CareHomeOwnerAccess` | 200 with `draft`/`submittedProfile`/`approvedProfile` per state matrix; no facility → 404 `CareHomes.Facility.NotFound`; GET performs no mutation |
| `GET /api/v1/care-homes/booking-requests/mine/{bookingId}/operational` | GET | `CareHomeOwnerAccess` | 200 with stored resident snapshots; missing/foreign booking → 404 `CareHomes.Bookings.NotFound` |

## What needs correction or backend work

No open correction is known on these reads. Owner dispute-list capability
remains FUR-08 and is not part of this delivery.

## Implementation connection

| Issue | Contract | Implementation/revision | Verification | Deployment | Remaining action |
| --- | --- | --- | --- | --- | --- |
| #24 | [FUR-04 contract](../../../../internal/work/portal-api-reconciliation/contracts/fur-04-care-home-readback.md) | `CareHomeOwnerProfile.SubmittedProfile`/`ApprovedProfile` (`CareHomeProfileRevisionProfile`: `revisionId` + `profile`) | Owner state-matrix unit tests (Draft/PendingReview/NeedsCorrection/Rejected/Approved/Approved-with-newer-draft) + worker PostgreSQL proof; final guarded Unit 3475/0/31; no-GET-mutation asserted | Deployed `31b13cc`; verifier passed; no migration applied | Render `submittedProfile` only when non-null (pending review) and `approvedProfile` whenever present; do not treat a null `draft` as "no profile" |
| #26 | [FUR-04 contract](../../../../internal/work/portal-api-reconciliation/contracts/fur-04-care-home-readback.md) | `CareHomeOwnerBookingOperationalResponse` resident snapshot fields from stored booking values | Focused unit + worker PostgreSQL proof incl. own-facility/404; final guarded Unit 3475/0/31; deployment verifier passed | Deployed `31b13cc`; no migration applied | Display stored snapshots as-is; do not substitute live identity data |

The linked delivery report is backend evidence, not proof of client adoption.

## Decisions and evidence requested

- Owner decisions: release approval for this phase is still required; no new scope is requested.
- Frontend evidence: confirm the owner portal handles null `draft`/`submittedProfile`/`approvedProfile` per state and never issues a save to refresh a read.
- Acceptance: no new endpoint; existing routes/policies/DTO meanings unchanged; additive nullable fields only.
- Configuration/migrations: none required (no migration or config change in this phase).
- Next action: Mastermind review, then owner release decision; owner forwards this notice after acceptance. No frontend code defect is asserted.
