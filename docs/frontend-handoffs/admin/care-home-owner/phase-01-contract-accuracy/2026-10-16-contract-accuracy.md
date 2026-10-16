# Care Home Owner portal — FUR-01 contract accuracy — inventory, visits, finance reads

Date: 2026-10-16 (Africa/Cairo). Phase: FUR-01.
Purpose: implementation delivery (local; not deployed).
Audience: Care Home Owner portal (Admin audience = Admin portal + Care Home Owner portal).
Communication state: ready for owner forwarding; receipt not confirmed.

## Baseline and authority

- Original intake and issue IDs: existing-contract portions of `FR-20261016-34` (owner inventory mutations/media/availability, booking-request list, visits and visit settings, dashboard/revenue), `FR-20261016-32` (visit enums/day), `FR-20261016-42` (documents/photos), `FR-20261016-45` (reason limits). Full evidence in [the intake audit](../../../../internal/work/portal-api-reconciliation/frontend-report-audit-2026-10-16.md).
- Canonical plan/approved contract: [FUR-01 contract](../../../../internal/work/portal-api-reconciliation/contracts/fur-01-contract-accuracy.md) (owner-approved 2026-10-16).
- Source revision and relevant local delta: intake `4b65369d760c8af6b2242ebd2022a6cb10057580`; FUR-01 local delta uncommitted. See [delivery report](../../../../internal/work/portal-api-reconciliation/reports/fur-01-contract-accuracy.md).
- Deployment revision: deployed Production remains release `9167e71`; FUR-01 changes are implemented and tested locally only — **not published, not deployed**.
- Evidence limits: source inspection plus isolated local tests (deterministic dispatch fakes + one minimal real inventory readback; no database network, provider, or VPS runs).
- Frontend repository inspected: no.

## What is correct and should remain

| Issue | Correct behavior/usage | Evidence |
| --- | --- | --- |
| FR-20261016-42 | Submission still requires all four document types in the current revision plus at least two profile photos including a cover. Keep those UI checks. | `docs/care-homes/owner-onboarding.md` |
| FR-20261016-32 | Visit `kind` 1 Prospective / 2 Resident; visit `status` 1–8; numeric `DayOfWeek` 1 = Monday. Keep numeric usage. | `docs/care-homes/visits.md` |
| FR-20261016-34 | Inventory mutations return the full `CareHomeInventoryDto` (`200`); media uploads return `201` with the media DTO; media removals return empty `204`; availability (both owner and admin) is a `POST` returning a `200` array. These were always the runtime shapes — only the metadata is new. | `docs/care-homes/inventory-and-availability.md`; `docs/postman/care-homes/Sanad.CareHomes.Owner.postman_collection.json` |

## What needs correction or backend work

| Issue | Classification | Current truth and expected frontend action | Backend responsibility / phase | Evidence |
| --- | --- | --- | --- | --- |
| FR-20261016-34 | Backend metadata defect (fixed locally; frontend report was correct) | These success schemas were genuinely absent from the published OpenAPI — the frontend correctly reported the gaps. The runtime shapes are pre-existing deployed behavior; only their metadata is new and local. Regenerate owner types from the new OpenAPI only after release: booking-request list is `200` array of `CareHomeBookingListItem` (status 1–9 `CareHomeBookingStatus`); owner visits list is `200` array of `CareHomeVisitResponse` with discoverable nested enums; GET and PUT visit-settings are `200 CareHomeVisitSettingsResponse`; dashboard/revenue are `200 CareHomeDashboard` / `CareHomeRevenueReport`. Do not code against guessed DTOs. No frontend code was inspected, so no client bug is claimed. | FUR-01 — implemented/tested locally; not published/deployed | `/openapi/v1.json`; `PortalRemainingResponseContractTests` |
| FR-20261016-34 | Accurate method correction (existing behavior; documented in FUR-01) | Availability is `POST`, not `GET` — for both owner (`/mine/availability`) and admin (`/admin/{facilityId}/availability`) routes. This corrects the assumed method; it is separate from the missing-schema additions above and reflects long-standing runtime truth. | None (existing behavior republished) | `docs/care-homes/inventory-and-availability.md` |
| FR-20261016-42 | No change | `contactName` stays explicit profile data (no automatic Identity derivation); Arabic and English admission conditions both stay required. | None (existing rules republished) | `docs/care-homes/owner-onboarding.md` |

## Implementation connection

| Issue | Contract | Implementation/revision | Verification | Deployment | Remaining action |
| --- | --- | --- | --- | --- | --- |
| FR-20261016-32/34/42 | [FUR-01 contract](../../../../internal/work/portal-api-reconciliation/contracts/fur-01-contract-accuracy.md) | `CareHomeInventoryController` (19 ops), `CareHomeBookingRequestsController.List`, `CareHomeVisitsController` (OwnerList + settings GET/PUT), `CareHomeFinanceController` (Dashboard/Revenue) metadata; Owner Postman descriptions corrected; local delta uncommitted | Isolated tests 179/179 incl. real inventory readback; strict build 0 warnings/errors; architecture 1/1; mapping script exit 0 (556/620) | Not deployed | Mastermind review → accepted-phase release → owner portal regenerates types |

Future-work links may point to a plan now; add actual implementation/evidence only
when it exists. A passing test is not a deployment receipt.

## Decisions and evidence requested

- Owner decisions: none for this topic.
- Frontend evidence: after release, confirm regenerated owner types show the 19 inventory operation shapes, booking-list item enums, visit settings DTO, and dashboard/revenue DTOs. The deployed API already returns these shapes today; only the published schemas are new.
- Acceptance: owner portal builds against released OpenAPI; availability called as POST; no invented success envelopes.
- Configuration/migrations: none. No routes, runtime behavior, config, or migrations changed.
- Next action: owner returns the worker delivery report to the planning Mastermind for review.
