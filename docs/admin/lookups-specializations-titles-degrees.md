# Admin specialization, professional title & academic degree lookups

The final three caregiver onboarding catalogs.

- **Specialization** — tied to a caregiver type. Medical and Companion caregivers each pick one typed specialization.
- **Professional Title** — Medical only (e.g. Specialist, Consultant).
- **Academic Degree** — Medical only (e.g. Bachelor, Master).

All are created from the admin dashboard and hard-delete is never exposed — deactivate instead.

**Base:** `/api/v1/admin/lookups`  
**Policy:** `CaregiversAdmin` — normal JWT (`access_type` = `Normal`) with `account_type` `SuperAdmin` or `ContentAdmin`. Others get **403**; missing/invalid token **401**.

Login first (`POST /api/v1/auth/login`, seeded Super Admin) and send `Authorization: Bearer <accessToken>`.

## Rules

- Specialization `caregiverType`: `1` Medical, `2` Companion; immutable after create.
- Specialization names are unique **within the same caregiver type** — the same name may exist once for Medical and once for Companion.
- Professional Title and Academic Degree are Medical-only; their names are unique globally.
- All three accept an `isActive` flag on creation (`true`/`false`), so an admin can stage a hidden entry and activate it later.
- Rename changes names only; the caregiver type never changes.

## Specializations

### Create
`POST /api/v1/admin/lookups/specializations` → `201`
```json
{
  "arabicName": "تمريض",
  "englishName": "Nursing",
  "caregiverType": 1,
  "isActive": true
}
```
Duplicate name within the same type → `409` `Caregivers.Lookups.NameAlreadyInUse`.

### Rename
`PUT /api/v1/admin/lookups/specializations/{id}` → `200`
```json
{ "arabicName": "تمريض متقدم", "englishName": "Advanced Nursing" }
```

### Activate / deactivate
```text
POST /api/v1/admin/lookups/specializations/{id}/activate     → 200
POST /api/v1/admin/lookups/specializations/{id}/deactivate   → 200
```

### List (active AND inactive)
`GET /api/v1/admin/lookups/specializations` → `200` — all specializations of both types with `isActive` and `caregiverType`. No filter; the dashboard groups by type client-side.

## Professional titles (Medical only)

### Create
`POST /api/v1/admin/lookups/professional-titles` → `201`
```json
{ "arabicName": "أخصائي", "englishName": "Specialist", "isActive": true }
```
Duplicate name → `409` `Caregivers.Lookups.NameAlreadyInUse`.

### Rename
`PUT /api/v1/admin/lookups/professional-titles/{id}` → `200`
```json
{ "arabicName": "أخصائي", "englishName": "Medical Specialist" }
```

### Activate / deactivate
```text
POST /api/v1/admin/lookups/professional-titles/{id}/activate     → 200
POST /api/v1/admin/lookups/professional-titles/{id}/deactivate   → 200
```

### List
`GET /api/v1/admin/lookups/professional-titles` → `200` — all titles with `isActive`.

## Academic degrees (Medical only)

### Create
`POST /api/v1/admin/lookups/academic-degrees` → `201`
```json
{ "arabicName": "بكالوريوس", "englishName": "Bachelor", "isActive": true }
```
Duplicate name → `409` `Caregivers.Lookups.NameAlreadyInUse`.

### Rename
`PUT /api/v1/admin/lookups/academic-degrees/{id}` → `200`
```json
{ "arabicName": "بكالوريوس", "englishName": "Bachelor's Degree" }
```

### Activate / deactivate
```text
POST /api/v1/admin/lookups/academic-degrees/{id}/activate     → 200
POST /api/v1/admin/lookups/academic-degrees/{id}/deactivate   → 200
```

### List
`GET /api/v1/admin/lookups/academic-degrees` → `200` — all degrees with `isActive`.

## Errors

| Code | HTTP | Meaning |
|---|---|---|
| `Caregivers.Lookups.NameAlreadyInUse` | 409 | Name already used in the same scope (per type for specialization, global for title/degree) |
| `Caregivers.Lookups.NotFound` | 404 | Lookup id does not exist |

Public (app) reads: `GET /api/v1/lookups/{specializations|professional-titles|academic-degrees}` — see `docs/app/public/lookups.md`.
