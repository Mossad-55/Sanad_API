# Family application UI audit

Status: screen/API mapping documented; approved medication dose-history slice implemented; closeout remains in progress. This is
the working report for the owner-described Family journey and the supplied
Library/Community user and admin UI screenshots. The remaining work is to
retain explicit deferrals for
decisions awaiting their relevant UI/phase, and complete final verification.
Care-home details are owner-deferred until that roadmap section is reached.

Audit baseline: `8ff8e8beed392b665bcceb288bf6466d6025c967` (`main`, matching
`origin/main`). The repository has API, docs, Postman, tests, and Bruno assets;
it does not contain the mobile or admin frontend source. Supplied UI assets are
in the owner-provided `UI/` directory and are not modified by this audit.

The screen-to-API reconciliation itself was read-only. The first approved
bounded gap (shared email-or-phone login) was subsequently implemented and
verified; evidence is recorded in the Sign in row and the private operations
handoff. Settings were explicitly excluded by the owner as already completed.
Subscription billing is closed and is not reopened absent a concrete defect.

## Supplied UI evidence inventory

All 22 current files in `UI/` were reviewed; these are grouped by observed
screen, not by filename spelling. `Saved.png` and `not Saved.png` show the
Community bottom tab selected, so whether these saved states aggregate Library
and Community items remains an owner question.

| Surface | Supplied files |
|---|---|
| Library app | `Library.png`, `Article.png`, `Article Details.png`, `Video.png`, `Video Details.png` |
| Library CMS | `CMS Dashboard.png`, `Content Type Selection.png`, `Article Title & Description.png`, `Article Information.png`, `Video Title & Description.png`, `Video Information.png` |
| Community app | `Community.png`, `Community (1).png` (filter sheet), `creat poast.png`, `detiels Community.png` |
| Community moderation CMS | `Content Moderation.png`, `details poast.png`, `ok- poat.png`, `rejecte.png`, `bloke user.png` |
| Saved states (shown under Community tab; shared scope unclear) | `Saved.png`, `not Saved.png` |

## Family journey mapping

| Screen / capability | Current API mapping | Current coverage | Gap and disposition |
|---|---|---|---|
| Choose language | `GET/PUT /api/v1/account/language` | Account docs, Postman, unit tests and auth-account Bruno preference scenario | API requires an authenticated account. **Needs Owner Verification:** should the first language choice persist before sign-in, or remain local until authentication? |
| CMS splash screens | `GET /api/v1/splash-screens`; admin CRUD/publish under `/api/v1/admin/splash-screens` | Public/admin Postman, admin docs and tests | API exists. **Needs Owner Verification:** during the UI walkthrough, confirm the displayed order, audience selection, and language behavior match the published CMS content. |
| Account type selection | `POST /api/v1/auth/register`; `POST /api/v1/account/accounts`; `POST /api/v1/account/switch` | Auth/account Postman and tests; account-switch Bruno suite | Account types include Family, Elderly, Medical Caregiver, and Companion Caregiver. No gap identified from the description. |
| Sign in | `POST /api/v1/auth/login` | Auth docs; canonical Auth and Family Postman collections; controller, host, validator, and handler tests; focused local Bruno phone request | **Decision implemented (2026-09-25):** Family, Medical Caregiver, and Companion Caregiver accounts can use email or phone plus password. Send exactly one of preferred `identifier` or legacy `email`; either property accepts email or ASCII E.164 phone (`+[1-9][0-9]{1,14}`). The shared handler also supports configured password-bearing administrative accounts because it selects users with a password credential and imposes no account-type filter. Elderly remains SMS OTP only. PendingVerification still returns a restricted JWT with no refresh token or DeviceSession; Active login/session behavior is preserved. Evidence: `AuthController.Login` maps `LoginRequest.LoginIdentifier` at `/api/v1/auth/login`; `AuthControllerLoginTests` covers alias compatibility/exclusivity; `LoginCommandValidatorTests` covers email/E.164 formats; `LoginCommandHandlerTests` covers phone lookup, account types, and pending-verification behavior; `AuthApiHostTests.LoginRoute_ShouldBeMapped_AndReturnValidationProblemDetails` covers route binding and validation. Bruno `auth-account/10-login-owner-by-phone.bru` uses local-only seeded Family owner `+201000000001`, defined by `TestUserDataSeeder`; it is not a VPS fixture. |
| Registration OTP | `POST /api/v1/auth/register`, `POST /api/v1/auth/verification/verify`, `POST /api/v1/auth/verification/resend` | Auth docs/Postman and registration/verification tests | Existing email/phone registration verification. **Needs Owner Verification:** confirm which verification prompt appears in the Family flow and whether both channels must be verified before continuing. |
| Forgot password OTP | `POST /api/v1/auth/password/reset/request`, then `POST /api/v1/auth/password/reset` | Password reset docs, Postman and tests | Email OTP reset is implemented. |
| Care-needs quiz | `GET /api/v1/family/assessment/questions`, `GET /api/v1/family/assessment/tiers`, `POST /api/v1/family/assessment` | Assessment docs/tests; Family Postman ordered quiz workflow; Bruno local negative contract case for foreign elderly ID | Quiz submission persists a family-scoped assessment and returns `assessmentId`; `elderlyId` is optional, and missing/foreign IDs return `Families.Assessment.InvalidSubmission`. CMS scoring and tier selection remain server-side. Dependent creation accepts the returned ID and links it only when the assessment is same-family and unlinked. Link and profile save atomically; failed links return `Families.Assessment.InvalidSubmission`. Retakes remain stored. No schema migration was needed. |
| Elderly profile | `POST/GET /api/v1/family/dependents`; `GET/PUT /api/v1/family/dependents/{dependentId}`; photo `GET/PUT /api/v1/family/dependents/{dependentId}/photo` | Dependent docs/Postman and Family tests | `GET` detail exposes nullable `latestAssessment`, selected by `CompletedOnUtc` descending then ID descending, with current tier display metadata. All family roles read; Owner/Editor create and manage. API stores date of birth and derives age; name, phone, relationship, and photo are represented. **Mismatch; Needs Owner Verification:** owner described an age field, while the API contract uses full date of birth; confirm what the UI collects. |
| Invite family member | `POST /api/v1/family/invitations`; invitee inbox `GET /api/v1/family/invitations`; joined members via `GET /api/v1/family` and `GET /api/v1/family/members/{memberId}`; Owner can revoke by ID | Family/invitation docs and Postman; `FamilyInvitationTests` and role-matrix tests; **no Bruno invitation-specific coverage found** | Create requires the recipient to already have a Family account; Owner/Editor may invite, but only Owner may revoke. The GET route is the invitee's pending, unexpired inbox, not inviter history. **Gap; Needs Owner Verification:** the described outgoing section needs invitation-to-member association, with at least pending/joined status. Confirm whether to add an Owner/Editor outbox and whether it shows only pending invitations or all states (accepted/declined/revoked/expired). Also confirm whether invitations should continue requiring a pre-existing Family account. |
| Family home profile header | `GET /api/v1/account`; avatar `GET /api/v1/auth/avatar` | Account/avatar docs, Postman and tests | Existing profile and avatar reads. |
| Daily reassurance check-in | No check-in endpoint found | No family check-in API/doc/test/client request found | **Gap. Defer contract/lifecycle to Phase 3** (elderly assistance); Family dashboard composition is Phase 4. Define submitter, daily transition, timezone/reset boundary, and family visibility before implementation. |
| Home medication summary | `GET /api/v1/family/dependents/{dependentId}/medications/dashboard?date=...` | Medication docs/Postman and medication tests | Active medication, low stock, daily dose totals and taken/remaining dose status are available. UI can compose this with other home data. |
| Elderly requests and reminder | No help-request, request-history, or caregiver-reminder endpoint found | No matching request API/docs/tests/Postman/Bruno flow found | **Gap. Defer to Phase 3** (elderly assistance). Define request types, recipient eligibility, lifecycle/status history, reminder limits, and authorization before implementation. |
| Call assigned caregiver | No family booking/request endpoint returns a caregiver phone contact for dialing | Booking and discovery docs/Postman cover booking/profile but not a private booking contact | **Gap. Defer to Phase 4** (booking completion): define authorization for returning an assigned caregiver phone and whether it belongs on booking detail or a separate contact route. |
| Recent activity feed | `GET /api/v1/family/dependents/{dependentId}/activities?limit=...` | Activity docs/Postman and handler tests | Summary metrics and timeline exist. Docs state only `ViewMedicalProfile` and `AddNote` are currently written; defined events including `UpdateMedications` are not. Response provides actor names but no actor avatar. **Defer completion to Phase 4**; finalize required event coverage and avatar behavior before implementation. |
| Activity permission | Same activity endpoint | Permission tests/docs | Current policy allows Family Owner and Editor; Viewer is denied. **Defer the Owner-only versus Owner/Editor interpretation to Phase 4** before changing this behavior. |
| Upcoming visits | `GET /api/v1/family/bookings?tab=1`; current `tab=2`; past `tab=3` | Booking docs/Postman, unit tests and booking lifecycle Bruno | Any active family member can read own-family lists/details; checkout is Owner/Editor, while cancellation/payment-intent are any member. Booking lifecycle exists. Care-home visits have no domain/API and remain open for a later care-home screen slice. |
| Caregiver recommendations/search | `GET /api/v1/caregivers` | Discovery docs/Postman, discovery tests and Bruno discovery request | NormalAccess required; any normal account role may browse. Server supports name search, caregiver type, location, specialization, availability, price range, min rating/experience, and paging. It returns average rating/review count, but no explicit top-five or sort contract. **Defer ranking/tie-break/minimum-rating rules to Phase 4** before changing search behavior. |
| Caregiver profile | `GET /api/v1/caregivers/{caregiverId}`; price quote `GET /api/v1/caregivers/{caregiverId}/quote` | Discovery docs/Postman and tests | NormalAccess required; any normal account role may browse. Public profile includes identity header, specialization/services/languages/areas/schedule/pricing and verified certificate names. No individual rating/review list or public phone endpoint found. |
| Filter availability | Search `availability` parameter | Discovery docs/Postman/tests | Current filter represents Available/Unavailable, not a separate Busy state. **Defer Busy-versus-Unavailable semantics to Phase 4** before changing the API. |
| Booking list/detail/cancel | `GET /api/v1/family/bookings?tab=...`; `GET /api/v1/family/bookings/{bookingId}`; `POST /api/v1/family/bookings/{bookingId}/cancel` | Booking docs/Postman, focused booking tests and Bruno lifecycle suites | Price breakdown, times, location/instructions and state exist. List response currently has null caregiver name/avatar; detail does not embed the full public caregiver profile. Client can separately fetch caregiver detail, but phone remains absent. |
| Chat with caregiver | No conversation/message route found | No chat tests, Postman, Bruno, or public chat guide found | **Gap.** V1 text chat is on the roadmap; pin participant, entry point, and authorization before implementation. Voice/video remain V2. |
| Booking rating/rebook | Existing booking detail exposes caregiver ID; no rating submission or review-list route found | No rating API/client coverage found | **Gap. Defer rating/review workflow to Phase 8.** Rebooking can reuse the caregiver ID and existing discovery/quote/checkout contracts. |
| Medical profile/history | `GET/PUT /api/v1/family/dependents/{dependentId}/medical-profile` | Medical profile docs/Postman and focused handler/domain tests; no Bruno-specific profile request found | Owner/Editor may read and update; Viewer is read-only. Conditions, allergies (category/allergen/reaction), blood type, height, weight, and a replaceable medical-history list exist. A successful read also writes a `ViewMedicalProfile` activity. `medicalHistory[].procedureDate` now accepts/returns an ISO `YYYY-MM-DD` date; `year` is retained for legacy data and derived from the date. Matching year/date is accepted and mismatch is rejected; legacy year-only entries return `procedureDate: null`. **Owner decision satisfied:** full procedure dates are implemented as JSON text storage with no migration. Bruno happy-path coverage is deferred until a disposable, repeatable owned-dependent fixture or cleanup contract exists; current local config provides owner credentials but no stable dependent ID, and writing the full replaceable profile against an assumed seeded dependent would mutate shared local test data. |
| Medication management | Existing routes under `/api/v1/family/dependents/{dependentId}/medications`: list/detail/create/update/stock/pause/resume/discontinue/dashboard and dose take/skip; dose history: `GET /api/v1/family/dependents/{dependentId}/medications/{medicationId}/doses/history?startDate=YYYY-MM-DD&endDate=YYYY-MM-DD` | Medication docs and Postman include dose history; focused ownership/unit coverage plus local-only Bruno required-date negative coverage. No live happy path was run because the repository has no stable seeded medication/dependent fixture. | **Implemented:** Owner/Editor/Viewer can read only for their own family's dependent; both ISO dates are required, start must be on/before end, and the inclusive range is at most 31 days. Results are persisted dose logs only, sorted by scheduled date/time ascending; no unlogged schedule rows are synthesized. Log event/status/time are persisted, while medication name/dosage/unit/quantity/instructions come from the current editable Medication row, not a prescription snapshot. The route is read-only and requires no migration. Existing family ownership rules, write roles, atomic stock editing, and stock-only route remain as documented. |
| Visit/medical reports and notes | `GET /api/v1/family/reports?type=visit|medical|all`; report photo route; dependent notes GET/POST/PUT/DELETE | Reports and notes docs/Postman, unit tests, Bruno report suites | All family roles may read reports and notes; Owner/Editor may add/update/delete notes. Existing APIs cover described reports and notes. |
| Notification inbox | `GET/PUT /api/v1/account/notification-preferences` only | Auth Postman/tests and preference Bruno scenario; no notification inbox API | **Gap. Defer** to the notification phase: define categories, delivery, read/unread state, navigation targets, and preference enforcement together. |
| Care homes | No care-home controller/domain/API found | No care-home docs, tests, Postman, or Bruno found | **Owner-deferred until the care-home section is reached**, as requested. At that point the owner will supply the UI and define list/detail, search, booking, visit, and long-stay contracts. This does not block closing the current Family UI report. |
| Library | No library/article/video API or CMS content controller found | No matching docs, tests, Postman requests, or Bruno scenarios found | **Gap confirmed.** Admin and user screenshots reviewed; detailed scope below. Keep in the family delivery plan; do not treat as implemented because subscription benefit `Library` exists. |
| Community | No post/comment/like/save/report/moderation API found | No matching docs, tests, Postman requests, or Bruno scenarios found | **Gap confirmed.** Admin and user screenshots reviewed; detailed scope below. Keep in the family delivery plan; notification preference `communityNotifications` is storage only, not community functionality. |

## Library UI review (`UI/`)

### User screens observed

- Library landing with Articles/Videos tabs, category filters, content cards, saved/bookmark control, premium/access badges, image or video thumbnail, title, summary, author, date/read-time or video duration, and view count.
- Article detail with cover, category, author, publication date, read time/views, formatted body, headings/lists, safety notice, and save/share controls.
- Video detail with player, category, title/summary, duration/views, educational bullet points, safety notice, and save/share/like controls.
- Saved-content list and empty state.

### Admin screens observed

- CMS content dashboard with Article/Video counts, content search, category/type/access filters, list/status/date, and row actions.
- Add-content wizard chooses Article or Video (step 1). The Article title/description/category screen is step 2; its rich-body/tags/access-tier screen is step 3. The Article step 4 is not supplied. The Video title/description/category screen is step 2; Video step 3 is not supplied; the supplied Video step 4 includes upload or YouTube/Vimeo URL, video content, recommendations, and access tier (`Free`, `Premium`, `Premium Plus`).

### Mapping and gaps

There is no matching admin or user API surface in the repository. Existing CMS endpoints cover splash, legal/help, assessments, and other separate resources; they do not provide article/video CRUD or a content feed. No current endpoint can serve library cards/details, categories, search/filtering, saved content, view counts, premium entitlement checks, or video assets. No tests, docs, Postman requests, or Bruno coverage exist for these features.

**Deferred to Phase 6 before design/implementation:** confirm the missing Article step 4 and Video step 3; whether article/video fields require Arabic and English variants; upload limits/storage and video hosting/transcoding; category/tag ownership; publish/draft/archive lifecycle; view-count semantics; save/share behavior; and how Free/Premium/Premium Plus access is evaluated against a family subscription and the `PremiumContent` benefit. The screens and absence of APIs are audited now; video library content is in scope, video calls are not.

## Community UI review (`UI/`)

### User screens observed

- Feed with search, category/type filters, article/post cards, author identity/avatar, role/category badge, relative time, title/body, optional image, and view/comment/like counts.
- Filter sheet with category, content type, and sort choices.
- Post detail with author/post data, replies/comments, role badge, timestamps, and reply composer.
- Create-post form with title, category, description, optional JPG/PNG image upload (screen states a 5 MB limit), anonymous-post toggle, and publish action.
- Saved-content view and empty state.

### Admin screens observed

- Moderation dashboard shows metrics for views, approved-today, reported posts, and blocked users; tabs for all, pending review, anonymous, and reported posts; search/filter, table rows, and pagination.
- Moderation detail shows the selected post and submitter name/email/date, with approve, reject, delete, and block-user actions. The create form has an anonymous-post toggle; **defer to Phase 6:** confirm whether anonymity hides identity only from public readers or also from moderators, since the moderator detail displays identity.
- Reject flow has a reason selector; block flow has a duration selector and reason field.

### Mapping and gaps

There is no community API surface in the repository. No endpoints exist for feed/search/filter, post create/detail, image upload, anonymous identity, comments/replies, likes, bookmarks, view counts, reports, moderation queues/actions, or user blocking initiated from moderation. Existing account blocking domain behavior and `communityNotifications` preference do not supply these user/admin workflows. No community docs, tests, Postman requests, or Bruno coverage were found.

**Deferred to Phase 6 before design/implementation:** who may create posts; whether anonymity hides identity from moderators or only public readers; exact category/content-type choices shown by the filter controls; edit/delete rules; report reasons and thresholds; moderation roles/status transitions; rejection reason taxonomy; block durations/appeal/unblock; comment moderation; upload/storage/security limits; view-count rules; and whether saved items include both Library and Community content. The supplied filter sheet labels category, content type, and sort; sort visibly includes newest, most interactive, and oldest. The screenshots and missing API surface remain in the Family audit now.

## Cross-cutting observations

- Focused verification rerun on 2026-09-25 across login/API contracts, medical-history dates, assessment/profile linking, and medication behavior: **90 passed, 0 failed**. Test-host startup emitted Data Protection keyring access/DPAPI warnings from the local environment; they did not fail tests.
- Dose-history focused verification: `MedicationHandlerTests` and `MedicationRequestContractTests` passed **23/23** after the endpoint implementation, including read roles, ownership, persisted-only rows, inclusive boundaries/order, and missing-date rejection.
- Latest two-way route/Postman mapping check after dose history: **246 controller actions, 285 Postman requests, 0 missing, 0 orphan**. This proves request mapping for routes that exist; it does not cover the missing UI features listed above.
- Unit tests exist for assessment, family, medications, medical profile, activity, reports, booking, and caregiver discovery. Bruno integration coverage is selective: the bounded assessment/profile slice has a foreign-elderly-ID contract case, and the medication slice now has a local-only seeded-owner negative contract case. A seeded assessment happy path remains unavailable because it needs explicitly provisioned CMS content.
- Assessment/profile linking verification: focused Families tests passed 14/14, including latest-result retrieval and concurrent row-link protection. `dotnet ef migrations has-pending-model-changes --context FamiliesDbContext` found no pending model changes; no schema migration was needed. Bruno now includes a local seeded-owner negative contract case proving a foreign/nonexistent elderly ID is rejected before CMS question data is needed. A Bruno happy path remains unconfigured because it depends on explicitly provisioned active CMS questions/tiers and a unique dependent phone; no live request was run.
- There is no frontend source in this repository, so visual behavior can only be reconciled from supplied UI images/descriptions and API contracts.
- Roadmap Phase 0 additionally asks for old-screen redirect/removal confirmation and mobile route/screen identifiers. No client source, navigation IDs, or legacy booking-route inventory was supplied, so these are explicitly deferred until the mobile navigation map or legacy screen inventory is provided. The owner-defined user-type list contains Family, Senior, Medical Caregiver, and Companion Caregiver. Current Identity and Caregiver enums align with that list and define no separate Nurse role; seeded “Test Nurse” data is a professional title. Treat Nurse as a Medical Caregiver title/specialization unless supplied UI contradicts this mapping.
- Caregiver name search currently uses case-insensitive Arabic/English substring matching. Recommended initial UI behavior: call the existing paged server search with query and filters together, debounce typing, and reset to page one on filter changes. Define ranked “best caregiver” ordering separately after the owner approves the rating tie-break rule.

## Recommended next steps

1. Continue Phase 0 for the Family journey. The supplied Library/Community images are recorded. Care homes remain owner-deferred until that roadmap section is reached. Keep settings excluded as requested.
2. Phone sign-in, assessment/profile linking, atomic medication save, full procedure dates, and medication dose history are implemented and documented. Dose history is scoped to one selected medication, returns persisted dose logs only, requires `startDate`/`endDate`, and caps the inclusive range at 31 days. Do not synthesize unlogged schedule rows.
3. The invitation outbox remains a separate candidate slice pending registered-account eligibility and status/role visibility decisions. Phase 0 still needs onboarding behavior and age-versus-date-of-birth input confirmation or explicit deferral. Mobile route IDs and legacy booking-screen redirects are explicitly deferred pending client navigation evidence; the four user types and Nurse title mapping are resolved from the supplied journey and current enums. Other decisions are explicitly deferred to Phases 1-8 in the register below. Assessment/profile linking is implemented: quiz submission precedes dependent creation, the returned `assessmentId` links the same-family unlinked assessment, and profile detail shows the latest completed result while preserving older submissions. Bruno happy-path execution still needs explicit active CMS question/tier content and a unique dependent phone; do not run it against a live service or assume seeded content.
4. After each slice, update this report with decisions, status, endpoint mapping, and evidence. Do not reopen subscription billing or settings without a concrete defect.

## Proposed gap-to-slice plan

This is an audit proposal, not blanket implementation authorization. Each slice
must be approved before code work; unresolved business rules stay marked below.
Phase assignments below follow the current owner-approved application-roadmap
priority mapping.

| Candidate slice | Current gap | Entry criteria / owner choices |
|---|---|---|
| Invitation outbox | No inviter-side list joining invites to recipient/member status | Confirm registered-account eligibility and which family roles see which invitation states. |
| Family dashboard and activity (Phase 4) | No daily reassurance check-in; activity event coverage and actor avatar data are incomplete | Defer contract decisions to Phase 4: check-in actor/timezone/reset, required activity events/avatar, and Owner-only versus Owner/Editor visibility. Existing medication dashboard remains reusable. |
| Elderly requests (Phase 3) and notifications (Phase 2) | No help-request/reminder API and no notification inbox/delivery API | Keep as separate roadmap slices. Define request types, recipients, lifecycle, and reminder limits in Phase 3; define categories, read state, delivery, navigation, and preference enforcement in Phase 2. |
| Caregiver discovery and booking completion (Phase 4) | Existing search/profile/booking APIs lack an agreed top-five ranking and authorized booking contact phone; individual reviews belong to Phase 8 | Defer ranking/tie-break, Busy mapping, contact authorization, and booking-detail versus separate-route decisions to Phase 4; defer rating workflow to Phase 8. |
| Library CMS then user app (Phase 6) | No article/video content APIs or CMS endpoints | Preserve the screens and gap inventory in this audit; defer field/localization, upload/hosting, taxonomy, lifecycle, view/save/share, entitlement, and missing-wizard-screen decisions to Phase 6 before design/implementation. |
| Community user app then moderation CMS (Phase 6) | No feed/post/comment/save/report/moderation APIs | Preserve the screens and gap inventory in this audit; defer posting, anonymity, categories/sort, edit/delete, moderation/report/block rules, uploads, and shared-save decisions to Phase 6 before design/implementation. |
| Care homes (Phase 5) | No care-home domain or routes | Explicitly owner-deferred until the care-home UI is supplied. |
| Text chat V1 (Phase 1) | No conversation/message route | Defer participant, entry-point, and authorization decisions to Phase 1 before implementation. Voice/video calls remain V2 and out of scope. |

## Owner decision and deferral register

### Needs Owner Verification

- Onboarding: pre-auth language persistence; splash audience/order/language confirmation; which registration verification prompt is shown and whether both email and phone must be verified before proceeding.
- Assessment/profile: implemented and documented. The UI should submit the quiz first, pass `assessmentId` to profile creation, and display `latestAssessment`; earlier quiz records remain history. The separate age-versus-date-of-birth input decision remains open. A repeatable Bruno happy path requires a fixture with active CMS content and a unique dependent phone; no assumed seed data is available.
- Invitations: whether to add an outgoing invitation list with pending/joined state and whether its permission follows Owner/Editor invite creation rights.
- Later-phase decisions are explicitly deferred in the table below; resolve them before their respective phase begins. The UI inventory and missing API scope remain in this audit.

### Explicit deferrals for unresolved Phase 0 contracts

- Onboarding language persistence, splash audience/order/language, and verification-prompt behavior are deferred until the mobile onboarding flow is supplied or explicitly described. Existing authenticated language preference and CMS splash APIs remain unchanged.
- Age-versus-date-of-birth presentation is deferred until the mobile dependent-profile form contract is supplied. The API continues to accept/store date of birth; do not infer a birth date from an age-only value.
- Invitation outbox, recipient eligibility, status presentation, and visibility policy are deferred to a separate invitation slice until the owner confirms its workflow and role policy. Existing invite creation/inbox/revocation behavior remains unchanged.
- A Bruno medical-profile date round-trip is deferred until a disposable, repeatable owned-dependent fixture or cleanup contract is available; do not mutate an assumed shared seed profile.

### Resolved owner decisions

- Medication edit form: **Satisfied (2026-09-25):** one atomic Save updates medication details and optional inventory quantity/threshold through nested `stock` on the main update route. Omit `stock` to preserve inventory; when present, both keys are required and explicit null values are allowed. The separate `/stock` route remains supported for stock-only clients.
- Medical history: **Product decision satisfied (2026-09-25):** procedure history stores an optional full `procedureDate` (`DateOnly`, serialized as `YYYY-MM-DD`) in the existing JSON text column; `year` remains for legacy compatibility and is derived from a full date. No migration was required. A supplied year/date mismatch is rejected; legacy year-only rows return a null `procedureDate`.
- Medication dose history: **Implemented (2026-09-25):** multi-day history for one selected medication with required `startDate` and `endDate`, ordered persisted logs only, and a 31-day inclusive maximum. No schedule-derived rows are synthesized. Dose event/status/time are persisted; descriptive medication fields come from the current editable Medication row, not a historical prescription snapshot. Read access is limited to Owner/Editor/Viewer members of the dependent's own family. No migration was needed.
- User type mapping: **Resolved from owner-provided journey and current enums (2026-09-25):** the four listed user types map to Family, Elderly/Senior, MedicalCaregiver, and CompanionCaregiver. Nurse is not a separate account/API role in the current role model and maps as a professional title/specialization under Medical Caregiver unless a later supplied screen contradicts this.

### Explicit scope deferrals

- Settings are excluded as already completed by the owner.
- Mobile route/screen identifiers and legacy Family booking screen redirect/removal decisions are deferred until the mobile navigation map or legacy screen inventory is supplied; this repository contains no client source from which to verify them.
- Care homes are owner-deferred until that UI section is supplied; no endpoint design is inferred now.
- V1 text chat details are deferred to Phase 1 so participant, entry-point, and authorization rules are set immediately before conversation design; voice/video calls are V2.
- Notification inbox/delivery decisions are deferred to Phase 2 so categories, read state, navigation targets, delivery, and preference enforcement are designed together.
- Elderly requests/reminders are deferred to Phase 3 because recipients, lifecycle, reminder limits, and permissions are not yet defined.
- Family check-in/dashboard/activity and caregiver search/booking completion decisions are deferred to Phase 4 because actor/time boundary, event coverage/visibility, ranking, availability mapping, and private contact placement need one cohesive dashboard/booking design.
- Care homes remain owner-deferred to Phase 5 until their UI is supplied.
- Library and Community are **not omitted from this audit**: supplied user/admin screens, current API absence, and gap inventories remain in scope. Their detailed content, entitlement, moderation, upload, and workflow decisions are deferred to Phase 6 before design/implementation because the roadmap schedules these features together and the screenshots leave workflow details open.
- Caregiver rating/review decisions are deferred to Phase 8. These phase assignments are explicit deferrals, not authorization to begin those implementations early.

## Closure criteria

The Family UI audit can close when every current family screen from splash through settings is mapped or explicitly marked deferred, all required owner decisions are resolved or recorded as accepted deferrals with reasons, and every API gap is assigned to an approved slice. Care homes are explicitly deferred until their roadmap section. Library and Community remain part of the Family scope and cannot be silently dropped.
