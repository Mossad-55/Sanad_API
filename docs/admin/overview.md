# Admin HTTP

Admin routes live under `/api/v1/admin/...`.

## Access

| Role | JWT `account_type` | Splash write (`CmsContent`) | Lookup write (`CaregiversAdmin`) |
|---|---|---|---|
| Super Admin | `SuperAdmin` | Yes | Yes |
| Content Admin | `ContentAdmin` | Yes | Yes |
| Support Admin | `SupportAdmin` | No | No |
| Family / Caregiver / Elderly | app types | No | No |

All admin writes also require `access_type` = `Normal`. Restricted verification tokens cannot call admin routes.

First Super Admin is **seeded** (`Identity__AdminSeed__*`). There is no public admin register.

## Current admin surface

| Area | Doc |
|---|---|
| Splash screens | `docs/admin/splash-screens.md` |
| Service lookups | `docs/admin/service-lookups.md` |
| Language & governorate lookups | `docs/admin/lookups-languages-governorates.md` |
| City & area lookups | `docs/admin/lookups-cities-areas.md` |
| Postman | `docs/postman/admins/Sanad.Admin.postman_collection.json` |

Admin lookup management uses list endpoints that return active **and** inactive records with `isActive`, separate from the public active-only app reads:

```text
GET /api/v1/admin/lookups/services                 (all)
GET /api/v1/admin/lookups/languages                (all)
GET /api/v1/admin/lookups/governorates             (all)
GET /api/v1/admin/lookups/cities?governorateId=    (all under that governorate)
GET /api/v1/admin/lookups/areas?cityId=            (all under that city)
```

App-facing (non-admin) reads live under `/api/v1/lookups/...` — see `docs/users/lookups.md` and `docs/users/service-lookups.md`.
