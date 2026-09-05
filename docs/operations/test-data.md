# Test data (E2E fixture)

An **opt-in, idempotent seeder** creates a stable multi-role fixture so API tests (manual, Postman, or an agent driving HTTP) can log in and exercise real flows without manual setup. It mirrors the `SuperAdminSeeder` pattern and lives at `src/API/Sanad.API/Seeding/TestUserDataSeeder.cs`.

## Enabling it

The seeder runs at startup **only** when explicitly enabled:

```text
App__TestUserSeed__Enabled=true
```

- Default is **off** — nothing is seeded unless the flag is set.
- **Never enable it in production.** As a seatbelt, the seeder *refuses to run* if `Paymob__SecretKey` starts with `sk_live`.
- Set it in the test environment's env file (e.g. the VPS `/etc/sanad/sanad.env` while the server is the TEST environment), then restart the API. Remove the line (and restart) when the environment stops being a test one.
- Password for all seeded password accounts (override with `App__TestUserSeed__Password`):

| Account | Login | Credential |
|---|---|---|
| Family **Owner** | `family.owner@test.sanad.local` | `Test-1234!` (email + password) |
| Family **Viewer** | `family.viewer@test.sanad.local` | `Test-1234!` (email + password) |
| Elderly (grandfather) | phone `+201000000005` | SMS OTP |
| Elderly (grandmother) | phone `+201000000006` | SMS OTP |
| SuperAdmin | from `Identity__AdminSeed__*` env | configured at deploy |

What gets created (idempotent — existing emails/phones/family are skipped):

- Family aggregate **"Test Family"** owned by the owner account, with the viewer account as a `Viewer` member (`Son` relationship).
- Two **elderly dependents**: Test Grandfather (`Grandfather`, linked to the grandfather login) and Test Grandmother (`Grandmother`, linked to the grandmother login).
- Elderly logins are born phone-verified and active — OTP login works immediately.

## Logging in per role

| Role | Flow |
|---|---|
| Family (owner/viewer) | `POST /api/v1/auth/login` with email + password — no OTP. |
| Elderly | `POST /api/v1/auth/elderly/request-otp` with the phone → receive the code (SMS Misr test panel, API logs under the development SMS sender, or ask the test coordinator to relay it) → `POST /api/v1/auth/elderly/verify-otp`. |
| Admin | `POST /api/v1/auth/login` with the `Identity__AdminSeed` account. |

An agent/tester without access to SMS can ask the test coordinator to relay the OTP code, or receive a short-lived access token directly.

## Caregivers (one-time real bootstrap)

Caregivers are **not** seeded: activation requires genuine onboarding data (selections, professional profile, medical certificates) enforced by the domain, and fabricating it in a seeder would duplicate the onboarding flow. Instead, bootstrap **once** through the real product:

1. Register a **Medical** caregiver (real registration + email verification), complete onboarding (services/languages/areas, medical profile, pricing, schedule, Practice License + certificates), submit, then approve via the admin account.
2. Do the same for one **Companion** caregiver.
3. Approved caregivers persist in the test database — every later test run reuses them (record their `caregiverId`s in the Postman test-data environment).

This keeps the fixture truthful: caregiver-side tests exercise the exact state a real approved caregiver has.

## Paymob TEST credentials

Test cards / wallet numbers / keys come from the Paymob dashboard (Test mode): *Developers → Payment Integrations* and the **Test Credentials** page of the official docs. Never use live keys in this environment; the seeder's live-key guard is a last line of defense, not a substitute for correct configuration.

## Quick smoke after enabling

```bash
# 1. login as family owner (expect 200 + tokens)
curl -i -X POST https://<test-host>/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"family.owner@test.sanad.local","password":"Test-1234!"}'

# 2. viewer attempts checkout (expect 403 Bookings.UnauthorizedRole)
# 3. list elderly dependents for the family (expect 2)
```
