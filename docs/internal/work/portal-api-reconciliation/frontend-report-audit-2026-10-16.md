# Frontend report audit and proposed delivery route

Date: 2026-10-16. Audience: owner, frontend developers and future backend workers.

**Status: audit for discussion, not an approved implementation contract.**

This document consolidates the completed read-only audit of
`C:\Users\mosad\Desktop\Frontend_Report.md`. It covers all 46 numbered requests,
the earlier answers, frontend work and blocked-screen list. Creating this file
does not authorize application changes, data repair, commits, publication or
deployment. The existing reconciliation plan remains the historical execution
record; the slices below are proposed follow-up work, not silently approved tasks.

## 1. Executive answer: what do we actually have?

The frontend report mixes five different situations: genuine missing capabilities,
real risks, old defects already fixed, inaccurate client assumptions, and requests
for entirely new products. It must not be handed to a builder as one defect list.

The main findings are:

- Several old complaints are already addressed: pending-user reactivation,
  library JSON casing and field errors, the original 28 response schemas,
  required query metadata, numeric visit-day examples and geography coverage.
- Genuine missing portal capabilities include moderation inspection, owner
  settlement reads, a caregiver payout eligibility selector, owner dispute reads,
  verification recovery after losing signup state and several existing-response
  fields.
- OTP resend has a concrete failure-ordering risk. The exact reported provider
  error remains unproven; it is not accurate to conclude that the current VPS
  simply has no SMS credentials.
- Six caregiver profiles currently have no matching Identity user in the
  inspected database. This is a data-integrity finding, not evidence that a join
  was forgotten. Repair requires a separate, explicit decision.
- Platform controls, family-home CMS, medical access auditing, growth reporting
  and similar requests are feature development, not corrections to existing GETs.
- The frontend must correct some assumptions, especially booking enums,
  certificate consequences, role boundaries and interpreting every 404 as
  "not deployed yet".

We should agree the first release scope, lock exact contracts, then execute one
bounded worker slice at a time. We do not need to build every proposed screen to
finish the daily portal workflows.

## 2. Evidence baseline and limits

| Evidence | Baseline or limitation |
| --- | --- |
| Source inspected | `main` at `4b65369d760c8af6b2242ebd2022a6cb10057580`; same HEAD confirmed when creating this document. |
| Deployed API | Release `9167e7111c5321b063fa9d893001e77dd42a32da`; Production-mode developer host, not a disposable Development database. |
| Report dates | Consolidated report includes observations from October 11 and October 14–16. An older observation is not automatically a current defect. |
| Inspection performed in the completed audit | Code, contracts, existing tests and reports, deployed OpenAPI, anonymous lookup/metadata reads and bounded read-only VPS configuration/database inspection. |
| Not executed in this audit | New regression tests, authenticated business workflows, OTP sends, payment/provider calls, API mutations, migrations, repair, seeding or deployment. Existing test evidence is historical, not a fresh test run. |
| SMS inspection | Presence of configuration only; no credentials, OTPs or provider request bodies reproduced. Provider validity was not tested. |
| Public network caveat | A direct request from the local network returned a redirection HTML page rather than API JSON. That result was discarded. Valid deployed JSON was inspected through SSH using server loopback. This is not evidence that the API was down. |
| Current working tree | The existing root `Frontend_Report.md` was preserved. This audit document is the only file intentionally added by this documentation task. |

Live findings below describe the earlier audit snapshot, not continuous monitoring.
No secrets or private database backups belong in this document.

## 3. Categories: how to interpret each finding

These are **seven classification categories, not seven implementation phases**.
A single report item may belong to more than one category.

| Category | Meaning | What happens next |
| --- | --- | --- |
| C1 — Defect or reliability risk | Existing behavior violates the accepted rule, or contains an evidenced unsafe failure path. | Reproduce with a safe fixture, fix only the cause and prove the regression. An old observation alone is not enough. |
| C2 — Missing portal read capability | The underlying business operation exists, but the portal lacks a scoped list, detail or field. | Reuse existing rules and calculations; add only the agreed read contract and permissions. |
| C3 — New product feature | Requires new behavior, persistence, enforcement, events or policy. | Separate product contract before implementation; not disguised as a small endpoint fix. |
| C4 — Contract/documentation gap | Runtime works, but schemas, examples or descriptions are missing or wrong. | Publish actual behavior without silently changing the wire contract. |
| C5 — Frontend correction or already resolved | Client assumption is wrong, behavior is intentional, or the cited defect is already fixed. | Send precise evidence and client guidance; do not rebuild or change correct backend behavior. |
| C6 — Data/environment issue | Records or configuration need diagnosis, not necessarily code changes. | Inspect exact targets and dependencies; no automatic production repair or reset. |
| C7 — Owner policy decision | The desired business rule is not established by the current contract. | Owner chooses the rule before the worker receives an implementation brief. |

Examples: owner payouts are C2; a maintenance-mode enforcement system is C3;
auth examples are C4; the old four-governorate complaint is C5 now; caregiver
orphans are C6; allowing ContentAdmin to suspend authors is C7, not an assumed
extension of moderation permission.

## 4. Item-by-item audit — A1: bugs and blocking issues

### #1 — Phone OTP resend returns 500

**Verdict: current exact cause unproven; concrete reliability risk confirmed in
source. C1 + C6.**

The current VPS runs Production. The inspected configuration had SMS username,
password and sender values present; a template was absent. With those three
values present, DI selects `SmsMisrSmsSender`, not the no-op Development sender.
Configuration presence does not prove valid credentials or a functioning provider.
The supplied trace was not successfully correlated to an exception during this
audit; no provider-specific root cause is claimed.

The real source risk is ordering: resend invalidates the old request, creates and
saves its replacement, then sends the code. A send failure can leave the caller
without a usable old request or the new request identifier. The real SMS adapter
can throw for HTTP/provider errors. The no-op sender itself does not throw merely
because credentials are absent.

Proposed work: reproduce provider failure with a fake, inspect a sanitized actual
exception if available, then agree retry-safe state and response behavior. Do not
add fixed OTPs or an admin force-verification backdoor to this Production host.

Evidence: [resend handler](../../../../src/Modules/Identity/Application/Sanad.Modules.Identity.Application/Authentication/Verification/ResendOtpCommandHandler.cs).

### #2 — Admin finance reads fail with a facilityId filter

**Verdict: the reported October 11 defect has an existing fix; current failure
was not reproduced. C5; targeted verification proposed.**

Commit `5edd3419781cdc1d3b106b2debb60ea0295f0ff2` replaces untranslatable
`FacilityId.Value` predicates with typed comparisons. Current source contains that
fix. Do not reopen this as a confirmed current 500 based on the old report.
Use an exact Npgsql query proof and an authorized disposable PostgreSQL execution
for the affected filtered read if further assurance is needed.

Evidence: [finance read models](../../../../src/Modules/CareHomes/Application/Sanad.Modules.CareHomes.Application/Bookings/CareHomeFinanceReadModels.cs).

### #3 — Draft owner dashboard/revenue fail

**Verdict: same historical query defect, not a current reproduced failure. C5.**

Current owner resolution is by ownership and does not require Approved status for
these reads. Existing isolated HTTP tests include Draft and empty/populated
financial views. Those tests are not fresh live-PostgreSQL proof. A Draft facility
is not supposed to fail simply because it is Draft.

### #4 — Reactivate returns 500

**Verdict: already addressed by PORTAL-01. C5.**

An unverified pending admin that was suspended/blocked is not activation-ready.
The accepted behavior is `409 Identity.Admin.ActivationNotReady`, not forced
activation and not 500. Verified, ready accounts can reactivate. The report's
conclusion that nobody can be unsuspended is too broad.

Evidence: [accepted phase report](reports/phase-01-reactivation.md).

### #5 — Caregiver rows without Identity users

**Verdict: actual deployed data inconsistency confirmed at the audit snapshot;
historical cause unknown. C6.**

Read-only inspection found seven caregiver profiles and six without matching
Identity rows in the inspected target. Module connection targets were checked
privately; this was not inferred only from blank frontend names. The list uses a
LEFT JOIN, so missing names/phone and inclusion in the count follow its design.

Do not hide this with an INNER JOIN, invent replacement users or delete profiles.
First identify dependencies and plausible recovery evidence, then agree repair,
quarantine or retention explicitly. Truncated IDs in the frontend report are not
safe mutation targets.

Evidence: [integrity diagnostic report](reports/phase-04-caregiver-integrity.md)
and [diagnostic runbook](diagnostics/caregiver-identity-runbook.md).

### #6 — Geography almost empty

**Verdict: obsolete after the approved import. C5.**

Executed import evidence records **27 governorates, 349 Cities and 5714 Areas**,
including intentionally retained legacy entries. The audit confirmed 27 public
governorates; the full hierarchy/count evidence is in the executed receipt.
This is the owner-accepted V1 catalogue, not a claim of exhaustive current
geographic certification. Do not rerun a replacement/reset to fix the old 4/3/2
observation. Care Home profile location fields remain strings; the lookup picker
does not establish a new foreign-key requirement.

Evidence: [preserve-and-add receipt](reports/geography-import-preserve-add.md).

## 5. Item-by-item audit — A2: requested endpoints

| # | Verdict and current implementation | Proposed disposition |
| --- | --- | --- |
| 7 | **Missing moderation inspection (C2); reports are additional scope (C3).** Admin currently has publish/reject, not the requested admin list/detail. Public feed hides author information and cannot substitute for a moderation queue. No community report persistence was found in the inspected context. | Add the agreed admin list/detail, search/paging and authorized author readback. A reports list/count needs a real report intake/storage contract; do not fabricate zeros or claim an existing reporting workflow. |
| 8 | **Mixed C2/C3/C7.** Requested statistics, persisted reject reasons and timed author blocking are not all existing capabilities. A global user suspension already exists under SuperAdmin-only user management. | Decide global blocking versus a new community-only timed ban. Recommendation: keep global block/suspend SuperAdmin-only; do not grant ContentAdmin global suspension through a moderation route. Define counters before implementation, especially reported and blocked authors. |
| 9 | **Missing owner settlement read (C2).** Admin payout ledger and calculations already exist. `netCollected` is customer collection after completed refunds, including platform fee/tax; it is not owner income. | Add owner-scoped settlement read using the existing payout/calculation truth. Define whether the list contains recorded transfers only or also unpaid eligible balances. Do not recompute commission in the frontend. |
| 10 | **Missing eligible-payout selector (C2).** Current admin list/detail/record/failure actions exist, but no eligible booking selector. | Reuse full existing eligibility: completed and paid booking, completion timestamp, policy delay, verified payout account, effective policy and no paid payout. Failed attempts must not permanently exclude a later eligible payout. |
| 11 | **Missing owner dispute list (C2).** Admin dispute reads exist. | Add own-facility-only read access; do not grant admin resolution powers. Clarify paging/filter/display fields with #27. |
| 12 | **Valid verification-recovery gap (C1/C2).** Restricted login returns verification flags but no verification request IDs; losing signup state is a real workflow issue. | Agree an authenticated recovery flow scoped to the restricted identity. Do not adopt arbitrary-email resend without enumeration/authorization/rate-limit analysis. Coordinate with #1. |
| 13 | **New aggregate capability (C2/C7).** `/admin/users` remains SuperAdmin-only. Landing statistics are not a complete Identity aggregate. | Define counted users/statuses and allowed admin roles. Add aggregate-only access if approved; do not broaden access to personal user lists. |
| 14 | **New analytics feature (C3/C7).** Requested growth series is not an existing documented portal contract. | Define entities, timestamps, timezone, bucket boundaries and allowed ranges. A chart mockup is not a complete analytics rule. |
| 15 | **New operational feature (C3/C7).** Platform active, maintenance and cash-on-delivery switches require enforcement, not only a settings table. | Separate contract: affected requests, allowed admin bypasses, status visibility, audit and payment/webhook handling. Prefer business platform status separate from deployment health. Cash-on-delivery must not silently create an unsupported payment workflow. |
| 16 | **New family-home CMS (C3).** Landing content, wellness tips and service catalogues do not collectively establish the proposed mobile-home contract. | Separate banners/links/featured-services model and publication/read contract; reuse existing media/tips/services where appropriate. |
| 17 | **New caregiver medical-access audit capability (C3), not total absence of auditing.** Family medical-profile views and several admin medical/help/SOS/check-in/notification reads already create audit/activity records. They do not provide complete caregiver granted/denied decision records. | Define capture at the actual permission decision, minimal fields, retention and admin access. Do not invent historical denied events. Recommendation: SuperAdmin-only initial audit access, subject to owner approval. |
| 18 | **New derived operational feature (C3/C7).** Negative check-in answers are not the same as no answer. | Agree expected population, daily window, timezone and grace period before creating a missed-check-in feed. |
| 19 | **New internal-note feature (C3).** Status reasons and booking notes are not a caregiver review-note history. | Separate bounded contract for author, timestamps, permissions, visibility and correction/deletion policy. |
| 20 | **Owner decision (C7).** Editable gateway credentials and connection-test semantics are not an approved existing portal feature. | Recommend server-managed secrets and a read-only capability/status card if needed. Do not add secret CRUD or real payment tests merely to satisfy a mock settings card. |
| 21 | **New configuration-management product (C3/C7).** No equivalent registry with pending changes/activity/stats exists. | Recommend hide/defer the screen unless the owner explicitly wants that engine. Do not build it as implied maintenance. |
| 22 | **Real media lifecycle limitation (C1/C3).** Successful unused/replaced landing uploads have no complete cleanup lifecycle. Failure cleanup is not orphan cleanup. | Separate reference-aware lifecycle design. Protect draft, published and historical references; never blind-delete by age or path. |
| 23 | **Explicitly postponed (C5).** No Supplier account type/module is present. | Keep frozen; no supplier API, dashboard or identity work in this release. |

Relevant existing implementations:
[admin moderation controller](../../../../src/API/Sanad.API/Controllers/AdminCommunityPostsController.cs),
[care-home payouts](../../../../src/Modules/CareHomes/Application/Sanad.Modules.CareHomes.Application/Bookings/CareHomePayoutHandlers.cs),
[caregiver payout commands](../../../../src/Modules/Caregivers/Application/Sanad.Modules.Caregivers.Application/Payouts/CaregiverPayoutCommands.cs).

## 6. Item-by-item audit — A3: missing fields

| # | Verdict | Proposed contract direction, not yet approval |
| --- | --- | --- |
| 24 | **Confirmed owner readback gap (C2).** `draft` selects an unfrozen revision. After submission/approval, it can correctly be null while no submitted/approved profile/contact projection is returned. | Return explicit current submitted/approved profile readback alongside draft state. Preserve private contact fields only in authorized owner/admin reads. Do not mutate/create a draft from a GET or expose owner contacts publicly. |
| 25 | **Confirmed admin media-discovery gap (C2).** Admin revision detail has profile/documents/history but not the discoverable per-revision media list requested. | Return media IDs, kind and position under their revision. Use authorized file routes, not storage keys. Include the query-translation risk below in the same slice. |
| 26 | **Confirmed detail gap (C2).** Owner operational booking detail lacks resident name/age; a list already has an English name. Booking stores Arabic/English name and age snapshots. | Use booking snapshots in detail; do not calculate a different age from current Identity data or introduce unnecessary cross-module calls. |
| 27 | **Confirmed dispute display gap (C2).** IDs/status/history exist but resident name and booking amount are missing. | Enrich using the related booking and explicitly name the amount's meaning. Share the agreed read projection between authorized admin and owner use where appropriate. |
| 28 | **Confirmed application-list enrichment gap (C2).** OwnerUserId exists; owner account name/phone do not. | Bounded backend enrichment with graceful missing/anonymized-user behavior. Do not make SupportAdmin call SuperAdmin-only user-detail APIs. |
| 29 | **Display enrichment plus historical-policy decision (C2/C7).** Actor ID alone cannot render a name/role. Looking up today's role does not prove the actor's role at the time of an old decision. | Agree current display versus historical snapshot semantics. Never label current role as historical truth. Handle missing/anonymized actors without breaking history reads. |

Additional risk found while inspecting #25:
`CareHomeProfileMediaController` contains `f.Id.Value == careHomeId` predicates in
file-access guards. This resembles previously proven untranslatable EF typed-ID
predicates. It is a **source-identified risk, not a newly reproduced live 500**.
Require Npgsql translation proof over the exact queries and focused tests before
claiming those newly discoverable file IDs work end-to-end.

Evidence:
[owner profile readback](../../../../src/Modules/CareHomes/Application/Sanad.Modules.CareHomes.Application/Facilities/CareHomeProfileCommands.cs),
[admin application projections](../../../../src/Modules/CareHomes/Application/Sanad.Modules.CareHomes.Application/Facilities/AdminCareHomeApplications.cs),
[media file controller](../../../../src/API/Sanad.API/Controllers/CareHomeProfileMediaController.cs).

## 7. Item-by-item audit — A4: docs and OpenAPI

### #30 — Auth payload examples

**Confirmed documentation defect (C4).** The working client/Postman request names
match the controller DTOs; the auth guide is stale.

| Operation | Current request truth |
| --- | --- |
| Register | `arabicFullName`, `englishFullName`, `email`, `phoneNumber`, `password`, `accountType`. Not `firstName/lastName/phone`. |
| Verify OTP | `verificationRequestId` as a GUID and `code`. Not email/purpose. |
| Resend OTP | `verificationRequestId` as a GUID. The response includes its replacement request ID and expiry. |

Correct the guide and any contradicted examples, not the functioning client shape.

### #31 — Strongly typed IDs

**Real inconsistency in established contracts, not approval for a global rewrite
(C4).** Login UserId, refresh DeviceSessionId and verification response IDs use
typed wrappers such as `{ "value": "<guid>" }`; many other DTOs flatten IDs.
The corresponding request field can still be a plain GUID. Published wrapper
schemas require `value` after the earlier metadata work.

Document exact per-operation shapes and fix incorrect examples. Do not install a
global flattening converter: that would change working clients' wire contracts.

### #32 — Enum values and aggregate shape

**Partly obsolete; visit metadata still incomplete (C4/C5).** Deployed OpenAPI now
publishes numeric values, descriptions and `x-enumNames` for the principal typed
enums. Numeric response JSON is intentional; accepting names in query filters does
not imply string response JSON. Existing generic admin enum parsing accepts names
and defined numeric strings.

| Enum | Verified values |
| --- | --- |
| UserStatus | 1 PendingVerification; 2 Active; 3 Suspended; 4 Blocked. |
| AccountType | 1 Family; 2 MedicalCaregiver; 3 CompanionCaregiver; 4 Elderly; 5 SuperAdmin; 6 ContentAdmin; 7 SupportAdmin; 8 CareHomeOwner; 9 FinanceAdmin. |
| DevicePlatform | 0 Unknown; 1 Android; 2 iOS; 3 Web. |
| CaregiverType | 1 Medical; 2 Companion. |
| CareHomeStatus | 1 Draft; 2 PendingReview; 3 NeedsCorrection; 4 Rejected; 5 Approved; 6 Suspended. |
| CareHomeDocumentStatus | 1 PendingReview; 2 Verified; 3 Rejected; 4 Revoked. |
| CareHomeRefundStatus | 0 None; 1 Pending; 2 Initiated; 3 Failed; 4 Completed; 5 ManuallyCompleted. |
| Admin user audit action | 1 Suspended; 2 Reactivated; 3 Blocked; 4 SessionsRevoked; 5 PasswordReset; 6 AccountTypeChanged; 7 Created; 8 Anonymized. Value 2 is source-confirmed, not guessed. |
| ElderlyHelpRequestStatus | 1 Pending; 2 Accepted; 3 InProgress; 4 Resolved; 5 Rejected; 6 Cancelled; 7 Reopened; 8 Acknowledged. |
| ElderlySosHistoryAction | 1 Created; 2 Acknowledged; 3 Resolved; 4 Cancelled. This is an action, not the current SOS status. |
| BookingCancellationReasonCategory | 1 Emergency; 2 MedicalIssues; 3 TransportationIssues; 4 AccountDeletion; 5 Other. Refund entitlement/decision-reason members are also published in the current schema. |
| Visit kind | 1 Prospective; 2 Resident. |
| Visit status | 1 Pending; 2 Approved; 3 Rejected; 4 FamilyCancelled; 5 FacilityCancelled; 6 Expired; 7 ReschedulePending; 8 Rescheduled. Source-confirmed; missing owner response typing prevents complete schema discovery. |
| DayOfWeek | 0 Sunday through 6 Saturday; Monday is 1. |

Help-request aggregate returns a dictionary/map of status to count, not an invented
`statusCounts` array/envelope. Its schema is an object with integer additional
properties. Do not guess enum dictionary-key spelling from prose; verify literal
serialized examples when updating that contract.

Two booking enums must remain distinct:

- Family/caregiver `BookingStatus` has **10** values: PendingPayment,
  PendingCaregiverApproval, Confirmed, InProgress, Completed, CancelledByFamily,
  DeclinedByCaregiver, CancelledByCaregiver, Refunded, Expired, numbered 1–10.
- `CareHomeBookingStatus` has **9** values: PendingPayment, PaidAwaitingDecision,
  Accepted, Rejected, Expired, Cancelled, RefundPending, RefundInitiated, Refunded,
  numbered 1–9.

The proposed frontend `BookingStatus 1–5` is incorrect.

### #33 — Visit day example

**Already corrected (C5).** Current guide and owner Postman use numeric
DayOfWeek. Keep Monday=1. Do not introduce string acceptance solely to fix an old
example.

### #34 — Missing response schemas

**Mixed: the original 28 are addressed, but additional metadata gaps remain (C4).**

Already typed in current source/deployed metadata:

- The original 22 GETs and six admin-user actions covered by PORTAL-03, including
  create-admin **201**, users/sessions/audit, elderly list/aggregate reads,
  notifications, sentence catalogue, wellness tips, admin inventory and disputes.
- Admin Care Home booking list/detail and payout ledger.
- Medication-lateness CMS response.
- Caregiver payout-policy create/current/history; actual route is under
  `/api/v1/admin/finance/caregiver-payout-policies`.
- Platform-charge current/history: current has **200 with DTO or 204 when absent**,
  not a fabricated 200-null object.

Remaining gaps identified:

| Operation group | Required correction |
| --- | --- |
| Admin elderly help-request detail/history | Publish actual response DTOs and statuses. |
| Owner inventory reads and business mutations, media removal/archive/maintenance | Publish each operation's real result; file endpoints must retain their file semantics. |
| Inventory availability, including the relevant admin operation | Correct metadata for the actual **POST**, not an invented GET. |
| Owner booking-request list | Publish actual paginated/read response. |
| Owner visits and GET/PUT visit settings | Publish real shapes and referenced enums. |
| Owner revenue/dashboard | Publish real financial read models. |
| Notification unread count | Publish its actual **bare integer**, not `{count: ...}`. |
| Platform-charge-rule create | Runtime **201 GUID** needs matching status/schema; bare 200 metadata is wrong. |

Do not copy guessed frontend types into schemas. Verify the controller result,
serialization and literal response. The original matrix is in
[contract tests](../../../../tests/Sanad.UnitTests/API/PortalOpenApiContractTests.cs).

### #35 — Required query parameters

**Already corrected (C5).** Current OpenAPI marks `through` on license expirations
and parent IDs on both public/admin cities/areas as required. Anonymous missing
city/area parent requests returned `400 Api.Validation.Failed` with field entries
for `GovernorateId`/`CityId`; the blanket "no field name" claim was not reproduced.
Unknown well-formed parents can return an empty list; omission is different.

### #36 — Library rules and generic errors

**Parser/error complaint already addressed; domain rules remain correct (C5).**
CamelCase and PascalCase section properties are now accepted. Malformed/null
entries are handled and fixed safe field/index errors are returned. Positive
display order, at least one Article section and a valid Video URL with positive
duration remain required. Do not relax these rules because the old parser failed.

Evidence: [phase-02 report](reports/phase-02-library.md).

### #37 — First-run support contact and category body ID

**Support-contact 404 is intentional; category metadata already fixed (C5).**
Treat `Cms.Help.SupportContactNotFound` as the documented unconfigured state.
Changing it to 204 or an empty success object is not necessary. Category update
now uses a dedicated request without Id; route ID remains authoritative, and a
legacy extra Id property is ignored rather than becoming authority.

## 8. Item-by-item audit — A5: policy questions

### #38 — Legal pages for owners

**Genuine audience-policy gap; reported status needs correction (C7/C3).**
Current audience selection supports Family, MedicalCaregiver, CompanionCaregiver
and Elderly, not a pure CareHomeOwner account. A pure owner claim reaches
`403 Cms.Content.UnsupportedAudience`, not the report's claimed universal
`404 Cms.Legal.NotPublished`. A supported selected audience can instead have no
published content. Diagnose selected account type and exact error code; never
request a raw token in a report.

Adding an owner legal audience requires content/publication and fallback decisions.
It is not just changing the client's 404 handling.

### #39 — Certificate-file role

**Documentation conflict; runtime is clear (C4/C5).** Certificate download inherits
`CaregiverReviewAdmin`: **SuperAdmin + SupportAdmin**. The similarly named
`CaregiversAdmin` policy is SuperAdmin + ContentAdmin, but it is not this
controller's review permission. Fix the conflicting guide, not the correct gate.

### #40 — Owner account deletion

**Pure owner self-deletion is not currently supported; product decision (C7).**
The claim that all non-caregivers fail is too broad because Family self-deletion
exists. A pure owner lacks the Family/caregiver route's supported account type.
Mixed-account behavior needs explicit coverage. Do not add owner deletion without
handling facility ownership, bookings, settlements and retention obligations.

### #41 — Owner National ID

**Pure owner is unsupported in this Identity workflow (C5/C7).** Supported accounts
are Family, MedicalCaregiver and CompanionCaregiver. Multi-account membership can
change eligibility, so "every owner gets 409" is too broad. Facility documents are
a different workflow. Owner personal-ID onboarding would be new scope.

### #42 — Contact name, admission language and required documents

**Existing rules are clear; changing them needs approval (C5/C7).**

- `contactName` is explicit profile data, not automatically derived from Identity.
  Frontend prefill can be editable; automatic permanent derivation is a rule change.
- Arabic and English admission conditions are both required for submission.
  Making English optional is not an existing fallback.
- All four document types—operating licence, registration, health and civil
  defence—must be present in the **current revision at submission**. They do not
  all need prior verification to submit.
- Approval requires the applicable current submitted-revision documents to be
  verified/valid. Invalid latest documents are not rescued by older unrelated ones.
- Submission currently requires **at least two profile photos including a cover**.
  The older answer that photos were not required is superseded by current behavior.

### #43 — Roles for new endpoints

**New role grants are not yet approved (C7).** Existing baseline and recommended
least-privilege direction:

| Capability | Existing baseline / proposed direction |
| --- | --- |
| Community moderation | Existing SuperAdmin + ContentAdmin. Proposed list/detail follows this scope. |
| Global user suspend/block | Existing SuperAdmin-only. Do not bypass it through moderation. |
| Caregiver review/certificate download | Existing SuperAdmin + SupportAdmin. |
| Care Home admin operations | Existing SuperAdmin + SupportAdmin. |
| Shared finance rule operations | Existing `FinanceOperationalAdmin`: SuperAdmin + SupportAdmin. |
| Payout operations/policy | Existing `PayoutOperationalAdmin`: SuperAdmin + FinanceAdmin. Not interchangeable with finance-rule permission. |
| Owner operational reads | Normal owner access, limited to owned facility. |
| Medical-access audit | SuperAdmin-only is the recommendation, not an already approved new policy. |
| New user/growth statistics | Explicit aggregate permission to be agreed; no implicit access to user PII. |

Evidence: [policy registration](../../../../src/API/Sanad.API/DependencyInjection.cs).

### #44 — Document rejection and revisions

**Current lifecycle is intentional (C5); frontend must distinguish two decisions.**
Rejecting a document changes that document and facility version, not automatically
the application's review status. Application correction/rejection is a separate
decision. Upload is blocked during PendingReview and Suspended. Editable states
can use an open/new revision; old frozen-revision files do not satisfy a new
submission merely because they are the latest upload somewhere in history.

Evidence:
[facility lifecycle](../../../../src/Modules/CareHomes/Domain/Sanad.Modules.CareHomes.Domain/Facilities/CareHomeFacility.cs),
[document lifecycle](../../../../src/Modules/CareHomes/Domain/Sanad.Modules.CareHomes.Domain/Facilities/CareHomeDocument.cs).

### #45 — Reason limits

**Documentation gap; preserve existing bounds (C4).**

| Field/rule | Existing maximum |
| --- | --- |
| Required Care Home facility review reason | 1000 characters. |
| Care Home document rejection reason | 1000 characters. |
| Caregiver payout-account decision reason | 500 characters. |
| Caregiver payout reason | 2000 characters. |
| Caregiver payout transfer reference | 200 characters. |
| Caregiver payout evidence | 2000 characters. |

Do not impose one shared 500-character cap on all resources. Keep existing
caregiver/identity-document limits scoped to those resources.

### #46 — Assessment illustration formats

**Documentation defect (C4).** Current storage accepts **JPEG, PNG and WebP**, not
SVG. The frontend picker is correct on this point. Fix the conflicting PNG/SVG/
WebP prose. The 2 MiB request limit applies to the whole multipart request, so
boundary tests must account for overhead rather than promise that any file of
exactly that size will fit.

Evidence: [storage validation](../../../../src/BuildingBlocks/Infrastructure/Sanad.BuildingBlocks.Infrastructure/Storage/LocalDiskFileStorage.cs).

## 9. A6, Part B and Part C — frontend guidance

The frontend repository was not supplied or inspected. Its claims that items are
committed/done cannot be independently certified from this backend audit. These
are backend-contract checks, not a frontend code review.

### Earlier answers to keep or correct

- AccountType 1–9 is correct; Supplier remains absent.
- Unread count is a bare integer; do not introduce an envelope in client types.
- Notification destination kind is free-form, not proof of a closed exhaustive enum.
- Shared facility version remains the inventory optimistic-concurrency basis.
- `/lookups/*` remains anonymous. Lookup import does not require Development seeding.
- Finance-rule permissions and payout permissions differ as described above.
- Two photos including a cover are required for submission; retain that UI check.
- Reported smoke leftovers are not permission to delete any records or files.

### Frontend work that can proceed without a new backend capability

Published/archived legal version viewing, supported list filters, lookup
deactivation warnings, confirmations and localized form labels generally belong
to the frontend using existing contracts. Confirm each actual operation rather
than introducing a new backend feature merely because a screen is incomplete.

Important corrections to the frontend plan:

1. **Do not create a five-value booking enum.** Keep the 10-value caregiver/family
   booking enum separate from the 9-value Care Home booking enum.
2. **Do not merge unrelated business enums just because they share values 1–4.**
   A reusable `TypedId` utility is different from collapsing distinct statuses.
3. **Certificate rejection and revocation differ.** A mandatory certificate
   rejection makes the caregiver Unavailable. Mandatory certificate revocation
   makes them Unavailable and suspends an Active caregiver profile. Do not claim
   both actions always suspend Identity login.
4. **Do not conflate finance and payout roles.** SupportAdmin's finance-rule
   permission does not imply payout-operation permission; FinanceAdmin is not
   automatically the shared-finance-rule role.
5. **Do not hide all non-Approved owner reads.** Draft dashboard/revenue access is
   intentional. Build navigation from each route's actual lifecycle capability.
6. **Do not treat every 404 as an undeployed endpoint.** It can mean no facility,
   missing record, unconfigured content or another normal resource outcome.
7. **Do not code final services/types against suggested new DTOs before contract
   approval.** The report explicitly offers endpoint names as suggestions.
8. **Owner contact E.164 is not an existing backend guarantee.** The current
   profile rule is required/length-bounded contact phone, not the same validator
   as every other phone workflow. Stronger validation is a separate agreement.

Other reported form bounds, transfer/window checks, refund displays, extension
badges and preference visibility should remain resource-specific. This audit does
not certify every frontend form merely because its listed limits look plausible.
Identity document revocation warnings must also be checked against the particular
Identity status transition, not copied from caregiver certificate behavior.

### What actually blocks the sample-data screens?

| Screen | Current conclusion |
| --- | --- |
| Community moderation | Genuine missing admin inspection; reporting/blocking requires additional decisions. |
| Owner payout ledger | Genuine missing owner settlement read, not missing payout business calculations. |
| Owner disputes | Genuine missing owner-scoped list. |
| Caregiver payout selector | Genuine missing eligible booking selector. |
| Medical review/access log | New audit capture/read product, not just a missing projection over all existing logs. |
| Applications hub / family-home editor | New registry/home-CMS scope; registry recommended deferred. |
| Platform settings / gateway | Platform enforcement is new scope; gateway secrets recommended server-managed. |
| Growth / non-Super user totals | New aggregate contracts; supplier component remains frozen. |
| Owner contact settings | Real current-profile readback gap. |
| Owner legal pages | Audience/content-policy gap, separate from profile readback. |
| Visit/help actions | Some schema work remains, but many enums are already published; use the verified values. |
| Resume signup | Genuine restricted-verification recovery gap. |

## 10. How we will operate

### Three layers, so nothing is confused

1. **Category:** what kind of finding it is—bug, missing read, new feature, etc.
2. **Slice:** one bounded delivery unit with one contract and acceptance criteria.
3. **Gate:** the evidence/approval needed before moving to the next step.

A category is not a worker prompt. A suggested endpoint is not an approved
contract. A passing unit test is not proof that an external SMS provider works.

### Proposed daily-workflow release slices

These are **nine candidate core slices**, not nine approved phases and not nine
workers running at once. Some may be reduced or deferred when we lock scope.

| Slice | Does what | Why it matters | Acceptance direction |
| --- | --- | --- | --- |
| S01 — Contract truth | Correct auth/examples, remaining response/status metadata, enum guidance, limits and role/format contradictions; send frontend correction list. | Stops frontend guessing and avoids unnecessary backend rewrites. | Real schemas/serialization match examples; compatibility unchanged. |
| S02 — Verification continuity | Investigate OTP failure, make resend failure/retry safe and add agreed restricted-session recovery. | Unblocks signup/resume without security bypasses. | Fake provider failures and recovery paths prove persisted state and safe errors; no live OTP send required for automated acceptance. |
| S03 — Finance regression verification | Verify current filtered admin and owner Draft/Approved reads against exact production queries and safe PostgreSQL fixtures. Fix only remaining reproduced defects. | Closes old 500 claims with relevant database evidence, not speculation. | Correct responses, ownership and totals; Npgsql proof plus disposable execution where needed. |
| S04 — Care Home readback | Current submitted/approved profile, admin revision media, resident snapshots, application-owner/dispute/history display fields; test media query risk. | Makes existing onboarding, review and booking operations inspectable. | Correct revision/ownership/privacy, discoverable authorized files, no GET-created drafts, honest actor-role semantics. |
| S05 — Owner settlement ledger | Owner-scoped read over existing payout/settlement truth. | Removes misleading use of customer collection as owner income. | Commission/refund/debt/transfer results match existing rules; foreign-facility data excluded. |
| S06 — Caregiver eligible payouts | Searchable/paged selector using existing full eligibility. | Removes pasted GUID workflow without weakening financial gates. | Delay/account/payment/policy/already-paid/failed-attempt cases agree with record-payout rules. |
| S07 — Community moderation | Admin list/detail and agreed reasons/counters; explicitly selected reporting/blocking scope. | Lets moderators inspect what they are approving/rejecting. | No anonymity leak to public clients, correct admin roles and persisted moderation semantics. |
| S08 — Owner disputes | Own-facility dispute list and agreed booking display fields. | Removes mock dispute page without giving owners admin resolution powers. | Paging/filter/empty/foreign-owner checks and consistent dispute DTOs. |
| S09 — Caregiver integrity disposition | Read-only dependency/recovery assessment, then only separately approved exact repair or quarantine. | Prevents invisible corruption being hidden or multiplied. | Exact target/records, backup/recovery plan and explicit repair authority; no silent deletion or manufactured Identity users. |

### Recommended order

**Contract lock → S01 → S02 → S03 → S04 → S05 → S06 → S07 → S08 → release gate.**

Inspect the already identified S09 data problem early without modifying records.
Its repair is a separate decision, not something a worker performs while testing
another slice. At release review, unresolved data issues must be explicitly
accepted/deferred or block only the affected scope; they must not disappear from
the handoff.

This order is a recommendation for planning. We can defer a slice explicitly;
we cannot silently label its blocked screen complete. Final integration is not a
new feature phase. Existing source work already on main does not require a
fictional branch merge.

### Separate expansion contracts — not prerequisites by default

| Expansion | Scope |
| --- | --- |
| E01 | User aggregate counts and growth analytics, with agreed role/time/bucket definitions. |
| E02 | Owner legal audience and content publication. Owner personal ID/deletion remain separate policy choices, not automatic additions. |
| E03 | Caregiver medical-access decision capture plus restricted admin audit reads. |
| E04 | Platform active/maintenance/payment-mode enforcement and public business status. |
| E05 | Family-home CMS and mobile publication reads. |
| E06 | Missed-check-in expectations/window/feed. |
| E07 | Caregiver internal review notes. |
| E08 | Reference-aware landing media lifecycle/cleanup. |

Recommended defaults for discussion: gateway secrets remain server-managed;
application configuration registry is deferred; Supplier remains postponed.
None of these defaults is a newly approved product contract.

### One slice, one worker, one acceptance loop

1. **Lock the slice with the owner.** State included report IDs, existing rules,
   proposed behavior, exclusions, permissions, DTOs/statuses/errors and acceptance
   cases. Resolve product decisions; do not leave them to the builder.
2. **Issue one pinned brief.** Current main SHA, exact file ownership, fixtures,
   gates and deliverables. Preserve others' edits. Follow current main-branch
   instruction; no routine branch/worktree unless separately requested.
3. **Worker verifies fixture readiness before API testing.** Inspect target/config
   safely, reuse verified disposable fixtures or seed the smallest missing data,
   then read back preconditions before exercising the endpoint.
4. **Implement and verify only approved scope.** Fake providers, focused regression
   tests, actual query-provider proof where relevant, accurate OpenAPI/docs/Postman.
5. **Mastermind performs bounded acceptance.** Compare requirements, changed diff,
   gates and evidence. Route concrete defects back for a bounded correction;
   do not restart the whole project audit.
6. **Present result before commit/push.** Report passed/failed/not-run checks,
   remaining limitations and frontend handoff. Commit/push requires explicit
   owner approval under the current standing instruction.
7. **Advance only after acceptance.** Do not let the worker start the next slice
   merely because their local tests passed.

## 11. Fixture-first testing contract

Every implementation brief must include this gate before a business API is run:

> Identify the actors and data states required. Confirm the exact disposable
> target and safe provider configuration. Reuse verified existing fixtures or
> create minimal deterministic fixtures. Read them back and verify ownership,
> status, versions and dependencies. Only then run the scenario. No production
> seeding, broad reset, migration or provider contact is implied.

Metadata-only tests do not need business data, but their host must still be
isolated. Some authenticated GETs write audit records; "GET" does not universally
mean a read-only production test.

| Area | Minimum relevant scenarios |
| --- | --- |
| OTP/recovery | Pending identity, verified channel controls, missing/lost request state, provider timeout/rejection, retry, expiry/rate limits, persisted-state readback; no force-verify shortcut. |
| Finance | Draft and Approved owners, empty/populated financial state, valid/missing/foreign filters, correct refund/fee semantics; exact Npgsql queries. |
| Profile/media | Editable/frozen/current revision, submitted/approved contacts, required photos/documents, media file authorization and typed-ID query translation. |
| Payouts | Existing policy boundary/delay, verified account, paid/completed booking, prior paid/failed attempts, refunds/debt and foreign owner isolation. |
| Moderation | Approved role matrix, public anonymity versus authorized author inspection, reject reasons, exact counter definitions and any explicitly approved report/block transitions. |
| Disputes | Own/foreign facility, empty/populated list, booking enrichment and no accidental resolution permission. |
| Integrity | Orphaned and valid profiles, dependent records, retained visible diagnostics; no automatic repair from a read request. |
| Schemas | Actual status codes, files, bare integers, wrapper IDs, nullable values and referenced enum members match serialized results. |

InMemory tests prove logic/mapping, not PostgreSQL translation or concurrency.
Use existing guards and inspect environment variables before test processes start;
do not assume blanking one shell changes a later process. No Bruno work is planned.

Build/test gates require successful exit and zero warnings. Run focused tests for
each change; full suite and architecture/contract gates belong to integration or
cross-cutting changes. Mark authored versus executed tests and explained skips
honestly. Documentation-only work does not justify launching the API/full suite.

## 12. Integration, publication and deployment are distinct gates

The intended flow after a contract is approved is:

**Approved contract → worker slice → review/corrections → accepted evidence →
explicit commit/push approval → integrated release checks → explicit publish/
deploy approval → configuration checklist → backed-up deployment → verification.**

- New endpoints, permissions, migrations and configuration are listed before the
  release is approved, not discovered by the owner after a service crash.
- A data repair/import requires its own exact authority and backup/recovery plan.
- Whether VPS configuration is needed cannot honestly be answered for all future
  slices until their contracts are locked. Each slice must declare **none** or an
  exact configuration delta, including required/optional/default/restart behavior.
- The current owner-approved CORS policy stays unchanged and is not a gap.
- Production smoke checks do not prove complete Chat device/push/soak coverage.
- No current approval to deploy the changes proposed in this document exists.

## 13. Decisions needed before the first execution handoff

This audit is complete enough to discuss scope, but it is not decision-complete
enough to implement every proposed feature. The next owner conversation should
lock only the decisions needed for the next slice:

1. **Release boundary:** recommended daily portal workflows first (core slices),
   with new platform/home/analytics/audit products deferred; or explicitly expand
   the release. Do not make all P2 mock screens mandatory without this decision.
2. **Moderation authority:** recommended existing SuperAdmin global blocking,
   versus a separately designed community-only timed ban. Report intake and
   statistics definitions must be explicit if included.
3. **Owner legal:** include a legal-audience/content contract now or defer it
   explicitly; do not automatically bundle owner ID and deletion.
4. **Data disposition:** retain the six orphaned profiles pending recovery evidence
   or approve a bounded recovery/quarantine proposal after dependencies are shown.

Further DTO/filter/timezone/retention choices belong in the relevant slice's
contract before that worker starts. We will not invent them merely to make this
audit look like a finished implementation specification.

**Immediate next action:** agree the release boundary and lock S01's exact
contract/acceptance brief. No backend implementation has been authorized by
requesting this Markdown preview.
