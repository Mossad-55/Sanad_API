# Architecture overview

Sanad Care is a modular Clean Architecture / DDD backend on .NET 10.

## Modules

```text
BuildingBlocks
Identity
Families
Caregivers
```

Each business module is split into Domain, Application, Infrastructure, and Presentation. HTTP composition currently lives in `Sanad.API`. Identity Presentation is a shell.

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

## Identity persistence

- PostgreSQL schema `identity`
- Aggregate roots: `User`, `VerificationRequest`, `DeviceSession`
- Historical social-authentication migrations remain immutable
- `RemoveSocialAuthentication` is the last Identity migration

## Authentication host

- Thin `AuthController` at `/api/v1/auth`
- JWT Bearer authentication
- Named policy `NormalAccess` requires JWT claim `access_type=Normal`
- Problem Details from `ResultProblemDetailsMapper`
- OpenAPI at `/openapi/v1.json`
- Swagger UI at `/swagger` in Development only

## Email and SMS

Application depends only on `IEmailSender` and `ISmsSender`.

| Configuration | Runtime |
|---|---|
| SMTP `Host` + `FromAddress` set | `SmtpEmailSender` |
| Otherwise | `DevelopmentEmailSender` (no-op) |
| SMS Misr username + password + sender, no template | `SmsMisrSmsSender` → `POST /api/SMS/` |
| Same + template | `SmsMisrSmsSender` → `POST /api/OTP/` |
| SMS Misr not configured | `DevelopmentSmsSender` (no-op) |

Credentials stay in environment variables. They are never committed.

## Current product boundary

Implemented: Identity Auth vertical slice.

Not implemented: Caregivers Application/Infrastructure/endpoints, Families Application/Infrastructure/endpoints, bookings, payments, and later modules.
