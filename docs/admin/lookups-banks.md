# Admin bank lookup

Admin-managed **Banks** used as payout-account choices. Banks are created active and hard-delete is never exposed — deactivate instead.

**Base:** `/api/v1/admin/lookups`
**Policy:** `CaregiversAdmin` — normal JWT (`access_type` = `Normal`) with `account_type` `SuperAdmin` or `ContentAdmin`. Others get **403**; missing/invalid token **401**.

Login first (`POST /api/v1/auth/login`, seeded Super Admin) and send `Authorization: Bearer <accessToken>`.

## Admin list (active AND inactive)

The dashboard reads this to render the management table with status badges and to obtain ids:

```text
GET /api/v1/admin/lookups/banks   → 200  (all banks, includes isActive)
```

No pagination and no filters — small catalog.

## Banks

`code` is the immutable bank key: uppercase letters/digits, 3–11 characters (SWIFT-compatible). The API trims/uppercases input. Code is globally unique. Names are bilingual and unique (no two banks may share an Arabic or English name).

### Create
`POST /api/v1/admin/lookups/banks` → `201`
```json
{ "code": "NBE", "arabicName": "البنك الأهلي المصري", "englishName": "National Bank of Egypt" }
```
Duplicate code → `409` `Caregivers.Lookups.BankCodeInUse`. Duplicate name → `409` `Caregivers.Lookups.NameAlreadyInUse`. Invalid code shape → `400`.

### Rename
`PUT /api/v1/admin/lookups/banks/{id}` → `200`
```json
{ "arabicName": "البنك الأهلي", "englishName": "National Bank of Egypt" }
```
Code is not changed. Unknown id → `404` `Caregivers.Lookups.NotFound`. Duplicate name → `409` `Caregivers.Lookups.NameAlreadyInUse`.

### Activate / deactivate
```text
POST /api/v1/admin/lookups/banks/{id}/activate     → 200
POST /api/v1/admin/lookups/banks/{id}/deactivate   → 200
```
Idempotent. Unknown id → `404`. Deactivation does not cascade; deactivated banks disappear from the public `GET /api/v1/lookups/banks` list.

## Errors

- `401` — missing/invalid token.
- `403` — valid token without `SuperAdmin`/`ContentAdmin` role.
- `404 Caregivers.Lookups.NotFound` — unknown bank id.
- `409 Caregivers.Lookups.BankCodeInUse` — duplicate bank code.
- `409 Caregivers.Lookups.NameAlreadyInUse` — duplicate Arabic or English name.

## Persistence

Table `caregivers.banks` (`id`, `code` unique, `arabic_name`, `english_name`, `is_active`, `created_on_utc`, `updated_on_utc`). Migration `AddBankLookup` is authored only and must not be applied except on an explicitly authorized database. No seed data is committed; the bank catalog starts empty and is filled through the Admin create route.
