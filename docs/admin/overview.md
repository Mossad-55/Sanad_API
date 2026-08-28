# Admin HTTP

Admin routes live under `/api/v1/admin/...`.

## Access

| Role | JWT `account_type` | Splash write (`CmsContent`) |
|---|---|---|
| Super Admin | `SuperAdmin` | Yes |
| Content Admin | `ContentAdmin` | Yes |
| Support Admin | `SupportAdmin` | No |
| Family / Caregiver / Elderly | app types | No |

All admin splash writes also require `access_type` = `Normal`. Restricted verification tokens cannot call admin routes.

First Super Admin is **seeded** (`Identity__AdminSeed__*`). There is no public admin register.

Caregiver lookup writes use the `CaregiversAdmin` policy — same role/token requirements as `CmsContent`.

## Current admin surface

| Area | Doc |
|---|---|
| Splash screens | `docs/admin/splash-screens.md` |
| Service lookups | `docs/admin/service-lookups.md` |
| Language & governorate lookups | `docs/admin/lookups-languages-governorates.md` |
| Postman | `docs/postman/admins/Sanad.Admin.postman_collection.json` |

Admin lookup management uses list-all endpoints (`GET /api/v1/admin/lookups/{services|languages|governorates}`) that return active **and** inactive records with `isActive`, separate from the public active-only app reads.

App-facing (non-admin) splash GET: `docs/users/splash-screens.md`.
