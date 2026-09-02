# Admin splash screens

CMS for the **shared** app splash (all four app roles see the same published list).

**Base:** `/api/v1/admin/splash-screens`  
**Policy:** `CmsContent`  
Requires a **normal** JWT (`access_type` = `Normal`) and `account_type` of `SuperAdmin` or `ContentAdmin`.  
Support Admin, Family, Medical, Companion, Elderly, and restricted verification tokens receive **403**. Missing/invalid token → **401**.

Login first with `POST /api/v1/auth/login` using the seeded Super Admin, then send `Authorization: Bearer <accessToken>`.

## Endpoints Summary

```text
POST   /api/v1/admin/splash-screens                 Create splash screen with image (multipart, Draft)
GET    /api/v1/admin/splash-screens                 List all splash screens (Draft and Published)
GET    /api/v1/admin/splash-screens/{id}            Get splash screen by ID
PUT    /api/v1/admin/splash-screens/{id}            Update splash screen details & optional image (multipart)
POST   /api/v1/admin/splash-screens/{id}/publish    Publish splash screen (status = Published)
POST   /api/v1/admin/splash-screens/{id}/unpublish  Unpublish splash screen (status = Draft)
DELETE /api/v1/admin/splash-screens/{id}            Delete splash screen (hard delete)
```

## Lifecycle

```text
Create → Draft
Publish → Published (visible on public GET /api/v1/splash-screens)
Unpublish → Draft (hidden from public GET)
Delete → row removed
```

`internalName` is unique and immutable after create.

---

## 1. List All Splash Screens (Admin)

`GET /api/v1/admin/splash-screens` → `200 OK`

Returns all splash screens (both `Draft` and `Published`), ordered by `displayOrder`.

### Response Body (`application/json`)
```json
[
  {
    "id": "0191ae10-0000-7000-8000-000000000001",
    "internalName": "welcome-screen",
    "arabicTitle": "مرحباً بكم في سند",
    "englishTitle": "Welcome to Sanad",
    "arabicDescription": "منصة الرعاية المتكاملة لكبار السن",
    "englishDescription": "Integrated care platform for the elderly",
    "arabicButtonText": "التالي",
    "englishButtonText": "Next",
    "imagePath": "splash/0191ae10.png",
    "backgroundColor": "#1A73E8",
    "displayOrder": 1,
    "status": 2
  }
]
```

*Note: `status` values: `1` = Draft, `2` = Published.*

---

## 2. Get Splash Screen by ID (Admin)

`GET /api/v1/admin/splash-screens/{id}` → `200 OK`

Returns the full details of a specific splash screen.

Missing/unknown ID → `404` `Cms.Splash.NotFound`.

---

## 3. Create Splash Screen

`POST /api/v1/admin/splash-screens` → `201 Created`
`Content-Type: multipart/form-data`
Image is uploaded **in the same request**. There is no `imagePath` field and no separate files endpoint.

### Form Fields:

| Field | Required | Notes |
|---|---|---|
| `internalName` | yes | Unique, immutable after create |
| `arabicTitle` | yes | |
| `englishTitle` | yes | |
| `arabicDescription` | yes | |
| `englishDescription` | yes | |
| `arabicButtonText` | yes | |
| `englishButtonText` | yes | |
| `backgroundColor` | yes | `#RRGGBB` hex code |
| `displayOrder` | yes | integer >= 0 |
| `file` | yes | `image/jpeg`, `image/png`, or `image/webp`. Max 2 MB |

The API stores the file on disk and persists a generated key such as `splash/{guid}.jpg`.

Duplicate `internalName` → `409` `Cms.Splash.InternalNameAlreadyInUse`.

Missing/empty file → `400` `Storage.File.Empty`.
Too large → `400` `Storage.File.TooLarge`.
Wrong type (for example GIF) → `400` `Storage.File.UnsupportedType`.

Public image URL:
```text
{baseUrl}/files/{imagePath}
```

---

## 4. Update Splash Screen

`PUT /api/v1/admin/splash-screens/{id}` → `200 OK`
`Content-Type: multipart/form-data`
`file` is optional. If provided, replaces the existing image on disk.

---

## 5. Publish / Unpublish

```text
POST /api/v1/admin/splash-screens/{id}/publish     → 200 OK
POST /api/v1/admin/splash-screens/{id}/unpublish   → 200 OK
```

Idempotent at Domain level (already published / already draft is a no-op success).

---

## 6. Delete

`DELETE /api/v1/admin/splash-screens/{id}` → `204 NoContent`
Hard delete. Missing ID → `404` `Cms.Splash.NotFound`.

---

## 7. Public Read (Anonymous Mobile App)

Apps call anonymous `GET /api/v1/splash-screens` which returns only `Published` items without admin metadata. See `docs/app/public/splash-screens.md`.
