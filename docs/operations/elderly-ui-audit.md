# Elderly UI audit

Status: Phase 0 inventory and API reconciliation in progress. Elderly
medication self-service and Admin medication operations are implemented and
pushed; CMS wellness tips are implemented and pushed; Elderly profile/timezone
is implemented and synchronized in the current checkpoint; the Family-managed
emergency-contact API is also implemented in the current uncommitted slice.
Other screen/API gaps remain open and assigned to bounded roadmap slices.

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
| Daily reassurance check-in and success state | No Elderly self check-in route or daily check-in persistence was found. Account notification preferences include `checkInAlerts`; currently this stores a preference only. | Preference docs/Postman/Bruno and user-preference tests cover storage only. No check-in endpoint/docs/test/Bruno flow. | **Gap.** Owner confirmed one final answer per Elderly profile-local calendar day (retries return the saved answer); a “not okay” answer creates a durable in-app alert for each active linked Family member whose `checkInAlerts` preference is enabled. Push is attempted when a provider is available; no SMS. Push retry/provider behavior and missed-check-in semantics remain `Needs Owner Verification`. |
| Medication checklist, take action, missed-dose alert | Family routes remain FamilyAccess-scoped. Elderly self-service is now available through `GET /api/v1/elderly/medications`, `GET /api/v1/elderly/medications/dashboard?date=YYYY-MM-DD`, and `POST /api/v1/elderly/medications/{medicationId}/doses/take`. All require a Normal Elderly account and resolve the dependent from the authenticated user; callers cannot select another dependent. Dashboard date is required and uses the Elderly profile-local calendar date. Take reuses `RecordDoseTakenRequest`; only an active prescription in its date range at a scheduled time is eligible. The shared family dose history records the Elderly actor and stock is decremented; duplicate/concurrent takes are rejected. Elderly cannot edit or skip prescriptions. | `docs/app/elderly/medications.md`, `docs/app/families/medications.md`; Elderly and Family Postman collections; `ElderlyMedicationsControllerTests`, `MedicationHandlerTests`; Elderly read-only Bruno contract requests and existing Family medication requests. | **Covered for the approved self-service slice.** Late/missed alert threshold and notification delivery remain separate work; no prescription edit/skip API is exposed to Elderly. |
| "Express your needs" sentence builder (actor/action/request/qualifier), custom sentence, speech input, send | No sentence-builder taxonomy, speech/phrase, or Elderly help-request route was found. | No matching tests, endpoint docs, Postman requests, or Bruno requests. | Both the selectable vocabulary/templates and supported order/combinations must be API/CMS-driven; custom sentence and microphone behavior need an explicit contract. Submission needs a real request lifecycle, recipient selection, visibility, notifications, and audit history. **Gap; Phase 3.** CMS authoring must include read/list/detail as well as content edits. |
| SOS / contact family | Family contact management is available at `GET/PUT /api/v1/family/dependents/{dependentId}/emergency-contact`: all active Family members may read, only current Family Owner may write; Elderly reads through identity-bound `GET /api/v1/elderly/profile`. No SOS/event route exists. | Family/Elderly endpoint docs, Postman and manual Bruno examples; focused contact/profile tests. | **Contact storage/read is covered for the bounded slice.** Owner confirmed SOS creates a tracked event, SMSes only that contact, and the app opens the device dialer. SOS location permission/precision/retention, event states, duplicate/cancel/escalation, SMS delivery/retry, contact removal and Admin contact visibility remain open. **SOS gap; Phase 3.** |
| Notifications list | Shared `NormalAccess` inbox routes now provide list, unread count, mark-one-read, and mark-all-read with recipient isolation, one-year read-time availability, cursor paging (default 20, bounds 1–100), and typed destination kind + ID. Preferences remain separate storage. | `docs/app/notifications.md`; Auth Postman notification requests; safe read/negative Bruno requests; focused notification tests. | The check-in producer caller, complete event/category inventory, event-specific deep-link policy, preference enforcement for every event, push/email delivery and retry, outbox/job delivery, and Admin inspection remain unimplemented or **Needs Owner Verification**. Physical purge is not part of the contract; records older than one year are excluded at read time. |
| Profile and emergency contact | Elderly profile/name/age/photo/assessment/timezone/contact reads use identity-bound `GET /api/v1/elderly/profile`; Family contact GET/PUT provides member-read/Owner-write access. | Elderly profile and Family dependent docs/Postman/Bruno; focused profile/contact tests. | **Profile and contact read/write covered.** Profile editing, contact clear/delete, Admin profile/contact inspection, and health-status badge remain open; do not infer further behavior. |
| Health and wellness tips list/detail | `GET /api/v1/elderly/wellness-tips` and `GET /api/v1/elderly/wellness-tips/{id}` provide a Normal Elderly JWT published-only paged feed/detail. CMS routes provide admin list/search/filter/detail/preview, multipart draft create/update, publish, and archive. | `docs/app/elderly/wellness-tips.md`, `docs/admin/wellness-tips.md`; Elderly/Admin Postman collections; WellnessTip controller/domain tests; Bruno wellness-tip contract requests. | **Covered for the bounded slice.** Content is bilingual and section-structured, images use generated `IFileStorage` keys, and page size is bounded (default 20, max 100). No featured/save/read/metrics/static/sample behavior is exposed. Clinical review workflow and tenant scope remain **Needs Owner Verification**; CMS storage is currently global/shared. |
| Admin oversight of Elderly records and CMS content | Existing CMS admin APIs support splash/legal/help/assessment/lookups. Medication operational reads and bounded wellness-tip CMS list/detail/lifecycle are available. No Admin API is defined here for check-ins, help requests, SOS, notification delivery, or Elderly profile/contact. Existing AdminBookings/Caregivers endpoints cover their own domains only. | Admin medication and wellness-tip tests/collections cover delivered slices; no Admin profile/contact examples. | The supplied UI folder contains no Admin screens, so exact admin tables/filters/metrics/actions remain unknown outside delivered slices. Contact values are not exposed to Admin by these Family/Elderly routes. Wellness CMS access is SuperAdmin/ContentAdmin with Normal access and is global/shared; tenant scope and clinical-review permissions remain **Needs Owner Verification**. |

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
- **SOS:** the Elderly action creates a tracked SOS, notifies only the
  primary phone-only emergency contact maintained by Family, sends an SMS to
  that contact, and opens the device dialer. It does not notify all linked
  family members.
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
| Elderly phone/SMS login | API, docs, Postman and unit coverage exist; no Elderly-specific Bruno end-to-end scenario identified. |
| Splash CMS/public | API, docs, Postman, unit and public Bruno coverage exist; audience is global. |
| Elderly profile/avatar | Normal Elderly profile, photo, and own primary emergency-contact reads are covered through identity-bound profile routes, including profile-local age and stored timezone. Admin profile/contact inspection is not implemented. |
| Medication adherence | Family and Elderly read/take APIs, public docs, Postman, and focused unit coverage exist. Elderly Bruno coverage is read-only; dose-taking is state-mutating and is not an automated Bruno request. |
| Check-in, needs/help, SOS, notification inbox | No matching API/domain docs/tests/Postman/Bruno surface found. |
| Wellness tips | Elderly list/detail and CMS admin lifecycle are covered by focused tests, public docs, Postman, and Bruno contract requests; see the matrix row above. |
| Admin Elderly operational visibility | No matching Elderly-specific list/detail/report APIs found; admin UI was not supplied. |

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
- No matching route, domain docs, tests, Postman request, or Bruno scenario
  was found for Elderly check-in, help-request/sentence-builder, or SOS.
  The shared notification inbox foundation is now implemented and documented;
  it has no check-in producer caller yet. Wellness tips are covered separately
  above.

## Needs Owner Verification

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
5. SOS workflow is confirmed: create a tracked SOS, SMS only the primary
   phone-only emergency contact maintained by Family, and open the device
   dialer. Location precision/retention, duplicate/cancel/escalation rules,
   event statuses, SMS delivery guarantee, and retry policy remain open.
   The contact is a Family-managed record with name, relationship and phone;
   it does not require its own Sanad account.
6. The complete notification category inventory, event-specific timezone
   semantics, preference enforcement for every event, push/email provider and
   retry rules, physical purge policy, and Admin inspection/trace surface.
   The delivered foundation defines one-year read-time availability, cursor
   paging, read state, and typed destination metadata only.
7. Health-tip category taxonomy, clinical review, featured/save/read metrics,
   and tenant scope. Bilingual structured authoring, Draft/Published/Archived,
   image constraints, list/detail/preview and direct ContentAdmin publishing
   are implemented; ContentAdmin publishing does not imply clinical approval.
8. Elderly Admin UI routes/screens, exact support-role field-level read
   permissions for clinical/location/contact/communication data, audit log
   contents, retention and export behavior. Admin role split and support-role
   request/SOS status management are confirmed.
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
  foundation is delivered; check-in creation, providers/retries, physical
  purge, category inventory, and Admin inspection remain open.
- Elderly check-in/dashboard, sentence-builder requests and SOS: Phase 3
  (after the unresolved permissions/lifecycle rules above are decided).
- Medication late/missed alerts and notification delivery remain separate work;
  the core Elderly list/dashboard/take self-service slice does not include
  Elderly skip or prescription editing. Admin operational inspection is now
  delivered for medication prescriptions, persisted dose timelines, and
  dose-log adherence through `docs/admin/elderly-medications.md`; each
  successful sensitive GET writes an immutable, payload-free audit row and is
  covered by manual/stateful collection examples only.
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
| 1 / Phase 2 | **Delivered foundation:** shared notification inbox | Authenticated NormalAccess list with cursor paging (default 20, bounds 1–100), unread count, mark-one-read, mark-all-read, one-year read-time availability, read timestamps, and typed entity-kind/ID destinations. No check-in producer caller is included. | No Admin inspection or delivery/retry surface is included. | Delivered: one-year availability, cursor paging, typed destination metadata, and read transitions. **Needs Owner Verification:** complete category inventory, other event timezones, preference enforcement for every event, email/push provider/retry, and physical purge policy. |
| 2 / Phase 3 | Elderly daily check-in | One final answer per profile-local calendar day; retries return saved answer; current-day status/history, actor/time, timezone/day boundary, Family visibility. “Not okay” creates durable in-app alerts for active linked members with `checkInAlerts` enabled; push when available, no SMS. | Operational list/detail/timeline, date/status filters, aggregates and audit trail under approved role. | Core check-in/alert behavior and recipient preference are approved; push provider/retry, reminders and missed-state rules still require verification. |
| 3 / Phase 3 | Elderly medication self-service and operational reads | **Core delivered:** Elderly-scoped prescription list, required-date profile-local dashboard, and scheduled-dose take; shared persisted history with Family and Elderly actor; duplicate/racing take rejection and stock decrement. Prescription edit/skip are not exposed. | Admin medication list/detail, persisted dose timeline, and dose-log adherence aggregate are delivered under `ElderlyMedicationOperationalRead`; SuperAdmin and SupportAdmin with Normal access only. Every successful sensitive read is audited before the result; late/missed alerts and notification delivery remain separate. | Core self-service and Admin read contracts are approved; collection examples are manual/stateful because GETs write audit rows. |
| 4 / Phase 3 | Sentence-builder catalog and help requests | CMS-driven localized builder vocabulary/templates plus custom sentence rules; request create/list/status and recipient workflow. | CMS full list/detail/preview/version/publish for vocabulary; operational requests queue/detail/timeline and approved accept/resolve actions. | Builder schema, recipient rules, request lifecycle, and permissions approved. |
| 5 / Phase 3 | SOS / emergency response | SOS creation, location consent/capture, recipients, status, duplicate protection, cancel/escalation and notification integration. | Restricted urgent-event queue/detail, response timeline, purpose-logged contact/location view and aggregate response measures. | Button behavior, responder rules, location precision/retention, and escalation/timeout approved. |
| 6 / Phase 6 | Health and wellness tips | **Delivered bounded slice:** public Elderly published-only feed/detail with bilingual titles/sections and bounded pagination. | CMS list/search/filter/detail/preview, multipart image-backed draft create/update, publish/archive. | Localization, media, and lifecycle are implemented; clinical review, tenant scope, featured/save/read metrics remain **Needs Owner Verification**. |

Do not defer the Admin list/detail requirement to a later, unowned “CMS CRUD”
project. For each operational resource, Admin visibility is part of the same
slice acceptance contract as the mobile endpoint. Keep clinical facts in their
domain of record and expose them to the CMS/Admin surface through permissioned
read APIs rather than copying them into editable CMS content tables.

## Audit closeout criteria

Before closing the Elderly inventory, provide the remaining onboarding/auth and
profile/navigation screens or confirm they are out of scope; resolve or accept
the decisions above as explicit phase-gated deferrals; reconcile Admin UI
screens/roles; and assign each gap to a roadmap slice. Implementation remains
out of scope until a specific slice is approved. For every future slice, update
API docs, Postman, Bruno where safe, focused tests, and admin read/inspection
coverage alongside the user-facing behavior.
