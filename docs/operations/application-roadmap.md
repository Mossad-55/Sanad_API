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

Implement one notification model shared by all roles:

- Cursor/page-based list with read state.
- Mark one read, mark all read, and unread count.
- Navigation target metadata for the related screen/entity.
- In-app event creation and preference enforcement.
- Email delivery after templates and provider are approved.

Needs owner verification: retention period, pagination contract, notification
categories, timezone behavior, deep-link format, email provider/templates,
and whether push is required in V1 or can follow realtime chat.

### Phase 3 — Elderly assistance, medication execution, and SOS

Complete the elderly flow on real data:

- Help-request domain with recipient, accept/reject, status history, and audit.
- Connect sentence-builder submission to the same help-request API.
- Medication task execution using the existing medication schedule and
  take/skip contracts; add history and family synchronization where missing.
- SOS creation, location capture, recipient notification, status tracking, and
  cancellation. Voice calling remains excluded as V2.
- Elderly dashboard composition for check-in, next dose, daily activity, and
  alerts.

Needs owner verification: help-request types and recipient rules, whether a
caregiver can reject a request, SOS escalation/timeout rules, location
precision and retention, emergency contacts, medication late threshold, and
whether skipped-dose reasons are free text or lookup values.

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
- Elderly health tips list/detail/category/featured/read/save behavior.

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
