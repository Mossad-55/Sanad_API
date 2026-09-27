# Sanad application roadmap

This roadmap reconciles the product-gap inventory supplied by the owner with
the API currently present in this repository. It is the planning source for
the remaining application work after subscription billing.

## Scope rules

- V1 includes text chat, conversations, attachments, read receipts, unread
  counts, realtime delivery, and push-notification integration.
- Video calls and voice calls are V2 and are not part of the current roadmap.
- Existing APIs must be connected to the relevant UI before creating a new
  duplicate contract.
- Every implementation slice requires focused tests, public endpoint docs,
  Postman/Bruno coverage where applicable, route mapping, and deployment
  validation.
- `Needs owner verification` means the backend contract cannot be safely
  designed from the current requirements alone. The mastermind will raise the
  exact decision when that phase is reached.
- Deferred Family items are tracked in [`family-ui-audit.md`](family-ui-audit.md),
  which records the gap, reason, and return trigger/roadmap phase. At each phase
  kickoff, review that register and pull the items assigned to that phase into
  its active scope before implementation. At phase closeout, either mark each
  item implemented with tests/docs/Postman/Bruno evidence or record an explicit
  new deferral and reason; do not silently drop or implement later-phase work
  early.

## Current baseline

Already available and reusable:

- Account profile, language, notification preferences, avatar, family and
  dependent profile APIs.
- Caregiver discovery, caregiver profile, services lookup, schedule,
  availability, and booking lifecycle APIs.
- Family medical profile, notes, activities, reports, medications, dashboard,
  dose-take, and dose-skip APIs.
- Family and caregiver booking lists, details, accept/decline/start/complete,
  visit reports, and medical reports.
- Subscription plans, checkout, renewal, plan changes, invoices/PDFs, and
  completion-only booking allowances.
- Admin CMS foundations for lookups, legal/help content, assessments,
  caregivers, bookings/refunds, and subscriptions.

These contracts still need UI integration where the product inventory says the
screen is using local or static data.

## Ordered implementation phases

### Phase 0 — Full application UI audit and contract reconciliation

Family-scope audit artifact: [`family-ui-audit.md`](family-ui-audit.md). It
maps the owner-described Family journey from onboarding through account
settings (settings are recorded as already completed by the owner), includes
the supplied user/admin Library and Community UI inventory, and records
current route, permission, and coverage evidence plus API gaps and explicit
owner-verification/deferral dispositions. This closes only the Family
inventory; Phase 0 remains open for the other application roles and screens,
plus any Family owner decisions listed in that report.

Elderly-scope audit artifact: [`elderly-ui-audit.md`](elderly-ui-audit.md).
It inventories the 17 supplied Elderly screens and maps current API,
authorization, test, docs, Postman, Bruno, and Admin-visibility evidence. It
records the owner's dynamic-data/CMS requirement separately for editorial
content and user-specific operational data, and keeps unresolved permissions
and business rules marked `Needs Owner Verification`. Elderly features remain
split across roadmap phases and proceed as bounded, verified slices under the
active Elderly UI goal; owner decisions and remaining verification stay
tracked in the report.

Walk every family, caregiver/nurse, elderly, admin, booking, subscription, and
settings screen against the deployed API. Produce a screen matrix with route,
current data source, required API, stale fallback, and owner decision.

Deliverables:

- Confirm which old screens are removed or redirected to the current booking
  screens.
- Confirm whether “nurse” is a separate role/API surface or a caregiver
  specialization.
- Confirm the mobile routes and screen identifiers for every gap.
- Mark every unresolved payload, permission, lifecycle, and CMS question as
  `Needs owner verification`.

### Phase 1 — V1 conversations and text messaging (highest priority)

Build the shared conversation domain for family, elderly, caregiver/nurse, and
support participants as allowed by policy.

Scope:

- Conversation list and participant/read-state summary.
- Paginated messages and message send.
- Text attachments with ownership, size/type validation, and private access.
- Read receipts and per-user unread counts.
- Realtime updates through the selected socket/realtime mechanism.
- Push notification events for new messages, respecting preferences.

Needs owner verification: allowed participant pairs, group conversations,
message edit/delete, attachment types and limits, retention, blocking/reporting,
whether an elderly user may start a conversation, and the selected realtime
provider/transport.

### Phase 2 — Notification center and delivery foundation

The durable inbox foundation is implemented and shared by all roles:

- Cursor/page-based list with read state.
- Mark one read, mark all read, and unread count.
- Navigation target metadata for the related screen/entity.
- Authenticated NormalAccess list, unread count, mark-one-read, and mark-all-read
  routes; see [`docs/app/notifications.md`](../app/notifications.md).
- One-year read-time availability, cursor paging with default page size 20, and
  typed destination entity kind + ID.

The check-in producer caller, complete category inventory, event-specific
timezone behavior, preference enforcement at event creation, email/push
providers and retries, and physical purge policy are not implemented or
contracted by this slice. Admin delivery/inspection is also not included.

Owner-approved initial in-app contract: a negative Elderly daily check-in
creates a durable in-app alert for every active linked Family member whose
`checkInAlerts` preference is enabled. Push may be added when a provider is
available; SMS is not used for this event. The inbox read foundation is
implemented; the approved check-in producer and provider delivery remain later
work.

Owner-approved inbox contract: retain notifications for one year; use cursor
pagination with a default page size of 20; represent destinations as typed
entity kind + ID metadata (the app owns route mapping). The complete
notification category inventory, timezone behavior for other event types,
email provider/templates, and push provider/retry semantics remain **Needs
Owner Verification**.

### Phase 3 — Elderly assistance, medication execution, and SOS

Complete the elderly flow on real data:

- Help-request domain with recipient, accept/reject, status history, and audit.
- Connect sentence-builder submission to the same help-request API.
- Medication task execution using the existing medication schedule and
  take/skip contracts; add history and family synchronization where missing.
- SOS creation, location capture, recipient notification, status tracking, and
  cancellation. Voice calling remains excluded as V2.
- The primary phone-only emergency contact is now Family Owner-managed and
  readable by linked Family members and the linked Elderly profile. This does
  not implement SOS delivery, Admin contact inspection, or contact removal.
- **Daily check-in:** the owner confirmed one final true/false answer per
  Elderly profile-local calendar day; retries return the saved answer. A
  negative answer creates the durable in-app alert described in Phase 2,
  respecting linked members' `checkInAlerts` preferences. SMS is excluded and
  push can be added when a provider exists. Push retry/provider behavior,
  reminders, missed-check-in semantics, and exact Family status/history reads
  remain **Needs Owner Verification**.
- Elderly dashboard composition for check-in, next dose, daily activity, and
  alerts.

Needs owner verification: help-request types and recipient rules, whether a
caregiver can reject a request, SOS escalation/timeout rules, location
precision and retention, medication late threshold, and
whether skipped-dose reasons are free text or lookup values. Contact clear/
delete and Admin field-level visibility remain unverified.

### Phase 4 — Role dashboards and booking execution integration

Replace static home and legacy execution cards with real contracts:

- Family dashboard: elderly status/check-in, activity/progress, caregiver
  cards, suggested care homes, and community content.
- Caregiver/nurse dashboard: today’s shift, booking actions, medication tasks,
  statistics, avatar, earnings summary, and rating summary.
- Booking detail medical snapshot, medication schedule, emergency alerts,
  upcoming visits, and dose confirmation.
- Remove production local fallbacks and redirect old caregiver/nurse execution
  screens to booking APIs.

Needs owner verification: dashboard calculation formulas, definition of
“best” caregivers and suggested content, nurse permissions, whether upcoming
visits are booking records or a separate appointment type, and the exact
medical data visible during an active booking.

### Phase 5 — Care homes

Create the care-home domain and UI contract:

- Searchable, filterable, sortable list and details.
- Services, facilities, rooms, pricing, availability, and visit slots.
- Visit booking, long-stay request, status tracking, and notifications.

Needs owner verification: care-home admin ownership, room inventory model,
pricing/tax/payment rules, visit approval rules, long-stay workflow, required
documents, cancellation/refund behavior, and whether care homes are included
in unified search in V1.

### Phase 6 — Community and health library

Build CMS-backed content and social interactions:

- Posts, articles, educational videos, categories, featured content.
- Create post, image upload, like, save, comments, views, search, and filters.
- Elderly health tips **bounded list/detail** and CMS authoring are delivered
  (`docs/app/elderly/wellness-tips.md`, `docs/admin/wellness-tips.md`).
  Featured, read/save, metrics, clinical review, and tenant-isolation behavior
  remain owner-verification work.

Video content is allowed as library content; this does not add video calls.

Needs owner verification: moderation roles and states, edit/delete rules,
comment moderation, upload limits/storage, view-count semantics, whether
content is admin-only or user-generated, and supported video formats.

### Phase 7 — Medical access grants and clinical sharing

Implement real medical sharing:

- Create grant with recipient, scope, and expiry.
- List active/history grants, usage status, last-used timestamp, and revoke.
- Enforce grant permissions in medical profile, medications, reports, and
  booking-related medical snapshot endpoints.

Needs owner verification: grant scopes, eligible recipients, maximum duration,
whether consent is required on first use, audit visibility, and whether a
booking automatically creates a temporary grant.

### Phase 8 — Ratings, earnings, invoices, and financial history

Complete the financial and trust surfaces:

- Rating submission tied to a completed booking, duplicate prevention,
  retrieval, and approved edit/delete policy.
- Earnings summary, transactions, payout status, date filtering, and details
  for caregiver/nurse roles.
- Family invoice list/detail/PDF integration using the existing invoice APIs,
  including payment/refund display states.

Needs owner verification: rating eligibility window, anonymous/public review
rules, moderation, payout provider/ledger source, payout states, currency/tax
display, and the complete payment/refund status taxonomy.

### Phase 9 — Unified search and remaining discovery integration

Add a unified search contract for caregivers, nurses, care homes, and community
content, then connect it to the existing discovery and service-lookup screens.

Needs owner verification: ranking, typo matching, language behavior, minimum
query length, result limits, role visibility, and whether care homes/community
are searchable before their dedicated phases are complete.

### Phase 10 — Final cross-role hardening and release

Run the complete role matrix and production readiness pass:

- Authorization isolation across family, elderly, caregiver/nurse, admin, and
  support.
- Pagination, idempotency, concurrency, audit, attachment, and privacy review.
- Full route/Postman reconciliation, Bruno scenarios, migration review, build,
  tests, deployment, health, and smoke verification.

## Explicitly deferred from this V1 roadmap

- Voice calls and video calls: V2.
- Marketplace: future product decision, not scheduled.
- Subscription enhancements not already closed: payment-method replacement,
  trial refinement, coupon redemption/marketing delivery, and provider/store
  policy compatibility.

## Priority mapping from the owner inventory

The supplied priorities map to phases as follows: chat → 1; notifications →
2; elderly help/SOS → 3; medication execution → 3; dashboards → 4; booking
execution/medical detail → 4; care homes → 5; community/library → 6; medical
sharing → 7; earnings/invoices/ratings → 8. Unified search and service lookup
integration are cross-cutting work in Phases 0 and 9.
