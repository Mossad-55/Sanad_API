# Admin Help Center (SET-13)

FAQ entries per app audience and one global support contact (hotline card).
All content is CMS-managed; nothing is hardcoded in the API.

**Base:** `/api/v1/admin/help-center`  
**Policy:** `CmsContent` for FAQs; `CmsHelpContact` for the two
`support-contact` actions  
Requires a **normal** JWT (`access_type` = `Normal`) and:
`SuperAdmin` or `ContentAdmin` for FAQ routes; `SuperAdmin`, `SupportAdmin`
or `ContentAdmin` for `GET`/`PUT support-contact`.  
Support Admin, Family, Medical Caregiver, Companion Caregiver, Elderly, and
restricted verification tokens receive **403** on FAQ routes (framework policy
rejection — bare 401/403 with no business `code`). Missing/invalid token →
**401**.

## Access table

| Role | JWT `account_type` | FAQ read/write (`CmsContent`) | Support contact read/write (`CmsHelpContact`) |
|---|---|---|---|
| Super Admin | `SuperAdmin` | Yes | Yes |
| Content Admin | `ContentAdmin` | Yes | Yes |
| Support Admin | `SupportAdmin` | No (not granted) | Yes (contact card only) |
| Family / Caregiver / Elderly / Care-home Owner | app types | No (they use the signed-in `GET /api/v1/help-center`) | No |

Support Admin gains **only** the two `support-contact` actions. FAQ
create/update/activate/deactivate and every other CMS write stay with
`CmsContent`, so Support Admin still receives **403** there.

## Enum values (numeric JSON binding)

```text
LegalAudience: Family = 1, MedicalCaregiver = 2, CompanionCaregiver = 3, Elderly = 4, CareHomeOwner = 5
```

An FAQ belongs to exactly one of the five app audiences. `CareHomeOwner = 5`
is part of the Help Center surface as of F5: a Care-home Owner signs in and
reads its own FAQ list like the other app roles. There is no FAQ or
support-contact audience for the three admin account types.

Create/update validate the audience as a Help Center audience. An undefined
numeric value returns the standard `400 Api.Validation.Failed` response with an
`audience` field error and must not fall through to an unexpected server error.

## Endpoints summary

```text
POST /api/v1/admin/help-center/faqs                  Create FAQ -> 201
GET  /api/v1/admin/help-center/faqs                  Admin list (filters: audience, isActive)
GET  /api/v1/admin/help-center/faqs/{id}             One FAQ
PUT  /api/v1/admin/help-center/faqs/{id}             Update FAQ -> 200
POST /api/v1/admin/help-center/faqs/{id}/activate    Idempotent activate -> 200
POST /api/v1/admin/help-center/faqs/{id}/deactivate  Idempotent deactivate -> 200
GET  /api/v1/admin/help-center/support-contact       Current global pair (both null when unconfigured) -> 200
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
  200: { "supportPhone": null, "supportEmail": null,
         "createdOnUtc": "0001-01-01T00:00:00Z", "updatedOnUtc": "0001-01-01T00:00:00Z" }
       (nothing configured yet — the empty state is a success, never 404)

PUT /api/v1/admin/help-center/support-contact
  body: { "supportPhone": "+201000000001", "supportEmail": "support@sanad.example" }
  200 with the stored pair (upsert of the single global row)
```

Each field is **independently nullable** and is validated only when it is
populated: `supportPhone` must be E.164, max 16 chars; `supportEmail` must be a
valid address, max 254 chars. A malformed populated value is still
`400 Api.Validation.Failed` naming that field.

`PUT` has **replacement semantics per field**:

- a supplied value replaces the stored one;
- `null` **clears that field** and leaves the other untouched —
  `{ "supportPhone": null, "supportEmail": null }` clears both;
- a blank string (`""` or whitespace) is **rejected with `400`** for that
  field; use `null` to clear;
- the singleton row is preserved by a clear; it is never deleted, so a
  subsequent `GET` returns `200` with null fields.

Repeated PUTs update the same row — persistence additionally guarantees the
table holds at most one row (`id = 1` check constraint).

Clients hide an unset contact option and fall back to in-app support; the
public reading surface is `GET /api/v1/support/contact` (see
`docs/auth/support-contact.md`), which has always answered the same two-null
shape when nothing is configured.

## App read (for completeness)

Signed-in app audiences call `GET /api/v1/help-center[?audience=]` (see
`docs/app/settings/help-support.md`). The audience is derived from the token's
`account_type` claims; the optional `audience` query parameter names one the
caller already owns when the token owns more than one, so no role can read
another role's FAQ list. Admin tokens on that route get
`403 Cms.Content.UnsupportedAudience` (a malformed `audience` is
`400 Cms.Content.InvalidAudience` for every account class).

## Errors

| HTTP | `code` | When |
|---|---|---|
| 400 | `Api.Validation.Failed` | Request shape validation |
| 401 | *(bare, no code)* | Missing/invalid JWT (framework policy) |
| 403 | *(bare, no code)* | Policy rejection: accounts without `CmsContent` on FAQ routes, or without `CmsHelpContact` on the two `support-contact` actions (framework policy) |
| 400 | `Cms.Content.InvalidAudience` | App read only: malformed/undefined `audience` (checked first, for every account class including admin) |
| 400 | `Cms.Content.AudienceRequired` | App read only: `audience` omitted or empty/whitespace while the token owns more than one audience |
| 403 | `Cms.Content.UnsupportedAudience` | Admin account types are not app audiences, or an app caller named an `audience` it does not own (app read route) |
| 404 | `Cms.Help.FaqNotFound` | Unknown FAQ id |

## Migration

The `cms.help_faqs` and `cms.support_contacts` tables (with the
`audience + is_active + display_order` read index and the singleton check
constraint) are created by the owner's EF migration
`AddLegalAndHelpCenterContent`. `cms.support_contacts.support_phone` and
`cms.support_contacts.support_email` are made nullable by
`MakeSupportContactNullable`, so an unset field is stored as SQL `NULL`
(never an empty string). The worker branch does not contain applied
migrations; the migration file is additive and is applied only in authorized
release work.
