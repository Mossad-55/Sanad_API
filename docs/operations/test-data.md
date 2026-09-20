# Test data (E2E fixture)

Medical Reports V1 uses the existing completed medical booking as a one-shot fixture. Submit at most
one report per completed booking; duplicate and concurrent submissions are rejected by the unique
booking constraint. The `medical-reports` Bruno collection is intended for a freshly seeded local
database only.

An **opt-in, idempotent seeder** creates a stable multi-role fixture so API tests (manual, Postman, or an agent driving HTTP) can log in and exercise real flows without manual setup. It mirrors the `SuperAdminSeeder` pattern and lives at `src/API/Sanad.API/Seeding/TestUserDataSeeder.cs`.

## Enabling it

The seeder runs at startup **only** when explicitly enabled:

```text
App__TestUserSeed__Enabled=true
```

- Default is **off** — nothing is seeded unless the flag is set.
- **Development only:** this fixture must never be enabled on TEST, staging, a VPS, or production. The seeder has an explicit Development-only hard gate and returns without database access in every other environment. As a second seatbelt, it *refuses to run* if `Paymob__SecretKey` starts with `sk_live`.
- Locally (Development), the flag is already set in `appsettings.Development.json`. Do not copy this setting to TEST, staging, a VPS, or production.
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

### Subscription fixtures

The Development fixture seeds three version-1 subscription plans and one current family
snapshot. The public catalog returns only the two published plans, in key order:

| Plan | Published | Available for new sales | Price | Cycle | Currency | Members | Monthly bookings | Rollover | Included benefit keys (`1`–`8`) |
|---|---:|---:|---:|---|---|---:|---:|---|---|
| `free` | yes | yes | `0` | Monthly | EGP | 3 | 5 | None | `1, 2, 3, 5` |
| `premium` | yes | no (retired) | `299` | Monthly | EGP | 10 | 20 | None | `1, 2, 3, 4, 5, 6, 7` |
| `premium-plus` | no (draft) | yes | `2499` | Annual | EGP | Unlimited | Unlimited | NotApplicable | `1, 2, 3, 4, 5, 6, 7, 8` |

Every plan has version `1` and all eight benefit keys. Benefits not listed as included
have `IsIncluded = false`. The current family snapshot is the `free` plan at version `1`
with price `0`, Monthly billing, EGP, member limit `3`, monthly booking limit `5`,
rollover `None`, and benefit keys `1, 2, 3, 5` included (keys `4, 6, 7, 8` excluded).

## Logging in per role

| Role | Flow |
|---|---|
| Family (owner/viewer) | `POST /api/v1/auth/login` with email + password — no OTP. |
| Caregiver (medical/companion) | `POST /api/v1/auth/login` with email + password. |
| Elderly | `POST /api/v1/auth/elderly/request-otp` with the phone → receive the code (SMS Misr test panel, API logs under the development SMS sender, or ask the test coordinator to relay it) → `POST /api/v1/auth/elderly/verify-otp`. |
| Admin | `POST /api/v1/auth/login` with the `Identity__AdminSeed` account. |

Sessions are capped (`DeviceSessionPolicy.MaximumActiveSessions`, currently 5): repeated logins from test scripts eventually return **409 `Identity.Login.SessionLimitReached`**. Remedy in a scratch environment: delete rows from `identity.device_sessions` (or revoke) and retry.

## Payments in Development

Without `Paymob__SecretKey` configured (plain local runs), the API resolves a **`DevelopmentPaymobClient`** which returns a fake intent/clientSecret so checkout→intent flows are testable offline. Webhook settlement, SDK payments and refunds need the **real TEST keys**:

- Test cards / wallet numbers / keys come from the Paymob dashboard (Test mode): *Developers → Payment Integrations* and the **Test Credentials** page of the official docs.
- Webhook settlement locally: replay the callback through the signed webhook simulator (HMAC-SHA512 over the 20-field Paymob concat) since Paymob cannot reach `localhost`.
- This fixture is not a TEST/staging/VPS/production data mechanism. Never enable it there or use live keys; the seeder's Development-only and live-key guards are last lines of defense, not a substitute for correct configuration.

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
