# Admin HTTP

Admin routes live under `/api/v1/admin/...`.

## Access

| Role | JWT `account_type` | Splash write (`CmsContent`) | Lookup write (`CaregiversAdmin`) | Caregiver review (`CaregiversAdmin`) |
|---|---|---|---|---|
| Super Admin | `SuperAdmin` | Yes | Yes | Yes |
| Content Admin | `ContentAdmin` | Yes | Yes | Yes |
| Support Admin | `SupportAdmin` | No | No | No |
| Family / Caregiver / Elderly | app types | No | No | No |

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
| Caregiver review | `docs/admin/caregivers-review.md` |
| Care-needs assessment quiz | `docs/admin/care-assessments.md` |
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

Public (app) reads are anonymous, active-only, and live under `/api/v1/lookups/...` — see `docs/app/public/lookups.md`.

## Caregiver review

Under `/api/v1/admin/caregivers/...`:

```text
GET    /caregivers?page=&pageSize=&status=&type=        paged list (reviewer name/phone joined from Identity)
GET    /caregivers/{id}                                 full caregiver profile
POST   /caregivers/{id}/approve
POST   /caregivers/{id}/reject
POST   /caregivers/{id}/request-correction
POST   /caregivers/{id}/suspend
POST   /caregivers/{id}/reactivate
POST   /caregivers/{id}/certificates/{certId}/verify
POST   /caregivers/{id}/certificates/{certId}/reject
POST   /caregivers/{id}/certificates/{certId}/revoke
GET    /caregivers/{id}/certificates/{certId}/file      private scan download (the only file-access path)
```

See `docs/admin/caregivers-review.md`. Caregiver self-service onboarding routes are separate, under `/api/v1/caregiver/...` (see `docs/app/caregivers/`).
