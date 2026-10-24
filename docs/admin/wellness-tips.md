# Wellness tips CMS

Wellness-tip administration uses the `CmsContent` policy. SuperAdmin and
ContentAdmin may read and author the shared/global catalog with a Normal JWT;
SupportAdmin and app accounts are denied. The current CMS implementation is
global/shared. Tenant scope is **Needs Owner Verification**.

## Routes

| Method | Route | Contract |
|---|---|---|
| GET | `/api/v1/admin/wellness-tips?page=1&pageSize=20&status=&category=&search=` | Paged list; optional status/category/title search filters |
| GET | `/api/v1/admin/wellness-tips/{id}` | Draft, published, or archived detail |
| GET | `/api/v1/admin/wellness-tips/{id}/preview` | Same CMS detail response; does not publish content |
| POST | `/api/v1/admin/wellness-tips` | Create draft (multipart) |
| PUT | `/api/v1/admin/wellness-tips/{id}` | Update a draft (multipart; replacement file optional) |
| POST | `/api/v1/admin/wellness-tips/{id}/publish` | Publish a draft |
| POST | `/api/v1/admin/wellness-tips/{id}/archive` | Archive content |

List pagination defaults to page 1/page size 20 and clamps page size to 100.
Status values are `Draft`, `Published`, and `Archived`. Search matches either
the Arabic or English title. The optional `category` filter is an exact,
case-sensitive match against the stored category string (free text, no
taxonomy behind it — see below), not a keyword search. A published or
archived item cannot be edited;
an archived item cannot be published again. The API has no delete route.

## Multipart authoring

Create and update use `multipart/form-data`, with a request limit of 5 MiB.
The required fields are `arabicTitle`, `englishTitle`, `category`, and
`sectionsJson`; `file` is required for create and optional for update.
`category` is free text with no taxonomy or picklist behind it: it is stored
verbatim as a plain string of at most 100 characters, and an over-long value
is a domain failure (`409 Cms.WellnessTip.InvalidOperation`).
`sectionsJson` is a JSON array of at least one section (maximum 40), each with
`DisplayOrder`, `ArabicText`, and `EnglishText`. The current parser treats the
embedded section JSON property names case-sensitively; use this exact casing.
Display orders must be unique.

The file is saved through `IFileStorage` under `wellness-tips` with a generated
storage key. Only non-empty `image/jpeg`, `image/png`, and `image/webp` files
are accepted, and the JPEG/PNG/WebP byte signature must match the declared
content type. An update file replaces the stored image key; omitting `file`
keeps the existing image. The response exposes the storage `imagePath`, not a
guessed public URL.

Publication is a simple draft/published/archived lifecycle. A separate
clinical-review state, reviewer workflow, metrics, and tenant isolation are
not implemented and are **Needs Owner Verification**; do not infer them from
the CMS status.

All admin routes publish their typed success schemas in the generated OpenAPI
document (`/openapi/v1.json`): the paged list (`200`), detail/preview/
publish/archive (`200`), create (`201`), and update (`200`) responses carry
their actual response types. Every failure entry (the `404`/`409` entries,
the list `400`, the create/update `400`s, and the `401`/`403` auth answers)
documents a ProblemDetails schema: the generator's `[ApiController]`
client-error mapping adds it even to status-only annotations. OpenAPI has a
single response body per status, so the bodyless malformed-`sectionsJson`
path above cannot be expressed there; clients must also accept empty bodies
on those statuses. Shared enum/ID conventions
are in [api-contract-conventions](api-contract-conventions.md).

Create returns `201`; successful reads, updates, publish, and archive return
`200`. Unauthenticated requests return `401`, and authenticated roles outside
`CmsContent` return `403`. Errors include `Cms.WellnessTip.NotFound` (404),
`Cms.WellnessTip.NotPublished` (404 for the Elderly published-only detail),
and `Cms.WellnessTip.InvalidOperation` (409 for invalid lifecycle/content
operations). A malformed `sectionsJson` form field returns `400` with no
response body (an empty `BadRequest()` from the controller), unlike the
coded ProblemDetails errors around it. Empty/missing files return
`400 Storage.File.Empty`; oversized files return `400 Storage.File.TooLarge`;
unsupported content type or mismatched byte signatures return
`400 Storage.File.UnsupportedType`.
