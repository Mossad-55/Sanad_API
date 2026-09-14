# Admin legal content (SET-12)

Dedicated CMS for the app **Privacy Policy** and **Terms & Conditions**.
Legal pages are NOT splash screens: they live in the CMS `legal_documents` /
`legal_sections` tables and are served only through the routes in this file.

**Base:** `/api/v1/admin/legal-documents`  
**Policy:** `CmsContent`  
Requires a **normal** JWT (`access_type` = `Normal`) and `account_type` of
`SuperAdmin` or `ContentAdmin`.  
Support Admin, Family, Medical Caregiver, Companion Caregiver, Elderly, and
restricted verification tokens receive **403** (framework policy rejection —
a bare 401/403 with no business `code`). Missing/invalid token → **401**.

## Access table

| Role | JWT `account_type` | Legal content write/read (`CmsContent`) |
|---|---|---|
| Super Admin | `SuperAdmin` | Yes |
| Content Admin | `ContentAdmin` | Yes |
| Support Admin | `SupportAdmin` | No (read-only; not granted) |
| Family / Caregiver / Elderly | app types | No (they use the signed-in app reads only) |

## Enum values (numeric JSON binding)

```text
LegalDocumentType: PrivacyPolicy = 1, TermsAndConditions = 2
LegalAudience:     Family = 1, MedicalCaregiver = 2, CompanionCaregiver = 3, Elderly = 4
LegalDocumentStatus: Draft = 1, Published = 2, Archived = 3
LegalSectionType:  Text = 1, UserRights = 2
```

There is **no** legal (or help) audience for `SuperAdmin`, `ContentAdmin`, or
`SupportAdmin`. App reads always derive the audience from the JWT; an admin
calling an app route receives `403 Cms.Content.UnsupportedAudience`.

## Endpoints summary

```text
POST   /api/v1/admin/legal-documents                  Create Draft (version assigned server side) -> 201
GET    /api/v1/admin/legal-documents                  List all versions (filters: documentType, audience, status)
GET    /api/v1/admin/legal-documents/{id}             One full version including sections
PUT    /api/v1/admin/legal-documents/{id}             Replace Draft sections (full list) -> 200
POST   /api/v1/admin/legal-documents/{id}/publish     Publish Draft -> 200 (full published version)
```

All bodies are `application/json`.

## Lifecycle: Draft → Published → Archived

```text
POST create        -> Draft, version = max(existing versions for (type, audience)) + 1 (starts at 1)
PUT  update        -> only while Draft
POST publish       -> that Draft becomes Published (publishedOnUtc set);
                      the previous Published version of the same (type, audience)
                      becomes Archived — in ONE transactional SaveChanges
Archive retention  -> Archived (and every older) version stays stored and is still
                      queryable here by id or list filters. Nothing is deleted, ever.
```

Invariants enforced by CMS validation and the domain:

- at least one section per document;
- unique **positive** `displayOrder` per document;
- bilingual: `arabicTitle`/`englishTitle` and `arabicDescription`/`englishDescription`
  are required in **both** languages on every section;
- a **PrivacyPolicy** version must contain **exactly one** `UserRights` section,
  and that section must carry at least one Arabic **and** one English bullet;
- a **TermsAndConditions** version must **not** contain a `UserRights` section;
- bullets are optional for `Text` sections;
- all localized content is authored in the CMS — the API never synthesizes or
  falls back to static legal text (no "حقوق المستخدم" placeholder exists in code).

Limits (named constants, validated on both layers): titles ≤ 150 chars,
descriptions ≤ 4000 chars, ≤ 20 bullets per language per section, each bullet
≤ 500 chars, ≤ 40 sections per document.

## Requests

### Create a Draft

`POST /api/v1/admin/legal-documents` → `201 Created`

```json
{
  "documentType": 1,
  "audience": 1,
  "sections": [
    {
      "sectionType": 1,
      "displayOrder": 1,
      "arabicTitle": "جمع البيانات",
      "englishTitle": "Data collection",
      "arabicDescription": "نجمع البيانات اللازمة لتقديم الرعاية.",
      "englishDescription": "We collect the data required to provide care.",
      "arabicBullets": null,
      "englishBullets": null
    },
    {
      "sectionType": 2,
      "displayOrder": 2,
      "arabicTitle": "حقوق المستخدم",
      "englishTitle": "User rights",
      "arabicDescription": "حقوقك في بياناتك.",
      "englishDescription": "Your rights over your data.",
      "arabicBullets": ["الوصول إلى البيانات", "طلب التصحيح", "طلب الحذف"],
      "englishBullets": ["Access your data", "Request correction", "Request deletion"]
    }
  ]
}
```

`201` response (`LegalDocumentResponse`):

```json
{
  "id": { "value": "0191ae10-0000-7000-8000-000000000001" },
  "documentType": 1,
  "audience": 1,
  "version": 1,
  "status": 1,
  "createdOnUtc": "2026-09-14T10:00:00Z",
  "updatedOnUtc": "2026-09-14T10:00:00Z",
  "publishedOnUtc": null,
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
      "arabicBullets": ["الوصول إلى البيانات", "طلب التصحيح", "طلب الحذف"],
      "englishBullets": ["Access your data", "Request correction", "Request deletion"]
    }
  ]
}
```

`id` is the repository's strongly typed id JSON shape `{ "value": "<guid>" }`.
`version` is **server assigned**; clients never send it, and neither status
nor publication timestamps are client writable.

### List versions

`GET /api/v1/admin/legal-documents?documentType=1&audience=1&status=2` → `200`

All three filters are optional (numeric enum values). Returns every matching
version — Draft, Published, and Archived — newest version first within each
`(documentType, audience)` pair.

### Get one version

`GET /api/v1/admin/legal-documents/{id}` → `200` (full `LegalDocumentResponse`
including ordered sections) · unknown id → `404 Cms.Legal.NotFound`.

### Update a Draft

`PUT /api/v1/admin/legal-documents/{id}` → `200`  
Body: `{ "sections": [ ... ] }` only — the full replacement list, same
section shape as create. Draft only; the response is the full updated version.

### Publish

`POST /api/v1/admin/legal-documents/{id}/publish` → `200` with the full
published version. Publishing a Draft archives the previous Published version
of the same pair. Publishing an already Published document is an idempotent
success. Publishing an Archived document → `409`.

## Errors

| HTTP | `code` | When |
|---|---|---|
| 400 | `Api.Validation.Failed` | Request shape validation (missing/long/blank fields, bad enum values, shape violations surfaced by validation) |
| 401 | *(bare, no code)* | Missing/invalid JWT (framework policy) |
| 403 | *(bare, no code)* | Policy rejection for accounts without `CmsContent` (framework policy) |
| 403 | `Cms.Content.UnsupportedAudience` | Admin account types are not app audiences (app read routes only) |
| 404 | `Cms.Legal.NotFound` | Unknown legal version id |
| 404 | `Cms.Legal.NotPublished` | App read: no current Published version for the (type, audience) pair |
| 409 | `Cms.Legal.DraftAlreadyExists` | A second Draft for the same (type, audience) pair |
| 409 | `Cms.Legal.PublishedDocumentImmutable` | Update attempted on Published/Archived |
| 409 | `Cms.Legal.InvalidOperation` | Publish of an Archived version or a domain rule violation |

## App reads (for completeness)

Signed-in app audiences call (see `docs/app/settings/legal.md`):

```text
GET /api/v1/legal/privacy-policy
GET /api/v1/legal/terms
```

They receive only the current Published version for their own audience,
derived from the JWT. There is no audience parameter to override.

## Migration

The `cms.legal_documents` / `cms.legal_sections` tables (with the unique
`document_type + audience + version` index, the filtered unique Draft index,
and the current-published read index) are created by the owner's EF migration
`AddLegalAndHelpCenterContent`. The worker branch does not contain migrations.
