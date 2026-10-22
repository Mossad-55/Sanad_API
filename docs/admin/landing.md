# Admin landing CMS

CMS authoring for the public landing sections served by
`GET /api/v1/landing/sections` (see `docs/app/public/landing.md`).
Testimonial/FAQ reference resolution is live (see "Curated refs" below);
landing history/audit rows are written but have no read endpoint in v1.

**Base:** `/api/v1/admin/landing`
**Policy:** `CmsContent`
Requires a **normal** JWT (`access_type` = `Normal`) and `account_type` of
`SuperAdmin` or `ContentAdmin`. Other roles receive **403**; missing/invalid
token → **401**.

## Registry and lifecycle

Closed type registry (kebab keys): `hero`, `partner-logos`, `about`,
`features`, `activity-cards`, `steps`, `dashboard-promo`, `testimonials`,
`faq`, `contact-form`, `footer`. `stats` is virtual and can never be stored.

Lifecycle: `Draft = 1`, `Published = 2`, `Archived = 3`, plus `isVisible` and
`displayOrder` on every row. Drafts may be partial. Publishing requires
bilingual titles (hero also requires its cover image); incomplete publishes
return `400 Cms.LandingSection.Incomplete` naming the gap.

Published rows are immutable: `PUT` targeting a published id creates (or
updates) its draft twin (linked by `sourceId`) and returns the twin with
`200`. Publishing a twin moves previously published rows of the same type to
`Archived` — history is retained, and archived rows never appear in public
reads. Only drafts can be deleted (`204`); deleting published/archived
history returns `409 Cms.LandingSection.PublishedHistory`.

Every publish, visibility change, and delete writes an immutable audit row
(actor, section, action, UTC; visibility also records the value, deletes
record the type key). Audit rows survive section deletion and have no
update/delete paths; there is no history read endpoint in v1.

## Media deletion (unreferenced landing assets only)

`DELETE /api/v1/admin/landing/media?key=<url-encoded key>`

Single asset per request. Only public landing assets are eligible: the key must
start with `landing/`, must not start with `private/`, must normalize to a
relative path that stays inside the effective public storage root after
`Path.GetFullPath` containment, and must resolve to a file that exists on
disk. Anything else is rejected as `400 Cms.Landing.Media.InvalidKey` without
touching the filesystem or storage interface.

Statuses:

- `204` — the asset existed and was unreferenced, so it was deleted (empty body).
- `400` — missing, empty, malformed, traversal-escaping, `private/` or non-`landing/` key.
- `404` — a well-formed landing key whose file does not exist (`Cms.Landing.Media.NotFound`).
- `409` — the asset is still referenced by retained content (`Cms.Landing.Media.Referenced`); nothing is deleted.

Reference discovery (safety core) searches every retained reference source for
an exact match on the normalized key before deleting: every `cms.landing_sections`
row in every status (Draft, Published, Archived); the retained
`cms.landing_section_history` rows; every `cms.family_home_sections` row (including
its `items_json` payload, where media keys are stored); and the retained
`cms.family_home_section_history` rows. A stored payload that cannot be read is
treated as a reference (fail closed). The match is performed on the normalized
key (trim, backslash-to-slash, trim slashes, case-insensitive).

Accepted limitation: reference discovery and physical deletion are not atomic
with concurrent editorial writes; a reference created between the check and the
delete can still leave a dangling path. There is no cross-module foreign key
over a stored key and none is invented. The window is kept as small as the
implementation allows (check and delete in one handler, no second query round
trip). No bulk/folder/wildcard delete, no sweeper, no scheduled cleanup, and
no deletion triggered by section edits.

## Endpoints summary

```text
GET    /api/v1/admin/landing/sections                   Full list incl. drafts -> 200 {items, page, pageSize, totalCount}
POST   /api/v1/admin/landing/sections                   Create draft (closed type key) -> 201
PUT    /api/v1/admin/landing/sections/{id}              Edit draft, or draft-twin of published -> 200
POST   /api/v1/admin/landing/sections/{id}/publish      Publish (validates completeness) -> 200
POST   /api/v1/admin/landing/sections/{id}/visibility   Toggle isVisible {value: bool} -> 204
DELETE /api/v1/admin/landing/sections/{id}              Delete draft only -> 204
POST   /api/v1/admin/landing/sections/reorder           Batch displayOrder -> 204
POST   /api/v1/admin/landing/media                      Image upload (jpeg/png/webp or sanitized svg) -> 201 {key}
DELETE /api/v1/admin/landing/media?key={key}            Delete one unreferenced asset -> 204 | 400 | 404 | 409
```

## Requests

List filters are optional: `status=Draft|Published|Archived`,
`type=<registry key>`, `search` (both titles), `page`/`pageSize` (default
20, cap 100). Invalid enum values return `400`.

Create/update bodies carry the bilingual copy plus `contentJson`
(type-specific structured content), `imagePath` (storage key from the media
endpoint), `displayOrder >= 0`, and `isVisible`. Unknown ids return
`404 Cms.LandingSection.NotFound`.

Reorder takes `{items: [{id, displayOrder}]}`; duplicate or unknown ids
return `400 Cms.Landing.ReorderInvalid`.

Media accepts `multipart/form-data` with a single `file`. The request is capped
at 5 MB (`[RequestSizeLimit]`); the file that is actually persisted is capped by
`Storage:Local:MaxBytes` (`DefaultMaxBytes` = 2 MiB), so an upload between the
two caps returns `400 Storage.File.TooLarge`:

- `image/jpeg`, `image/png`, `image/webp` — accepted after a byte-signature check.
- `image/svg+xml` — accepted only **after sanitization**, never on the declared
  MIME type alone. The sanitizer parses the payload as XML with DTD processing
  prohibited and no external resolver, so a DTD/entity payload (XXE or entity
  expansion) fails to parse and is **rejected**, not filtered; a payload that
  cannot be parsed safely, whose root element is not `<svg>`, or that nests
  deeper than 256 elements is rejected too. From the parsed tree it removes
  `<script>`, `<foreignObject>`, `<style>`, `<iframe>`, `<embed>`, `<object>`
  and the SMIL animation elements (`<set>`, `<animate>`, `<animateTransform>`,
  `<animateMotion>`, `<discard>`), XML comments and processing instructions,
  every `on*` event-handler attribute, and every external reference
  (`href`/`xlink:href`/`src` that is not a same-document `#fragment`, and any
  retained attribute whose value carries a `url(...)` that is not a
  same-document `url(#…)`). The **sanitized** bytes are what is stored, and an
  SVG served from `/files` carries `X-Content-Type-Options: nosniff` and a
  strict `Content-Security-Policy`.

Invalid, unsafe or unparseable content and oversize files return `400`
(`Storage.File.Empty`, `Storage.File.TooLarge` or `Storage.File.UnsupportedType`).
SVG is the one allowlisted type that is restricted by folder: it can only be
persisted through this landing route, so no other upload path can store an
unsanitized SVG.

## Curated refs (testimonials, FAQ, contact config)

`testimonials` and `faq` sections store ordered ref arrays in `contentJson`:

```json
{ "refs": [{ "kind": "caregiver", "id": "<rating-guid>", "hidden": false }] }
{ "refs": [{ "id": "<faq-guid>", "hidden": false }] }
{ "requiredFields": ["name", "phone", "message"], "inboxNote": "Riyadh intake" }
```

- Testimonial `kind` is `caregiver` (a `CaregiverRating` id) or `family`
  (a family `BookingReview` id). Resolution is live: stars/text from the
  rating, names from the rater's family (fallback “Verified family”).
- FAQ `id` is a `HelpFaq` id; only active entries resolve (audience is
  ignored — explicit curation wins). Bodies always come from HelpCenter.
- `hidden: true` suppresses a ref without deleting it. Dangling refs
  (missing/inactive/unknown) are skipped in public reads and counted in the
  admin list `danglingRefCount` — fix or remove them; they never `500`.
- `contact-form` sections carry field config only; submissions go to the
  shared authenticated `POST /api/v1/support/contact` flow.
