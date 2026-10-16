# Admin portal — FUR-01 contract accuracy — admin operations and conventions

## Release update — 2026-10-16

Mastermind accepted the corrected phase and deployed source `f9231fb`.
The actual service OpenAPI passes all 29 phase contracts; nine migration heads
match with zero applied, configuration unchanged. See the
[release receipt](../../../../internal/work/portal-api-reconciliation/reports/fur-01-deployment.md)
for exact revision, gates and the direct-public-network limitation. Refresh
OpenAPI/types on your normal API connection; no new business behavior is added.
The original local-delivery snapshot below is preserved as historical evidence;
its not-yet-deployed statements describe that earlier checkpoint.

Date: 2026-10-16 (Africa/Cairo). Phase: FUR-01.
Purpose: implementation delivery (local; not deployed).
Audience: Admin portal (Admin = Admin portal + Care Home Owner portal).
Communication state: ready for owner forwarding; receipt not confirmed.

## Baseline and authority

- Original intake and issue IDs: `FR-20261016-30` through `FR-20261016-35` and existing-contract portions of `FR-20261016-37/39/43/45/46`, plus A6 photo clarification. Full evidence in [the intake audit](../../../../internal/work/portal-api-reconciliation/frontend-report-audit-2026-10-16.md).
- Canonical plan/approved contract: [FUR-01 contract](../../../../internal/work/portal-api-reconciliation/contracts/fur-01-contract-accuracy.md) (owner-approved 2026-10-16); [worker brief](../../../../internal/work/portal-api-reconciliation/worker-prompts/fur-01-contract-accuracy.md).
- Source revision and relevant local delta: intake `4b65369d760c8af6b2242ebd2022a6cb10057580`; FUR-01 local delta uncommitted (metadata attributes, tests, guides, Postman). See [delivery report](../../../../internal/work/portal-api-reconciliation/reports/fur-01-contract-accuracy.md).
- Deployment revision: deployed Production remains release `9167e71`; FUR-01 changes are implemented and tested locally only — **not published, not deployed**. Do not treat new OpenAPI metadata as live until the accepted-phase release completes.
- Evidence limits: source inspection plus isolated local tests (no database, provider, OTP, or VPS runs). Authenticated portal behavior is contract evidence, not a fresh live run.
- Frontend repository inspected: no. Rows below distinguish backend defects fixed
  locally from client assumptions that were never proven against frontend code.

## What is correct and should remain

| Issue | Correct behavior/usage | Evidence |
| --- | --- | --- |
| FR-20261016-35 | Required query flags (`through`, `governorateId`, `cityId`) are already published; missing parents return `400` with field errors. Keep current handling. | `docs/admin/api-contract-conventions.md`; OpenAPI `required: true` |
| FR-20261016-33 | Numeric `DayOfWeek` (0 Sunday–6 Saturday; Monday = 1). String day names are rejected with 400. Keep numeric usage. | `docs/care-homes/visits.md`; `PortalAuthExampleContractTests.NumericEnums` |
| FR-20261016-37 | First-run support-contact `404 Cms.Help.SupportContactNotFound` is the intended unconfigured state, not an error to code around as success. | `docs/admin/help-center.md` |
| FR-20261016-45 | Keep resource-specific reason limits: Care Home review/document reasons 1000 each; caregiver payout-account decisions 500; caregiver payout reason/evidence 2000 and reference 200. There is no single shared 500 cap. | `docs/admin/care-homes.md`; `docs/app/caregivers/payouts.md` |

## What needs correction or backend work

| Issue | Classification | Current truth and expected frontend action | Backend responsibility / phase | Evidence |
| --- | --- | --- | --- | --- |
| FR-20261016-32 | Unproven client assumption (assumption guidance, not a proven code defect) | Help-request aggregate is a dictionary with **enum-name keys** (`{ "Pending": 4, "Resolved": 9 }`); do not model a `statusCounts` array. Keep booking enums distinct: Family/caregiver `BookingStatus` 1–10 vs `CareHomeBookingStatus` 1–9; do not use a 1–5 enum. SOS history `action` (Created/Acknowledged/Resolved/Cancelled) is not SOS status. No frontend code was inspected, so these are contract assumptions to correct, not certified client bugs. | None (existing runtime truth; documented in FUR-01) | `docs/admin/elderly-help-requests.md`; OpenAPI `ElderlyHelpRequestStatus` |
| FR-20261016-34 | Backend metadata defect (fixed locally; frontend report was correct) | These success schemas were genuinely absent from the published OpenAPI — the frontend correctly reported the gaps. The runtime shapes themselves are pre-existing deployed behavior; only their metadata is new and local: help-request detail (`ElderlyHelpRequestResponse`), history (array), platform-charge-rule create (`201` GUID — the old bare-200 metadata was wrong), unread count (bare integer). Regenerate portal types from the new OpenAPI only after release. | FUR-01 — implemented/tested locally; not published/deployed | `/openapi/v1.json`; `PortalRemainingResponseContractTests` |
| FR-20261016-39 | Backend docs defect (fixed locally) | Certificate file download requires `CaregiverReviewAdmin` (SuperAdmin + SupportAdmin). A guide line naming `CaregiversAdmin` was corrected; do not request ContentAdmin access for certificate files. | FUR-01 — implemented/tested locally; not published/deployed | `docs/admin/caregivers-review.md` |
| FR-20261016-43 | No change | Finance-rule operations stay `FinanceOperationalAdmin`; payout operations stay `PayoutOperationalAdmin` (SuperAdmin + FinanceAdmin). Do not conflate the roles. | None (existing truth republished) | `docs/admin/finance-platform-charges.md` |

## Implementation connection

| Issue | Contract | Implementation/revision | Verification | Deployment | Remaining action |
| --- | --- | --- | --- | --- | --- |
| FR-20261016-32/34/39/43/45/46 | [FUR-01 contract](../../../../internal/work/portal-api-reconciliation/contracts/fur-01-contract-accuracy.md) | `AdminElderlyHelpRequestsController` detail/history + `AdminFinanceController` create metadata; guides/Postman corrected; local delta at `4b65369` + FUR-01 changes (uncommitted) | Isolated tests 179/179 incl. literal guide/Postman conformance; strict build 0 warnings/errors; architecture 1/1; Postman JSON 20/20 parse; mapping script exit 0 (556/620) | Not deployed (release `9167e71` unchanged) | Mastermind review → accepted-phase release → frontend regenerates types |

Future-work links may point to a plan now; add actual implementation/evidence only
when it exists. A passing test is not a deployment receipt.

## Decisions and evidence requested

- Owner decisions: none for this topic. Accept (or correct) this report, then authorize the accepted-phase release so the new metadata reaches the deployed OpenAPI.
- Frontend evidence: after release, confirm regenerated Admin types show help-request detail/history schemas, `201` GUID on charge-rule create, and enum-name aggregate keys. The deployed API already returns these shapes today; only the published schemas are new.
- Acceptance: portal builds against released OpenAPI with no invented envelopes; certificate files requested only with SuperAdmin/SupportAdmin tokens.
- Configuration/migrations: none. No routes, runtime behavior, config, or migrations changed.
- Next action: owner returns the worker delivery report to the planning Mastermind for review.
