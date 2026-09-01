# Sanad Care API

Sanad Care (سند) is a bilingual Arabic/English healthcare and caregiving platform. This repository is the .NET 10 backend.

The active development branch is `develop`.

## Current status

The Caregivers Domain, the non-social Authentication vertical slice, the shared splash CMS, the complete Caregivers **lookups** HTTP surface (all eight admin-managed lookups), and the complete Caregiver **onboarding** HTTP surface (self-service onboarding + admin review) are implemented.

Implemented HTTP surface:

- Family / Medical Caregiver / Companion Caregiver registration
- Dual-channel email and SMS OTP verification and resend
- Email/password login with normal or restricted access
- Elderly phone + SMS OTP login
- Refresh-token rotation and reuse detection
- Session list, current logout, logout-all, and owned-session revoke
- Password reset and authenticated password change
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

Email and SMS delivery:

- Provider-neutral SMTP adapter (MailKit)
- SMS Misr adapter
- If SMTP or SMS Misr is not configured, the host keeps the development no-op senders
- SMS Misr with username, password, and sender but no template uses `POST /api/SMS/`
- SMS Misr with a template token uses `POST /api/OTP/`

Not in this repository yet:

- Families Application / Infrastructure / HTTP endpoints
- Bookings (ratings/reviews surface also starts there)
- Social / Google / Apple authentication (cancelled and removed)

## Solution layout

```text
src/
├── API/Sanad.API                         HTTP host
├── BuildingBlocks/                       Shared Domain, Application, Infrastructure
└── Modules/
    ├── Identity/                         Auth Domain, Application, Infrastructure
    ├── Cms/                              Shared splash Domain, Application, Infrastructure, HTTP
    ├── Caregivers/                       Domain complete; lookups + onboarding + admin review HTTP live
    └── Families/                         Domain foundation; other layers are shells
tests/
├── Sanad.ArchitectureTests
└── Sanad.UnitTests
docs/
├── architecture/
├── auth/                                 Authentication flows, claims/policies, error catalog
├── app/                                  Mobile-app HTTP (one area per consumer inside)
│   ├── public/                           Anonymous app surfaces (splash, active lookups)
│   └── caregivers/                       Caregiver self-service onboarding
├── admin/                                Admin HTTP (splash, lookups, caregiver review)
├── operations/                           Configuration, migrations, security
└── postman/
    ├── admins/                           Admin Postman collection
    └── app/                              App Postman collections (Public + Caregiver)
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

Caregivers and CMS fall back to `ConnectionStrings__IdentityDatabase` when their own connection strings are not set.

### CORS

A single default CORS policy currently allows any origin, header, and method (mobile development). Lock it down to known origins before production launch. It is registered in `AddSanadApi` (`AddCors`) and applied in `UseSanadApi` (`UseCors`, between authentication and authorization).

### Local file storage

Public uploads (splash images, service icons) and private uploads (caregiver certificate scans) are stored on local disk:

- Public root: `{AppContext.BaseDirectory}/sanad-files`; override with `Storage__Local__RootPath="/var/sanad/files"`. Served anonymously at `GET /files/{key}`. Limit 2 MB; jpeg/png/webp.
- Private root: a sibling directory (`<root>-private`) that is **not** served statically. Certificate scans (pdf/jpeg/png/webp, 5 MB limit) are only reachable through the admin download endpoint `GET /api/v1/admin/caregivers/{id}/certificates/{certId}/file`.

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

Identity uses PostgreSQL schema `identity`, CMS uses schema `cms`, and Caregivers uses schema `caregivers`. The API applies module migrations at startup (`Database.Migrate()` per module), so `dotnet ef` is not required on the server. EF history tables live per schema (for example `identity.__EFMigrationsHistory`).

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

Password change and all session actions require policy `NormalAccess`: an authenticated JWT whose `access_type` claim is `Normal`. Restricted verification tokens receive 403.

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

Postman: `docs/postman/app/Sanad.App.Caregiver.postman_collection.json`.

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

Adding a dependent creates an Elderly Identity account server-side (no email/password, Active, phone verified) so SMS OTP login works immediately. One elderly identity is linked to at most one family. Invitations go to already-registered Family users by email with a `sanad://family/invite?token=...` deep link (7-day expiry, hashed token).

Postman: `docs/postman/app/Sanad.App.Family.postman_collection.json`.

## Admin endpoints

Admin management uses policy `CaregiversAdmin` (Normal JWT + `account_type` SuperAdmin or ContentAdmin).

- Splash CMS: `docs/admin/splash-screens.md`
- Caregiver lookups (create/rename/activate/deactivate + admin list-all for all eight lookups): `docs/admin/`
- Caregiver review: `docs/admin/caregivers-review.md` — paged list (reviewer name/phone joined from Identity), detail, approve/reject/request-correction/suspend/reactivate, certificate verify/reject/revoke, private certificate file download.

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
