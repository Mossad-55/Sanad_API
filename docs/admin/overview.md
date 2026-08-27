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

## Current admin surface

| Area | Doc |
|---|---|
| Splash screens | `docs/admin/splash-screens.md` |
| Postman | `docs/postman/admin/Sanad.Admin.postman_collection.json` |

App-facing (non-admin) splash GET: `docs/users/splash-screens.md`.
