# Admin HTTP

Admin routes live under `/api/v1/admin/...`.

## Access

| Role | JWT `account_type` | CMS content write — splash, legal, help center (`CmsContent`) | Lookup write (`CaregiversAdmin`) | Caregiver review (`CaregiversAdmin`) |
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
| Legal content (Privacy Policy & Terms) | `docs/admin/legal-content.md` |
| Help Center (FAQs & global support contact) | `docs/admin/help-center.md` |
| Service lookups | `docs/admin/service-lookups.md` |
| Language & governorate lookups | `docs/admin/lookups-languages-governorates.md` |
| City & area lookups | `docs/admin/lookups-cities-areas.md` |
| Specialization, title & degree lookups | `docs/admin/lookups-specializations-titles-degrees.md` |
| Caregiver review | `docs/admin/caregivers-review.md` |
| Care Homes operational review, inventory, disputes, refunds, and payouts | `docs/admin/care-homes.md` |
| National ID review | `docs/admin/identity-documents.md` |
| Care-needs assessment quiz | `docs/admin/care-assessments.md` |
| Bookings (cancellations & refunds) | `docs/admin/bookings.md` |
| Subscription plans, publication/retirement, and coupon configuration | `docs/admin/subscriptions.md` |
| Elderly medication operational reads (prescriptions, dose logs, adherence) | `docs/admin/elderly-medications.md` |
| Medication lateness CMS and operational evaluation | `docs/admin/elderly-medications.md` |
| Elderly help-request operations and history | `docs/admin/elderly-help-requests.md` |
| Elderly SOS operational reads, history, and status | `docs/admin/elderly-sos.md` |
| Durable notification inspection | `docs/admin/notifications.md` |
| Postman | General Admin: `docs/postman/admins/Sanad.Admin.postman_collection.json`; Care Homes operations: `docs/postman/admins/Sanad.Admin.CareHomes.postman_collection.json` |

## Durable notification inspection

Admin notification list, detail, timeline, and aggregate GET routes use
`AdminNotificationOperationalRead` for Normal SuperAdmin and SupportAdmin.
ContentAdmin cannot inspect notification records. Every request is audited
before its query; audit failure returns no notification data. All views exclude
records older than one rolling year. Record projections expose only ID,
category, type, created time, and read time; they omit recipient identity,
title, body, and destination. See
[docs/admin/notifications.md](notifications.md) for filters, paging, date
validation, response shapes, and remaining owner-verification items.

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

## National ID review

Under `/api/v1/admin/identity-documents/...` (`CaregiversAdmin`):

```text
GET    /identity-documents?page=&pageSize=&status=     paged list (no file URLs)
GET    /identity-documents/{userId}                    metadata only
GET    /identity-documents/{userId}/front              private front image
GET    /identity-documents/{userId}/back               private back image
POST   /identity-documents/{userId}/verify             Pending → Verified
POST   /identity-documents/{userId}/reject             Pending → Rejected (reason)
POST   /identity-documents/{userId}/revoke             Verified → Revoked; User Blocked
```

See `docs/admin/identity-documents.md`.

## Medication lateness boundary

The active medication lateness threshold is CMS-managed and versioned. CMS
`GET/POST /api/v1/admin/cms/medication-lateness...` uses `CmsContent` and is
available to Normal `SuperAdmin` and `ContentAdmin` accounts. The operational
`POST /api/v1/admin/elderly/medications/late/evaluate?dependentId=...` route
uses `ElderlyMedicationOperationalManage` and is available only to Normal
`SuperAdmin` and `SupportAdmin` accounts. SupportAdmin may manage this
evaluation action but does not edit prescription, dose-log, or identity source
records. Successful operational actions are audited; late alerts are durable
in-app only, with push/email/outbox/scheduler delivery deferred.
