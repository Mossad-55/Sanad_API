# Library (family-facing content)

The Library is family-facing educational content managed inside the CMS
module under the existing `CmsContent` policy (`SuperAdmin` or
`ContentAdmin`). All `/api/v1/library` routes require an authenticated
account; anonymous requests are rejected with `401`. Content is bilingual
(Arabic + English) and lives in two types: `Article` (ordered sections with
bullet lists) and `Video` (external YouTube/Vimeo/embed URL plus duration —
there is no video hosting or transcoding in V1).

- [Postman App library collection](../../postman/app/Sanad.App.Library.postman_collection.json)
- [Postman Admin collection (Library CMS folder)](../../postman/admins/Sanad.Admin.postman_collection.json)

## Categories

Categories are a bilingual, admin-managed lookup. The public list is
available to any authenticated account:

`GET /api/v1/library/categories` → `200` with a bare list of **active**
categories only, ordered by creation time then id:

```json
[
  { "id": "0198e2c2-4444-7777-8888-000000000001", "arabicName": "نصائح صحية", "englishName": "Health tips" }
]
```

Administrators with the `CmsContent` policy manage the full lookup under
`/api/v1/admin/library/categories`:

| Route | Method | Notes |
| --- | --- | --- |
| `…/categories` | `GET` | All categories (including inactive) with `isActive` and timestamps. |
| `…/categories` | `POST` | `{arabicName, englishName, isActive?}` → `201`. Names required, ≤200 chars, trimmed. Invalid → `400 Cms.LibraryCategory.Invalid`. |
| `…/categories/{id}` | `PUT` | Same body + `isActive` required → `200`. Unknown id → `404 Cms.LibraryCategory.NotFound`. |
| `…/categories/{id}` | `DELETE` | `204`. Referenced by any content → `409 Cms.LibraryCategory.InUse` (deactivate instead). |

Content referencing a category keeps the reference if the category is later
deactivated; the `SET NULL` delete guard only applies when the category row
itself is deleted, which is blocked while content references it.

## Content lifecycle

Content moves `Draft → Published → Archived`, mirroring community posts but
without moderation (authors are admins):

- `POST /api/v1/admin/library/content` — multipart form create (see below);
  new content always starts `Draft`.
- `PUT /api/v1/admin/library/content/{id}` — only `Draft` content can be
  updated (`409 Cms.LibraryContent.InvalidOperation` otherwise). Omitting the
  `file` part keeps the existing cover.
- `POST /api/v1/admin/library/content/{id}/publish` — `Draft → Published`;
  sets `publishedOnUtc` on first publish. Publishing an already-published
  item is a no-op success; publishing `Archived` content returns `409
  Cms.LibraryContent.InvalidOperation` (there is no restore-from-archive
  action in V1).
- `POST /api/v1/admin/library/content/{id}/archive` — `200`, idempotent.
- `GET /api/v1/admin/library/content?status=&type=&search=&page=&pageSize=`
  — admin paging list (all statuses; `search` matches both titles).
- `GET /api/v1/admin/library/content/{id}` — admin detail (full sections and
  video fields regardless of status).

Create/update body is `multipart/form-data` (5 MB request cap):

| Field | Type | Rules |
| --- | --- | --- |
| `type` | `Article` \| `Video` | Required; case-insensitive. |
| `categoryId` | UUID string, optional | Must exist and be active: unknown → `400 Cms.Library.InvalidCategory`, inactive → `409 Cms.Library.CategoryInactive`. |
| `arabicTitle` / `englishTitle` | string | Required, ≤200, trimmed. |
| `arabicSummary` / `englishSummary` | string | Required, ≤1000, trimmed. |
| `isPremium` | bool | Premium gating, see below. |
| `videoUrl` | string (Video only) | Absolute http(s) URL, ≤500. Articles must not send one. |
| `durationSeconds` | int (Video only) | Required, > 0. Articles must not send one. |
| `sectionsJson` | JSON string (Article only) | Array of `{displayOrder, arabicTitle, englishTitle, arabicBullets, englishBullets}`. ≥1 section, `displayOrder` positive and unique, ≤40 sections, ≤30 bullets each, bullet ≤500 chars. Videos must not send sections. |
| `file` | multipart file (optional on update, required on create) | Cover image; JPEG/PNG/WebP signatures validated server-side, ≤5 MB. Stored under the `library` prefix. |

Canonical form examples (also in Postman and exercised by controller tests):

| Field | Create Article | Update an existing Draft Video |
| --- | --- | --- |
| `type` | `Article` | `Video` |
| `categoryId` | Existing active category UUID | Existing active category UUID |
| `arabicTitle` | `نصيحة صحية` | `فيديو جديد` |
| `englishTitle` | `Healthy tip` | `New video` |
| `arabicSummary` | `ملخص قصير` | `ملخص قصير` |
| `englishSummary` | `Short summary` | `Short summary` |
| `isPremium` | `false` | `false` |
| `videoUrl` | Omit | `https://youtu.be/abc123` |
| `durationSeconds` | Omit | `300` |
| `sectionsJson` | See below | Omit |
| `file` | Choose a valid cover image | Omit to keep the old cover |

Article `sectionsJson` (send as one text form part, not a nested multipart object):

```json
[{"displayOrder":1,"arabicTitle":"الأول","englishTitle":"First","arabicBullets":["نقطة"],"englishBullets":["bullet"]}]
```

Invalid type combinations (article with `videoUrl`, video without duration,
sections on a video, duplicate display orders, …) return
`400 Cms.LibraryContent.Invalid` with a safe fixed detail, an `errors` map of
field paths to messages, and a `traceId`. Section keys accept both the
canonical camelCase (`displayOrder`, `arabicTitle`, `arabicBullets`, …) and
the legacy PascalCase spellings; JSON number handling is unchanged: section
`displayOrder` must be a JSON number, not a quoted numeric string. A
`sectionsJson` value of `[null]`, malformed JSON, or a non-null non-array root returns
`400 Cms.Library.InvalidSections`; an unrecognized `type` returns `400
Cms.Library.InvalidType`; a malformed `categoryId` returns `400
Cms.Library.InvalidCategory`. Indexed section failures use paths such as
`sectionsJson[0].displayOrder` and `sectionsJson[0].arabicBullets[0]`.
The legacy JSON root `null` normalizes to an empty section array (so Article
content rejects it); this differs from omitting the form field on update.

On update, omitted `sectionsJson` preserves the existing sections while an
explicit `[]` clears the requested array and fails for Article; an omitted
`file` keeps the existing cover. Type-specific rules are validated against the
persisted content type — the request `Type` remains parsed but ignored, so a
valid update is never rejected solely because that ignored value differs.
Rejected requests leave no new cover or content row; a failed update keeps the
old cover and sections, and only a newly uploaded uncommitted cover is cleaned
up.

## Public reads

Only `Published` content is visible through `/api/v1/library`. Draft or
archived ids return `404 Cms.LibraryContent.NotPublished` on detail.

`GET /api/v1/library/content?type=&categoryId=&search=&sort=&page=&pageSize=`
(all optional, combinable):

- `type=Article|Video` (case-insensitive; invalid → `400
  Cms.Library.InvalidType`).
- `categoryId=` — only content in that category (inactive/unknown categories
  simply match nothing).
- `search=` — case-insensitive match against Arabic/English titles and
  summaries.
- `sort=Newest|MostViewed` (default `Newest`; `MostViewed` breaks ties by
  recency then id; invalid → `400 Cms.Library.InvalidSort`).
- `page=` (≥1) and `pageSize=` (1–100). The envelope is a bare list — no
  wrapper object, no `totalCount`.

Every list item carries `isLiked`, `isSaved`, `isPremium`, and `canAccess`
computed for the authenticated caller, plus `viewCount`, `likesCount`,
category names, and `durationSeconds` (videos).

`GET /api/v1/library/content/{id}` returns the detail with `sections`
(articles) or `videoUrl` + `durationSeconds` (videos) — but see premium
gating below. Unknown or unpublished ids → `404
Cms.LibraryContent.NotPublished`.

## Premium gating

`isPremium` content is reserved for families whose **current** subscription
grants the `PremiumContent` benefit (benefit key 8). The check mirrors the
Families v1 rule: the family the user owns takes precedence, otherwise the
earliest (by creation) family they were invited into; deleted families never
grant access.

- `canAccess = !isPremium || subscription grants PremiumContent`.
- Gated detail returns **metadata only** with `canAccess: false`: titles,
  summaries, cover, category, counters, and `isPremium` stay visible;
  `videoUrl` is `null` and `sections` is empty. No error status is returned —
  clients render the teaser and prompt for upgrade.
- List items always include the teaser metadata with their `canAccess` flag.

## Saved, liked, and views

All three are per authenticated user, set explicitly, and only apply to
`Published` content (unknown/unpublished → `404 Cms.LibraryContent.NotFound`):

- `PUT /api/v1/library/content/{id}/saved` with `{"value": true|false}` —
  idempotent `204`. Repeat calls do not duplicate rows.
- `PUT /api/v1/library/content/{id}/liked` with `{"value": true|false}` —
  idempotent `204`; also maintains the content's `likesCount` (never below
  zero).
- `POST /api/v1/library/content/{id}/view` — `204`; increments `viewCount`
  **on every call** (documented: the client should call it once per actual
  open; the server does not deduplicate).
- `GET /api/v1/library/saved?page=&pageSize=` — the caller's saved published
  content, newest-saved-first, same item shape as the feed. Un-saving removes
  the item from the next listing.

## Recommendations

`GET /api/v1/library/recommendations?contentId=&categoryId=&page=&pageSize=`
— deterministic, no ML:

1. Seed precedence: `contentId` (the item being viewed) beats `categoryId`.
2. Same-category published content, newest first, excluding the seed id.
3. Top-viewed fallback fills the remainder (`viewCount` desc, then recency,
   then id).
4. Unknown seed ids simply skip step 2 and fall through to step 3.

Items use the same list shape, including `isLiked`/`isSaved`/`isPremium`/
`canAccess` for the caller.

## Error codes

| Code | Status | Meaning |
| --- | --- | --- |
| `Cms.LibraryCategory.NotFound` | 404 | Admin category id unknown. |
| `Cms.LibraryCategory.Invalid` | 400 | Category names failed validation. |
| `Cms.LibraryCategory.InUse` | 409 | Category referenced by content. |
| `Cms.Library.InvalidCategory` | 400 | Content `categoryId` unknown. |
| `Cms.Library.CategoryInactive` | 409 | Content `categoryId` inactive. |
| `Cms.LibraryContent.NotFound` | 404 | Interaction target unknown/unpublished. |
| `Cms.LibraryContent.NotPublished` | 404 | Public detail on non-published content. |
| `Cms.LibraryContent.Invalid` | 400 | Content failed validation; the response carries a fixed detail plus an `errors` map and `traceId`. |
| `Cms.LibraryContent.InvalidOperation` | 409 | Lifecycle violation (update after publish, re-publish archive rules). |
| `Cms.Library.InvalidType` / `InvalidSort` / `InvalidStatus` / `InvalidSections` | 400 | Query/form enum and section-JSON parsing failures. |
