# Admin Help Center (SET-13)

FAQ entries per app audience and one global support contact (hotline card).
All content is CMS-managed; nothing is hardcoded in the API.

**Base:** `/api/v1/admin/help-center`  
**Policy:** `CmsContent`  
Requires a **normal** JWT (`access_type` = `Normal`) and `account_type` of
`SuperAdmin` or `ContentAdmin`.  
Support Admin, Family, Medical Caregiver, Companion Caregiver, Elderly, and
restricted verification tokens receive **403** (framework policy rejection —
bare 401/403 with no business `code`). Missing/invalid token → **401**.

## Access table

| Role | JWT `account_type` | FAQ + support-contact write/read (`CmsContent`) |
|---|---|---|
| Super Admin | `SuperAdmin` | Yes |
| Content Admin | `ContentAdmin` | Yes |
| Support Admin | `SupportAdmin` | No (read-only; not granted) |
| Family / Caregiver / Elderly | app types | No (they use the signed-in `GET /api/v1/help-center`) |

## Enum values (numeric JSON binding)

```text
LegalAudience: Family = 1, MedicalCaregiver = 2, CompanionCaregiver = 3, Elderly = 4
```

An FAQ belongs to exactly one of the four app audiences. There is no FAQ or
support-contact audience for admin account types.

## Endpoints summary

```text
POST /api/v1/admin/help-center/faqs                  Create FAQ -> 201
GET  /api/v1/admin/help-center/faqs                  Admin list (filters: audience, isActive)
GET  /api/v1/admin/help-center/faqs/{id}             One FAQ
PUT  /api/v1/admin/help-center/faqs/{id}             Update FAQ -> 200
POST /api/v1/admin/help-center/faqs/{id}/activate    Idempotent activate -> 200
POST /api/v1/admin/help-center/faqs/{id}/deactivate  Idempotent deactivate -> 200
GET  /api/v1/admin/help-center/support-contact       Current global pair or 404
PUT  /api/v1/admin/help-center/support-contact       Upsert the single global row -> 200
```

All bodies are `application/json`. FAQs are **not versioned** and there is
**no destructive FAQ delete route** in this slice — deactivation hides an FAQ
from apps without removing history.

## FAQ create / update

`POST /api/v1/admin/help-center/faqs` → `201`

```json
{
  "audience": 1,
  "arabicQuestion": "كيف أغيّر كلمة المرور؟",
  "englishQuestion": "How do I change my password?",
  "arabicAnswer": "من الإعدادات ثم الأمان.",
  "englishAnswer": "Open Settings, then Security.",
  "displayOrder": 1,
  "isActive": true
}
```

Response (`HelpFaqResponse`):

```json
{
  "id": { "value": "0191ae10-0000-7000-8000-000000000002" },
  "audience": 1,
  "arabicQuestion": "كيف أغيّر كلمة المرور؟",
  "englishQuestion": "How do I change my password?",
  "arabicAnswer": "من الإعدادات ثم الأمان.",
  "englishAnswer": "Open Settings, then Security.",
  "displayOrder": 1,
  "isActive": true,
  "createdOnUtc": "2026-09-14T10:00:00Z",
  "updatedOnUtc": "2026-09-14T10:00:00Z"
}
```

`PUT` takes the same fields (full replace) and returns `200` with the updated
record. All four question/answer fields are required **in both languages**.
Limits (named constants): question ≤ 300 chars, answer ≤ 3000 chars,
`displayOrder >= 0`.

## Active / inactive behavior

- App reads return **only `isActive = true`** FAQs for the caller's own
  audience, ordered by `displayOrder` then stable id.
- Admin reads (`GET /faqs`) return **active and inactive** records unless the
  `isActive` filter is supplied; the optional numeric `audience` filter is
  also supported.
- `activate` / `deactivate` are idempotent (repeating them is a success and
  preserves the record).
- Unknown FAQ id → `404 Cms.Help.FaqNotFound`.

## Global support contact

One global pair — support phone + support email — for **every** app role.
It is not per-role. It is contact-card data only:

- **no SMS**, no email sending, no notification dispatch, no support-ticket
  behavior changes;
- the existing ticket intake route `POST /api/v1/support/contact` is
  separate and unchanged (see `docs/auth/support-contact.md`).

```text
GET /api/v1/admin/help-center/support-contact
  200: { "supportPhone": "+201000000001", "supportEmail": "support@sanad.example",
         "createdOnUtc": "...", "updatedOnUtc": "..." }
  404: Cms.Help.SupportContactNotFound   (not configured yet)

PUT /api/v1/admin/help-center/support-contact
  body: { "supportPhone": "+201000000001", "supportEmail": "support@sanad.example" }
  200 with the stored pair (upsert of the single global row)
```

Both fields are required and validated (`supportPhone` must be E.164, max 16
chars; `supportEmail` must be a valid address, max 254 chars). Repeated PUTs
update the same row — persistence additionally guarantees the table holds at
most one row (`id = 1` check constraint).

## App read (for completeness)

Signed-in app audiences call `GET /api/v1/help-center` (see
`docs/app/settings/help-support.md`). The audience is derived from the JWT;
there is no query-string override, and no role can read another role's FAQ
list. Admin tokens on that route get `403 Cms.Content.UnsupportedAudience`.

## Errors

| HTTP | `code` | When |
|---|---|---|
| 400 | `Api.Validation.Failed` | Request shape validation |
| 401 | *(bare, no code)* | Missing/invalid JWT (framework policy) |
| 403 | *(bare, no code)* | Policy rejection for accounts without `CmsContent` (framework policy) |
| 403 | `Cms.Content.UnsupportedAudience` | Admin account types are not app audiences (app read route) |
| 404 | `Cms.Help.FaqNotFound` | Unknown FAQ id |
| 404 | `Cms.Help.SupportContactNotFound` | Admin GET before any support contact is configured |

## Migration

The `cms.help_faqs` and `cms.support_contacts` tables (with the
`audience + is_active + display_order` read index and the singleton check
constraint) are created by the owner's EF migration
`AddLegalAndHelpCenterContent`. The worker branch does not contain migrations.
