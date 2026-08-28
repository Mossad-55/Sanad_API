# Admin service lookups

Admin-managed **Services** that caregivers select during onboarding. Each service is tied to one caregiver type and has a required icon.

**Base:** `/api/v1/admin/lookups/services`  
**Policy:** `CaregiversAdmin`  
Requires a **normal** JWT (`access_type` = `Normal`) and `account_type` of `SuperAdmin` or `ContentAdmin`.  
Support Admin, Family, Medical, Companion, Elderly, and restricted verification tokens receive **403**. Missing/invalid token → **401**.

Login first with `POST /api/v1/auth/login` using the seeded Super Admin, then send `Authorization: Bearer <accessToken>`.

## Rules

- `caregiverType`: `1` = Medical, `2` = Companion. Immutable after create.
- A service **cannot** move between Medical and Companion.
- Arabic/English name uniqueness is scoped **per caregiver type**: the same name may exist once as Medical and once as Companion. A duplicate name within the same type → `409`.
- New service `isActive` is chosen by the admin (`true`/`false`).
- Hard delete is not exposed; deactivate instead (historical selections reference services).
- Icon is uploaded in the **same multipart request** as creation; there is no separate files endpoint.

## Create

`POST /api/v1/admin/lookups/services` → `201`  
`Content-Type: multipart/form-data`

| Field | Required | Notes |
|---|---|---|
| `arabicName` | yes | ≤ 100 chars |
| `englishName` | yes | ≤ 100 chars |
| `caregiverType` | yes | `1` Medical, `2` Companion |
| `isActive` | yes | `true`/`false` |
| `file` | yes | Icon, `image/jpeg`, `image/png`, or `image/webp`. Max 2 MB |

The API stores the icon on disk and persists a generated key such as `services/{guid}.png`. The client file name is never used in the path. If persistence fails after upload, the orphaned icon is deleted automatically.

Duplicate name within the same type → `409` `Caregivers.Lookups.NameAlreadyInUse`.  
Missing/empty file → `400` `Storage.File.Empty`. Too large → `400` `Storage.File.TooLarge`. Wrong type → `400` `Storage.File.UnsupportedType`.

Public icon URL:

```text
{baseUrl}/files/{iconPath}
```

## Rename

`PUT /api/v1/admin/lookups/services/{id}` → `200`  
`Content-Type: application/json`

```json
{ "arabicName": "طبيب", "englishName": "Doctor" }
```

Only the names change. Type and icon are untouched. Unknown id → `404` `Caregivers.Lookups.NotFound`. Renaming to a name already used by another service of the same type → `409`.

## Activate / deactivate

```text
POST /api/v1/admin/lookups/services/{id}/activate     → 200
POST /api/v1/admin/lookups/services/{id}/deactivate   → 200
```

Idempotent at Domain level (already active/inactive is a no-op success). Unknown id → `404`.

## Public read (no admin token)

Apps call `GET /api/v1/lookups/services`. See `docs/users/service-lookups.md`.

## Errors

| Code | HTTP | Meaning |
|---|---|---|
| `Caregivers.Lookups.NameAlreadyInUse` | 409 | Name already used by a service of the same type |
| `Caregivers.Lookups.NotFound` | 404 | Service id does not exist |
| `Storage.File.Empty` | 400 | No icon file in the multipart request |
| `Storage.File.TooLarge` | 400 | Icon exceeds 2 MB |
| `Storage.File.UnsupportedType` | 400 | Icon is not jpeg/png/webp |