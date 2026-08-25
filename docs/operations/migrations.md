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

## Rules

- Do not generate another social-removal migration
- Do not edit applied migrations
- Do not commit a database password
- Caregivers and Families have no Infrastructure migrations yet
