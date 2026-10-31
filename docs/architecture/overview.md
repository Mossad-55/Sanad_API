# Architecture overview

Ahlena (أهلنا) is a **modular monolith**: one .NET 10 HTTP host composing nine bounded modules built with Clean Architecture / DDD layering. It is not a microservice deployment and does not use CQRS-with-separate-stores — MediatR-based command/query separation lives inside each module, and every module persists to its own PostgreSQL schema in a single database. This overview reflects the inspected source on 2026-10-31; see `current-api-tech-stack-2026-10-29.md` for the detailed stack record.

```text
┌─────────────────  one deployable host: Ahlena.API  ─────────────────┐
│  controllers (per business area) + integration hosts (care homes,  │
│  caregivers, chat, families, notifications) + hosted push/email     │
│  outbox processors (FCM send, SMTP send, token retirement)          │
└──────────────────────────────┬──────────────────────────────────────┘
                               │ composes in one process
     ┌─────────┬────────┬───────┼────────┬──────────┬─────────┬─────────┬────────────┐
     │Identity│   Cms  │Caregivers│Families│CareHomes│  Chat   │Community│ Finance │Notifications│
     └────┬────┴───┬────┴────┬─────┴───┬────┴────┬────┴────┬────┴────┬────┴─────┬─────┘
          │  each module: Domain ← Application ← Infrastructure   │
          │              (own DbContext + own schema)             │
          ▼
┌────────────── one PostgreSQL database ──────────────┐
│ schemas: identity · cms · caregivers · families ·   │
│ care_homes · chat · community · finance ·           │
│ notifications                                       │
└─────────────────────────────────────────────────────┘
```

## Modules (9)

| Module | Responsibility |
|---|---|
| `Identity` | Non-social auth: registration, email/SMS OTP, JWT + refresh rotation, device sessions, account choices, avatars, National ID, admin user management, support inbox |
| `Cms` | Shared content: splash screens, landing, legal documents, help center, elderly welcome, wellness library, site settings, sentence-builder catalog |
| `Caregivers` | Lookups, caregiver onboarding, admin review, discovery, earnings/payout accounts, care-record access for caregivers |
| `Families` | Families, dependents, invitations, assessment, medical profiles, medications, notes/activities, care records, caregiver bookings + Paymob, subscriptions and invoices |
| `CareHomes` | Facility onboarding, bookings, visits, check-ins/disputes, resident records, inventory, finance/ratings |
| `Chat` | Conversations, messages, attachments, blocks, reports, presence |
| `Community` | Posts, categories, comments, likes/favorites, moderation |
| `Finance` | Platform fee/tax configuration, caregiver payouts and policies, subscription refunds, complaint reviews, duplicate-charge claims |
| `Notifications` | In-app inbox, preference lookups, push/email outbox stores, admin inspection reads |

Each module is split into Domain, Application, and Infrastructure. HTTP composition lives in `Ahlena.API` (controllers plus per-module integration folders/adapters inside the API host, such as `CareHomesIntegration`, `CaregiversIntegration`, and `NotificationsIntegration`, which host the push/email outbox processors). Module `Presentation` projects are empty shells by design.

## Dependency direction

```text
Ahlena.API / Presentation  →  Application  →  Domain
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

One PostgreSQL database; one schema per module context (connection strings fall back to `ConnectionStrings:IdentityDatabase` when a module's own is unset):

| Module | Schema | Notes |
|---|---|---|
| Identity | `identity` | Users, verification requests, device sessions, support inbox. Historical social-auth migrations are retained and applied in order; Identity's current migration head is verified per release (`docs/internal/operations/migrations.md`) and not pinned here. |
| Cms | `cms` | Splash, landing, legal/help, wellness library, site settings |
| Caregivers | `caregivers` | Profiles, lookups, certificates, schedules, pricing, ratings |
| Families | `families` | Families, dependents, invitations, assessment, medications, notes, bookings, subscriptions, invoices |
| CareHomes | `care_homes` | Facilities, bookings, visits, records, inventory, ratings, payouts |
| Chat | `chat` | Conversations, messages, attachments, blocks |
| Community | `community` | Posts, categories, comments, reactions, moderation |
| Finance | `finance` | Fee/tax configuration, payouts, subscription refunds, complaints, duplicate-charge claims |
| Notifications | `notifications` | In-app notifications, `push_outbox` (FCM), `email_outbox` (SMTP, plain-text + HTML snapshots) |

The host applies `Database.Migrate()` per module at startup.

## Authentication host

- Thin `AuthController` at `/api/v1/auth`
- JWT Bearer authentication; session-bound access where applicable
- Named policies registered in `AddAhlenaApi`, including:
  - `NormalAccess`, `SessionBoundAccess` — Normal JWT basics
  - `FamilyAccess`, `CaregiverAccess`, `ElderlyAccess`, `CareHomeOwnerAccess` — role-scoped consumer access
  - `CaregiversAdmin`, `CaregiverLookupAdmin`, `CaregiverReviewAdmin`, `CmsContent`, `CommunityModeration`, `ChatSupportAdmin`, `SubscriptionPlanAdmin`, `SuperAdminOnly` — management and content administration
  - Operational read/manage policies for notifications, support inbox, finance, payouts, refunds, platform controls, elderly medication/check-in/help-request/SOS operations, and medical-access auditing (e.g. `AdminNotificationOperationalRead`, `OperationalReadAdmin`, `FinanceOperationalAdmin`, `PayoutOperationalAdmin`, `RefundWorkflowAdmin`, `PlatformControlsAdmin`, `ElderlyMedicationOperationalRead`/`Manage`, `ElderlyHelpRequestOperational`, `ElderlySosOperational`, `MedicalAccessAuditAdmin`, `CmsHelpContact`)
- Problem Details from `ResultProblemDetailsMapper`
- OpenAPI at `/openapi/v1.json`
- Swagger UI at `/swagger` in Development only

## Email, SMS, push, and payments

Application depends on abstractions only: `IEmailSender`, `ISmsSender`, notification gateways, and a Paymob client abstraction.

| Configuration | Runtime |
|---|---|
| SMTP `Host` + `FromAddress` set | `SmtpEmailSender` |
| Otherwise | development no-op email sender |
| SMS Misr username + password + sender, no template | `SmsMisrSmsSender` → `POST /api/SMS/` |
| Same + template | `SmsMisrSmsSender` → `POST /api/OTP/` |
| SMS Misr not configured | development no-op SMS sender |
| FCM configured | push delivery via `notifications.push_outbox` and the hosted `PushOutboxProcessor` |
| Paymob `SecretKey` set | `PaymobClient` |
| Paymob not configured | `DevelopmentPaymobClient` |

Notification email is delivered asynchronously through `notifications.email_outbox` (status/retry/backoff, HTML snapshots); OTP and invitation mail keep the direct Identity send path.

Webhook: `POST /api/v1/payments/webhooks/paymob` (anonymous HMAC-SHA512). Credentials stay in environment variables and are never committed.

## Current product boundary

Implemented (summary; the root README carries the HTTP-surface catalog):

- Identity auth vertical slice (no social login) with sessions, account choices, avatars, National ID
- CMS: splash, landing, legal, help center, wellness library, site settings; app feedback (app ratings) and anonymous partner contact; platform status
- Caregivers: lookups, onboarding, admin review, discovery, ratings
- Families: full dependent-care surface, bookings with Paymob, refund-policy-v2 snapshots, subscriptions with invoices, plan-change rules (409 `Subscriptions.PlanChange.PaidPeriodActive` during a paid period; deferred pending downgrade)
- CareHomes: discovery through finance, visits, check-in disputes, ratings (refund policy v2 dormant — Owner decision)
- Chat, Community with moderation
- Finance: payouts, subscription refunds, complaints, duplicate-charge claims
- Notifications: inbox, preferences, push/email outbox delivery with hosted processors

Open (Needs Owner Verification, not "deferred work in flight"):

- Physical notification purge, complete category/event inventory, deleted-recipient mapping, seven-year audit-retention enforcement
- Activation of the dormant care-home refund policy v2
- Social / Google / Apple authentication (cancelled and removed)

Operational note: the last deployment rehearsed end-to-end was revision `b861e3e5` under the previous brand; the current local tree — including the rebrand — is uncommitted and not deployed.
