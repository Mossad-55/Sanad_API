# Architecture overview

Sanad Care is a modular Clean Architecture / DDD backend on .NET 10.

## Modules

```text
BuildingBlocks
Identity
Cms
Caregivers
Families
```

Each business module is split into Domain, Application, Infrastructure, and Presentation. HTTP composition currently lives in `Sanad.API`. Module Presentation projects are empty shells (Identity, Caregivers, Families).

## Dependency direction

```text
Sanad.API / Presentation  →  Application  →  Domain
Infrastructure            →  Application + Domain + BuildingBlocks.Infrastructure
Domain                    →  BuildingBlocks.Domain only
```

Domain must not reference EF Core, ASP.NET Core, MediatR, MailKit, SMS Misr, or Infrastructure.

## Application style

- CQRS with MediatR
- FluentValidation before handlers
- Application `Result` / `Result<T>`
- Direct EF Core `DbContext` use
- No generic repository
- No extra Unit-of-Work wrapper

## Persistence

| Module | PostgreSQL schema | Notes |
|---|---|---|
| Identity | `identity` | Aggregates: `User`, `VerificationRequest`, `DeviceSession`. Historical social-auth migrations stay immutable; `RemoveSocialAuthentication` is the last Identity migration. |
| Cms | `cms` | Splash screens |
| Caregivers | `caregivers` | Profiles, lookups, certificates, schedules, pricing. `average_rating` / `reviews_count` columns exist; no review HTTP yet. |
| Families | `families` | Family, members, elderly dependents, invitations, assessment, medications, notes, bookings |

CMS, Caregivers, and Families fall back to `ConnectionStrings:IdentityDatabase` when their own connection string is unset. The host runs `Database.Migrate()` per module at startup.

## Authentication host

- Thin `AuthController` at `/api/v1/auth`
- JWT Bearer authentication
- Named policies in `AddSanadApi`:
  - `NormalAccess` — JWT `access_type=Normal`
  - `CaregiverAccess` — Normal + MedicalCaregiver or CompanionCaregiver
  - `FamilyAccess` — Normal + Family
  - `CmsContent` / `CaregiversAdmin` — Normal + SuperAdmin or ContentAdmin
- Problem Details from `ResultProblemDetailsMapper`
- OpenAPI at `/openapi/v1.json`
- Swagger UI at `/swagger` in Development only

## Email, SMS, and payments

Application depends only on `IEmailSender` and `ISmsSender`. Families bookings depend on a Paymob client abstraction.

| Configuration | Runtime |
|---|---|
| SMTP `Host` + `FromAddress` set | `SmtpEmailSender` |
| Otherwise | `DevelopmentEmailSender` (no-op) |
| SMS Misr username + password + sender, no template | `SmsMisrSmsSender` → `POST /api/SMS/` |
| Same + template | `SmsMisrSmsSender` → `POST /api/OTP/` |
| SMS Misr not configured | `DevelopmentSmsSender` (no-op) |
| Paymob `SecretKey` set | `PaymobClient` |
| Paymob not configured | `DevelopmentPaymobClient` |

Webhook: `POST /api/v1/payments/webhooks/paymob` (anonymous HMAC-SHA512; query, body `hmac`, or `X-Paymob-Hmac`). `503` if `Paymob__HmacSecret` is unset.

Credentials stay in environment variables. They are never committed.

## Current product boundary

Implemented:

- Identity auth vertical slice (no social login)
- CMS splash (public + admin)
- Caregiver lookups (admin + anonymous public reads)
- Caregiver onboarding and admin review
- Families: bootstrap, dependents, invitations, medical profile, medications, notes/activities, care assessment
- Caregiver discovery (authenticated) and server-side quotes
- Bookings lifecycle + Paymob payment intent and webhook
- Caregiver booking list/detail (including family and caregiver cancellations)
- Admin closed-booking list (cancelled, failed refund, refunded)

Not implemented yet:

- Ratings / reviews HTTP
- Booking cancellation fee tiers
- Social / Google / Apple authentication (cancelled and removed)
