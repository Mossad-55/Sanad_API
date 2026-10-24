# Library (family-facing content)

The Library is family-facing educational content managed inside the CMS
module under the existing `CmsContent` policy (`SuperAdmin` or
`ContentAdmin`). All `/api/v1/library` routes require an authenticated
account; anonymous requests are rejected with `401`. Content is bilingual
(Arabic + English) and lives in two types: `Article` (ordered sections with
bullet lists) and `Video` (an uploaded MP4 stored by the API plus a duration —
there is no transcoding in V1, and playback is served by the API itself, not
by an external host).

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

Content moves `Draft → Published → Archived`, with one restore edge back from
`Archived → Draft`, mirroring community posts but without moderation (authors
are admins):

- `POST /api/v1/admin/library/content` — multipart form create (see below);
  new content always starts `Draft`.
- `PUT /api/v1/admin/library/content/{id}` — only `Draft` content can be
  updated (`409 Cms.LibraryContent.InvalidOperation` otherwise). Omitting the
  `file` part keeps the existing cover.
- `POST /api/v1/admin/library/content/{id}/publish` — `Draft → Published`;
  sets `publishedOnUtc` on first publish. Publishing an already-published
  item is a no-op success; publishing `Archived` content returns `409
  Cms.LibraryContent.InvalidOperation` (an archived item must be restored
  first).
- `POST /api/v1/admin/library/content/{id}/archive` — `200`, idempotent.
- `POST /api/v1/admin/library/content/{id}/restore` — `Archived → Draft` only,
  returning `200` with the content response. Restoring anything that is not
  archived is `409 Cms.LibraryContent.InvalidOperation`; an unknown id is
  `404 Cms.LibraryContent.NotFound`. A restore is **not** a publication:
  restored content is a normal draft again and must pass the same validation
  before it can be published.
- `GET /api/v1/admin/library/content?status=&type=&search=&page=&pageSize=`
  — admin paging list (all statuses; `search` matches both titles).
- `GET /api/v1/admin/library/content/{id}` — admin detail (full sections and
  video fields regardless of status).
- `POST /api/v1/admin/library/content/{id}/video` — attaches (or replaces) the
  uploaded MP4 bytes on `Draft` or `Published` content; see below.
- `DELETE /api/v1/admin/library/content/{id}/video` — detaches the stored bytes
  and then deletes the object; idempotent `204` when nothing is attached.

Create/update body is `multipart/form-data` (5 MB request cap):

| Field | Type | Rules |
| --- | --- | --- |
| `type` | `Article` \| `Video` | Required; case-insensitive. |
| `categoryId` | UUID string, optional | Must exist and be active: unknown → `400 Cms.Library.InvalidCategory`, inactive → `409 Cms.Library.CategoryInactive`. |
| `arabicTitle` / `englishTitle` | string | Required, ≤200, trimmed. |
| `arabicSummary` / `englishSummary` | string | Required, ≤1000, trimmed. |
| `isPremium` | bool | Premium gating, see below. |
| `videoUrl` | string (Video only) | **Optional legacy metadata.** When supplied it must be an absolute http(s) URL, ≤500; it is stored and shown in the admin detail but is never used for app playback. Articles must not send one. |
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
| `videoUrl` | Omit | Omit (legacy metadata; the bytes are uploaded separately) |
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

The same `errors` map is returned whether the failure is caught by the
controller (form parsing/validation) or by the command handler (domain
invariants) — both carry the stable `Cms.LibraryContent.Invalid` code and the
identical field paths (`arabicTitle`, `englishTitle`, `videoUrl`,
`durationSeconds`, `sectionsJson`, `sectionsJson[N].*`). Errors that are not
field-level validation (not-found, lifecycle, category failures) never carry
an `errors` map.

On update, omitted `sectionsJson` preserves the existing sections while an
explicit `[]` clears the requested array and fails for Article; an omitted
`file` keeps the existing cover. Type-specific rules are validated against the
persisted content type — the request `Type` remains parsed but ignored, so a
valid update is never rejected solely because that ignored value differs.
Rejected requests leave no new cover or content row; a failed update keeps the
old cover and sections, and only a newly uploaded uncommitted cover is cleaned
up.

### Uploading video bytes

A `Video` row carries no playable bytes until one is uploaded; the create and
update form does **not** accept a video file. Bytes are attached with a
dedicated route:

- `POST /api/v1/admin/library/content/{id}/video` — `multipart/form-data` with
  a single `file` part; returns `200` with the admin content response. Accepted
  on `Draft` and `Published` content. `Archived` content is
  `409 Cms.LibraryContent.InvalidOperation`; an `Article` target is
  `400 Cms.LibraryContent.Invalid` (field `videoStorageKey`); an unknown id is
  `404 Cms.LibraryContent.NotFound`.
- Accepted file: MP4 only — declared `video/mp4`, ≤50 MB
  (`52_428_800` bytes; the multipart request limit adds a small envelope
  allowance), and the bytes must carry the ISO base-media `ftyp` container
  marker, so a declared type alone is never trusted.
- The bytes are stored in **private** storage under the `library-video/`
  prefix and are never served as public static files. The storage key stays
  server-side: only the admin detail exposes it as `videoStorageKey`.
- Uploading over an existing reference replaces it: the superseded object is
  deleted only after the new reference is persisted, so a failed attach leaves
  no reference to missing bytes.
- `DELETE /api/v1/admin/library/content/{id}/video` — detaches the reference
  first (playback stops immediately) and then deletes the object; `204`. A
  target without bytes is a successful no-op. Unknown id is `404
  Cms.LibraryContent.NotFound`, `Article` is `400 Cms.LibraryContent.Invalid`,
  `Archived` is `409 Cms.LibraryContent.InvalidOperation`. The legacy
  `videoUrl` metadata is never touched by either route.

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

`GET /api/v1/library/content/{id}/video` streams the uploaded bytes of a
published, entitled video. It is an authenticated route: the caller must send
`Authorization`, and the API re-checks publication and the premium entitlement
itself. The response is the stored MP4 with byte-range support
(`enableRangeProcessing`), so a player can seek; the address never reveals the
storage key. Every failure — unknown id, not published, not a video, no
uploaded bytes, unreadable object, or missing entitlement — returns the same
`404 Cms.LibraryContent.NotPublished`, so the route cannot be used to probe
entitlement or stored-file state.

### Field sources and nullability

Every field of the four reads (`/categories`, `/content`, `/content/{id}`,
`/recommendations`) and of `/saved` comes from a stored row; nothing is
invented and nothing is defaulted.

| Field | List / detail | Source | `null` when |
| --- | --- | --- | --- |
| `id` | list + detail | the content row | never |
| `type` | list + detail | the stored type (`Article` or `Video`) | never |
| `arabicTitle` / `englishTitle` | list + detail | the content row | never |
| `arabicSummary` / `englishSummary` | list + detail | the content row | never |
| `coverImagePath` | list + detail | the stored storage key, e.g. `library/<file>.jpg` | never — a content row always has a cover |
| `videoUrl` | detail only | the generated relative playback route `/api/v1/library/content/{id}/video`, and only when the row has uploaded bytes **and** the caller is entitled | articles have no video; a video without uploaded bytes returns `null`, and a gated premium video returns `null` |
| `durationSeconds` | list + detail | the stored duration | articles have no duration |
| `sections` | detail only | the stored ordered sections | articles only; a gated premium article returns `[]` |
| `categoryId` | list + detail | the stored category reference | the content has no category |
| `categoryArabicName` / `categoryEnglishName` | list + detail | the referenced category row | the content has no category, or the category row is gone |
| `isPremium` | list + detail | the stored flag | never |
| `canAccess` | list + detail | the premium gate (below) | never |
| `isLiked` / `isSaved` | list + detail | the caller's own interaction rows | never (`false` when the caller never acted) |
| `viewCount` / `likesCount` | list + detail | the stored counters | never |
| `createdOnUtc` / `publishedOnUtc` | list + detail | the stored timestamps | `publishedOnUtc` is `null` only before the first publish, which the public reads never serve |

The category list (`GET /api/v1/library/categories`) returns the active
categories only, as `{ id, arabicName, englishName }`; `isActive` and the
timestamps are admin-only.

### Covers

`coverImagePath` is the **storage key** of the uploaded cover, saved under the
`library/` prefix (`library/<file>.jpg`). It is served as a public static file:
build the URL as `{baseUrl}/files/{coverImagePath}` and fetch it without an
`Authorization` header. The API never rewrites the key into an absolute URL,
and there is no image endpoint under `/api/v1/library`.

### The playable sample

A `Video` content is playable from uploaded bytes, and its real length is
`durationSeconds`. There is no transcoding in V1, no built-in demo sample, and
no arbitrary external fetch: a playable sample exists only once an
administrator has uploaded the MP4 bytes (`POST …/content/{id}/video`) and
published the row with a positive `durationSeconds`. An externally linked
`videoUrl` is legacy admin metadata and is **not** a playback source — an old
`example.com`-style link never plays, which is the intended locked behavior
until real bytes are uploaded for that row. An `Article` content has neither
(`videoUrl` is `null`, `durationSeconds` is `null`) and is read through
`sections`.

The consumer contract is:

1. `GET /api/v1/library/content/{id}` → `videoUrl` is either `null` (no bytes
   yet, or the caller is not entitled) or the relative playback path
   `/api/v1/library/content/{id}/video`.
2. If it is non-null, `GET` that path **with the same `Authorization`
   header** and play the returned MP4; byte-range requests are supported natively.
3. If it is `null`, render the teaser/upgrade state — do not call the playback
   route and do not try the legacy metadata URL.

A playback `404 Cms.LibraryContent.NotPublished` after a successful detail read
means the bytes were detached between the two calls (an admin delete, or a
replacement race); treat it as "video unavailable" rather than a client error.

Two card fields from the app design have **no implemented source** and are
therefore not returned: the content **author** (content rows carry no author)
and an article **read time** (no duration is stored for articles). Do not
render invented values for them.

## Premium gating

`isPremium` content is reserved for families whose **current** subscription
grants the `PremiumContent` benefit (benefit key 8). The check mirrors the
Families v1 rule: the family the user owns takes precedence, otherwise the
earliest (by creation) family they were invited into; deleted families never
grant access.

- `canAccess = !isPremium || subscription grants PremiumContent`.
- Gated detail returns **metadata only** with `canAccess: false`: titles,
  summaries, cover, category, counters, `durationSeconds` and `isPremium` stay
  visible; `videoUrl` is `null` and `sections` is empty. No error status is
  returned — clients render the teaser and prompt for upgrade.
- The playback route re-checks the same entitlement: an unentitled caller gets
  `404 Cms.LibraryContent.NotPublished` rather than a `403` or the bytes, so it
  cannot be used to probe whether premium bytes exist.
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
| `Cms.LibraryContent.NotFound` | 404 | Interaction target unknown/unpublished; also an unknown id on the admin video attach/delete routes. |
| `Cms.LibraryContent.NotPublished` | 404 | Public detail on non-published content; also every failure of `GET /api/v1/library/content/{id}/video` (unknown, unpublished, no bytes, unreadable, unentitled). |
| `Cms.LibraryContent.Invalid` | 400 | Content failed validation; the response carries a fixed detail plus an `errors` map and `traceId`. The admin video routes also use it for an `Article` target (field `videoStorageKey`). |
| `Cms.LibraryContent.InvalidOperation` | 409 | Lifecycle violation (update after publish, restore of a non-archived item, publish of an archived item, video upload/delete on archived content). |
| `Storage.File.Empty` / `TooLarge` / `UnsupportedType` | 400 | Admin video upload refused: missing/empty part, over 50 MB, not a declared `video/mp4`, or bytes without the `ftyp` marker. |
| `Cms.Library.InvalidType` / `InvalidSort` / `InvalidStatus` / `InvalidSections` | 400 | Query/form enum and section-JSON parsing failures. |
