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
| Specialization, title & degree lookups | `docs/admin/lookups-specializations-titles-degrees.md` |
| Postman | `docs/postman/admins/Sanad.Admin.postman_collection.json` |

## Caregiver lookups

Eight admin-managed lookups, each with create / rename / activate / deactivate and an admin list that returns active **and** inactive records with `isActive`:

```text
Services            POST/PUT/POST activate/POST deactivate  GET list (all)
Languages           same shape
Governorates        same shape
Cities              parent = governorate (active chain); GET ?governorateId=
Areas               parent = city + governorate (active chain); GET ?cityId=
Specializations     typed (Medical/Companion); name unique per type
Professional titles Medical only; name unique globally
Academic degrees    Medical only; name unique globally
```

Public (app) reads are anonymous, active-only, and live under `/api/v1/lookups/...` — see `docs/users/lookups.md`.
