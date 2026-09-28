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

The shared inbox remains a read foundation; delivery providers, complete
category inventory, event-specific timezone behavior beyond check-in,
email/push provider and retry semantics, and physical purge policy are not
implemented by that foundation. The check-in producer and its Admin
operational reads are now delivered separately below. Admin notification
list/detail/timeline/aggregate views are delivered with audit-before-query, a
rolling one-year cutoff, and ID/category/type/created/read record projections.
Recipient identity, title, body, and destination are omitted. Deleted-recipient
mapping remains **Needs Owner Verification**. Provider delivery and retries
are not included.

Owner-approved initial in-app contract: a negative Elderly daily check-in
creates a durable in-app alert for every active linked Family member whose
`checkInAlerts` preference is enabled. Push may be added when a provider is
available; SMS is not used for this event. The delivered producer uses a
separate Notifications persistence boundary: the check-in write and alert
fan-out are not one atomic cross-context transaction, while per-recipient,
Elderly, and local-date idempotency makes same-answer retry safe.

Owner-approved inbox contract: retain notifications for one year; use cursor
pagination with a default page size of 20; represent destinations as typed
entity kind + ID metadata (the app owns route mapping). The complete
notification category inventory, timezone behavior for other event types,
email provider/templates, and push provider/retry semantics remain **Needs
Owner Verification**.

### Phase 3 — Elderly assistance, medication execution, and SOS

Complete the elderly flow on real data:

- **Delivered bounded help-request/sentence-builder slice:** Elderly create/list/detail/cancel routes use identity-bound `ElderlyAccess`; Admin list/detail/history/aggregate/status routes use `ElderlyHelpRequestOperational`; and CMS catalog list/detail/create/activate/deactivate routes use `CmsContent`. The catalog is bilingual and revisioned. Requests persist `Pending`, `Accepted`, `InProgress`, `Resolved`, `Rejected`, `Cancelled`, or `Reopened`; history is append-only, available for one year, and operator reasons are capped at 500 characters. `Idempotency-Key` replay returns the original request and a different payload conflicts. Durable in-app alerts target active linked Family members with `helpRequestAlerts` enabled, the assigned caregiver only during an active `Confirmed`/`InProgress` booking when the caregiver account is active and has `helpRequestAlerts` enabled, and active SupportAdmin accounts. A missing/mismatched identity-bound Elderly profile or any required Identity recipient-query failure yields no recipients (fail closed); alert creation is idempotent per request/recipient. Push/email/provider/outbox/retry remain deferred to Notifications/Events; SMS is excluded. See [`docs/app/elderly/help-requests.md`](../app/elderly/help-requests.md) and [`docs/admin/elderly-help-requests.md`](../admin/elderly-help-requests.md).
- Medication task execution using the existing medication schedule and
  take/skip contracts; add history and family synchronization where missing.
- **Delivered medication late/missed slice:** `GET /api/v1/elderly/medications/late`
  evaluates only the authenticated Elderly profile's current IANA-local day;
  the CMS-managed threshold is versioned and initially 60 minutes after the
  scheduled dose, with no historical recalculation. Normal `SuperAdmin` and
  `SupportAdmin` can use the audited `POST
  /api/v1/admin/elderly/medications/late/evaluate?dependentId=...` action under
  `ElderlyMedicationOperationalManage`; CMS reads/revisions use `CmsContent`.
  Durable in-app notifications target active linked Family members honoring
  `MedicationReminders`, the assigned caregiver only during an active
  `Confirmed`/`InProgress` booking, and active SupportAdmin users. Push/email,
  outbox, and scheduler delivery remain deferred/Needs Owner Verification; SMS
  is excluded. Preserve the existing prescription, dose-timeline, adherence,
  and Elderly dose-taking contracts.
- **Delivered bounded SOS slice:** `POST/GET /api/v1/elderly/sos`, identity-bound
  detail/cancel, and Admin list/detail/history/status routes are documented in
  [`docs/app/elderly/sos.md`](../app/elderly/sos.md) and
  [`docs/admin/elderly-sos.md`](../admin/elderly-sos.md). The slice uses the
  `Open` → `Acknowledged` → `Resolved`/`Cancelled` lifecycle, required
  idempotency, consent-gated paired coordinates rounded to three decimals,
  30-day coordinate read visibility, deleted-Family isolation, audit-before-
  Admin-read, and durable in-app recipients (active linked Family members,
  active-booking caregivers, and active SupportAdmin users). SMS and server
  dialing are excluded. Dedicated SOS preference semantics, push/email,
  outbox/scheduler, physical purge, device dialer, and export remain **Needs
  Owner Verification**; voice calling remains excluded as V2.
- The primary phone-only emergency contact is now Family Owner-managed and
  readable by linked Family members and the linked Elderly profile. This does
  not implement SOS delivery, Admin contact inspection, or contact removal.
- **Daily check-in:** `POST /api/v1/elderly/check-ins` accepts one final
  Boolean answer per Elderly profile-local IANA calendar day; a same-answer
  retry returns the saved record and an opposite answer returns `409
  Families.ElderlyCheckIn.AlreadyAnswered`. Invalid stored timezones fail
  closed. A negative answer creates durable in-app alerts for active linked
  Family members with `checkInAlerts` enabled. Push/provider retry behavior,
  reminders, missed-check-in semantics, and exact Family status/history reads
  remain **Needs Owner Verification**; SMS is excluded.
- **Admin check-in operations:** read-only list, detail, timeline, and
  aggregate routes are available under the existing operational-read policy.
  They use filters/paging, inclusive ranges up to 31 days, safe projected
  fields, and audit-before-read behavior. See
  [`docs/admin/elderly-check-ins.md`](../admin/elderly-check-ins.md).
- Elderly dashboard composition for check-in, next dose, daily activity, and
  alerts.

Needs owner verification for this phase now excludes the delivered
help-request/CMS and bounded SOS route/lifecycle contracts. Remaining SOS
decisions are dedicated preference semantics; delivery providers/retries,
outbox/scheduler and physical purge; device dialer; exact precision,
location-retention and export policy; and any escalation/timeout behavior.
Medication skipped-dose reasons, alert recipients, event
category/payload/deep-link, preference/channel mapping, durable retry/
idempotency, historical recalculation, and Admin alert/threshold audit also
remain unresolved. Contact clear/delete and Admin field-level visibility remain
unverified.

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
