# Ahlena Care API — technology, architecture and improvements

**Date:** 2026-10-29

**Audience:** Project Owner, developers and technical stakeholders

**Purpose:** Explain the current API, its technology stack, architecture, business responsibilities, improvements and limitations in plain English.

This document records the overview discussed with the Owner. It is grounded in the inspected source, project/package files and recorded release evidence. Package versions are repository-declared versions, not a claim that they are the latest available. Historical test results are not a fresh test run on this date. Accuracy note (2026-10-31): the root README, architecture overview, documentation index, and feature guides have been reconciled against the current source; where this document predates those updates it describes the state at its inspection date. The rebrand (Sanad → Ahlena) was committed and released from this repository on 2026-10-31; release/deployment receipts live in `docs/internal/work/ahlena-rebrand-2026-10-31.md`.

## Executive summary

**Ahlena Care's API is a C#/.NET 10 backend for a bilingual Arabic/English caregiving platform.** It serves the mobile applications, admin dashboard, care-home dashboard and public landing website.

Its architecture is a **modular monolith using Clean Architecture, domain-driven design principles and CQRS**.

In plain English: the API is the platform's business engine and source of truth, not simply a collection of endpoints connected to tables.

## 1. Purpose of the API

The frontend displays screens. The API decides:

- Who may access or change information.
- Whether a caregiver or care home is approved.
- What a booking costs.
- Whether someone can book, cancel or receive a refund.
- Which subscription benefits a family has.
- Which documents are current, pending or historical.
- Who receives a notification.
- What financial and medical activity must be recorded.

### Main users

| User | Main purpose |
|---|---|
| Family | Manage elderly dependents, find care, book services and monitor care |
| Elderly person | Check-ins, care records, medication-related features and assistance |
| Medical caregiver | Professional profile, certificates, availability and care delivery |
| Companion caregiver | Profile, availability, bookings and companionship services |
| Care-home owner | Facility onboarding, documents, rooms, residents and bookings |
| Super Admin | Broad platform administration |
| Support Admin | Authorized support and operational work |
| Finance Admin | Authorized financial and payout operations |
| Content Admin | Authorized content management |

Permissions are assigned per operation. Being an admin does not automatically grant access to every operation.

## 2. Core technology stack

| Area | Technology | Purpose |
|---|---|---|
| Programming language | C# | Application and business logic |
| Runtime | .NET 10 | Runs the backend |
| Web framework | ASP.NET Core 10 | HTTP endpoints, middleware, authentication and hosting |
| Application web server | Kestrel | Receives API connections |
| API style | Controller-based HTTP/JSON API | Routes such as `/api/v1/...` |
| Database | PostgreSQL | Persistent business data |
| Database access | Entity Framework Core 10.0.11 | Mapping, queries, updates and migrations |
| PostgreSQL provider | Npgsql EF Core 10.0.0 | Connects EF Core to PostgreSQL |
| Request dispatch | MediatR 14.2.0 | Sends commands and queries to handlers |
| Validation | FluentValidation 12.1.1 | Checks request/business inputs |
| JSON | System.Text.Json | Serializes requests and responses |
| Authentication | JWT Bearer | Authenticated API access |
| Password hashing | ASP.NET Core Identity password hasher | Stores password hashes, not plaintext passwords |
| Real-time communication | ASP.NET Core SignalR | Chat updates, presence and typing |
| API specification | Microsoft.AspNetCore.OpenApi 10.0.11 | Generates the OpenAPI contract |
| API explorer | Swagger UI 10.2.3 | Interactive API exploration; configured for Development |
| Push notifications | Firebase Cloud Messaging; FirebaseAdmin 3.7.0 | Device notifications |
| Email | SMTP through MailKit/MimeKit 4.17.0 | Email delivery |
| SMS | SMS Misr integration | SMS and OTP delivery |
| Payments | Paymob HTTP integrations | Checkout, subscription operations and refunds |
| Financial validation | IbanNet 6.2.0 | IBAN validation |
| File storage | Local disk behind a storage abstraction | Public media and private documents |

The API project targets `net10.0` and enables nullable reference types. Package versions are managed centrally in `Directory.Packages.props`, helping prevent inconsistent versions between projects. The README specifies PostgreSQL 16+ as a prerequisite; this overview does not independently establish the server's exact PostgreSQL version.

## 3. Architecture

### 3.1 Modular monolith

One deployable API application is divided into separate business modules:

```text
Mobile apps / portals / landing website
                   |
                   v
           ASP.NET Core API
                   |
     +-------------+--------------+
     | Identity                   |
     | Families                   |
     | Caregivers                 |
     | Care Homes                 |
     | Finance                    |
     | Notifications              |
     | Community                  |
     | Chat                       |
     | CMS                        |
     +-------------+--------------+
                   |
               PostgreSQL
```

Benefits:

- Simpler deployment than many independent services.
- Business areas remain organized.
- Fewer distributed-system problems.
- Modules can evolve without placing everything in one giant application layer.

**This is not a microservices deployment.** Separate modules do not mean separate servers or independently deployed services.

### 3.2 Clean Architecture

| Layer | Responsibility |
|---|---|
| Domain | Business entities, rules and state transitions |
| Application | Use cases such as creating a booking, verifying a certificate or listing documents |
| Infrastructure | Database access, file storage and external integrations |
| API | HTTP requests, authentication, responses and application composition |

Typical request flow:

```text
HTTP request
    -> Controller
    -> Validation and command/query handler
    -> Domain rules
    -> Database or external provider
    -> HTTP response
```

The domain is deliberately independent of ASP.NET Core and EF Core. Dependency direction is generally:

```text
API / Presentation -> Application -> Domain
Infrastructure -> Application + Domain + shared infrastructure
Domain -> shared domain building blocks
```

Some module Presentation projects are empty shells; actual HTTP controllers are composed in `Ahlena.API`.

### 3.3 Domain-driven design principles

Important business concepts are represented explicitly, including `CareHomeBooking`, `FamilySubscription`, `CaregiverCertificate` and `CareHomeFacility`.

These entities enforce business rules rather than allowing arbitrary status changes. For example:

- Expired certificates must not pass the relevant compliance checks.
- Current documents cannot be removed through history-delete operations.
- A refund cannot be declared complete merely because a request returned an ID.

Strongly typed IDs and value objects reduce accidental misuse of business values.

### 3.4 CQRS

Read operations and change operations have separate handlers:

- Query: get a family's sent invitations.
- Command: revoke an invitation.
- Query: get certificate history.
- Command: approve a certificate version.

MediatR dispatches requests and FluentValidation validates them. Application operations use `Result` / `Result<T>` outcomes. Module database access is exposed through context abstractions without a generic repository layer.

CQRS here does **not** mean separate read/write databases or event sourcing.

## 4. Business modules and implemented responsibilities

### Identity

- Registration and login.
- Email/SMS verification flows.
- Elderly phone/OTP access.
- Password reset and change.
- Access and refresh tokens.
- Refresh-token rotation and reuse detection.
- Session listing, logout and revocation.
- Account types and dashboard switching.
- Avatars and identity documents.
- Administrative account operations.

Actual email/SMS delivery requires the corresponding external-provider configuration.

### Families

- Families and membership.
- Elderly dependents.
- Received and sent invitations.
- Medical profiles and care assessments.
- Medications and dose logs.
- Daily check-ins and related operational data.
- Notes, activities, SOS/help features.
- Care records.
- Caregiver bookings.
- Family subscriptions and invoices.

### Caregivers

- Medical and companion profiles.
- Professional qualifications and lookup selections.
- Service areas, languages and pricing.
- Schedules and availability.
- Onboarding and administrative review.
- Discovery/search.
- Certificates, verification and revocation.
- Current/pending/history certificate versions.
- Private certificate downloads and history removal.

### Care Homes

- Facility onboarding and profile revisions.
- Administrative review.
- Documents and license expiry.
- Rooms, room types, beds and related inventory.
- Availability and maintenance.
- Visits and bookings.
- Resident-related workflows.
- Care-home care records.
- Refund lifecycle.
- Current/pending/history document views and protected history removal.

### Finance

- Shared fee/tax configuration.
- Duplicate-charge claims.
- Refund budgets and execution controls.
- Complaint/refund-review operations.
- Payout-related operations.
- Platform payment/maintenance controls.

Not every financial operation writes to one universal ledger. Care-home refund state is tracked on its booking; subscription refund ledgers have a different scope.

### CMS

- Splash content.
- Landing sections and site settings.
- FAQs/help content.
- Legal documents.
- Library and premium content.
- Other managed content surfaces.

The backend supplies publishing tools; content teams supply and approve content.

### Notifications

- Persisted inbox notifications.
- Read/unread state.
- Preferences.
- Device tokens and push delivery.
- Email/push outbox processing.
- Recipient-language selection.
- Administrative inspection.

### Community

- Posts, comments and replies.
- Likes, favorites and viewer state.
- Categories, reports and moderation.
- Author blocks.
- Anonymous-post privacy.
- Author display information and protected avatar access.

### Chat

- Conversations and messages.
- Participant information.
- History and synchronization.
- Attachments.
- Read state.
- Real-time events, presence and typing.
- Chat-specific abuse controls.

## 5. Database design

### Separate schemas and contexts

There are nine module database contexts, with schemas:

```text
identity
cms
caregivers
families
care_homes
notifications
community
chat
finance
```

On the rehearsal deployment these reside in the shared PostgreSQL database. Module connection settings exist, but this does not make the system nine independently deployable databases.

Schemas establish practical ownership boundaries. Cross-module communication uses defined gateways/interfaces, with deliberate SQL-based cross-module projections in selected places. This is not a claim of perfect physical isolation.

### Migrations

Schema changes are versioned with EF Core migrations, and each module maintains its migration history. Recent examples include caregiver certificate-version storage and care-home history-removal audit columns. Startup migration behavior is controlled by application policy/configuration.

### Transactions and concurrency

We use:

- Database transactions when multiple saves must succeed together.
- Optimistic concurrency tokens.
- Unique indexes and constraints.
- Claims/guards around sensitive operations.

Example: certificate promotion must not commit “old version no longer current” and fail before committing “new version current.” Promotion was corrected to make that operation atomic.

## 6. Payments and subscriptions

### Paymob integration

- Payment intentions and hosted checkout.
- Booking payments.
- Subscription-related provider operations.
- HMAC-verified callbacks.
- Refund requests.
- Authenticated refund-status readback.
- Controlled test seams for specifically configured synthetic transactions.

### Safety mechanisms

- Prices and refund amounts are derived server-side.
- Payment callbacks require verification.
- Idempotency and execution-state checks reduce duplicate processing.
- Unknown outcomes are not treated as confirmed success.
- A failed verification read does not automatically issue another refund.

### Refund improvement

A real provider-contract mismatch was fixed:

- Paymob's refund child transaction can have `is_refund=true` and `is_refunded=false`.
- The original payment carries the refunded flag.
- The old handler relied on the wrong interpretation.

The API now verifies the provider refund ID, original payment, order, amount, currency and successful/non-pending refund outcome before readback-based completion.

This was exercised against the actual Paymob **test account**, and the application reached `Completed`. The readback verifies an immediately settled result; pending outcomes can still need a later signed callback or authorized reconciliation. Future live merchant credentials, integrations and webhook configuration still require validation.

### Subscription controls

- Persisted plan/subscription benefit snapshots.
- Paid-period restrictions on immediate plan changes.
- Existing-subscription purchase restrictions.
- Early-renewal restrictions.
- Auto-renew cancellation that does not erase the paid period.
- Owner-authorized invoice access.

Invoice PDF rendering is implemented in the application; this overview does not claim a separate commercial PDF service/library.

## 7. Files and document security

| Public assets | Private assets |
|---|---|
| Landing images and approved public media | Certificates, identity documents and protected attachments |
| Served through public file routes | Served through authorized application routes |
| Intended for public display | Ownership/role checks required |

The storage abstraction keeps disk operations out of scattered business logic. The deployed implementation is local disk, not a deployed S3/Azure Blob setup.

Upload protection includes:

- Size limits.
- Allowed content types.
- File-signature checks on supported upload paths.
- Private storage keys kept out of normal API responses.

The document lifecycle release adds:

- Non-destructive replacements.
- Current/pending/history distinctions.
- Derived expiry indicators.
- Version-specific review and file access.
- Logical removal with audit retention.
- Protection against deleting current or pending documents.
- Protection against reviving invalid approvals through history deletion.

Expiry is separate from approval/classification. A current document can be expired; removal does not automatically make an older historical document current. These certification rules should not be interpreted as a new guarantee about every booking path.

## 8. Background processing and real-time updates

### Database-backed outboxes

Rather than sending every email/push inline:

1. The application records delivery work.
2. A background worker claims it.
3. The worker sends it.
4. Failures can be retried or parked according to policy.

This improves responsiveness and reliability but does not guarantee exactly-once delivery by external providers.

### Hosted background services

Examples:

- Email and push dispatch.
- Care-home booking expiry.
- Care-home license monitoring.
- Care-home notification processing.
- Chat dispatch.
- Chat attachment cleanup.
- Selected audit retention.

These are .NET hosted/background services, not a separately deployed RabbitMQ/Kafka system.

### SignalR

SignalR provides chat real-time updates; REST/history/sync remain authoritative. Chat presence and connection registries are currently in memory. Configuration explicitly assumes a **single-instance topology**; multi-instance chat requires additional coordination/backplane work.

## 9. Optimizations and improvements

Performance optimizations and correctness/reliability fixes are different categories. We should not describe every bug fix as a measured speed improvement.

### Database/query behavior

- Replaced EF expressions that failed PostgreSQL translation.
- Used mapped properties instead of incorrectly accessing wrapped `.Value` fields inside queries.
- Used PostgreSQL `ILIKE` for relevant name searches.
- Escaped `%`, `_` and `\` for literal-text search where required.
- Used pagination and stable ordering.
- Used no-tracking queries for many read paths.
- Used split queries where multiple collections would otherwise multiply result rows.
- Batched author-profile lookups instead of querying once per post.

Benefits: fewer query translation failures and avoidable query costs. No system-wide speedup percentage or throughput figure has been established by load testing in this review.

### Correctness and reliability

- Fixed FAQ sorting failures.
- Fixed resident/picker queries.
- Added idempotent response replay on selected mutation paths.
- Strengthened refund state handling and provider verification.
- Made certificate promotion transactional.
- Added optimistic concurrency protection.
- Preserved valid approved certificates during replacement review.
- Prevented history removal from resurrecting invalid approvals.

### Resource control

- Bounded pagination.
- Upload limits.
- Chat frame-size limits.
- Chat-specific rate controls.
- Contact-form anti-spam controls.
- Bounded background-processing batches.
- Provider-token caching.

### Maintainability

- Central package versions.
- Provider and storage abstractions.
- Dedicated command/query handlers.
- Shared error-to-HTTP mapping.
- Consumer documentation and Postman contracts.
- Explicit migrations and deployment verification.

## 10. Security: implemented versus unfinished

### Implemented protections

- JWT authentication.
- Named role/access policies.
- Ownership checks on sensitive operations.
- Restricted/normal access distinctions.
- Password hashing.
- Session and refresh-token controls.
- Private file access.
- HMAC validation for payment callbacks.
- Selected audit trails.
- Selected rate limits.
- File validation and upload limits.
- Concurrency and idempotency controls.

### Not claimed complete

- Comprehensive security audit.
- API-wide rate-limiting strategy: contact/chat controls are not universal coverage.
- Final production CORS policy: inspected code still allows any origin/header/method.
- Final production HTTPS/domain infrastructure.
- Full trusted-proxy/CDN deployment review.
- Previously identified coordinated HMAC-secret rotation.
- A guarantee of healthcare legal/regulatory compliance.

### CORS and mobile

CORS governs cross-origin browser JavaScript, not ordinary native mobile HTTP requests. WebViews/web builds may differ. CORS is not a replacement for authentication, authorization or rate limiting; rejecting every request without an `Origin` header would break legitimate native/server-to-server scenarios.

## 11. Testing stack and recorded verification

| Tool | Purpose |
|---|---|
| xUnit 2.9.3 | Automated tests |
| FluentAssertions 8.10.0 | Readable assertions |
| ASP.NET Core MVC Testing | In-process HTTP/application tests |
| EF Core InMemory | Fast handler tests |
| SQLite | Selected relational tests |
| Disposable PostgreSQL databases | Real provider behavior, migrations and concurrency |
| NetArchTest.Rules 1.3.2 | Architectural dependency checks |
| Coverlet | Coverage collection support |
| Postman collections | Consumer contracts and manual/integration checks |
| Bruno tooling | Selected API verification workflows |

### Latest recorded document-lifecycle release gate

- Strict build: **0 warnings / 0 errors**.
- Full unit suite: **5,887 passed / 27 skipped / 0 failed**.
- Real PostgreSQL tests executed separately.
- Architecture: **1 passed**.
- Provisioner: **75 passed**.

These are recorded release results, **not a new execution on 2026-10-29**. Skipped tests are not passes, and test count does not prove complete coverage or absence of vulnerabilities.

## 12. Deployment and operations

The established rehearsal setup uses:

| Component | Purpose |
|---|---|
| Linux VPS | Hosts the application |
| nginx | Reverse proxy |
| Kestrel | Runs the ASP.NET Core application |
| systemd | Starts/manages the API process |
| PostgreSQL | Stores business data |
| Environment/configuration files | Server-side settings and secrets |
| Persistent storage directories | Preserve uploads across releases |
| Persistent Data Protection keys | Support protected application state |
| PowerShell deployment tooling | Packaging and verification |
| Git and release manifests | Identify exactly which revision is deployed |

### Release process

1. Build and test.
2. Stage only approved changes.
3. Scan staged changes for secrets.
4. Commit and push.
5. Publish a revision-tagged package.
6. Back up application/database where required.
7. Preserve server configuration and storage.
8. Apply approved migrations.
9. Restart and verify.
10. Record the release receipt.

This is not currently a Kubernetes or microservices deployment. No repository GitHub Actions workflows were found in this inspection, so this overview does not describe releases as fully automated CI/CD.

The last independently verified runtime revision in the conversation was `b861e3e5`, with subsequent documentation-only commits. This overview does not claim a new live-server verification on 2026-10-29.

## 13. Known limitations and future work

- Rehearsal infrastructure is not final production infrastructure.
- Local storage is not a distributed object-storage deployment.
- Chat horizontal scaling needs additional infrastructure.
- No system-wide distributed cache/message broker is part of the stack inspected here.
- Comprehensive APM/monitoring and load-test capacity have not been demonstrated by this review.
- AI security scanning is planned, not completed.
- Some documentation has historical claims that require continued reconciliation with source.
- Demo data can have missing legacy files. Version retention cannot recover files deleted or absent before retention existed.

## 14. Short description to share

> Ahlena Care is a bilingual healthcare and caregiving platform powered by a C#/.NET 10 ASP.NET Core API. It uses a modular-monolith Clean Architecture with domain-driven design principles, CQRS/MediatR, FluentValidation and PostgreSQL through EF Core/Npgsql. The platform supports identity, family and elderly care, caregiver onboarding, care-home operations, bookings, subscriptions, Paymob payments/refunds, content management, notifications, community and SignalR chat. Reliability improvements include transactional certificate versioning, private document history, idempotent operations, concurrency protection and provider-verified refund completion.

**Bottom line:** the next major focus should be measurable security hardening, operational readiness and performance validation—not adding complexity for its own sake.

## Source and evidence references

Primary implementation files inspected for this overview:

- `Directory.Packages.props`
- `src/API/Ahlena.API/Ahlena.API.csproj`
- `src/API/Ahlena.API/Program.cs`
- `src/API/Ahlena.API/DependencyInjection.cs`
- `src/API/Ahlena.API/ApplicationBuilderExtensions.cs`
- `src/Modules/Families/Infrastructure/Ahlena.Modules.Families.Infrastructure/DependencyInjection.cs`
- `src/Modules/Identity/Infrastructure/Ahlena.Modules.Identity.Infrastructure/Security/AspNetPasswordHasher.cs`
- `src/Modules/Notifications/Infrastructure/Ahlena.Modules.Notifications.Infrastructure/Push/FcmPushSender.cs`
- `src/API/Ahlena.API/NotificationsIntegration/PushOutboxProcessor.cs`
- `tests/Ahlena.ArchitectureTests/Ahlena.ArchitectureTests.csproj`

Release/evidence records:

- [Document lifecycle response](../internal/work/document-lifecycle-response-2026-10-27.md)
- [Document lifecycle execution brief](../internal/work/document-lifecycle-2026-10-27.md)
- [Frontend/landing response and refund proof](../internal/work/frontend-landing-response-2026-10-27.md)

Documentation-only deliverable. No application, database, security configuration or deployment changes were performed to create this file.
