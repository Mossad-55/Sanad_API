# Test data (E2E fixture)

An **opt-in, idempotent seeder** creates a stable multi-role fixture so API tests (manual, Postman, or an agent driving HTTP) can log in and exercise real flows without manual setup. It mirrors the `SuperAdminSeeder` pattern and lives at `src/API/Sanad.API/Seeding/TestUserDataSeeder.cs`.

## Enabling it

The seeder runs at startup **only** when explicitly enabled:

```text
App__TestUserSeed__Enabled=true
```

- Default is **off** — nothing is seeded unless the flag is set.
- **Never enable it in production.** As a seatbelt, the seeder *refuses to run* if `Paymob__SecretKey` starts with `sk_live`.
- Locally (Development): the flag is already set in `appsettings.Development.json`. On the TEST box: set it in `/etc/sanad/sanad.env`, restart, and remove it when the environment stops being a test one.
- Password for all seeded password accounts (override with `App__TestUserSeed__Password`):

| Account | Login | Credential |
|---|---|---|
| Family **Owner** | `family.owner@test.sanad.local` | `Test-1234!` (email + password) |
| Family **Viewer** | `family.viewer@test.sanad.local` | `Test-1234!` (email + password) |
| **Medical caregiver** | `medical.caregiver@test.sanad.local` | `Test-1234!` (email + password) |
| **Companion caregiver** | `companion.caregiver@test.sanad.local` | `Test-1234!` (email + password) |
| Elderly (grandfather) | phone `+201000000005` | SMS OTP |
| Elderly (grandmother) | phone `+201000000006` | SMS OTP |
| SuperAdmin | from `Identity__AdminSeed__*` env | configured at deploy |

## What gets created (idempotent — every section is skipped if already present)

- Identity accounts for all six logins above (elderly accounts born phone-verified and active — OTP works immediately).
- Family aggregate **"Test Family"** owned by the owner account, with the viewer as a `Viewer` member (`Son` relationship).
- Two **elderly dependents**: Test Grandfather (`Grandfather`) and Test Grandmother (`Grandmother`), linked to their login users.
- Lookup rows (governorate/city/area/language/services/specializations/title/degree, all clearly named `Test …`) created only if missing.
- **Two Active + Available caregivers**, built through the *real domain readiness flow* (selections → profile → pricing → schedule → certificates → submit → verify → approve → available), so caregiver-side tests exercise the exact state a real approved caregiver has:
  - **Medical**: 8 yrs, home-visit windows Sat/Wed 08:00–22:00, morning shift Monday, home visit 500.
  - **Companion**: 5 yrs, hourly windows Sat/Mon 09:00–18:00, hourly 80.
  - Note: their certificate `file_path`s are placeholders (`test-data/certificates/…`) — anything that downloads the physical file will 404; everything else is genuine.
- A **booking portfolio** for the family (conflict-free dates, realistic prices):

| # | Final status | Payment | Notes |
|---|---|---|---|
| 1 | `Completed` (5) | paid | full lifecycle: paid → accepted → started → completed |
| 2 | `Confirmed` (3) | paid | accepted, awaiting the visit |
| 3 | `PendingPayment` (1) | — | ready for the payment-intent test |
| 4 | `CancelledByFamily` (6) | never paid | carries a cancellation reason |
| 5 | `CancelledByCaregiver` → `Refunded` (9) | paid + refunded | companion hourly (92.00), refund id recorded |
| 6 | `DeclinedByCaregiver` → `Refunded` (9) | paid (never accepted) + refunded | decline replaces acceptance |

All seeds are written through the domain aggregates — no raw row fabrication.

## Logging in per role

| Role | Flow |
|---|---|
| Family (owner/viewer) | `POST /api/v1/auth/login` with email + password — no OTP. |
| Caregiver (medical/companion) | `POST /api/v1/auth/login` with email + password. |
| Elderly | `POST /api/v1/auth/elderly/request-otp` with the phone → receive the code (SMS Misr test panel, API logs under the development SMS sender, or ask the test coordinator to relay it) → `POST /api/v1/auth/elderly/verify-otp`. |
| Admin | `POST /api/v1/auth/login` with the `Identity__AdminSeed` account. |

Sessions are capped (`DeviceSessionPolicy.MaximumActiveSessions`, currently 5): repeated logins from test scripts eventually return **409 `Identity.Login.SessionLimitReached`**. Remedy in a scratch environment: delete rows from `identity.device_sessions` (or revoke) and retry.

## Payments in TEST

Without `Paymob__SecretKey` configured (plain local runs), the API resolves a **`DevelopmentPaymobClient`** which returns a fake intent/clientSecret so checkout→intent flows are testable offline. Webhook settlement, SDK payments and refunds need the **real TEST keys**:

- Test cards / wallet numbers / keys come from the Paymob dashboard (Test mode): *Developers → Payment Integrations* and the **Test Credentials** page of the official docs.
- Webhook settlement locally: replay the callback through the signed webhook simulator (HMAC-SHA512 over the 20-field Paymob concat) since Paymob cannot reach `localhost`.
- Never use live keys in this environment; the seeder's live-key guard is a last line of defense, not a substitute for correct configuration.

## Known findings (tracked, not yet decided)

- The admin caregiver cancellation summary counts bookings with `status = CancelledByCaregiver (8)` only; because cancellation auto-refunds flip the status to `Refunded (9)`, refunded cancellations do not appear in the count. Tracked as a product decision.
- `POST /api/v1/family/bookings/{id}/payments/intent` a second time on a booking that already has a pending intent currently succeeds (no 409 guard) — pending decision on intended behavior.

## Quick smoke after enabling

```bash
# 1. login as family owner (expect 200 + tokens)
curl -i -X POST https://<test-host>/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"family.owner@test.sanad.local","password":"Test-1234!","deviceName":"smoke","devicePlatform":1,"appVersion":"test"}'

# 2. discovery (expect 200 with the seeded medical caregiver)
curl -i "https://<test-host>/api/v1/caregivers?type=1&availability=1&page=1&pageSize=10" \
  -H "Authorization: Bearer <access-token>"

# 3. family tabs (expect statuses 1+3 upcoming, 5/6/9 past)
curl -i "https://<test-host>/api/v1/family/bookings?tab=1" -H "Authorization: Bearer <access-token>"
curl -i "https://<test-host>/api/v1/family/bookings?tab=3" -H "Authorization: Bearer <access-token>"
```
