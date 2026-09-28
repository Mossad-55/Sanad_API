# Elderly UI audit

Status: Authorized Elderly UI/API slices are implemented and in final verification.
Dynamic self-service, CMS content, operational Admin reads, notification inbox,
and Elderly OTP pre-validation are covered by current implementation and tests.
Welcome placement/content mapping and support visibility of sensitive profile
fields remain explicitly **Needs Owner Verification**; do not infer them.

Screens supplied: 17 images in `UI/Phase 0 - Elderly/`. The repository has no
mobile client source or navigation map, so screen IDs and transitions are
based on the supplied images only. Sample names, dates, medication names,
counts, and notification copy shown in those images are design examples, not
approved seed/static production data.

### Supplied screen inventory

| Supplied image | Observed UI/state |
|---|---|
| `Welcome Senior.png` | Welcome headline, intro copy, four benefit tiles, and start CTA. |
| `Senior Home.png` | Elderly name/photo/date header; reassurance button; shortcut to needs builder; today's medication; wellness tips; persistent family-contact/emergency CTA. |
| `Check-in Success State.png` | Successful daily reassurance confirmation on home. |
| `Senior Home (1).png` | Final needs-request confirmation, request text, recipient toggles (companion, nurse if needed, family), send/cancel. |
| `Medication Checklist.png` | Today's scheduled medication cards and taken action/state. |
| `Missed Medication Alert.png` | Late/missed dose alert and take-now action on home. |
| `Express Your Needs.png` | Needs category tiles plus custom full-sentence action. |
| `اختر الفاعل.png` | Sentence-builder actor step (self or plural/group wording). |
| `اختر الفعل.png` | Sentence-builder action step (need/want/feel/request options). |
| `اختر الطلب.png` | Sentence-builder object/request vocabulary, including water, medication, food, bathroom, rest, nurse/help. |
| `اختر التوضيح.png` | Optional qualifier vocabulary, including temperature, timing, speed, and politeness. |
| `تسجيل.png` | Sentence builder with a spoken-sentence example and active audio-recording state/stop control. |
| `Notifications.png` | Inbox examples for request delivery, caregiver en route, medication time/taken, and SOS. |
| `Health and Wellness Tips.png` | Wellness-tip list cards with image/title. |
| `2- Health and Wellness Tips.png` | Tip detail with category, title, image, and multiple structured advice sections. |
| `sos.png` | Emergency confirmation, location/family-alert promise, call-now and cancel actions. |
| `profile.png` | Photo/edit affordance, name, age, health-state label, personal phone, emergency contact name/relationship/phone. |

The route/screen ordering is not supplied. In particular, the welcome screen,
SMS OTP login, role selection, and authenticated home relationship must be
confirmed against the mobile navigation flow before screen IDs are finalized.

## Data and CMS rule from the owner

- Do not ship screen content, options, events, or display values as hard-coded
  product data. App screens must read current data from an API and show
  appropriate empty/loading/error states rather than silently falling back to
  sample content.
- CMS-authored/configurable content includes welcome/splash copy, health tips,
  and the sentence-builder vocabulary/templates. It needs public/app read
  routes and CMS authoring routes with list/detail visibility, lifecycle/status,
  localization, and safe publish controls.
- User-specific or clinical facts (profile, medication prescriptions, dose
  events, check-ins, help requests, SOS events, notification events) are
  dynamic domain data, not CMS copy. They need domain APIs; admin visibility
  is a separate, permissioned operational-read requirement. The owner-approved
  role split is SuperAdmin broad access, ContentAdmin content plus aggregate
  content metrics, and ElderlyOperations/SupportAdmin operational reads with
  limited request/SOS status management; sensitive access is audited. A CMS
  create/edit API alone does not satisfy the owner's requirement that admins
  can inspect the resulting records and activity.
- The owner requires admins to be able to see the data, not only create/update/
  delete it. Every future Admin/CMS slice must therefore assess list/search,
  detail, status/history, and useful aggregate/report views in addition to
  mutations. The role split is owner-confirmed; exact sensitive field visibility,
  retention/export rules, and audit-log retention remain `Needs Owner Verification`.

## Screen/API gap matrix

| Screen / displayed data | Existing API and authorization | Evidence in tests/docs/collections | Gap and disposition |
|---|---|---|---|
| Welcome Senior; "ابدأ الآن" | `POST /api/v1/auth/elderly/request-otp`, `POST /api/v1/auth/elderly/verify-otp` support phone/SMS login for a family-provisioned Elderly identity. `GET /api/v1/splash-screens` is anonymous and serves published content to all roles; it has no audience targeting. CMS admins have `GET/POST /api/v1/admin/splash-screens`, `GET/PUT/DELETE /{id}`, and publish/unpublish actions, under `CmsContent` (SuperAdmin/ContentAdmin). | `docs/auth/elderly-sms-login.md`, `docs/app/public/splash-screens.md`, `docs/admin/splash-screens.md`; auth/splash Postman requests; SMS-login and splash unit tests; Bruno public splash request. | Owner confirmed CMS-published splash content after language selection and no static product copy. Existing splash API is dynamic and shared across roles. The separate Welcome Senior headline/benefit tiles are not represented by the current splash contract, and exact welcome-to-OTP/role-selection navigation remains unclear. **Gap; placement/content mapping Needs Owner Verification.** |
| Elderly home header/profile summary | `GET /api/v1/elderly/profile` and `/photo` require a Normal Elderly JWT and resolve the linked dependent by authenticated identity. The profile returns bilingual names, profile-local age, photo state/path, latest assessment, stored IANA timezone, and nullable primary emergency contact; address and health notes are omitted. | `docs/app/elderly/profile.md`, `docs/app/families/dependents.md`; Elderly and Family Postman plus manual Bruno examples. | **Covered for the bounded profile/contact read slice.** Health-status badge, Elderly profile editing, and Admin profile inspection remain separate unresolved contracts. |
| Daily reassurance check-in and success state | `POST /api/v1/elderly/check-ins` accepts `{ "answer": boolean }` for the authenticated Elderly identity. One final answer is persisted per profile-local IANA calendar day; same-answer retries return the saved record and opposite answers reject. Invalid stored timezone fails closed. | `docs/app/elderly/check-in.md`; Elderly Postman manual request; API project build: 0 warnings/0 errors; focused `FamilyNotificationRecipientGateway` tests: 7/7; `tests/Bruno/collections/Sanad/elderly-wellness-events/` (scoped local-fixture run: 20/20 requests and assertions). | **Delivered for the approved core contract.** A negative answer creates durable in-app alerts for each eligible active linked Family member with `checkInAlerts` enabled, with per-recipient/Elderly/local-date deduplication. No caregiver recipient is implied for check-in. Families and Notifications use separate persistence boundaries; no atomic cross-context transaction is claimed. Push/provider/retry, reminders, missed-check-in semantics, and Family status/history reads remain `Needs Owner Verification`; no SMS. |
| Medication checklist, take action, missed-dose alert | Family routes remain FamilyAccess-scoped. Elderly self-service is available through `GET /api/v1/elderly/medications`, `GET /api/v1/elderly/medications/dashboard?date=YYYY-MM-DD`, `GET /api/v1/elderly/medications/late`, and `POST /api/v1/elderly/medications/{medicationId}/doses/take`. All resolve the dependent from the authenticated Elderly identity; callers cannot select another dependent. The late evaluation is current-day/profile-local only, uses the versioned CMS threshold (initially 60 minutes), and does not recalculate history. | `docs/app/elderly/medications.md`, `docs/app/families/medications.md`; Elderly and Family Postman collections; Elderly read-only/negative Bruno requests and existing Family medication requests. | **Delivered for the approved slice.** The Admin CMS threshold GET/revision POST and audited Admin evaluate POST are documented under `CmsContent` and `ElderlyMedicationOperationalManage`; Normal SuperAdmin/SupportAdmin manage the evaluation, while ContentAdmin also manages CMS revisions. Durable in-app alerts are idempotent and target active linked Family members with `MedicationReminders`, the assigned caregiver only for active Confirmed/InProgress booking, and active SupportAdmin. Push/email/outbox/scheduler remain deferred/Needs Owner Verification; SMS is excluded. Preserve the current owner recipient rule and the delivered prescription/list/dashboard/take/Admin-read behavior; prescription edit/skip remains out of scope. |
| "Express your needs" sentence builder (actor/action/request/qualifier), custom sentence, speech input, send | `POST /api/v1/elderly/help-requests` consumes active CMS catalog keys; `GET/GET by ID/cancel` are identity-bound under `ElderlyAccess`. Speech-to-text remains client-side and no audio is uploaded. | `docs/app/elderly/help-requests.md`; Elderly Postman manual/read requests; focused help-request tests; `help-request-fan-out` Bruno run: 14 assertions passed for Elderly create, linked Family owner/viewer, active-booking caregiver, non-assigned caregiver exclusion, active SupportAdmin, and unauthenticated/wrong-role rejection. | **Delivered bounded slice.** Custom plain text is optional and capped at 500 characters; catalog is bilingual and revisioned with Actor/Action/Need/Qualifier categories. Requests have append-only one-year history and the approved status lifecycle. Durable in-app alerts target active linked Family members with `helpRequestAlerts`, the assigned caregiver only during an active `Confirmed`/`InProgress` booking when the account is active and `helpRequestAlerts` is enabled, and active SupportAdmin accounts. A missing/mismatched identity-bound Elderly profile or required Identity-query failure yields no recipients; per-request/per-recipient idempotency remains. Push/email/provider/outbox/retry are deferred to Notifications/Events; SMS is excluded. |
| SOS / contact family | Family emergency-contact management remains available at `GET/PUT /api/v1/family/dependents/{dependentId}/emergency-contact`; all active Family members may read, only the current Family Owner may write, and the linked Elderly reads through identity-bound `GET /api/v1/elderly/profile`. The bounded SOS slice separately provides identity-bound create/list/detail/cancel, durable in-app recipient fan-out, and Admin operational reads/status history. | Family/Elderly contact docs plus SOS docs, Postman, focused tests, and `tests/Bruno/collections/Sanad/elderly-wellness-events/` (scoped local-fixture run: 20/20 requests and assertions). | **Contact storage/read and bounded SOS route/lifecycle are covered.** Eligible linked Family members follow the current event preference lookup; the assigned caregiver is included only with an active `Confirmed`/`InProgress` booking and applicable active/preference rules; active `SupportAdmin` is included and unrelated caregivers are excluded. Dedicated SOS preference semantics, exact precision/retention/export policy, push/email/outbox/scheduler, physical purge, device dialer, escalation, and timeout remain **Needs Owner Verification**. SMS and server dialing are excluded. |
| Notifications list | Shared `NormalAccess` inbox routes provide list, unread count, mark-one-read, and mark-all-read with recipient isolation, one-year read-time availability, cursor paging, and typed destination kind + ID. Admin provides metadata-only list, detail, timeline, and aggregate reads. | `docs/app/notifications.md`, `docs/admin/notifications.md`; Auth/Admin Postman; `tests/Bruno/collections/Sanad/elderly-wellness-events/` (scoped local-fixture run: 20/20 requests and assertions); focused notification/Admin tests. | Admin reads require audited Normal SuperAdmin/SupportAdmin access, exclude rows older than one rolling year, and project only notification ID/category/type/created/read timestamps; recipient identity, title, body, and destination are omitted. List supports exact category/type and date filters; timeline requires an inclusive UTC range of at most 31 days and returns page metadata; aggregate returns total/read/unread/category counts. Deleted-recipient mapping, physical purge, and seven-year audit-retention enforcement remain **Needs Owner Verification**. Full event inventory, preferences for every event, push/email delivery/retry, and outbox/job delivery remain deferred or **Needs Owner Verification**. |
| Profile and emergency contact | Elderly profile/name/age/photo/assessment/timezone/contact reads use identity-bound `GET /api/v1/elderly/profile`; Family contact GET/PUT provides member-read/Owner-write access. | Elderly profile and Family dependent docs/Postman/Bruno; focused profile/contact tests. | **Profile and contact read/write covered.** Profile editing, contact clear/delete, Admin profile/contact inspection, and health-status badge remain open; do not infer further behavior. |
| Health and wellness tips list/detail | `GET /api/v1/elderly/wellness-tips` and `GET /api/v1/elderly/wellness-tips/{id}` provide a Normal Elderly JWT published-only paged feed/detail. CMS routes provide admin list/search/filter/detail/preview, multipart draft create/update, publish, and archive. | `docs/app/elderly/wellness-tips.md`, `docs/admin/wellness-tips.md`; Elderly/Admin Postman collections; WellnessTip controller/domain tests; Bruno wellness-tip contract requests. | **Covered for the bounded slice.** Content is bilingual and section-structured, images use generated `IFileStorage` keys, and page size is bounded (default 20, max 100). No featured/save/read/metrics/static/sample behavior is exposed. Clinical review workflow and tenant scope remain **Needs Owner Verification**; CMS storage is currently global/shared. |
| Admin oversight of Elderly records and CMS content | Existing CMS admin APIs support splash/legal/help/assessment/lookups, sentence-builder catalog authoring, and bounded wellness-tip CMS list/detail/lifecycle. Check-in, medication, help-request, SOS, and notification operational reads are available to Normal SuperAdmin/SupportAdmin under their operational policies. Existing AdminBookings/Caregivers endpoints cover their own domains only. | Admin medication, help-request, SOS, notification, sentence-builder, and wellness-tip Postman/Bruno contract coverage; no Admin profile/contact examples. | The supplied UI folder contains no Admin screens, so exact UI tables/actions remain unknown outside delivered surfaces. Sensitive operational reads audit before query; help-request and SOS reads omit deleted Families. Notification views omit recipient identity, title, body, and destination; deleted-recipient mapping remains **Needs Owner Verification**. No Admin profile/contact route exists. **Needs Owner Verification before exposing** emergency-contact name/relationship/phone, DOB/derived age, photo, detailed address, health notes, or latest assessment to support staff; do not infer this visibility from the existing operational-read policy. Wellness and sentence-builder CMS access is SuperAdmin/ContentAdmin with Normal access and global/shared scope. |

## Current dynamic-content / administration findings

- Splash is the one applicable CMS-backed experience already available. It
  supports authoring, list/detail, publish/unpublish/delete, but published
  splash screens are shared across all four app roles, not Elderly-targeted.
- The Elderly welcome image is not enough to conclude it maps to the splash
  carousel; obtain the remaining onboarding/auth/navigation flow before
  assigning it to that API.
- Existing preference storage, medication dose recording, and profile APIs
  demonstrate dynamic domain data for other actors, but do not satisfy Elderly
  access or Admin visibility. Do not label these screens "done" merely because
  a related Family or account API exists.
- Sample medication names/times, notification events, health state, profile
  details, and sentence-builder options in the images must be replaced by
  API-backed values. Empty states and localization should also come from the
  approved contracts where they represent configurable product content.

## Recommended Admin experience (no Admin UI supplied)

No Admin UI was supplied; the design below is the default implementation
baseline for the active Elderly UI goal, with unresolved field-level access
choices retained as `Needs Owner Verification`. Keep two Admin concerns
distinct:

1. **CMS authoring** for welcome/splash material, sentence-builder vocabulary,
   and health/wellness tips. Provide list/search/filter, detail/preview,
   create/edit, draft/publish/archive, localized fields, ordering, and content
   engagement aggregates. Existing splash management is a reusable pattern;
   it is globally audience-shared today.
2. **Elderly operations** for check-ins, medication adherence, requests, SOS,
   notifications, and profile/contact records. Provide an overview dashboard
   with actionable counts/trends; searchable and filterable record lists;
   per-record detail and event/status timeline; and safe escalation/resolution
   actions only where the approved workflow allows them. Read-only visibility
   should be the default until a workflow explicitly requires an Admin action.

Recommended least-privilege split:

- ContentAdmin authors/publishes CMS content and sees aggregate content usage;
  it does not receive individual clinical, medication, contact, or precise
  location records by default.
- A dedicated ElderlyOperations/support permission can manage help-request and
  SOS statuses, and read other operational records. It must not edit
  prescriptions, identity/contact source fields, or immutable dose history.
  Emergency-contact/precise location/clinical detail should be field-scoped to
  the response need, purpose-logged, and access-audited.
- SuperAdmin can view and manage all Elderly operational data and CMS content;
  sensitive access and mutations must still be audited. Reuse the existing
  `SupportAdmin` account type for the separate operations policy unless source
  review finds a concrete reason to create a new role.

Every operational Admin surface should answer: what happened, to whom, when,
current status, who acted, what changed, and what follow-up is needed. Include
pagination, date/status/type filters, role/tenant isolation, audit history,
and aggregate metrics. Do not expose raw contact/location/clinical values in
dashboard totals or broad exports. Data retention, export, escalation actions,
and exact field-level access remain `Needs Owner Verification`.

### Owner-confirmed contract decisions

- **Admin boundary:** SuperAdmin may see and manage all Elderly operational
  data as well as CMS content. ContentAdmin is limited to CMS authoring and
  aggregate content metrics. A separate ElderlyOperations/support boundary
  may manage help-request/SOS status and read other operational records; it
  does not edit prescriptions, identity/contact source data, or dose history.
- **SOS high-level outcome:** the Elderly action is intended to create a
  tracked SOS, notify the maintained primary emergency contact, and open the
  device dialer where that behavior is documented. It does not establish a
  route, domain, persistence model, notification channel, or delivery
  guarantee. The owner later approved the operational recipient rule below;
  the SOS implementation must preserve the emergency-contact behavior and
  add the approved durable in-app recipients.
- **Operational alert recipients:** Elderly help requests, medication
  late/missed alerts, and SOS events may notify eligible active linked Family
  members, the assigned caregiver only while that caregiver has an active
  booking for the Elderly/dependent, and the existing SupportAdmin/
  ElderlyOperations boundary. A caregiver without an active booking and an
  unassigned caregiver are not recipients. SupportAdmin/ElderlyOperations
  may inspect and manage these operational events; booking availability alone
  does not create a caregiver recipient. Push/email delivery remains deferred
  to the Notifications/Events phase.
- **Medication link:** Elderly medication reads and dose recording use the
  dependent currently linked to that Elderly OTP identity, with dose events
  shared back to the same family-dependent medication history. The existing
  `Elderly.IdentityUserId` is uniquely indexed, so repository cardinality is
  one Elderly identity to one dependent row. Prescription edits remain Family
  management; the supplied Elderly screen only shows dose-taking actions.
- **Missed-dose threshold:** lateness must be a CMS-managed setting, not a
  hard-coded constant; initial configured value is 60 minutes after the
  scheduled dose. Admin changes must be reflected by future dashboard/alert
  responses. Exact historical recalculation and reminder-delivery semantics
  remain open.
- **Timezone:** daily check-in will use the stored Elderly IANA timezone, not
  the server's UTC day. The profile/timezone slice now stores and exposes the
  value, defaults/backfills it to `Africa/Cairo`, and permits only the Family
  Owner to change it. Daily recurrence and alert semantics remain open.

## Coverage summary

| Surface | Current coverage |
|---|---|
| Elderly phone/SMS login | `POST /api/v1/auth/elderly/request-otp` returns 204 only for an Active Elderly-only Identity user with a matching usable profile in a non-deleted Family. Unknown and every other ineligible phone returns the same 404 `Identity.ElderlyLogin.AccountNotRegistered`; eligibility is checked before OTP persistence/dispatch. PendingVerification is ineligible. | `docs/auth/elderly-sms-login.md`, `docs/auth/auth-errors.md`, `docs/postman/Sanad.Auth.postman_collection.json`; focused Identity/Families tests; guarded local no-op Bruno requests `elderly-otp-registration` (2 requests, 4 assertions passed). Positive OTP dispatch tests must run only against Development's no-op SMS sender. |
| Splash CMS/public | API, docs, Postman, unit and public Bruno coverage exist; audience is global. |
| Elderly profile/avatar | Normal Elderly profile, photo, and own primary emergency-contact reads are covered through identity-bound profile routes, including profile-local age and stored timezone. Admin profile/contact inspection is not implemented. |
| Medication adherence | Family and Elderly read/take APIs, public docs, Postman, and focused unit coverage exist. Elderly Bruno coverage is read-only; dose-taking is state-mutating and is not an automated Bruno request. |
| Needs/help and SOS | Help-request/sentence-builder API, CMS catalog, Admin operations, and SOS routes/status history are delivered with focused tests, Postman, and safe Bruno contracts. SOS delivery-provider, dialer, purge, export, and dedicated-preference details remain separately gated. The check-in and notification-inbox slices are also delivered. |
| Wellness tips | Elderly list/detail and CMS admin lifecycle are covered by focused tests, public docs, Postman, and Bruno contract requests; see the matrix row above. |
| Admin Elderly operational visibility | Check-in, medication, help-request, and SOS list/detail/history/status surfaces are delivered with bounded policies, deleted-Family isolation, and audit behavior; notification list/detail/timeline/aggregate views are also delivered with metadata-only record projections. Notification deleted-recipient mapping, physical retention/purge and audit-retention enforcement, plus profile/contact inspection, remain open or **Needs Owner Verification**. Admin UI was not supplied. |

### Existing endpoint/authorization map checked

| Endpoint(s) | Current access and fit for Elderly UI |
|---|---|
| `POST /api/v1/auth/elderly/request-otp`; `POST /api/v1/auth/elderly/verify-otp` | Anonymous phone/SMS login for an existing, family-provisioned Elderly identity. No Elderly self-registration. |
| `GET /api/v1/account`; `PUT /api/v1/account` | Normal authenticated account's own name/email/phone/verification/profile response. The response does not contain age, emergency-contact details, or the visible health-state label. |
| `GET /api/v1/auth/avatar`; `PUT /api/v1/auth/avatar` | Normal-authenticated private avatar; application handler only allows Family/Medical Caregiver/Companion Caregiver accounts and rejects Elderly. |
| `GET /api/v1/splash-screens` | Anonymous published splash list, display-order sorted, same content for all roles. |
| `GET/POST /api/v1/admin/splash-screens`; `GET/PUT/DELETE /api/v1/admin/splash-screens/{id}`; `POST .../{id}/publish`; `POST .../{id}/unpublish` | `CmsContent` policy (SuperAdmin or ContentAdmin); admin can list and inspect records as well as author/publish. No role audience on this resource. |
| `GET /api/v1/family/dependents/{dependentId}/medications`; `/dashboard`; `/{medicationId}`; `/{medicationId}/doses/history`; `POST .../{medicationId}/doses/take`; `POST .../{medicationId}/doses/skip` | Controller `FamilyAccess`; query/read handlers authorize membership in the owning Family and write handlers require management rights. Elderly Normal token is not accepted by this family policy. |
| `GET/PUT /api/v1/account/notification-preferences` | Normal authenticated caller's preferences only. Current docs state storage only; no event delivery is implied. |
| `GET /api/v1/notifications`; `GET /api/v1/notifications/unread-count`; `PUT /api/v1/notifications/{notificationId}/read`; `PUT /api/v1/notifications/read-all` | Shared `NormalAccess` inbox foundation. Recipient isolation comes from the authenticated user; list/unread/read-all use a one-year read-time availability window, list uses cursor paging (default 20, bounds 1–100), and each item carries category/type/title/body, created/read UTC timestamps, and typed destination entity kind + ID. |
| `GET/PUT /api/v1/family/dependents/{dependentId}`, `PUT/GET .../{dependentId}/photo` | Family-managed dependent profile/photo, `FamilyAccess`; no Elderly self-profile route for those fields. |
| `GET/PUT /api/v1/family/dependents/{dependentId}/emergency-contact` | Active Family membership is required to read; only current Family Owner can set/update; reads/writes are scoped to an active owning Family. Elderly reads only through identity-bound profile; no Admin route or clear/delete operation. |
| `GET /api/v1/elderly/profile` | Normal Elderly only, resolved by authenticated identity; includes nullable primary emergency contact, names, profile-local age, photo state/path, timezone, and latest linked assessment. |
| `GET /api/v1/elderly/medications`; `GET /api/v1/elderly/medications/dashboard?date=YYYY-MM-DD`; `POST /api/v1/elderly/medications/{medicationId}/doses/take` | Normal Elderly only; profile is resolved from authenticated user ID. Dashboard date is required in profile-local calendar semantics. Take writes to shared medication history; Elderly cannot edit or skip prescriptions. |

Auth policy definitions are in `src/API/Sanad.API/DependencyInjection.cs` and
`src/API/Sanad.API/Authorization/AuthorizationPolicies.cs`; the medication
controller itself applies `FamilyAccess`. `NormalAccess` does not mean every
normal account may access a route explicitly protected by `FamilyAccess`.

### Existing coverage locations checked

- Elderly SMS login: `tests/Sanad.UnitTests/Identity/ElderlyLogin/`;
  `docs/auth/elderly-sms-login.md`; request examples in
  `docs/postman/Sanad.Auth.postman_collection.json`. No Elderly-specific SMS
  happy-path Bruno flow was found.
- Account/profile/avatar/preferences: `tests/Sanad.UnitTests/Identity/Users/`
  and `Identity/Account/`; docs in `docs/auth/account.md` and
  `docs/auth/avatar.md`; Auth Postman collection; limited Bruno account and
  notification-preference scenarios under `tests/Bruno/collections/Sanad/`.
- Splash app/admin: `tests/Sanad.UnitTests/Cms/SplashScreen*Tests.cs`;
  `docs/app/public/splash-screens.md`, `docs/admin/splash-screens.md`; Public
  and Admin Postman collections; Bruno public request
  `tests/Bruno/collections/Sanad/public/04-splash-screens.bru`. No admin splash
  Bruno workflow was found.
- Medication: `tests/Sanad.UnitTests/Families/Medication*Tests.cs`;
  `docs/app/families/medications.md`; Family Postman collection; limited
  family-scoped Bruno tests under `tests/Bruno/collections/Sanad/family-medications/`.
- Elderly check-in now has a route, domain/persistence, focused tests, public
  docs, Postman coverage, and safe Bruno contract examples. Help-request /
  sentence-builder and the bounded SOS route/status slice are delivered with
  public docs, Postman coverage, and safe Bruno contract examples.
  The shared notification inbox is implemented and the negative check-in,
  help-request, and SOS producers are wired to it. The bounded notification /
  check-in / SOS recipient-flow local-fixture Bruno run passed **20/20 requests
  and assertions** at `tests/Bruno/collections/Sanad/elderly-wellness-events/`.
  The historical check-in 500 root exception was not captured, although the
  current scoped run passes. Postman endpoint examples describe check-in and
  SOS behavior but do not fully express recipient fan-out; the already-dirty
  Postman collection was inspected and left unchanged. Provider delivery and
  retries remain open beyond the delivered Admin operational views. Admin
  notification views expose only record metadata;
  deleted-recipient mapping and retention/purge questions remain separately
  marked for owner verification.

## Needs Owner Verification

### Delivered help-request/sentence-builder slice (2026-09-27)

The bounded contract is delivered: the bilingual CMS catalog uses immutable
revisions and Actor/Action/Need/Qualifier categories; Elderly create/list/
detail/cancel is identity-bound; Admin operational list/detail/history/
aggregate/status actions are audited and filter deleted Families. Requests and
history are available for one year, status transitions include
`Pending`, `Accepted`, `InProgress`, `Resolved`, `Rejected`, `Cancelled`, and
`Reopened`, and operator reasons are capped at 500 characters. Idempotency
replay/conflict and durable in-app recipient filtering are implemented.
Speech-to-text remains client-side with no audio persistence. Push/email
providers and retries remain deferred to Notifications/Events; SMS is
excluded. The bounded SOS route/lifecycle/Admin-read slice is documented below; delivery, preference, dialer, purge, export, escalation, and timeout remain separately gated.

1. The Family dependent is canonical for Elderly identity fields: Elderly sees
   a read-only bilingual name/photo profile, age computed from DOB, and latest
   linked assessment result; Family Owner/Editor retains the existing dependent
   write policy. Only Family Owner changes the stored IANA timezone, defaulting
   to Africa/Cairo. Emergency contact is one Family Owner-managed phone-only
   record. Any later Elderly editing of these fields is outside the supplied
   contract and must not be inferred.
2. Medication read/take-recording against the currently linked dependent,
   with family visibility, is confirmed. Skip is not shown in the Elderly UI
   and is not added by inference. A CMS-managed missed-dose threshold with an
   initial value of 60 minutes is confirmed; when the missed state is
   recalculated for past events and how the alert is delivered remain open.
3. Daily check-in uses the stored Elderly IANA timezone. One final answer is
   allowed per local calendar day; retries return the saved answer. A “not okay”
   answer creates a durable in-app alert for all active linked Family members
   whose `checkInAlerts` preference is enabled, with push when a provider is
   available; SMS is not used. Push retry/provider behavior, reminders,
   missed-check-in behavior, and exact family read surface remain open.
4. Sentence builder vocabulary schema/localization, actor semantics, custom
   sentence constraints, speech-to-text ownership/provider, recipients and
   help-request lifecycle.
5. The bounded SOS implementation is identity-bound and documents the Elderly
   create/list/detail/cancel routes plus Admin list/detail/history/status routes.
   It uses required idempotency, consent-gated paired coordinates rounded to
   three decimals, 30-day coordinate visibility, Open/Acknowledged/Resolved/
   Cancelled lifecycle, deleted-Family isolation, audit-before-Admin-read, and
   durable in-app recipients: active linked Family members, an assigned
   caregiver with an active Confirmed/InProgress booking, and active
   SupportAdmin users. Dedicated SOS preference semantics, exact precision and
   location-retention/export policy, push/email/outbox/scheduler, physical
   purge, dialer, escalation, and timeout behavior remain Needs Owner
   Verification. SMS and server dialing are excluded. The contact is a
   Family-managed record with name, relationship and phone; it does not
   require its own Sanad account.
6. The complete notification category inventory, event-specific timezone
   semantics, preference enforcement for every event, push/email provider and
   retry rules, physical purge policy, and any future recipient-level trace
   beyond the delivered metadata-only Admin list/detail/timeline/aggregate
   views. The inbox defines one-year read-time availability, cursor paging,
   read state, and typed destination metadata.
7. Health-tip category taxonomy, clinical review, featured/save/read metrics,
   and tenant scope. Bilingual structured authoring, Draft/Published/Archived,
   image constraints, list/detail/preview and direct ContentAdmin publishing
   are implemented; ContentAdmin publishing does not imply clinical approval.
8. Elderly Admin UI routes/screens, exact support-role field-level read
   permissions for clinical/location/contact/communication data, audit log
   contents, retention and export behavior. The Admin role split is confirmed;
   support-role request/SOS status-management semantics remain gated on the
   respective implementation contracts.
9. Welcome screen placement relative to pre-auth language, CMS-published splash,
   OTP login, role selection and authenticated home. CMS-managed welcome/splash
   content is confirmed; screen-to-resource mapping and navigation are open.
10. Emergency-contact clearing/removal and Admin visibility remain unverified;
    current contract supports one Owner-managed contact and member/Elderly reads,
    but intentionally exposes no delete route or Admin read route.
11. Whether a stored emergency contact must be erased during Family account
    deletion/anonymization, and the required retention period, remain unverified.

## Deferred to assigned roadmap slices

- Notification producers and delivery: Phase 2. The durable inbox read
  foundation and negative check-in producer are delivered; provider/retry,
  physical purge, complete category inventory, and other event producers remain
  open or assigned to their domain slices. Audited metadata-only Admin
  list/detail/timeline/aggregate inspection is delivered; recipient-level
  tracing is not exposed.
- Elderly check-in/dashboard, sentence-builder/help-request, and the bounded
  SOS route/lifecycle/Admin-read slice are delivered in Phase 3. SOS delivery,
  preference, dialer, purge, export, escalation, and timeout decisions remain
  owner verification items.
- Medication late/missed evaluation is delivered alongside the core Elderly
  list/dashboard/take self-service slice; Elderly skip and prescription editing
  remain unavailable. Admin operational inspection is delivered for medication
  prescriptions, dose timelines, adherence, and the audited evaluate action;
  CMS owns the immutable versioned threshold (initially 60 minutes). Evaluation
  is profile-local/current-day only and does not recalculate history. Durable
  in-app alerts are idempotent for active linked Family recipients honoring
  `MedicationReminders`, the assigned caregiver only during active
  `Confirmed`/`InProgress` booking, and active SupportAdmin users. Push/email,
  outbox, scheduler, and their retry semantics remain deferred/Needs Owner
  Verification; SMS is excluded.
- CMS health/wellness library bounded list/detail and authoring slice: delivered. Featured/save/read metrics, clinical review, and tenant isolation remain deferred pending owner decisions.
- Elderly profile/timezone and Family-managed primary emergency-contact APIs
  are delivered for identity-only Elderly reads and current Family membership/
  Owner writes. Admin Elderly profile/contact inspection remains a separate
  open gap; emergency-contact data is domain data, not CMS-managed.
  Operational Admin visibility ships with each operational domain; do not
  defer it as an unowned CRUD-only project.

## Recommended bounded delivery slices

These are recommendations for backlog planning, not implementation approval.
Each Admin read/inspection surface ships with the domain slice that creates
the records, so visibility and privacy are designed with the data lifecycle.

| Order / roadmap | Bounded slice | Required user/API outcome | CMS/Admin outcome shipped with it | Entry gate |
|---|---|---|---|---|
| 0 / Phase 0 | **Delivered:** Elderly identity, profile, timezone and Family emergency-contact contract | Canonical linked dependent; identity-only Elderly profile/photo/contact reads; profile-local age; Family Owner timezone/contact writes and member contact reads. | Admin role split approved; SuperAdmin broad access, ContentAdmin CMS plus aggregate content metrics, SupportAdmin/ElderlyOperations operational read plus request/SOS status. Admin Elderly profile/contact screens and exact field-level policy remain open. | Identity/link, profile, timezone and contact ownership/read decisions approved; Admin UI/field visibility and retention still require closure. |
| 1 / Phase 2 | **Delivered foundation:** shared notification inbox | Authenticated NormalAccess list with cursor paging (default 20, bounds 1–100), unread count, mark-one-read, mark-all-read, one-year read-time availability, read timestamps, and typed entity-kind/ID destinations. The negative check-in producer now writes eligible durable alerts through a separate Notifications context with local-date recipient idempotency. | Audited one-year Admin list/detail/timeline/aggregate reads for Normal SuperAdmin/SupportAdmin; ContentAdmin has no individual operational read. Record projections contain ID/category/type/created/read only and omit recipient identity, title, body, and destination. | Delivered: inbox reads, check-in alert fan-out/deduplication, and Admin operational views. **Needs Owner Verification:** deleted-recipient mapping, complete category inventory, other event timezones, email/push provider/retry, physical purge/retention, and seven-year audit-retention enforcement. |
| 2 / Phase 3 | Elderly daily check-in | One final Boolean answer per profile-local IANA calendar day; retries return saved answer; invalid timezone fails closed; no same-day edit; negative answer creates durable in-app alerts for active linked members with `checkInAlerts` enabled; no SMS. | Admin read-only list/detail/timeline/aggregate under the operational-read policy, with filters/paging, inclusive ranges up to 31 days, safe projections, and audit-before-read. | Core check-in/alert behavior and Admin operational reads are delivered. Separate Families/Notifications persistence is not an atomic transaction; push provider/retry, reminders, missed-state rules, and exact Family status/history remain verification items. |
| 3 / Phase 3 | **Delivered:** Elderly medication self-service, lateness, and operational reads | Elderly-scoped prescription list, required-date profile-local dashboard, scheduled-dose take, and current-day late/missed evaluation using the versioned CMS threshold; shared persisted history with Family and Elderly actor; duplicate/racing take rejection and stock decrement. Prescription edit/skip are not exposed. | Admin medication list/detail, persisted dose timeline, adherence aggregate, CMS threshold GET/revision, and audited evaluate action are delivered. Read/manage policies admit Normal SuperAdmin/SupportAdmin as documented; CMS revision authoring also admits ContentAdmin. Late alerts are durable in-app only with the approved recipient/preference/booking rule. | Core self-service, CMS, Admin, notification, Postman, route-mapping, and safe negative Bruno contracts are synchronized. Stateful evaluation/authoring examples remain manual-only; push/email/outbox/scheduler are deferred/Needs Owner Verification. |
| 4 / Phase 3 | **Delivered bounded:** sentence-builder catalog and help requests | CMS-driven bilingual Actor/Action/Need/Qualifier catalog; Elderly identity-bound create/list/detail/cancel; optional 500-character custom text; one-year append-only request/history lifecycle with idempotency replay/conflict. Durable in-app alerts target active linked Family members with `helpRequestAlerts`, the assigned caregiver only during an active `Confirmed`/`InProgress` booking when active and preference-enabled, and active SupportAdmin accounts; missing/mismatched Elderly profile or required Identity-query failure returns no recipients; per-request/per-recipient idempotency remains. | CMS list/detail/create/activate/deactivate; Admin operational list/detail/history/aggregate/status actions under `ElderlyHelpRequestOperational`, with audit-before-read, safe projections, deleted-Family filtering, and bounded reasons. | **Delivered.** Push/email/provider/outbox/retry remain deferred to Notifications/Events; SMS is excluded. Speech-to-text is client-side and no audio is stored. |
| 5 / Phase 3 | **Delivered bounded:** SOS / emergency response | Identity-bound Elderly create/list/detail/cancel; consent-gated paired location rounded to 3 decimals; 30-day coordinate visibility; Open/Acknowledged/Resolved/Cancelled lifecycle; durable in-app recipient fan-out; no SMS/server dialer. | Admin list/detail/history/status with Normal SuperAdmin/SupportAdmin access, audit-before-read, deleted-Family isolation, and append-only history. | **Bounded slice delivered.** Dedicated preference, exact precision/retention/export, push/email/outbox/scheduler, physical purge, dialer, escalation, and timeout remain **Needs Owner Verification**. |
| 6 / Phase 6 | Health and wellness tips | **Delivered bounded slice:** public Elderly published-only feed/detail with bilingual titles/sections and bounded pagination. | CMS list/search/filter/detail/preview, multipart image-backed draft create/update, publish/archive. | Localization, media, and lifecycle are implemented; clinical review, tenant scope, featured/save/read metrics remain **Needs Owner Verification**. |

Do not defer the Admin list/detail requirement to a later, unowned “CMS CRUD”
project. For each operational resource, Admin visibility is part of the same
slice acceptance contract as the mobile endpoint. Keep clinical facts in their
domain of record and expose them to the CMS/Admin surface through permissioned
read APIs rather than copying them into editable CMS content tables.

## Check-in slice update (2026-09-27)

The earlier gap entries above describe the pre-implementation audit baseline. The delivered slice now provides `POST /api/v1/elderly/check-ins` for a Normal Elderly JWT, one final Boolean answer per profile-local IANA calendar day, same-answer retry of the saved row, opposite-answer rejection, and fail-closed invalid-timezone handling. Negative answers create durable in-app alerts for active linked Family members with `checkInAlerts` enabled; notification rows are idempotent per recipient, Elderly, and local date. Families and Notifications are separate persistence contexts, so no atomic cross-context transaction is claimed. Push/provider/retry, reminders, missed-check-in, SMS, and Family current-status/history behavior remain deferred.

Admin now provides read-only check-in list, detail, timeline, and aggregate routes under the existing operational-read policy for Normal SuperAdmin/SupportAdmin accounts. Filters/paging, inclusive ranges up to 31 days, safe field projections, deleted-Family fail-closed filtering, and audit-before-read behavior are documented in [`docs/admin/elderly-check-ins.md`](../admin/elderly-check-ins.md). Admin SOS list/detail/history/status routes are documented in [`docs/admin/elderly-sos.md`](../admin/elderly-sos.md). Audited metadata-only Admin notification list/detail/timeline/aggregate views are documented in [`docs/admin/notifications.md`](../admin/notifications.md); Admin profile/contact inspection remains open.

## Audit closeout criteria

Before closing the Elderly inventory, provide the remaining onboarding/auth and
profile/navigation screens or confirm they are out of scope; resolve or accept
the decisions above as explicit phase-gated deferrals; reconcile Admin UI
screens/roles; and assign each gap to a roadmap slice. Implementation remains
out of scope until a specific slice is approved. For every future slice, update
API docs, Postman, Bruno where safe, focused tests, and admin read/inspection
coverage alongside the user-facing behavior.
