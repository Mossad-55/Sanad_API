# Care Homes — Backend Task Checklist

Status: Ready for owner-directed execution; implementation Not started. All unchecked items are Not started unless explicitly marked Blocked. Block only dependent work on unresolved contracts in [Care_Homes_Decisions.md](Care_Homes_Decisions.md). Backend/API only. Preserve the existing worker execution phases: scout → implementer → test author → reviewer → documenter; the groupings below are dependency groupings, not a replacement workflow.

## Guardrails and acceptance

- [ ] HC-TASK-001 — Pin the approved revision/worktree delta, inspect current identity/payment/notification/storage conventions, and preserve unrelated changes.
- [ ] HC-TASK-002 — Map established fee-basis/rounding/tax and payout eligibility/reconciliation conventions; ask only genuinely missing contract choices. Fee type/refund treatment, manual payouts, non-expiring documents, SMS OTP and dispute authority are resolved. Block only affected tasks.
- [ ] HC-TASK-007 — Scout publishes the bounded file manifest and endpoint contract map (method, route, permission, request/response/errors, dependencies, unit/Bruno cases, fixtures) before implementation. Include existing fee/payout/rating/notification infrastructure and caregiver authorization callers; propose only missing APIs and do not redo completed behavior.
- [ ] HC-TASK-003 — Keep all Care homes APIs backend-only; do not modify UI repositories or broaden into caregiver booking work.
- [ ] HC-TASK-004 — Every new or behaviorally changed endpoint has unit tests and Bruno requests covering success, validation, authentication, authorization, and applicable conflict/state cases, using disposable fixtures with readback and cleanup.
- [ ] HC-TASK-005 — Maintain a separate Postman collection or feature folder for Care homes with verified examples, variables, descriptions, and response assertions; do not document unverified behavior.
- [ ] HC-TASK-006 — Mastermind builds the project, runs focused slice tests, then the full suite with zero warnings, plus all changed-endpoint Bruno coverage including caregiver authorization/rating changes; document exact commands/results and blockers.

## Identity, facility ownership, and onboarding

- [ ] HC-TASK-010 — Add a facility-owner account/ownership model for self-registration, one facility per owner in V1, phone/email login with password, and OTP verification-only flow. SMS verification OTP is allowed; do not grant platform Admin roles or introduce ordinary SMS notifications.
- [ ] HC-TASK-011 — Add bilingual draft facility profile, contact/address, media, amenities, medical services, admission conditions, and facility-defined catalogs with server-side validation and accepted upload limits.
- [ ] HC-TASK-012 — Add required operating-license, registration, health, and civil-defense documents, nullable expiry dates, replacement history, private access, MIME/size validation, and owner/Admin expiry entry.
- [ ] HC-TASK-013 — Implement submitted-revision freeze, correction/resubmission, rejection, approval, suspension, reactivation, reasoned audit history, optimistic version checks, and last-approved-public behavior for sensitive edits.
- [ ] HC-TASK-014 — Implement required-license expiry behavior, distinguishing Admin-verified non-expiring documents from unverified null expiry: Admin alert and new-booking block while preserving existing stays; replacement upload remains pending review.

## Inventory, rooms, beds, and availability

- [ ] HC-TASK-020 — Add room types, physical rooms, beds, maintenance blocks, and lifecycle/state rules; derive availability from holds, stays, and maintenance.
- [ ] HC-TASK-021 — Enforce room allocation rules: Family selects room type, facility assigns physical room/bed before check-in, shared reserves one bed, private room/suite reserves the whole room.
- [ ] HC-TASK-022 — Add bilingual facility-defined room pricing in EGP and calendar-month period calculation (same date next month, month-end clamp), without automatic renewal.
- [ ] HC-TASK-023 — Add room transfers and maintenance availability updates; explicitly defer walk-ins, automatic no-show cancellation, and advanced fault-management workflows.

## Family discovery, stays, payments, and refunds

- [ ] HC-TASK-030 — Add authorized elderly dropdown contract accepting `elderlyId`, server-side Family ownership authorization, server-derived name/age, medical-data access controls, additional care notes, and responsible contact.
- [ ] HC-TASK-031 — Add approved/licensed public facility discovery with lowest active room-type monthly “starting from” price, and Family home top-10 Care homes ranking by eligible Family star ratings.
- [ ] HC-TASK-032 — Add one-month prepaid Paymob Card/Wallet stay booking, 15-minute checkout hold, verified-payment 24-hour facility decision hold, 24-hour earliest arrival, accept/reject/timeout lifecycle, and concurrency protection.
- [ ] HC-TASK-033 — Add/reuse platform-fee configuration/application for Family charges and facility payouts using Admin-configured percentages and confirmed proportional fee refunds; resolve remaining calculation basis before money movement, and version and snapshot quoted base/fees, rounding and totals so later fee edits do not change existing transactions. Map existing global consumers before changing shared fee behavior.
- [ ] HC-TASK-038 — Implement facility payable ledger, percentage payout-fee deduction and Admin-recorded manual bank transfers with evidence/reference and settlement history; confirm missing payout eligibility/timing conventions before dependent behavior. Reconcile failed refunds and payout reversals; do not assume Paymob collection includes payouts.
- [ ] HC-TASK-039 — Verify concurrent last-capacity checkout, duplicate/out-of-order provider callbacks, late successful payments, acceptance versus expiry/cancellation races and repeated/manual refunds. Authenticate callbacks, enforce idempotency and never overbook or double-refund; distinguish external payment completion from local state.
- [ ] HC-TASK-034 — Add actual check-in/check-out, Family check-in confirmation, Admin dispute cases, room allocation, and auditable state transitions with evidence-based SuperAdmin/SupportAdmin dispute resolution and auditable effective check-in time.
- [ ] HC-TASK-035 — Add refund policy: full refund before check-in, 50% of full monthly payment after Family cancellation post-check-in, full refund for facility rejection/cancellation/timeout; support Paymob status/retry/manual failed-refund action and duplicate protection.
- [ ] HC-TASK-036 — Add paid, facility-approved extensions without automatic charging; anchor and capacity behavior must follow the resolved contract; fully refund future unused extension periods when applicable.
- [ ] HC-TASK-037 — Add receipts, revenue/dashboard read models, exports, and internal booking notes; defer resident wallet and advanced financial claims until separately authorized.

## Visits and ratings

- [ ] HC-TASK-040 — Add prospective and resident visit request contracts, authorization, free one-hour slots, Egypt timezone, exact visitor count, facility hours/closures, 24-hour notice, pending capacity hold, 24-hour expiry, approval, cancellation, and rescheduling rules after unresolved semantics are confirmed.
- [ ] HC-TASK-041 — Add one editable Care-home rating per verified booking after confirmed check-in/service received, with 1–5 stars and optional text, authorization, aggregation and ranking tie-breakers; no unapproved moderation workflow.
- [ ] HC-TASK-042 — Add completed-service caregiver ratings and Family home top-10 caregiver ranking only; do not add caregiver booking/endpoints in this slice.

## Admin operations and bounded caregiver permission correction

- [ ] HC-TASK-050 — Add SuperAdmin/SupportAdmin-only Care-home application queue, detail/submitted snapshot, private document inspection, revision history, reasoned approve/reject/request-correction, suspend/reactivate, and expiring-license views.
- [ ] HC-TASK-051 — Add SuperAdmin/SupportAdmin booking/payment/refund inspection, check-in dispute handling, failed-refund follow-up, fee configuration, and auditable manual refund operations according to resolved contracts.
- [ ] HC-TASK-052 — Ensure ContentAdmin cannot perform Care-home operational review or private-document reads; keep CMS responsibilities separate.
- [ ] HC-TASK-053 — Correct the existing caregiver review authorization from ContentAdmin to SuperAdmin and SupportAdmin, with focused authorization regression tests. Keep caregiver booking scope unchanged.

## Notifications and documentation

- [ ] HC-TASK-060 — Add in-app and email events for onboarding/review, corrections, approvals, suspension/reactivation, license alerts, booking decisions/expiry, payments, refunds, disputes, and relevant Admin actions; no SMS.
- [ ] HC-TASK-061 — Document each verified endpoint separately with route, permission, payload, response, errors, lifecycle, privacy, and idempotency notes; update the Care homes Postman collection/folder with runnable examples and assertions.
- [ ] HC-TASK-062 — Document deferred items and unresolved blockers; update slice evidence and handoff without duplicating this checklist.

## Verification and closeout

- [ ] HC-TASK-070 — Test every endpoint’s success, validation, auth, authorization, and applicable conflict/refund/idempotency paths with unit tests and Bruno.
- [ ] HC-TASK-071 — Run disposable stateful booking/payment/refund/room-allocation fixtures with readback and cleanup; never use production state.
- [ ] HC-TASK-072 — Mastermind integrates only approved changes, resolves concrete review findings without restarting the workflow, builds, runs focused tests then full suite with zero warnings, route/contract checks and all changed-endpoint Bruno coverage; validate synchronized Postman requests. Include month-end/leap-year, Egypt-timezone, cross-owner privacy, license expiry/null dates and ContentAdmin denial cases.
- [ ] HC-TASK-073 — Mark tasks Done only with evidence, report remaining blockers, and prepare the completed-slice handoff. Do not silently start another slice or delete files. Migrations/database resets, commits/pushes and deployment require their own owner authorization.
