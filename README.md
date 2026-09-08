# Sanad Care API

Sanad Care (سند) is a bilingual Arabic/English healthcare and caregiving platform. This repository is the .NET 10 backend.

The active development branch is `develop`.

## Current status

Identity (non-social auth), shared splash CMS, Caregivers (lookups, onboarding, admin review, discovery), and Families (family/dependents/invitations, assessment, medical profile, medications, notes/activities, bookings + Paymob) are implemented as Domain + Application + Infrastructure with HTTP in `Sanad.API`. Module `Presentation` projects are empty shells by design.

Implemented HTTP surface:

- Family / Medical Caregiver / Companion Caregiver registration
- Dual-channel email and SMS OTP verification and resend
- Email/password login with normal or restricted access
- Elderly phone + SMS OTP login
- Refresh-token rotation and reuse detection
- Session list, current logout, logout-all, and owned-session revoke
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
- **Bookings**: family checkout / list / detail / cancel / Paymob payment intent; caregiver list/detail (Past includes family and caregiver cancellations) + accept / decline / start / complete
- **Admin bookings** (`CaregiversAdmin`): closed bookings with `finance` filter (cancelled / failed refund / refunded) and detail
- **Paymob webhook** `POST /api/v1/payments/webhooks/paymob` (anonymous HMAC-SHA512; query, JSON `hmac`, or `X-Paymob-Hmac`; development client when Paymob is not configured)
- **Admin care assessments**: questions, tiers, submissions (`docs/admin/care-assessments.md`)

Email, SMS, and payments:

- Provider-neutral SMTP adapter (MailKit)
- SMS Misr adapter
- If SMTP or SMS Misr is not configured, the host keeps the development no-op senders
- SMS Misr with username, password, and sender but no template uses `POST /api/SMS/`
- SMS Misr with a template token uses `POST /api/OTP/`
- Paymob intention + refunds when `Paymob__SecretKey` is set; otherwise `DevelopmentPaymobClient`

Not in this repository yet:

- Family/caregiver **ratings and reviews** HTTP (caregiver `average_rating` / `reviews_count` columns exist; no review API)
- Booking cancellation **fee tiers** (cancel is allowed; no fee deducted yet)
- Social / Google / Apple authentication (cancelled and removed)

## Solution layout

```text
src/
├── API/Sanad.API                         HTTP host
├── BuildingBlocks/                       Shared Domain, Application, Infrastructure
└── Modules/
    ├── Identity/                         Auth Domain, Application, Infrastructure (Presentation shell)
    ├── Cms/                              Splash Domain, Application, Infrastructure
    ├── Caregivers/                       Lookups + onboarding + admin review + discovery
    └── Families/                         Family, dependents, assessment, meds, notes, bookings
tests/
├── Sanad.ArchitectureTests
└── Sanad.UnitTests
docs/
├── architecture/
├── auth/                                 Authentication flows, claims/policies, error catalog
├── app/                                  Mobile-app HTTP (one area per consumer inside)
│   ├── public/                           Anonymous app surfaces (splash, active lookups)
│   ├── caregivers/                       Caregiver onboarding + booking actions
│   └── families/                         Family app (dependents, discovery, bookings, …)
├── admin/                                Admin HTTP (splash, lookups, caregiver review, assessments)
├── operations/                           Configuration, migrations, security
└── postman/
    ├── admins/                           Admin Postman collection
    └── app/                              App Postman collections (Public + Caregiver + Family)
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
export ConnectionStrings__IdentityDatabase="Host=localhost;Port=5432;Database=sanad_identity;Username=REPLACE_ME;Password=REPLACE_ME"
export Identity__Jwt__Issuer="sanad-api"
export Identity__Jwt__Audience="sanad-clients"
export Identity__Jwt__SigningKey="REPLACE_WITH_AT_LEAST_32_UTF8_BYTES"
```

`Identity__Jwt__SigningKey` must contain at least 32 UTF-8 bytes or the host will not start.

Design-time EF migrations also require `ConnectionStrings__IdentityDatabase` (the Caregivers design-time factory falls back to `ConnectionStrings__CaregiversDatabase` then `IdentityDatabase`).

Caregivers, CMS, and Families fall back to `ConnectionStrings__IdentityDatabase` when their own connection strings are not set.

### CORS

A single default CORS policy currently allows any origin, header, and method (mobile development). Lock it down to known origins before production launch. It is registered in `AddSanadApi` (`AddCors`) and applied in `UseSanadApi` (`UseCors`, between authentication and authorization).

### Local file storage

Public uploads (splash images, service icons) and private uploads (caregiver certificate scans, National ID front/back) are stored on local disk:

- Public root: `{AppContext.BaseDirectory}/sanad-files`; override with `Storage__Local__RootPath="/var/sanad/files"`. Served anonymously at `GET /files/{key}`. Limit 2 MB; jpeg/png/webp.
- Private root: a sibling directory (`<root>-private`) that is **not** served statically. Certificate scans (pdf/jpeg/png/webp, 5 MB limit) are only reachable through the admin download endpoint `GET /api/v1/admin/caregivers/{id}/certificates/{certId}/file`. National ID images (jpeg/png/webp, 5 MB per side) are stored under folder `identity-documents` and are not downloadable in this slice.

### Optional SMTP

Omit this block to keep the development email sender.

```bash
export Identity__Email__Smtp__Host="REPLACE_ME"
export Identity__Email__Smtp__Port="587"
export Identity__Email__Smtp__UseSsl="true"
export Identity__Email__Smtp__Username="REPLACE_ME"
export Identity__Email__Smtp__Password="REPLACE_ME"
export Identity__Email__Smtp__FromAddress="REPLACE_ME"
export Identity__Email__Smtp__FromName="Sanad Care"
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

Identity uses PostgreSQL schema `identity`, CMS uses schema `cms`, Caregivers uses schema `caregivers`, and Families uses schema `families`. The API applies module migrations at startup (`Database.Migrate()` per module), so `dotnet ef` is not required on the server. EF history tables live per schema (for example `identity.__EFMigrationsHistory`).

Historical social-authentication migrations remain in the project and must be applied in order. The last Identity migration, `RemoveSocialAuthentication`, drops those tables.

## Run

From the repository root:

```bash
dotnet restore
dotnet build Sanad.slnx
dotnet run --project src/API/Sanad.API/Sanad.API.csproj --launch-profile https
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

See `docs/app/public/`. Postman: `docs/postman/app/Sanad.App.Public.postman_collection.json`.

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
| POST | `/caregiver/bookings/{bookingId}/decline` | Decline (family refunded) |
| POST | `/caregiver/bookings/{bookingId}/start` | Mark visit started |
| POST | `/caregiver/bookings/{bookingId}/complete` | Complete visit |

Booking actions: `docs/app/caregivers/bookings.md`. Postman: `docs/postman/app/Sanad.App.Caregiver.postman_collection.json`.

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
| POST | `/api/v1/payments/webhooks/paymob` | Paymob HMAC webhook (anonymous) |

Adding a dependent creates an Elderly Identity account server-side (no email/password, Active, phone verified) so SMS OTP login works immediately. One elderly identity is linked to at most one family. Invitations go to already-registered Family users by email with a `sanad://family/invite?token=...` deep link (7-day expiry, hashed token).

Full reference: `docs/app/families/`. Postman: `docs/postman/app/Sanad.App.Family.postman_collection.json`.

## Admin endpoints

Admin management uses policy `CaregiversAdmin` (Normal JWT + `account_type` SuperAdmin or ContentAdmin).

- Splash CMS: `docs/admin/splash-screens.md`
- Caregiver lookups (create/rename/activate/deactivate + admin list-all for all eight lookups): `docs/admin/`
- Caregiver review: `docs/admin/caregivers-review.md` — paged list (reviewer name/phone joined from Identity), detail, approve/reject/request-correction/suspend/reactivate, certificate verify/reject/revoke, private certificate file download.
- National ID review: `docs/admin/identity-documents.md` — paged list, detail, private front/back download, verify/reject/revoke.
- Care-needs assessment CMS: `docs/admin/care-assessments.md` — questions, scoring tiers, submissions.
- Bookings (cancellations & refunds): `docs/admin/bookings.md` — paged closed bookings (`finance` = all / cancelled / failed refund / refunded), detail, and `POST .../refund` to retry a failed Paymob refund.

Postman: `docs/postman/admins/Sanad.Admin.postman_collection.json`.

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
dotnet test Sanad.slnx
```

## Documentation

```text
docs/auth/                              Auth flows, claims/policies, error catalog
docs/app/public/                        Anonymous mobile-app HTTP (splash, public lookups)
docs/app/caregivers/                    Caregiver self-service onboarding HTTP
docs/app/families/                      Family app HTTP (family, dependents, invitations)
docs/admin/                             Admin HTTP (splash, lookups, caregiver review)
docs/architecture/                      Architecture notes
docs/operations/                        Configuration, migrations, security
docs/postman/Sanad.Auth.postman_collection.json
docs/postman/app/Sanad.App.Public.postman_collection.json
docs/postman/app/Sanad.App.Caregiver.postman_collection.json
docs/postman/app/Sanad.App.Family.postman_collection.json
docs/postman/admins/Sanad.Admin.postman_collection.json
docs/postman/Sanad.hostinger.postman_environment.json
docs/postman/Sanad.local.postman_environment.json
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
