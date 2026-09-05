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

The `bookings` table is created by `AddBookingsAggregate`; `AddBookingAcceptanceWindow` adds `acceptance_deadline_utc` (required) and `expired_on_utc` (nullable).

## Caregivers migrations

Caregivers uses PostgreSQL schema `caregivers` and also applies automatically at startup (`ApplyCaregiversMigrations`).

## Rules

- Do not edit applied migrations
- Do not commit a database password
- New module migrations are added to their module's Infrastructure project and wired into startup
