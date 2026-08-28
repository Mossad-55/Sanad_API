# Admin city & area lookups

Admin-managed **Cities** (markaz) and **Areas** used in caregiver onboarding geography.

```text
Governorate → City → Area
```

Each level is a separate lookup; a City references a `governorateId` and an Area references a `cityId`. New records are created active and hard-delete is never exposed — deactivate instead.

**Base:** `/api/v1/admin/lookups`  
**Policy:** `CaregiversAdmin` — normal JWT (`access_type` = `Normal`) with `account_type` `SuperAdmin` or `ContentAdmin`. Others get **403**; missing/invalid token **401**.

Login first (`POST /api/v1/auth/login`, seeded Super Admin) and send `Authorization: Bearer <accessToken>`.

## Parent rules

- A City can only be created under a Governorate that **exists and is active**.
- An Area can only be created under a City that **exists and is active**, and whose Governorate is also **active** (the full chain).
- Parent ids are **immutable** — there is no re-parent endpoint.
- Deactivating a Governorate/City does **not** change child records. Children simply disappear from the public (app) reads because the active chain is broken; they remain visible in the admin list.
- City names are unique **within the same Governorate**; Area names are unique **within the same City** (Arabic or English). The same name may exist under different parents.

## Admin lists (scoped by parent)

Because cities and areas are numerous, the parent id is **required** on the list endpoints:

```text
GET /api/v1/admin/lookups/cities?governorateId={id}   → 200 (all cities of that governorate, active and inactive)
GET /api/v1/admin/lookups/areas?cityId={id}           → 200 (all areas of that city, active and inactive)
```

Missing/empty parent id → `400`. Responses include `isActive`, `governorateId`/`cityId`.

## Cities

### Create
`POST /api/v1/admin/lookups/cities` → `201`
```json
{
  "governorateId": "01900000-0000-7000-8000-000000000003",
  "arabicName": "دمنهور",
  "englishName": "Damanhur"
}
```
- Governorate id does not exist → `404` `Caregivers.Lookups.ParentNotFound`.
- Governorate exists but is inactive → `409` `Caregivers.Lookups.ParentNotActive` (reactivate it first).
- Duplicate name within that governorate → `409` `Caregivers.Lookups.NameAlreadyInUse`.

### Rename
`PUT /api/v1/admin/lookups/cities/{id}` → `200`
```json
{ "arabicName": "دمنهور", "englishName": "Damanhur City" }
```
Only names change; the governorate is untouched. Unknown id → `404`.

### Activate / deactivate
```text
POST /api/v1/admin/lookups/cities/{id}/activate     → 200
POST /api/v1/admin/lookups/cities/{id}/deactivate   → 200
```
Idempotent. Unknown id → `404`.

## Areas

### Create
`POST /api/v1/admin/lookups/areas` → `201`
```json
{
  "cityId": "01900000-0000-7000-8000-000000000010",
  "arabicName": "مركز دمنهور",
  "englishName": "Damanhur Markaz"
}
```
- City id does not exist (or its governorate is missing) → `404` `Caregivers.Lookups.ParentNotFound`.
- City or its governorate is inactive → `409` `Caregivers.Lookups.ParentNotActive`.
- Duplicate name within that city → `409` `Caregivers.Lookups.NameAlreadyInUse`.

### Rename
`PUT /api/v1/admin/lookups/areas/{id}` → `200`
```json
{ "arabicName": "مركز دمنهور", "englishName": "Damanhur Markaz" }
```

### Activate / deactivate
```text
POST /api/v1/admin/lookups/areas/{id}/activate     → 200
POST /api/v1/admin/lookups/areas/{id}/deactivate   → 200
```

## Errors

| Code | HTTP | Meaning |
|---|---|---|
| `Caregivers.Lookups.ParentNotFound` | 404 | Parent id does not exist |
| `Caregivers.Lookups.ParentNotActive` | 409 | Parent exists but is inactive (reactivate it first) |
| `Caregivers.Lookups.NameAlreadyInUse` | 409 | Name already used under the same parent |
| `Caregivers.Lookups.NotFound` | 404 | City/Area id does not exist |

Public (app) reads use the parent-filtered, active-chain endpoints — see `docs/users/lookups.md`.
