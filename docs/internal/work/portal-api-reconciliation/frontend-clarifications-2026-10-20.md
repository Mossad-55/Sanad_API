# Frontend reply — corrections and outstanding questions

2026-10-20. Communication note, **not an implementation contract**.
Approved work is defined in the [follow-up contract](contracts/frontend-follow-up-2026-10-20.md).
Do not promote unanswered requests into phase scope. No UI screens are required;
use the approved minimal operational reads without waiting for designs.

## Corrections to send

- A5 is closed/excluded: SMS Misr OTP delivery works per Owner. We do not provide
  the requested Development OTP bypass/environment. No OTP work is scheduled.
- A6: verified activation-ready users can reactivate; unready users receive coded
  `409 Identity.Admin.ActivationNotReady`. Send a current failing request/trace if
  a 500 persists; unconditional activation is not the contract.
- A7: Draft facility dashboard/revenue access already has PostgreSQL verification.
  No owned facility is distinct from an owned Draft facility. Send fresh evidence
  if current behavior differs.
- Current auth registration guide uses bilingual full names, phoneNumber and
  verificationRequestId. Admin conventions document wrapper IDs versus plain GUIDs.
  Visits guide/owner Postman use numeric days. Library has field-level errors.
  Send exact current examples/doc revision for remaining inconsistencies.
- Owner booking actions, operational reads, receipts, notes and visits need their
  actual distinct response DTOs, not one universal booking response schema.
- Certificate files use CaregiverReviewAdmin (SuperAdmin/SupportAdmin). Owner-only
  National ID/self-delete is not currently supported by those account flows; mixed
  accounts must not be inferred from owner-only rules.
- Contact name can be prefilled client-side and sent. Both admission languages are
  required for submission completeness. Four document types are required to submit;
  verified valid documents are required to approve. Two photos including cover are
  required. Approved unchanged asset carry-forward is now approved work.
- Keep counts SuperAdmin-only, payment keys server-managed, FAQ deactivation and
  library new-copy workflow. Supplier remains postponed. Do not build settings
  controls that imply missing backend permissions/features.

## Outstanding frontend questions — excluded additions

1. Family home: are anonymous reads, automatic expiry or non-service destinations
   essential? Existing approved scope is authenticated Family content with catalog/
   service targets, no scheduling. Name the additional destinations if needed.
2. Chat: scope/booking ID are approved. Is a stay/room-type summary essential too?
   Specify the required meaning for caregiver versus Care Home bookings.
3. Missed check-ins: approved feed means unanswered completed profile-local days,
   current eligible cohort and recomputed results. Is any different timing actually
   required? Reminders/incident resolution are not included.
4. Multi-account legal/help reads: should the active portal choose the audience?
   Current code uses the first claim; account/switch is client-local and does not
   change JWT audience. Recommended future behavior is an explicit audience checked
   against owned account types, but this change is NOT yet approved.
5. Growth beyond registrations: specify a metric/time basis, not just “services
   growth.” No extra series will be implemented without that definition.
6. App-rating feedback is deferred. Confirm a real review need before requesting it.
   Family/visit/booking reads proceed with the approved minimum; no screens awaited.
7. For stale A6/A7/docs/library complaints, supply current method/path, safe request,
   actual status/body/trace and source revision. Never send passwords/tokens.

## Owner-approved defaults communicated, not reopened

Support: users submit/read; SuperAdmin/SupportAdmin reply/change status explicitly;
no user replies or reopening initially; durable replies survive delivery failures.
Ratings: anonymous owner views, one unresolved owner flag, Admin hide/restore;
hidden ratings excluded from visible reviews and aggregates; edits do not unhide.
Scope refinements must be reported separately, not silently folded into these defaults.

## Bounded preparation decisions for the Mastermind

Before dependent dispatch pin support transition matrix, flag-resolution semantics,
review-note limits and media-reference protection in the active brief. Resolve any
material choice with the Owner rather than delegate it implicitly to a Worker.
Existing incomplete facility drafts must not be auto-repaired if missing inheritance
cannot be distinguished from an intentional removal. Other ready work can proceed.
