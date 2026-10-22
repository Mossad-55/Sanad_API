# Legal pages — Privacy Policy & Terms (app / settings)

The in-app **Privacy Policy** and **Terms & Conditions** screens render
dedicated CMS legal content (SET-12). These pages are **not** splash
screens — `GET /api/v1/splash-screens` serves the onboarding carousel only.

Both routes are for **signed-in app accounts**. The audience is derived from
the JWT `account_type` claims, which carry **one claim per owned account**.
An optional `audience` query parameter selects the audience explicitly when
the token owns more than one; there is no audience body field.

The same two documents are also readable **without any login** through the
anonymous public route (`GET /api/v1/public/legal/{documentType}`) when an
admin has published them for the **Public** audience — see
[Public (no-login) reads](#public-no-login-reads). The **Help Center is not
public**: it stays on the authenticated route only.

| Screen action | Endpoint | Auth |
|---|---|---|
| Privacy Policy | `GET /api/v1/legal/privacy-policy[?audience=]` | Normal JWT (Family, Medical Caregiver, Companion Caregiver, Elderly, or Care Home Owner) |
| Terms & Conditions | `GET /api/v1/legal/terms[?audience=]` | Normal JWT (Family, Medical Caregiver, Companion Caregiver, Elderly, or Care Home Owner) |
| Privacy Policy / Terms (public website) | `GET /api/v1/public/legal/{documentType}` | **Anonymous** — no token |


## Audience behavior

`audience` is a `LegalAudience` value. Send the **numeric** value (the enum
name is also accepted, case-insensitive). This value space is separate from
`AccountType`: Care Home Owner is `5` here, not `8`.

```text
Family = 1, MedicalCaregiver = 2, CompanionCaregiver = 3, Elderly = 4, CareHomeOwner = 5
```

`Public` (`0`) is the anonymous public-website audience. It is **not** a
token-derived app audience, so it is never accepted on these routes:
`?audience=0` and `?audience=Public` are `400 Cms.Content.InvalidAudience`
(exactly like any other undefined value), and no token ever resolves to it.
It is served only by the anonymous route below.

An empty or whitespace-only value (`?audience=`) is treated exactly like an
omitted parameter. If a client repeats the parameter
(`?audience=1&audience=5`), ASP.NET Core's scalar query binding keeps the
**first** value (`ValueProviderResult.FirstValue`,
`Microsoft.AspNetCore.Mvc.ModelBinding` — no custom binder is registered); that
is not a privilege issue because whichever value arrives is still validated
against the audiences the token owns.

Selection is deterministic and never depends on the order of the token's
`account_type` claims; duplicate claims are collapsed:

- **`audience` supplied** and owned by the caller → that audience is served.
- **`audience` supplied** but malformed or undefined (non-numeric, out of
  range, or a comma list) → `400 Cms.Content.InvalidAudience`. This check runs
  **first and regardless of account class**: an admin token that sends a
  malformed `audience` also gets `400 Cms.Content.InvalidAudience`, never the
  `403` below.
- **`audience` supplied** and well-formed but **not owned** by the caller →
  `403 Cms.Content.UnsupportedAudience`.
- **`audience` omitted or empty/whitespace** with exactly **one** owned
  audience → that audience is selected automatically.
- **`audience` omitted or empty/whitespace** with **more than one** owned
  audience → coded `400 Cms.Content.AudienceRequired`. Account switching is
  client-local and does **not** reissue a JWT, so the caller must name the
  audience it wants; the server cannot infer the active portal from the token.
- **`audience` omitted or empty/whitespace** with **no owned app audience**
  (admin account types) → the documented
  `403 Cms.Content.UnsupportedAudience` denial.

Each app role only ever receives the current **Published** version authored
for its own audience. There is no fallback to another audience or another
document type, and there are no legal pages for admin account types.

Example — a multi-account token reading the Care Home Owner privacy policy:

```text
GET /api/v1/legal/privacy-policy?audience=5
Authorization: Bearer <normal access token>
```

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
- `audience`: the resolved audience (1–5; 5 = CareHomeOwner).
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

## Public (no-login) reads

The public website can show the **Privacy Policy** and **Terms & Conditions**
without any login (WEB-08 / T9-B decision 6A). This is deliberately a
**minimal public set**: only those two published documents for the `Public`
audience. The Help Center, FAQs and support-contact card remain
authenticated-only (the signed-in routes above); there is no anonymous
`?audience=` parameter and no anonymous Help Center route.

```text
GET /api/v1/public/legal/PrivacyPolicy
GET /api/v1/public/legal/TermsAndConditions
GET /api/v1/public/legal/1
GET /api/v1/public/legal/2
```

- No `Authorization` header is required (and none is read). The audience is
  fixed to `Public`; a caller can never request a different audience or a
  different document type here.
- `{documentType}` binds from the route as a `LegalDocumentType`: the numeric
  value (`1` PrivacyPolicy, `2` TermsAndConditions) **or** the enum name,
  case-insensitive. Only those two types exist.
- The response is exactly the same `LegalDocumentResponse` shape as the app
  read (bilingual ordered `sections` included), with `audience: 0`
  (`Public`) and `status: 2` (Published). It returns only the current
  **Published** version for `(type, Public)`; drafts and archived versions are
  never served, and there is no fallback to a different audience (so a
  published Family document does not satisfy a public read).

Example:

```text
GET /api/v1/public/legal/TermsAndConditions
```

```json
{
  "id": { "value": "0191ae10-0000-7000-8000-0000000000a1" },
  "documentType": 2,
  "audience": 0,
  "version": 1,
  "status": 2,
  "createdOnUtc": "2026-10-22T10:00:00Z",
  "updatedOnUtc": "2026-10-22T10:00:00Z",
  "publishedOnUtc": "2026-10-22T12:00:00Z",
  "sections": [
    {
      "sectionType": 1,
      "displayOrder": 1,
      "arabicTitle": "الشروط",
      "englishTitle": "Terms",
      "arabicDescription": "…",
      "englishDescription": "…",
      "arabicBullets": [],
      "englishBullets": []
    }
  ]
}
```

Failures (same ProblemDetails shape as the app routes):

| HTTP | `code` | When |
|---|---|---|
| 400 | `Cms.Content.InvalidDocumentType` | `{documentType}` is malformed or undefined (not `1`/`2`/`PrivacyPolicy`/`TermsAndConditions`) |
| 404 | `Cms.Legal.NotPublished` | No current **Published** version exists for the requested type and the `Public` audience |

Treat `404 Cms.Legal.NotPublished` as "not available yet" on the public site
too — do not render a fabricated page or fall back to bundled app copy as if
it were the current publication.

## Errors

| HTTP | `code` | When |
|---|---|---|
| 401 | *(bare, no code)* | Missing/invalid/expired JWT (app routes only; the public route is anonymous) |
| 403 | *(bare, no code)* | Restricted verification token (framework policy) |
| 400 | `Cms.Content.InvalidAudience` | App routes: `audience` is malformed or undefined (non-numeric, out of range, a comma list, or `0`/`Public`) — checked before ownership, for every account class including admin |
| 400 | `Cms.Content.AudienceRequired` | App routes: `audience` omitted or empty/whitespace while the token owns more than one audience |
| 403 | `Cms.Content.UnsupportedAudience` | App routes: `audience` is well-formed but not owned by the caller, or the caller owns no app audience (admin account types) |
| 400 | `Cms.Content.InvalidDocumentType` | Public route: `{documentType}` is malformed or undefined (not `1`/`2`/`PrivacyPolicy`/`TermsAndConditions`) |
| 404 | `Cms.Legal.NotPublished` | No current Published version for this (document type, audience) pair — app routes use the caller's audience, the public route uses `Public` |
| 400 | `Api.Validation.Failed` | Request validation failures (app routes) |

## Authoring

Content, versioning (Draft → Published → Archived), and per-audience
publication are managed by admins — see `docs/admin/legal-content.md`.
