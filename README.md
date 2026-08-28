# Sanad Care API

Sanad Care (سند) is a bilingual Arabic/English healthcare and caregiving platform. This repository is the .NET 10 backend.

The active development branch is `develop`.

## Current status

The Caregivers Domain, the non-social Authentication vertical slice, the shared splash CMS, and the Caregivers **lookups** HTTP surface are implemented.

Implemented HTTP surface:

- Family / Medical Caregiver / Companion Caregiver registration
- Dual-channel email and SMS OTP verification and resend
- Email/password login with normal or restricted access
- Elderly phone + SMS OTP login
- Refresh-token rotation and reuse detection
- Session list, current logout, logout-all, and owned-session revoke
- Password reset and authenticated password change
- Shared splash screens (anonymous GET) plus admin splash CMS (multipart image create/update, publish, delete)
- Anonymous file serving at `GET /files/{key}`
- Caregiver **lookups**:
  - Public active reads for services, languages, governorates, cities, and areas
  - Admin management (create / rename / activate / deactivate / list-all) for the same lookups

Email and SMS delivery:

- Provider-neutral SMTP adapter (MailKit)
- SMS Misr adapter
- If SMTP or SMS Misr is not configured, the host keeps the development no-op senders
- SMS Misr with username, password, and sender but no template uses `POST /api/SMS/`
- SMS Misr with a template token uses `POST /api/OTP/`

Not in this repository yet:

- Caregiver onboarding commands/queries HTTP (submission, review, certificates)
- Specialization, Professional Title, and Academic Degree lookup endpoints
- Families Application / Infrastructure / HTTP endpoints
- Social / Google / Apple authentication (cancelled and removed)

## Solution layout

```text
src/
├── API/Sanad.API                         HTTP host
├── BuildingBlocks/                       Shared Domain, Application, Infrastructure
└── Modules/
    ├── Identity/                         Auth Domain, Application, Infrastructure
    ├── Cms/                              Shared splash Domain, Application, Infrastructure, HTTP
    ├── Caregivers/                       Domain complete; lookups Application/Infrastructure/HTTP live
    └── Families/                         Domain foundation; other layers are shells
tests/
├── Sanad.ArchitectureTests
└── Sanad.UnitTests
docs/
├── architecture/
├── auth/
├── users/                                App-facing (non-admin) HTTP
├── admin/                                Admin HTTP
├── operations/
└── postman/
    ├── users/
    └── admins/
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

Design-time EF migrations also require `ConnectionStrings__IdentityDatabase`.

Caregivers and CMS fall back to `ConnectionStrings__IdentityDatabase` when their own connection strings are not set.

### Optional local file storage

Uploaded splash images and service icons are stored on local disk. The default root is `{AppContext.BaseDirectory}/sanad-files`; override with:

```bash
export Storage__Local__RootPath="/var/sanad/files"
```

Files are served anonymously at `GET /files/{key}`. The upload limit is 2 MB per file (jpeg/png/webp for images).

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

```bash
dotnet ef database update \
  --project src/Modules/Identity/Infrastructure/Sanad.Modules.Identity.Infrastructure/Sanad.Modules.Identity.Infrastructure.csproj \
  --startup-project src/API/Sanad.API/Sanad.API.csproj
```

Do not generate a new removal migration. Do not rewrite historical migrations.

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

Current-session logout also requires header:

```text
X-Device-Session-Id: <device session guid>
```

A missing or invalid header returns `400` with code `Api.Auth.InvalidDeviceSessionHeader`.

## Caregiver lookup endpoints

Public reads are anonymous, active-only, and return `200 []` when empty.

| Method | Path | Access | Notes |
|---|---|---|---|
| GET | `/api/v1/lookups/services` | Anonymous | Active services, ordered by Arabic name |
| GET | `/api/v1/lookups/languages` | Anonymous | Active languages, ordered by code |
| GET | `/api/v1/lookups/governorates` | Anonymous | Active governorates |
| GET | `/api/v1/lookups/cities?governorateId={id}` | Anonymous | Active cities whose governorate is active |
| GET | `/api/v1/lookups/areas?cityId={id}` | Anonymous | Active areas whose city + governorate are active |

Admin management uses policy `CaregiversAdmin` (Normal JWT + `account_type` SuperAdmin or ContentAdmin):

| Method | Path | Success |
|---|---|---|
| POST | `/api/v1/admin/lookups/services` | 201 (multipart with icon) |
| PUT | `/api/v1/admin/lookups/services/{id}` | 200 |
| POST | `/api/v1/admin/lookups/services/{id}/activate` · `/deactivate` | 200 |
| GET | `/api/v1/admin/lookups/services` | 200 (active + inactive) |
| POST | `/api/v1/admin/lookups/languages` | 201 |
| PUT | `/api/v1/admin/lookups/languages/{id}` | 200 |
| POST | `/api/v1/admin/lookups/languages/{id}/activate` · `/deactivate` | 200 |
| GET | `/api/v1/admin/lookups/languages` | 200 (active + inactive) |
| POST | `/api/v1/admin/lookups/governorates` | 201 |
| PUT | `/api/v1/admin/lookups/governorates/{id}` | 200 |
| POST | `/api/v1/admin/lookups/governorates/{id}/activate` · `/deactivate` | 200 |
| GET | `/api/v1/admin/lookups/governorates` | 200 (active + inactive) |
| POST | `/api/v1/admin/lookups/cities` | 201 (requires active governorate) |
| PUT | `/api/v1/admin/lookups/cities/{id}` | 200 |
| POST | `/api/v1/admin/lookups/cities/{id}/activate` · `/deactivate` | 200 |
| GET | `/api/v1/admin/lookups/cities?governorateId={id}` | 200 (active + inactive) |
| POST | `/api/v1/admin/lookups/areas` | 201 (requires active city + governorate) |
| PUT | `/api/v1/admin/lookups/areas/{id}` | 200 |
| POST | `/api/v1/admin/lookups/areas/{id}/activate` · `/deactivate` | 200 |
| GET | `/api/v1/admin/lookups/areas?cityId={id}` | 200 (active + inactive) |

Lookup error codes: `Caregivers.Lookups.NameAlreadyInUse` (409), `Caregivers.Lookups.LanguageCodeInUse` (409), `Caregivers.Lookups.ParentNotActive` (409), `Caregivers.Lookups.NotFound` (404), `Caregivers.Lookups.ParentNotFound` (404).

## Important Auth rules

- Non-Elderly registration is Family (`1`), MedicalCaregiver (`2`), or CompanionCaregiver (`3`) only. Elderly cannot self-register.
- Phone numbers must be exact ASCII E.164: `+[1-9][0-9]{1,14}`.
- OTP codes must be exactly six ASCII digits.
- Password policy: 10–128 characters, at least one uppercase, one lowercase, and one number. Symbol is optional.
- Email/password login accepts email only, not phone.
- PendingVerification users receive a 15-minute restricted access token and no refresh token.
- Active users receive access + refresh tokens and a DeviceSession. Maximum five active sessions.
- Elderly login is phone + SMS OTP only. Unknown numbers do not self-register and do not reveal whether an account exists.
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
docs/architecture/overview.md
docs/auth/                              Auth flows, claims/policies, error catalog
docs/admin/                             Admin HTTP (splash, caregiver lookups)
docs/users/                             App-facing HTTP (splash, public lookups)
docs/operations/                        Configuration, migrations, security
docs/postman/admins/                    Admin Postman collection
docs/postman/users/                     App Postman collection
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
