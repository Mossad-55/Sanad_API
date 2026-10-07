# Care Homes — Authoritative Decisions

Status: Owner answers consolidated, including final clarifications; execution in progress. Backend-only scope. Updated 2026-10-02. The bounded caregiver review permission correction, Family caregiver rating/top-10 behavior, and the initial Care-home owner draft endpoints are implemented; remaining Care homes behavior is still in progress.

This document consolidates owner-confirmed Care homes behavior from the operator and Family UI intake, including all 24 batch answers. It is authoritative for resolved requirements; unresolved contracts are explicitly listed below and must not override confirmed rules. Screenshots are evidence of user experience, not an authority for unapproved rules.

## Scope and ownership

- V1 covers Care-home facility onboarding, review, inventory, public discovery, Family stays, visits, ratings, payment/refund behavior, notifications, and the facility/Admin backend APIs.
- Facility owners self-register. V1 supports one facility per owner; model the ownership boundary so staff and branches can be added later, but do not implement them now.
- Facility login uses phone and/or email with password. OTP is for verification only, not login. SMS verification OTP is explicitly allowed; ordinary notifications remain in-app/email only. Reuse existing verification infrastructure where applicable.
- The operator portal is backend-only for this slice; no frontend repository changes are authorized.
- Caregiver booking/endpoints are a later slice. This slice includes only the Family caregiver rating/top-10 requirement and the bounded existing caregiver review-permission correction described below.

## Facility onboarding and review

- Facility names, descriptions, room types, and other public bilingual content require Arabic and English.
- Required documents are operating license, registration, health certificate, and civil-defense certificate. Each expiry date is nullable; the owner may provide it and Admin may add it while verifying the document.
- Admin may approve a document with null expiry when the document genuinely has no expiry date. Distinguish verified non-expiring from missing/unverified expiry; null alone must not bypass document review or mean expired.
- Recommended upload limits are accepted: facility/gallery images JPG/PNG up to 5 MB each; private documents PDF/JPG/PNG up to 10 MB each; one cover plus up to 10 gallery images. Validate actual content type and size server-side.
- Facility setup can be saved as a draft. Submission freezes the submitted revision while under review.
- Review actions are approve, reject, request corrections, resubmit, suspend, and reactivate. Corrections and rejected submissions can be revised and resubmitted.
- SuperAdmin and SupportAdmin review Care homes. ContentAdmin is CMS-only and must not approve facilities or access private operational documents.
- Changes to licenses, facility identity/address, and medical services create a new review revision while the last approved revision remains public. Routine availability changes are immediate.
- Required-license expiry blocks new bookings and alerts Admin while existing stays remain for review. A replacement upload is not approval by itself.
- Every sensitive review decision has a reason, actor, revision/version check, and immutable history. Operator labels such as “Super Admin” must never grant Sanad platform SuperAdmin privileges.

## Inventory and pricing

- V1 tracks facility-defined room types, physical rooms, and individual beds.
- Availability is derived from stays, temporary holds, and maintenance. Do not expose arbitrary manual availability counts as the source of truth.
- A Family selects a room type; the facility assigns the physical room/bed before check-in. A shared-room booking reserves one bed. A single room or private suite reserves the whole physical room for one selected elderly person.
- Room types are facility-defined (single/shared/private suite are examples, not an exhaustive enum) and have bilingual information and a monthly EGP base price.
- A stay charges one calendar month from the start date to the same date in the next calendar month, clamped to that month’s last day (for example, January 31 to February 28/29). It is not a fixed 30-night charge.
- The facility base price is separate from platform fees. SuperAdmin and SupportAdmin administer platform fees across booking payments and facility payouts. The system must support a manual refund action when an automated refund fails.
- Admin configures separate percentage platform-fee and tax rates, each calculated on the payment base price, across caregiver payments, Care homes, and Family subscriptions. Do not calculate tax on the fee or silently default absent configuration. Preserve each quoted rate and amount on the transaction so later configuration changes do not reprice it.
- The Family cancellation refund percentage also applies to the Family platform fee: full-refund cases return that fee in full; a 50% refund returns 50% of the monthly base payment and 50% of its Family platform fee.
- Facility payouts are manual bank transfers recorded by SuperAdmin/SupportAdmin only after a completed stay, not automated provider transfers. Keep payout fee, gross/net amounts, transfer reference/evidence, actor and timestamp auditable. A later refund/reversal after payout is recorded as a facility balance owed. Recording a transfer is not permission to initiate one.
- No automatic renewal. An extension purchases another calendar month starting at the existing paid-through date, with a new Family payment and facility approval. Rejection does not cancel the current stay; future unused extension periods are fully refundable if the stay ends before they begin.

## Family stay booking and lifecycle

- The Family chooses an authorized elderly person from a dropdown. The request submits `elderlyId`; the server retrieves name/age and verifies that the elderly record belongs to that Family. Never trust client-supplied identity fields.
- Authorized medical information is retrieved from the elderly record. The booking may include additional care needs/notes and a separate responsible contact; prefill the Family contact. Only the receiving facility and explicitly authorized support staff receive relevant private clinical information; do not equate general CMS access with clinical access.
- Payment is prepaid through the existing Paymob Card/Wallet integration. Payment completion is not admission acceptance.
- Hold capacity for a 15-minute checkout/payment window. After verified payment, hold the selected capacity during the facility’s 24-hour decision window. The earliest arrival is 24 hours after booking.
- The facility accepts or rejects the paid request. Rejection or decision timeout automatically cancels and fully refunds the booking.
- The facility records actual check-in and check-out. The Family confirms check-in. Missing or disputed confirmation creates an Admin review case and does not automatically settle the refund transition.
- An authorized facility may transfer an accepted stay to another physical room/bed both before and after check-in, but the destination must remain in the booking's original room type. The Cairo-local effective date must be within the stay's start-inclusive/end-exclusive interval, cannot be backdated, and is limited to one transfer per booking per local date. Validate facility ownership, active inventory, capacity, maintenance, and overlapping holds/stays for the applicable period. Preserve the old/new assignment, effective date, and actor in the operational history; notify the Family in-app and by email with durable retry. A same-type physical transfer does not change the booking's room type or price/payment snapshot and does not require Family approval. The physical transfer remains effective if notification delivery is delayed; delivery is retried independently.
- Changing the room type is a separate accommodation-change workflow, not a room transfer, and is outside HC-023. Do not change the booking unless the Family has reviewed and accepted the proposed accommodation and its separately specified price/payment consequences. Do not infer an automatic charge, refund, or repricing rule.
- Facility owners may amend or cancel future maintenance blocks. “Future” means the block's start date is strictly after the current Cairo-local date; blocks starting today or earlier cannot be amended/cancelled. Recheck active occupancy and overlapping maintenance constraints, and make availability reflect the change immediately. This scope does not add an advanced maintenance/fault-management workflow.
- SuperAdmin/SupportAdmin may resolve disputed check-in using evidence and record the effective check-in time; preserve original timestamps, evidence and reason in the audit history.
- Family cancellation before check-in receives a full refund. Family cancellation after check-in receives 50% of the full monthly payment, not 50% of unused days. Facility cancellation or rejection receives a full refund.
- HC-035 clarification (2026-10-07): the refund boundary is the recorded facility check-in, not facility approval. A paid cancellation after approval but before check-in remains a full refund. The post-check-in 50% applies to the total amount charged, including fee and tax. Family and owner cancellation of accepted bookings are in scope; failed Paymob refunds can be retried by operational Admins, and externally completed refunds can be recorded with a reason and reference.
- Refund state must distinguish initiated, completed, failed/retrying, and manually completed; prevent duplicate refunds and retain an audit trail.
- Include room transfers and maintenance blocks. Defer walk-in bookings and automatic no-show cancellation. Advanced fault-management workflows are deferred.

## Visits, discovery, and ratings

- Support both prospective facility visits and visits to an elderly resident. Resident visits require an authorized elderly relationship.
- Visits are free, one hour, use Egypt local time, require facility approval, and follow facility hours/closures, facility-configured visitor capacity, exact visitor counts, 24-hour notice, pending capacity holds, and a 24-hour approval expiry. Family or facility may cancel. Rescheduling requires approval. Use timezone-aware calculations, not a fixed UTC offset.
- Public discovery shows only approved, active facilities with valid required licenses. Display the lowest active monthly room-type price as “starting from”. Suspended or expired facilities remain available to existing-stay reads but cannot receive new bookings.
- The Family home provides top 10 Care homes and top 10 caregivers by highest Family-submitted star ratings, not by price. Return fewer than 10 when fewer eligible providers exist; exclude unrated providers.
- A rating is one editable 1–5-star rating with optional text per verified Care-home booking after confirmed check-in/service received. Caregiver ratings are after completed caregiver service. Rank by average rating descending, then review count descending, then stable ID; eligibility is enforced server-side. Do not count an edit as an additional review.

## Finance, Admin, and notifications

- V1 includes booking payments/refunds, receipts, revenue dashboard data, and exports. Include internal booking notes. Defer legal-support integration, notification-test controls, resident wallet, and advanced preventive maintenance.
- Admin review and oversight include application queue, submitted details/private documents, revision history, approve/reject/request-correction, suspend/reactivate, expiring-license queue, booking/payment/refund inspection, check-in dispute handling, and failed-refund follow-up.
- SuperAdmin and SupportAdmin perform these operational actions with authorization checks and reasons. ContentAdmin remains CMS-only.
- Caregiver review currently grants ContentAdmin access in the existing implementation. Include the bounded correction to grant SuperAdmin and SupportAdmin instead, with focused regression tests; do not expand caregiver booking work.
- Notifications are in-app and email only. No SMS notification channel is in scope. Include booking, review, license, payment, refund, and relevant Admin events through existing supported infrastructure.

## Execution contract details — inspect existing conventions first

### HC-TASK-023 implementation boundary

- Owner confirmed Cairo-local effective-date precision and bounds for transfers, matching the current `DateOnly` availability/maintenance intervals; one transfer per booking per local date avoids same-day ordering ambiguity.
- Owner confirmed amend/cancel support for future maintenance blocks, with the future-only boundary, conflict checks, and immediate availability updates. The current owner API only creates blocks.
- Owner confirmed Family notification in-app and by email with durable retries; delayed delivery does not reverse or delay the physical transfer.

The final owner answers resolved fee type/refund applicability, percentage fee and tax basis, payout mechanism and eligibility, nullable expiry, SMS OTP and dispute authority. Do not ask those questions again. Existing verification/recovery provider and exact check-in/out time contracts remain to be mapped; reuse compatible established behavior. No automatic confirmation timeout is approved. Extensions already begin at the paid-through date.

The owner explicitly approved a shared Admin rate configuration across caregiver payments, Care homes, and Family subscriptions. Map and reuse an established global mechanism if suitable; preserve existing transactions and do not silently reprice them. No unrelated product workflows are in scope.

Only genuinely unresolved dependent behavior should be blocked; proceed with independent authorized work. Do not convert resolved intake questions into recurring gates.
