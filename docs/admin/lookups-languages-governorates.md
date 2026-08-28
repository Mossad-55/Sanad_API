# Admin language & governorate lookups

Admin-managed **Languages** and **Governorates** used in caregiver onboarding. Both are created active and hard-delete is never exposed — deactivate instead.

**Base:** `/api/v1/admin/lookups`  
**Policy:** `CaregiversAdmin` — normal JWT (`access_type` = `Normal`) with `account_type` `SuperAdmin` or `ContentAdmin`. Others get **403**; missing/invalid token **401**.

Login first (`POST /api/v1/auth/login`, seeded Super Admin) and send `Authorization: Bearer <accessToken>`.

## Admin lists (active AND inactive)

The dashboard reads these to render management tables with status badges and to obtain ids:

```text
GET /api/v1/admin/lookups/languages      → 200  (all languages, includes isActive)
GET /api/v1/admin/lookups/governorates   → 200  (all governorates, includes isActive)
GET /api/v1/admin/lookups/services       → 200  (all services, includes isActive + iconPath)
```

No pagination and no filters — small catalogs.

## Languages

`code` is the immutable ISO key: lowercase 2–3 letters (`ar`, `en`, `eng`). Regional tags such as `ar-EG` are rejected. Code is globally unique; the API lowercases/trims input. Names are bilingual and unique (no two languages may share an Arabic or English name).

### Create
`POST /api/v1/admin/lookups/languages` → `201`
```json
{ "code": "ar", "arabicName": "العربية", "englishName": "Arabic" }
```
Duplicate code → `409` `Caregivers.Lookups.LanguageCodeInUse`. Duplicate name → `409` `Caregivers.Lookups.NameAlreadyInUse`.

### Rename
`PUT /api/v1/admin/lookups/languages/{id}` → `200`
```json
{ "arabicName": "العربية الفصحى", "englishName": "Modern Standard Arabic" }
```
Code is not changed. Unknown id → `404` `Caregivers.Lookups.NotFound`.

### Activate / deactivate
```text
POST /api/v1/admin/lookups/languages/{id}/activate     → 200
POST /api/v1/admin/lookups/languages/{id}/deactivate   → 200
```
Idempotent. Unknown id → `404`. Deactivation does not cascade.

## Governorates

Bilingual names, globally unique (Arabic or English). Active on creation.

### Create
`POST /api/v1/admin/lookups/governorates` → `201`
```json
{ "arabicName": "البحيرة", "englishName": "Beheira" }
```
Duplicate name → `409` `Caregivers.Lookups.NameAlreadyInUse`.

### Rename
`PUT /api/v1/admin/lookups/governorates/{id}` → `200`
```json
{ "arabicName": "البحيرة", "englishName": "Beheira Governorate" }
```

### Activate / deactivate
```text
POST /api/v1/admin/lookups/governorates/{id}/activate     → 200
POST /api/v1/admin/lookups/governorates/{id}/deactivate   → 200
```

## Errors

| Code | HTTP | Meaning |
|---|---|---|
| `Caregivers.Lookups.LanguageCodeInUse` | 409 | Language code already exists |
| `Caregivers.Lookups.NameAlreadyInUse` | 409 | Arabic/English name already used |
| `Caregivers.Lookups.NotFound` | 404 | Lookup id does not exist |

Public (app) reads: `GET /api/v1/lookups/languages` and `/governorates` — see `docs/users/lookups.md`.