# Admin landing CMS

CMS authoring for the public landing sections served by
`GET /api/v1/landing/sections` (see `docs/app/public/landing.md`).
Testimonial/FAQ reference wiring lands in Phase 3; history/audit in Phase 4.

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

## Endpoints summary

```text
GET    /api/v1/admin/landing/sections                   Full list incl. drafts -> 200 {items, page, pageSize, totalCount}
POST   /api/v1/admin/landing/sections                   Create draft (closed type key) -> 201
PUT    /api/v1/admin/landing/sections/{id}              Edit draft, or draft-twin of published -> 200
POST   /api/v1/admin/landing/sections/{id}/publish      Publish (validates completeness) -> 200
POST   /api/v1/admin/landing/sections/{id}/visibility   Toggle isVisible {value: bool} -> 204
DELETE /api/v1/admin/landing/sections/{id}              Delete draft only -> 204
POST   /api/v1/admin/landing/sections/reorder           Batch displayOrder -> 204
POST   /api/v1/admin/landing/media                      Image upload -> 201 {key}
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

Media accepts `multipart/form-data` with a single `file` (jpeg/png/webp,
signature-checked, 5 MB cap); invalid content or size returns `400`.
