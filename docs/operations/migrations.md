# Identity migrations

Identity uses PostgreSQL schema `identity`.

EF history table: `identity.__EFMigrationsHistory`.

## Apply

```bash
export ConnectionStrings__IdentityDatabase="Host=localhost;Port=5432;Database=sanad_identity;Username=REPLACE_ME;Password=REPLACE_ME"

dotnet ef database update \
  --project src/Modules/Identity/Infrastructure/Sanad.Modules.Identity.Infrastructure/Sanad.Modules.Identity.Infrastructure.csproj \
  --startup-project src/API/Sanad.API/Sanad.API.csproj
```

The design-time factory reads only `ConnectionStrings__IdentityDatabase`.

## Current Identity migrations

Apply them in order. Do not rewrite history.

| Migration | Purpose |
|---|---|
| `InitialIdentity` | Users, VerificationRequests, DeviceSessions |
| `AddExternalAuthenticationNonces` | Historical social nonce table |
| `AddSocialChallengeEmailAuthority` | Historical social challenge column |
| `RemoveSocialAuthentication` | Drops social/nonce/challenge tables |
| `20261005101531_AddFeedback` | Adds the `identity.feedback` app-rating table |

Social authentication is cancelled. Those earlier migrations stay in the project so existing databases can upgrade. `RemoveSocialAuthentication` is the cleanup step.

## Families migrations

Families uses PostgreSQL schema `families` (table history in `families.__EFMigrationsHistory`). Migrations apply automatically at API startup (`ApplyFamiliesMigrations`); a manual update mirrors the Identity command with the Families Infrastructure project.

| Migration | Purpose |
|---|---|
| `20260901053024_AddFamiliesAggregate` | Families schema evolution (Phase F). |
| `20260901092021_AddFamilyInvitations` | Families schema evolution (Phase F). |
| `20260902022127_AddElderlyRelationshipType` | Families schema evolution (Phase F). |
| `20260902035404_AddCareAssessmentQuiz` | Families schema evolution (Phase F). |
| `20260902083751_AddElderlyMedifcalProfile` | Families schema evolution (Phase F). |
| `20260903071547_AddMedicationsAndDoseLogs` | Families schema evolution (Phase F). |
| `20260903113603_AddElderlyNotesAndActivityLogs` | Families schema evolution (Phase F). |
| `20260905043011_AddBookingsAggregate` | Booking aggregate table (families.bookings). |
| `20260905083721_AddBookingAcceptanceWindow` | Acceptance deadline + expired columns. |
| `20261005101503_AddBookingReviewsAndMedicalAccessGrants` | Adds immutable completed-booking reviews and dependent medical-access grants. |
| `20261005111105_AddMedicalAccessGrantGrantee` | Adds the named grantee user to each medical-access grant and its lookup/index. |

The `bookings` table is created by `AddBookingsAggregate`; `AddBookingAcceptanceWindow` adds `acceptance_deadline_utc` (required) and `expired_on_utc` (nullable).

## Caregivers migrations

Caregivers uses PostgreSQL schema `caregivers` and also applies automatically at startup (`ApplyCaregiversMigrations`).

## Community migrations

Community uses PostgreSQL schema `community` and has its own history table
`community.__EFMigrationsHistory`. The initial migration is present in the
Community Infrastructure project and has been applied only to the owner-approved
disposable Bruno database `SanadBrunoTestDb` on localhost.

| Migration | Purpose | Applied in this worktree |
|---|---|---|
| `20261005101553_InitialCommunity` | Creates posts, comments, replies, ratings, check-ins, and per-user interactions with typed-ID conversions and uniqueness constraints. | Yes — disposable Bruno database only |

Generate or apply this migration only against an explicitly authorized database.
The application to `SanadBrunoTestDb` does not imply it is applied in staging or
production. Do not infer deployment application from a passing model/build check.

## Rules

- Do not edit applied migrations
- Do not commit a database password
- New module migrations are added to their module's Infrastructure project and wired into startup

## Current worktree migration inventory and generation

The following feature migrations are present in the worktree and are applied
to `localhost:5432/SanadBrunoTestDb`. No production or remote database was
changed by that authorized application; the separate local `SanadDb` rollback
is recorded below:

- Families: `20261005101503_AddBookingReviewsAndMedicalAccessGrants`
- Families: `20261005111105_AddMedicalAccessGrantGrantee`
- Identity: `20261005101531_AddFeedback`
- Community: `20261005101553_InitialCommunity`
- Care Homes: `20261008041054_AddCareHomeProfileMedia`
- Care Homes: `20261008052316_AddCareHomePayoutLedger`
- Care Homes: `20261008112556_AddCareHomeRatings`
- Care Homes: `20261008122857_AddCareHomeBookingExtensions`
- Care Homes: `20261008141359_AddCareHomeFinanceReporting`

To inspect SQL without changing a database, use the module Infrastructure
project and the API startup project:

```bash
dotnet ef migrations list \
  --project src/Modules/Community/Infrastructure/Sanad.Modules.Community.Infrastructure/Sanad.Modules.Community.Infrastructure.csproj \
  --startup-project src/API/Sanad.API/Sanad.API.csproj

dotnet ef migrations script \
  --project src/Modules/Community/Infrastructure/Sanad.Modules.Community.Infrastructure/Sanad.Modules.Community.Infrastructure.csproj \
  --startup-project src/API/Sanad.API/Sanad.API.csproj \
  --output community-migration.sql
```

Use equivalent `--project` paths for Families and Identity when generating
their scripts. Any further `update` command requires separate authorization for
the exact database target and must never be directed at production without
explicit authorization.

## Local development rollback evidence (2026-10-05)

During fixture setup, API startup with test-user seeding disabled used the
configured local `SanadDb` connection rather than the guarded Bruno target. The
Identity feedback migration and Community initial migration were applied there.
The owner authorized rolling back exactly those two migrations. Identity was
returned to `20260927163146_AddHelpRequestAlertPreference`; Community was
returned to migration version `0`. A subsequent EF migration listing reported
`20261005101531_AddFeedback` and `20261005101553_InitialCommunity` as pending on
`localhost:5432/SanadDb`.

The two `Down` operations removed only `identity.Feedbacks` and the tables
created by `community.InitialCommunity`. No authenticated feature write
succeeded against `SanadDb` during that unguarded startup window. Subsequent
stateful Bruno runs used the fixture-seed guard, which pins the API databases
to `localhost:5432/SanadBrunoTestDb`. Do not use the unguarded Development API
startup for Bruno tests.
