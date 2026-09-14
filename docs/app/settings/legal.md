# Legal pages — Privacy Policy & Terms (app / settings)

The in-app **Privacy Policy** and **Terms & Conditions** screens render
dedicated CMS legal content (SET-12). These pages are **not** splash
screens — `GET /api/v1/splash-screens` serves the onboarding carousel only.

Both routes are for **signed-in app accounts**. The audience is always
derived from the JWT `account_type` claim; the client must never send an
audience (no query parameter, no body field).

| Screen action | Endpoint | Auth |
|---|---|---|
| Privacy Policy | `GET /api/v1/legal/privacy-policy` | Normal JWT (any of the four app types) |
| Terms & Conditions | `GET /api/v1/legal/terms` | Normal JWT (any of the four app types) |

## Audience behavior

```text
Family = 1, MedicalCaregiver = 2, CompanionCaregiver = 3, Elderly = 4
```

Each app role only ever receives the current **Published** version authored
for its own audience. There is no fallback to another audience or another
document type, and there are no legal pages for admin account types.

## Success response (200)

Bilingual fields; `sections` is ordered by `displayOrder`. Every privacy
policy version includes a dedicated `userRights`-type section (`sectionType: 2`)
whose `arabicBullets` / `englishBullets` are the rights list rendered as the
"حقوق المستخدم" block — the app renders exactly what the CMS returns and never
invents legal text.

```json
{
  "id": { "value": "0191ae10-0000-7000-8000-000000000001" },
  "documentType": 1,
  "audience": 1,
  "version": 2,
  "status": 2,
  "createdOnUtc": "2026-09-14T10:00:00Z",
  "updatedOnUtc": "2026-09-14T10:00:00Z",
  "publishedOnUtc": "2026-09-14T12:00:00Z",
  "sections": [
    {
      "sectionType": 1,
      "displayOrder": 1,
      "arabicTitle": "جمع البيانات",
      "englishTitle": "Data collection",
      "arabicDescription": "نجمع البيانات اللازمة لتقديم الرعاية.",
      "englishDescription": "We collect the data required to provide care.",
      "arabicBullets": [],
      "englishBullets": []
    },
    {
      "sectionType": 2,
      "displayOrder": 2,
      "arabicTitle": "حقوق المستخدم",
      "englishTitle": "User rights",
      "arabicDescription": "حقوقك في بياناتك.",
      "englishDescription": "Your rights over your data.",
      "arabicBullets": ["الوصول إلى البيانات", "طلب التصحيح"],
      "englishBullets": ["Access your data", "Request correction"]
    }
  ]
}
```

Field notes:

- `documentType`: `1` PrivacyPolicy, `2` TermsAndConditions (echoes the route).
- `audience`: the caller's audience (1–4).
- `version`: server-assigned version of the current publication.
- `status`: always `2` (Published) on app reads.
- `id` is the strongly typed id JSON shape `{ "value": "<guid>" }`.
- Render Arabic or English per the app language; both are always present.

## Empty / not-published behavior

If CMS has not published a version for the caller's (document type,
audience) pair yet:

```json
HTTP 404
{
  "type": "https://httpstatuses.com/404",
  "title": "Not Found",
  "status": 404,
  "detail": "The requested resource was not found.",
  "instance": "/api/v1/legal/privacy-policy",
  "code": "Cms.Legal.NotPublished"
}
```

Show "not available yet"; do not cache a fabricated page. The client may
also keep a bundled copy of the last fetched version for offline display.

## Errors

| HTTP | `code` | When |
|---|---|---|
| 401 | *(bare, no code)* | Missing/invalid/expired JWT |
| 403 | *(bare, no code)* | Restricted verification token (framework policy) |
| 403 | `Cms.Content.UnsupportedAudience` | Admin account types are not app audiences |
| 404 | `Cms.Legal.NotPublished` | No current Published version for this pair |
| 400 | `Api.Validation.Failed` | Request validation failures |

## Authoring

Content, versioning (Draft → Published → Archived), and per-audience
publication are managed by admins — see `docs/admin/legal-content.md`.
