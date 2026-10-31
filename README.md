# Ahlena API

> **Brand note:** this repository was historically branded **Sanad**. The Owner-approved full rebrand to **Ahlena** was released end-to-end on 2026-10-31: pushed as commit `f27cc1195` and deployed to the VPS as the `ahlena-api` systemd unit (`/root/ahlena-api`, `/etc/ahlena/ahlena.env`, `/var/ahlena/{keys,files,files-private}`, nginx site `ahlena-api`; public 8091 and upstream loopback 5001 unchanged). The live VPS database was cloned `SanadDb` -> `AhlenaDb` with verified identity, and the old remote `SanadDb` was dropped **after** verification; the validated backup file retained at `/var/backups/ahlena/rebrand-20261031T092843Z/` is the rollback boundary. Existing client tokens issued under the old JWT issuer/audience are invalidated by the rebrand and all users must log in again. See `docs/internal/work/ahlena-rebrand-2026-10-31.md` for the complete receipt. Historical documents, work records, and evidence throughout the repo may still reference the old brand or legacy physical names (`SanadBrunoTestDb` and `@test.sanad.local` identities remain intentionally unchanged), which later records keep as facts. The repository is `Mossad-55/Ahlena_API`; the checkout folder becomes `D:\Ahlena_API` when the parent completes the physical move (still pending at release time).

Ahlena (أهلنا) is a bilingual Arabic/English healthcare and caregiving platform. This repository is the .NET 10 backend.

The active development branch is `main`.

## Current status

Repository workflow: synchronize affected `docs/` pages and Postman requests when a business rule or API contract changes, and update this README when the HTTP surface, setup, roadmap status, or workflow changes. Follow the approved phase contract and repository governance. The five Codex role profiles remain available for distinct needs; they are not a mandatory serial relay or mandatory participants in the OpenCode workflow.

For an approved implementation phase, repository guidance prefers one OpenCode worker for the approved whole-phase scope, with the Mastermind integrating and reviewing the report. Use a Codex role profile only when it supplies a distinct needed result; do not start the historical 16-step release checklist.

Mastermind model policy: The Mastermind uses the model explicitly selected by the Owner for the active session. No particular model is mandatory by default. Existing Codex model settings and role profiles remain available as configuration; the role profiles are optional in the OpenCode workflow.

For a substantial release using the full lifecycle, record the selected workers, gates, owner actions, and handoff. For routine work, give a concise progress update and run only relevant checks.

Run Bruno for changes whose affected behavior needs an end-to-end API check, and record its results. When a worker is used, summarize its outcome concisely. Update the current-phase todo only for an active release phase.

For an authorized deployment, validate the relevant API behavior and verify the exact migration target. Stage changes by logical scope and keep private control files unstaged.

Identity (non-social auth, sessions, accounts, profile, messaging), shared CMS (splash screens, landing, legal, help center, wellness library, site settings), Caregivers (lookups, onboarding, admin review, discovery), Families (family/dependents/invitations, assessment, medical profile, medications, notes/activities, bookings + Paymob, subscriptions), CareHomes (facilities, bookings, visits, records, inventory, finance, ratings), Chat, Community, Finance (platform fees/tax, payouts, subscription refunds, complaints), and Notifications (inbox, push/email outboxes, devices) are implemented as Domain + Application + Infrastructure with HTTP in `Ahlena.API`. Module `Presentation` projects are empty shells by design; each module owns its own EF Core `DbContext` and PostgreSQL schema, composed behind the single API host.

Implemented HTTP surface:

- Family / Medical Caregiver / Companion Caregiver registration
- Dual-channel email and SMS OTP verification and resend
- Email/password login with normal or restricted access
- Elderly phone + SMS OTP login
- Refresh-token rotation and reuse detection
- Session list, current logout, logout-all, and owned-session revoke
- Account choices: list/add owned regular account types and validate dashboard switching (`/api/v1/account/accounts`, `/api/v1/account/switch`)
- Password reset and authenticated password change
- Avatar self-service (`GET`/`PUT /api/v1/auth/avatar`, Normal JWT, private storage)
- National ID self-service (`GET`/`PUT /api/v1/auth/identity-document`, Normal JWT, private storage; no file URLs)
- Admin National ID review (`GET`/`POST /api/v1/admin/identity-documents/...`, `CaregiversAdmin`; private front/back download)
- Shared splash screens (anonymous GET) plus admin splash CMS (multipart image create/update, publish, delete)
- Anonymous public file serving at `GET /files/{key}` (public assets only)
- Caregiver **lookups** (admin management + anonymous public reads) for:
  - Services, Languages, Governorates
  - Cities and Areas (parent-scoped)
  - Specializations, Professional Titles, Academic Degrees
- Caregiver **onboarding** (self-service, `CaregiverAccess` policy):
  - profile bootstrap/get, medical & companion professional profile, detailed address
  - bulk selections (services/languages/areas), medical & companion pricing
  - bulk weekly schedules (shifts/home-visit windows; companion availability windows), availability toggle
  - certificate upload/replace/remove (multipart, private storage) and submit/resubmit for review
- Caregiver **admin review** (`CaregiversAdmin` policy):
  - paged caregiver list (reviewer name/phone joined from Identity), caregiver detail
  - approve / reject / request-correction / suspend / reactivate
  - certificate verify / reject / revoke and private certificate file download
- **Families** (`FamilyAccess` policy): bootstrap/rename, dependents (incl. Elderly identity provision), invitations, medical profile, medications, notes, activity timeline, care-needs assessment
- **Discovery** (`NormalAccess`): paged Active-caregiver search, public profile, server-side price quote
- **Bookings**: family checkout / list / detail / cancel / Paymob payment intent; caregiver list/detail (Past includes family and caregiver cancellations) + accept / decline / cancel / start / complete
- **Attendance and reports**: caregiver start / complete, immutable Visit Reports, Medical Reports with private optional photos, and family report feeds
- **Admin bookings** (`CaregiversAdmin`): closed bookings with `finance` filter (cancelled / failed refund / refunded), cancellation history, detail, and failed-refund retry
- **Subscriptions**: Owner-only family catalog/current snapshot, server-side quotes, initial Card enrollment when a local plan has `paymobSubscriptionPlanId` and Card 3DS is configured, manual renewal Card/Wallet payment intents, `sub_` webhook settlement, seven-day renewal grace, original-anchor payment retry, and cancel-renewal/re-enable; Super Admin plan and family-subscription list/detail reads, plan authoring with Paymob mapping, publication/retirement, coupon configuration, and the legacy SuperAdmin-only tax adapter (`docs/admin/subscriptions.md`). Shared fee/tax configuration is documented in `docs/admin/finance-platform-charges.md`.
- **Paymob webhook** `POST /api/v1/payments/webhooks/paymob` accepts anonymous booking callbacks and subscription callbacks. Subscription callbacks require `subscription_data.id`, `trigger_type`, body `hmac` (HMAC-SHA512 over `{trigger_type}for{subscription_data.id}`), and optional `initial_transaction`/`amount_cents`/`state`/`next_billing`; renewal triggers also require `paymob_request_id`. Accepted trigger spellings are `CREATED`, `Subscription Created`, `Successful Transaction`, `Failed Transaction`, and `Failed Overdue Transaction`. Use the safe/manual provider fixture in the Public Postman collection, not an automated provider mutation request.
- **Admin care assessments**: questions, tiers, submissions (`docs/admin/care-assessments.md`)
- **Admin elderly medication operational reads**: Normal SuperAdmin/SupportAdmin prescription, persisted dose-log, and adherence reads; every successful sensitive GET writes an immutable payload-free audit row (`docs/admin/elderly-medications.md`)
- **Elderly daily check-in**: Normal Elderly `POST /api/v1/elderly/check-ins` records one final Boolean answer per profile-local IANA calendar day; same-answer retries return the saved record, opposite answers reject, and negative answers create durable in-app alerts for eligible linked Family members (`docs/app/elderly/check-in.md`)
- **Admin Elderly check-in operations**: Normal SuperAdmin/SupportAdmin read-only list, detail, timeline, and aggregate routes with filters, paging, inclusive ranges up to 31 days, safe projections, and audit-before-read behavior (`docs/admin/elderly-check-ins.md`)
- **Elderly wellness tips**: Normal Elderly published-only paged list/detail plus SuperAdmin/ContentAdmin Normal CMS list/search/detail/preview, multipart draft authoring with generated image storage keys, publish, and archive (`docs/app/elderly/wellness-tips.md`, `docs/admin/wellness-tips.md`). CMS content is currently global/shared; tenant scope and clinical review remain Needs Owner Verification.
- **Elderly profile**: Normal Elderly-only `GET /api/v1/elderly/profile` and `/photo` resolve the linked dependent from JWT identity, return bilingual names, profile-local computed age, photo state/path, latest linked assessment, stored IANA timezone, and nullable primary emergency contact (`docs/app/elderly/profile.md`). Family Owners may set a dependent timezone and add/update the primary contact; all active Family members may read the contact.
- **Notifications inbox and Admin inspection**: authenticated NormalAccess inbox list/cursor, unread count, mark-one-read, and mark-all-read routes; durable eligible-recipient check-in alerts; push device registration/list/remove at `/api/v1/devices`; and audited, one-year Admin list/detail/timeline/aggregate views for Normal SuperAdmin/SupportAdmin (`docs/app/notifications.md`, `docs/admin/notifications.md`). Admin records expose only ID/category/type/created/read metadata; recipient identity, title, body, and destination are omitted. Push (FCM via `notifications.push_outbox`) and email (SMTP via `notifications.email_outbox`, plain-text plus HTML snapshots, backed-off retries, unregistered-token retirement) are delivered by hosted outbox processors. Physical purge, the complete category/event inventory, deleted-recipient mapping, and seven-year audit-retention enforcement remain Needs Owner Verification.
- **Subscription lifecycle**: booking-completion allowance consumption is implemented; successful initial and renewal settlement creates immutable branded invoice PDFs with owner-only list/detail/download routes; provider subscription callback settlement, shared identity persistence, and seven-day grace handling are implemented; Wallet remains one-time/manual. Family plan changes are quoted read-only (`POST plan-change/quote`) and refused with `409 Subscriptions.PlanChange.PaidPeriodActive` while a paid period runs; a pending downgrade can be scheduled (`PUT pending-downgrade`) and applies at the original period boundary.
- **Care homes**: owner facility onboarding and management (`/api/v1/care-homes/facilities`), inventory, room/bed/profile media, and maintenance windows; anonymous discovery; family booking requests, extensions, check-ins, and disputes (`/api/v1/family/care-home-bookings`); owner and admin visit, resident-record, finance-reporting, payout, refund, and moderation operations (`docs/care-homes/`)
- **Ratings**: family caregiver ratings (`PUT /api/v1/family/caregiver-ratings/bookings/{bookingId}` after a completed booking, `GET .../top-10`); family care-home ratings (`PUT /api/v1/family/care-home-ratings/bookings/{bookingId}`); care-home owner reads and flags (`/api/v1/care-homes/facilities/mine/ratings`); admin caregiver, care-home, and app-feedback rating views under `/api/v1/admin/...`
- **Chat**: NormalAccess conversations, messages, attachments, blocks, reports, unread/read sync, and presence under `/api/v1/chat` (`docs/app/chat/`)
- **Community**: posts, categories, comments, likes/favorites, author photos, uploads, and blocks under `/api/v1/community`, with admin moderation (`docs/app/community/`)
- **Wellness library**: paged categories, content, video, and recommendations plus saved/liked/view tracking under `/api/v1/library`; premium-marked content requires an active subscription whose benefits include `PremiumContent` (Premium Plus tiers only); admin authoring under `/api/v1/admin/library`
- **Public site surfaces**: anonymous landing (`/api/v1/landing`), help center, legal documents, site settings, splash screens, and public lookups; anonymous `POST /api/v1/partner-contact`; platform status at `GET /api/v1/platform/status`
- **Support and feedback**: contact and ticket list/detail under `/api/v1/support`; app-store rating submission (`POST /api/v1/feedback/app-rating`)

Email, SMS, and payments:

- Provider-neutral SMTP adapter (MailKit)
- SMS Misr adapter
- If SMTP or SMS Misr is not configured, the host keeps the development no-op senders
- SMS Misr with username, password, and sender but no template uses `POST /api/SMS/`
- SMS Misr with a template token uses `POST /api/OTP/`
- Paymob intention + refunds when `Paymob__SecretKey` is set; otherwise `DevelopmentPaymobClient`

Current boundaries (source-verified):

- **Caregiver-booking cancellation refunds** follow policy revision 2 for new bookings: full refund of the captured amount at or before 24 hours before the scheduled start, 50% when later than 24 hours before the start but before the visit starts, and nothing once the visit has started. Each booking snapshots the policy version at creation; bookings made before the revision keep the legacy revision-1 rules, and reschedules preserve the original version. See the policy table in `docs/app/families/bookings.md`.
- **Care-home cancellation refund policy v2** (nothing refundable after a recorded check-in) ships dormant by Owner decision; care-home cancellations continue under the documented version-1 rule until it is activated (`docs/care-homes/booking-lifecycle.md`).
- **Premium library content** is included only in Premium Plus tiers; Free and Premium plans list it as unavailable. Non-premium library content stays readable for all.
- Physical notification purge, the complete category/event inventory, deleted-recipient mapping, and seven-year audit-retention enforcement remain open (Needs Owner Verification).
- Initial Card enrollment is available only with a local `paymobSubscriptionPlanId` and configured Card 3DS integration; Wallet remains one-time/manual; immediate plan changes are refused with `409 Subscriptions.PlanChange.PaidPeriodActive` while a paid period runs.
- Social / Google / Apple authentication (cancelled and removed)

## Solution layout

```text
src/
├── API/Ahlena.API                         HTTP host (controllers, integrations, outbox processors)
├── BuildingBlocks/                       Shared Contracts, Domain, Application, Infrastructure
└── Modules/
    ├── Identity/                         Auth, sessions, accounts, OTP, profile, messaging
    ├── Cms/                              Splash, landing, legal, help center, wellness library, site settings
    ├── Caregivers/                       Lookups, onboarding, admin review, discovery
    ├── Families/                         Family, dependents, assessment, meds, notes, bookings, subscriptions
    ├── CareHomes/                        Facilities, bookings, visits, records, inventory, finance, ratings
    ├── Chat/                             Conversations, messages, attachments, blocks
    ├── Community/                        Posts, categories, comments, moderation
    ├── Finance/                          Platform fees/tax, payouts, subscription refunds, complaints
    └── Notifications/                    In-app inbox, push/email outboxes, devices
tests/
├── Ahlena.ArchitectureTests
├── Ahlena.PortalProvisionerTests
├── Ahlena.UnitTests
└── Bruno/                                Stateful API check collections
docs/
├── admin/                                Admin HTTP guides
├── app/                                  Family/caregiver/elderly and app-wide HTTP guides
├── architecture/                         Overview and current tech-stack notes
├── auth/                                 Authentication flows, claims/policies, error catalog
├── care-homes/                           Care-home feature guides
├── frontend-handoffs/                    Dated admin/app integration handoffs
├── internal/                             Governance, operations runbooks, tools, work records
└── postman/                              24 Postman collections (admins/, app/, care-homes/) — see docs/postman/README.md
```

Dependency direction:

```text
Presentation / API  →  Application  →  Domain
Infrastructure      →  Application + Domain + BuildingBlocks.Infrastructure
Domain              →  BuildingBlocks.Domain only
```

There is no generic repository or Unit-of-Work wrapper. Handlers use the module `DbContext` directly.

## Prerequisites

- .NET 10 SDK
- PostgreSQL 16+
- Git

## Configuration

Use environment variables. Do not commit secrets, and do not put passwords in `appsettings.json`.

ASP.NET Core maps `__` to nested keys.

### Required

```bash
export ASPNETCORE_ENVIRONMENT=Development
export ConnectionStrings__IdentityDatabase="Host=localhost;Port=5432;Database=AhlenaDb;Username=REPLACE_ME;Password=REPLACE_ME"
export Identity__Jwt__Issuer="ahlena-api"
export Identity__Jwt__Audience="ahlena-clients"
export Identity__Jwt__SigningKey="REPLACE_WITH_AT_LEAST_32_UTF8_BYTES"
```

`Identity__Jwt__SigningKey` must contain at least 32 UTF-8 bytes or the host will not start.

Design-time EF migrations also require `ConnectionStrings__IdentityDatabase` (the Caregivers design-time factory falls back to `ConnectionStrings__CaregiversDatabase` then `IdentityDatabase`).

All module contexts other than Identity (Caregivers, CMS, Families, CareHomes, Chat, Community, Finance, Notifications) fall back to `ConnectionStrings__IdentityDatabase` when their own connection strings are not set.

### CORS

A single default CORS policy allows any origin, header, and method, registered in `AddAhlenaApi` (`AddCors`) and applied in `UseAhlenaApi` (`UseCors`, between authentication and authorization). Status: open CORS is the correct posture for the current development stage only; this session's rename made no runtime change to it. The production origin allowlist is deferred until the final domains are approved, at which point it becomes an explicit configuration slice. Native mobile clients legitimately send no `Origin` header because CORS is a browser-side enforcement mechanism — that is not the rationale for allowing any origin, and it does not make the open policy acceptable for production. Security rules: `docs/internal/operations/security.md`.

### Local file storage

Public assets (splash images, lookup service icons, assessment tier images, community images) and private documents (caregiver certificate scans, National ID front/back, chat attachments, wellness library videos) are stored on local disk. Limits and access are route-specific:

- Public root: `{AppContext.BaseDirectory}/sanad-files` (Development default only); outside Development configure a durable absolute path with `Storage__Local__RootPath="/var/ahlena/files"` — the host refuses to start when the root sits inside the versioned release folder. Served anonymously at `GET /files/{key}`. Default public limit 2 MB for images (splash/lookup icons/tier images/community images, jpeg/png/webp).
- Private root: a sibling directory (`<root>-private`) that is **not** served statically:
  - **Caregiver certificates** (pdf/jpeg/png/webp, 5 MB): self-service under `/api/v1/caregiver/certificates` (upload, replace file, remove, version history). The caregiver downloads their own certificate files: `GET /api/v1/caregiver/certificates/{certificateId}/file` and `GET /api/v1/caregiver/certificates/{certificateId}/versions/{versionId}/file`; admin reviewers download at `GET /api/v1/admin/caregivers/{caregiverId}/certificates/{certificateId}/file`.
  - **National ID** (jpeg/png/webp, 5 MB per side, private `identity-documents` folder): self-service `GET`/`PUT /api/v1/auth/identity-document`; the self GET returns status metadata only (uploaded, verification status, review reason — no file URLs). Admin reviewers download front/back at `GET /api/v1/admin/identity-documents/{userId}/front` and `/back`.
  - **Chat attachments**: images 5 MB, video 50 MB, PDF documents 10 MB.
  - **Wellness library** admin video uploads: 50 MB.

### Optional SMTP

Omit this block to keep the development email sender.

```bash
export Identity__Email__Smtp__Host="REPLACE_ME"
export Identity__Email__Smtp__Port="587"
export Identity__Email__Smtp__UseSsl="true"
export Identity__Email__Smtp__Username="REPLACE_ME"
export Identity__Email__Smtp__Password="REPLACE_ME"
export Identity__Email__Smtp__FromAddress="REPLACE_ME"
export Identity__Email__Smtp__FromName="Ahlena"
```

SMTP is enabled only when `Host` and `FromAddress` are both set.

### Optional SMS Misr

Omit this block to keep the development SMS sender.

```bash
export Identity__Sms__SmsMisr__Username="REPLACE_ME"
export Identity__Sms__SmsMisr__Password="REPLACE_ME"
export Identity__Sms__SmsMisr__Sender="REPLACE_ME"
export Identity__Sms__SmsMisr__Environment="2"
```

SMS Misr is enabled when `Username`, `Password`, and `Sender` are set.

- Do not set `Identity__Sms__SmsMisr__Template` for a test sender. The adapter then calls `https://smsmisr.com/api/SMS/`.
- Set `Identity__Sms__SmsMisr__Template` only after SMS Misr gives you an approved OTP template token. The adapter then calls `https://smsmisr.com/api/OTP/`.
- `Environment=2` is SMS Misr test. Use `1` only after they approve live sending.

Never put SMS Misr or SMTP credentials in a mobile app or in Git.

## Database

Each module owns its own PostgreSQL schema: `identity` (Identity), `cms`, `caregivers`, `families`, `care_homes`, `chat`, `community`, `finance`, and `notifications`. The API applies module migrations at startup (`Database.Migrate()` per module), so `dotnet ef` is not required on the server. EF history tables live per schema (for example `identity.__EFMigrationsHistory`).

Historical social-authentication migrations remain in the project and must be applied in order; `RemoveSocialAuthentication` drops those tables at its point in the sequence. The README intentionally does not pin Identity's current migration head — the applied head is verified per release (`docs/internal/operations/migrations.md`, deploy verify).

## Run

From the repository root:

```bash
dotnet restore
dotnet build Ahlena.slnx
dotnet run --project src/API/Ahlena.API/Ahlena.API.csproj --launch-profile https
```

Launch profiles:

- `https`: `https://localhost:7296` and `http://localhost:5235`
- `http`: `http://localhost:5235`

If the browser warns about the development certificate:

```bash
dotnet dev-certs https --trust
```

OpenAPI document:

```text
GET /openapi/v1.json
```

In Development, Swagger UI is at `/swagger` and reads `/openapi/v1.json`. Production does not serve `/swagger`.

## Auth endpoints

Base route: `/api/v1/auth`

| Method | Path | Access | Success |
|---|---|---|---|
| POST | `/register` | Anonymous | 201 |
| POST | `/verification/verify` | Anonymous | 200 |
| POST | `/verification/resend` | Anonymous | 200 |
| POST | `/login` | Anonymous | 200 |
| POST | `/refresh` | Anonymous | 200 |
| POST | `/elderly/request-otp` | Anonymous | 204 |
| POST | `/elderly/verify-otp` | Anonymous | 200 |
| POST | `/password/reset/request` | Anonymous | 204 |
| POST | `/password/reset` | Anonymous | 204 |
| POST | `/password/change` | Normal JWT | 204 |
| POST | `/sessions/logout` | Normal JWT + `X-Device-Session-Id` | 204 |
| POST | `/sessions/logout-all` | Normal JWT | 204 |
| GET | `/sessions` | Normal JWT | 200 |
| DELETE | `/sessions/{sessionId}` | Normal JWT | 204 |
| GET | `/avatar` | Normal JWT | 200 file |
| PUT | `/avatar` | Normal JWT, multipart `file` | 204 |
| GET | `/identity-document` | Normal JWT | 200 |
| PUT | `/identity-document` | Normal JWT, multipart `front` + `back` | 200 |

Password change, session actions, and National ID require policy `NormalAccess`: an authenticated JWT whose `access_type` claim is `Normal`. Restricted verification tokens receive 403. National ID is Family / Medical Caregiver / Companion Caregiver only; Elderly receives `409 Identity.IdentityDocument.UnsupportedAccountType`.

## App — public endpoints

Anonymous app reads (splash, active lookups) return `200 []` when empty. `caregiverType`: `1` Medical, `2` Companion.

| Method | Path | Notes |
|---|---|---|
| GET | `/api/v1/splash-screens` | Published splash screens |
| GET | `/api/v1/lookups/services` | Active services with icons |
| GET | `/api/v1/lookups/languages` | Active languages, ordered by code |
| GET | `/api/v1/lookups/governorates` | Active governorates |
| GET | `/api/v1/lookups/cities?governorateId={id}` | Active cities whose governorate is active |
| GET | `/api/v1/lookups/areas?cityId={id}` | Active areas whose city + governorate are active |
| GET | `/api/v1/lookups/specializations` | Active specializations (both types; carries `caregiverType`) |
| GET | `/api/v1/lookups/professional-titles` | Active Medical professional titles |
| GET | `/api/v1/lookups/academic-degrees` | Active Medical academic degrees |

See `docs/app/public/`. Postman: `docs/postman/app/Ahlena.App.Public.postman_collection.json`.

## App — caregiver onboarding endpoints

Self-service routes under `/api/v1/caregiver/...` require policy `CaregiverAccess` (Normal JWT with `account_type` MedicalCaregiver or CompanionCaregiver). Restricted-verification tokens receive 403. Full reference: `docs/app/caregivers/`.

| Method | Path | Notes |
|---|---|---|
| POST | `/caregiver/profile` | Bootstrap (no body); 201, 409 if already exists |
| GET | `/caregiver/profile` | Full own state; 404 until bootstrapped |
| PUT | `/caregiver/profile/medical` | Medical professional profile |
| PUT | `/caregiver/profile/companion` | Companion professional profile |
| PUT | `/caregiver/profile/address` | Detailed address |
| PUT | `/caregiver/selections` | Bulk services/languages/areas |
| PUT | `/caregiver/pricing/medical` | Four medical prices |
| PUT | `/caregiver/pricing/companion` | Three companion prices |
| PUT | `/caregiver/schedule/medical` | Shifts + home-visit windows (bulk) |
| PUT | `/caregiver/schedule/companion` | Availability windows (bulk) |
| POST | `/caregiver/availability/available` | Active + compliant only |
| POST | `/caregiver/availability/unavailable` | Always allowed |
| POST | `/caregiver/certificates` | Multipart add (Medical only, ≤5 MB) |
| PUT | `/caregiver/certificates/{certificateId}/file` | Multipart replace |
| DELETE | `/caregiver/certificates/{certificateId}` | Additional certificates only |
| POST | `/caregiver/submit` | Submit (Onboarding) / resubmit (NeedsCorrection) |
| GET | `/caregiver/bookings?tab=` | Own bookings (Past includes family and caregiver cancellations) |
| GET | `/caregiver/bookings/{bookingId}` | Own booking detail |
| POST | `/caregiver/bookings/{bookingId}/accept` | Accept paid booking awaiting approval |
| POST | `/caregiver/bookings/{bookingId}/decline` | Decline (family refund attempted) |
| POST | `/caregiver/bookings/{bookingId}/cancel` | Cancel an accepted visit before start; policy-controlled refund |
| POST | `/caregiver/bookings/{bookingId}/start` | Mark visit started |
| POST | `/caregiver/bookings/{bookingId}/complete` | Complete visit |
| POST | `/caregiver/bookings/{bookingId}/visit-report` | Submit one immutable Visit Report |
| POST | `/caregiver/bookings/{bookingId}/medical-report` | Medical caregiver report with optional private photo |

Booking actions: `docs/app/caregivers/bookings.md`. Postman: `docs/postman/app/Ahlena.App.Caregiver.postman_collection.json`.

## App — family endpoints

Family routes under `/api/v1/family/...` require policy `FamilyAccess` (Normal JWT with `account_type` Family). Within a family, access is role-based (Owner/Editor/Viewer). Full reference: `docs/app/families/`.

| Method | Path | Notes |
|---|---|---|
| POST | `/family` | Bootstrap family; 201, 409 if already exists |
| GET | `/family` | Family + members; 404 until bootstrapped |
| PUT | `/family/name` | Rename (Owner only) |
| POST | `/family/dependents` | Add elderly dependent (multipart); provisions Elderly login |
| GET | `/family/dependents` | List dependents |
| GET | `/family/dependents/{id}` | One dependent |
| PUT | `/family/dependents/{id}` | Update dependent profile |
| DELETE | `/family/dependents/{id}` | Remove dependent (hard delete; Identity user kept) |
| PUT | `/family/dependents/{id}/photo` | Set/replace private photo (multipart, ≤5 MB) |
| GET | `/family/dependents/{id}/photo` | Authorized photo download (members only) |
| POST | `/family/invitations` | Invite by email (Owner/Editor) |
| GET | `/family/invitations` | My pending invitations |
| POST | `/family/invitations/accept` | Accept by token (invitee) |
| POST | `/family/invitations/decline` | Decline by token (invitee) |
| DELETE | `/family/invitations/{id}` | Revoke (Owner) |
| GET/PUT | `/family/dependents/{id}/medical-profile` | Chronic conditions, allergies, history |
| GET/POST | `/family/assessment/...` | Care-needs quiz questions, tiers, submit |
| * | `/family/dependents/{id}/medications...` | Medication inventory, stock, doses |
| * | `/family/dependents/{id}/notes` | Care notes |
| GET | `/family/dependents/{id}/activities` | Activity timeline |
| GET | `/api/v1/caregivers` | Discovery search (Normal JWT) |
| GET | `/api/v1/caregivers/{id}` | Public caregiver profile |
| GET | `/api/v1/caregivers/{id}/quote` | Server-side price quote |
| POST | `/family/bookings/checkout` | Create booking + price snapshot (Owner/Editor) |
| GET | `/family/bookings` | Tab list |
| GET | `/family/bookings/{id}` | Booking detail |
| POST | `/family/bookings/{id}/payments/intent` | Paymob mobile-SDK handoff |
| POST | `/family/bookings/{id}/cancel` | Cancel |
| GET | `/family/reports?type=visit\|medical\|all` | Paged family report feed |
| GET | `/family/reports/{reportId}/photo` | Authorized private medical photo |
| GET | `/family/subscriptions/plans` | Published plan catalog (Owner only) |
| GET | `/family/subscriptions/current` | Current stored subscription snapshot (Owner only) |
| POST | `/family/subscriptions/quote` | Server-calculated fee/tax-inclusive purchase quote (Owner only; read-only) |
| POST | `/family/subscriptions/payment-intent` | Start initial Card/Wallet checkout from a server quote (Owner only) |
| POST | `/family/subscriptions/cancel-renewal` | Disable auto-renew (Owner only) |
| POST | `/family/subscriptions/reenable-auto-renew` | Re-enable auto-renew before period end (Owner only) |
| POST | `/api/v1/payments/webhooks/paymob` | Paymob HMAC webhook (anonymous) |

Adding a dependent creates an Elderly Identity account server-side (no email/password, Active, phone verified) so SMS OTP login works immediately. One elderly identity is linked to at most one family. Invitations go to already-registered Family users by email with a `ahlena://family/invite?token=...` deep link (7-day expiry, hashed token).

Full reference: `docs/app/families/`. Postman: `docs/postman/app/Ahlena.App.Family.postman_collection.json`.

## Admin endpoints

Most admin management uses policy `CaregiversAdmin` (Normal JWT + `account_type` SuperAdmin or ContentAdmin).
Subscription plan and coupon configuration uses the separate `SubscriptionPlanAdmin` policy and is
Super Admin-only with a Normal JWT. See `docs/admin/subscriptions.md`.

- Splash CMS: `docs/admin/splash-screens.md`
- Caregiver lookups (create/rename/activate/deactivate + admin list-all for all eight lookups): `docs/admin/`
- Caregiver review: `docs/admin/caregivers-review.md` — paged list (reviewer name/phone joined from Identity), detail, approve/reject/request-correction/suspend/reactivate, certificate verify/reject/revoke, private certificate file download.
- National ID review: `docs/admin/admin-identity-documents.md` — paged list, detail, private front/back download, verify/reject/revoke.
- Care-needs assessment CMS: `docs/admin/care-assessments.md` — questions, scoring tiers, submissions.
- Bookings (cancellations & refunds): `docs/admin/bookings.md` — paged closed bookings (`finance` = all / cancelled / failed refund / refunded), detail, and `POST .../refund` to retry a failed Paymob refund.
- Subscriptions: `docs/admin/subscriptions.md` — Super Admin plan list/detail, family subscription list/detail, plan authoring including optional Paymob mapping, one-time publication, retirement, coupon create/list/detail/hard-delete configuration, and the legacy tax adapter; shared fee/tax configuration is in `docs/admin/finance-platform-charges.md`. Family owners can request server-calculated purchase and plan-change quotes (read-only); immediate plan changes are refused with `409 Subscriptions.PlanChange.PaidPeriodActive` while a paid period runs, and a pending next-renewal downgrade applies at the original period boundary. Initial Card enrollment when configured, manual renewal payment intents, Paymob callback settlement with durable identity/idempotency, renewal-grace recovery, anchor-preserving payment retry, and immutable invoice list/detail/private PDF downloads are implemented, and booking-completion allowance consumption follows the plan.
- Elderly medication operations: `docs/admin/elderly-medications.md` — SuperAdmin and SupportAdmin with Normal access can list/detail prescriptions, inspect persisted dose timelines (inclusive ranges up to 31 days), and read persisted-dose adherence aggregates. No write APIs; successful sensitive reads audit before returning data. Postman/Bruno examples are manual/stateful and must not run against shared/live environments.

Postman: `docs/postman/admins/Ahlena.Admin.postman_collection.json`; shared Finance configuration: `docs/postman/admins/Ahlena.Admin.Finance.postman_collection.json`.

Lookup error codes: `Caregivers.Lookups.NameAlreadyInUse` (409), `Caregivers.Lookups.LanguageCodeInUse` (409), `Caregivers.Lookups.ParentNotActive` (409), `Caregivers.Lookups.NotFound` (404), `Caregivers.Lookups.ParentNotFound` (404).

## Important Auth rules

- Non-Elderly registration is Family (`1`), MedicalCaregiver (`2`), or CompanionCaregiver (`3`) only. Elderly cannot self-register.
- Phone numbers must be exact ASCII E.164: `+[1-9][0-9]{1,14}`.
- OTP codes must be exactly six ASCII digits.
- Password policy: 10–128 characters, at least one uppercase, one lowercase, and one number. Symbol is optional.
- Email/password login accepts email only, not phone.
- PendingVerification users receive a 15-minute restricted access token and no refresh token.
- Active users receive access + refresh tokens and a DeviceSession. Maximum five active sessions.
- Elderly login is phone + SMS OTP only. Unknown numbers do not self-register and do not reveal whether an account exists. Elderly accounts are created server-side when a family adds a dependent (no email/password; Active + phone-verified, so OTP login works immediately). See `docs/app/families/dependents.md`.
- Password reset request is non-enumerating and always returns 204.
- Successful password reset or change revokes every refresh session.
- National ID is a separate authenticated multipart endpoint (`GET`/`PUT /api/v1/auth/identity-document`). Do not send ID files on `POST /register`. Replacing an ID while Active returns the user to PendingVerification and revokes sessions.
- Development senders do not deliver codes. The API never returns the raw OTP.

Details live in `docs/`.

## Tests

```bash
dotnet test Ahlena.slnx
```

## Documentation

```text
docs/auth/                              Auth flows, claims/policies, error catalog
docs/app/                               App HTTP guides (public, caregivers, families, elderly, chat, community, library)
docs/admin/                             Admin HTTP guides
docs/care-homes/                        Care-home feature guides
docs/architecture/                      Architecture overview and current tech-stack notes
docs/frontend-handoffs/                 Dated admin/app integration handoffs
docs/internal/                          Governance, operations runbooks, tools, work records
docs/postman/                           24 Postman collections (admins/, app/, care-homes/) and environments
```

## Security

Never commit:

- GitHub tokens
- JWT signing keys
- Database passwords
- SMS/email provider credentials
- Production connection strings
- `.env` contents
- Access, refresh, or OTP plaintext

Use placeholders in docs and examples.
