# Family home CMS

Admin authoring for the shared, personalised-free Family home read served by
`GET /api/v1/family/home/sections` (see `docs/app/families/home.md`). This is a
separate surface and separate storage from the anonymous Landing CMS
(`docs/admin/landing.md`): publishing a family home section never changes
Landing content, and the Landing virtual `stats` key does not exist here.

**Base:** `/api/v1/admin/family-home/sections`
**Policy:** `CmsContent` — a Normal JWT (`access_type` = `Normal`) with
`account_type` of `SuperAdmin` or `ContentAdmin`. Other roles receive **403**;
a missing or invalid token returns **401**.

The ids in this surface are flattened `Guid` values (request and response), not
the `{ "value": "…" }` wrapper used by the Landing CMS. See
[api-contract-conventions](api-contract-conventions.md).

## Routes

| Method | Route | Contract |
|---|---|---|
| GET | `/api/v1/admin/family-home/sections?status=&kind=&search=&page=&pageSize=` | Paged section list (drafts included) → `200` `{ items, page, pageSize, totalCount }` |
| GET | `/api/v1/admin/family-home/sections/{id}` | Typed section detail → `200` |
| POST | `/api/v1/admin/family-home/sections` | Create draft → `201` |
| PUT | `/api/v1/admin/family-home/sections/{id}` | Edit draft, or create/update the draft twin of a published row → `200` |
| POST | `/api/v1/admin/family-home/sections/{id}/publish` | Publish (validates stored content) → `200` |
| POST | `/api/v1/admin/family-home/sections/{id}/visibility` | Set `isVisible` → `204` |
| DELETE | `/api/v1/admin/family-home/sections/{id}` | Delete a draft only → `204` |
| POST | `/api/v1/admin/family-home/sections/reorder` | Batch `displayOrder` → `204` |

There is **no media-upload and no media-validation route** on this surface.
Image/icon keys are produced by the existing CMS upload flow
(`POST /api/v1/admin/landing/media` under the `landing` folder) and are only
referenced here. There is also no history read endpoint in v1; history rows are
written, not served.

## Section kinds

Closed registry, kebab keys: `banners`, `quick-links`, `featured-services`.
Requests accept those keys, and the stored enum names (`Banners`,
`QuickLinks`, `FeaturedServices`) case-insensitively; a bare number is refused
with `400 Cms.FamilyHome.InvalidKind` — unlike the older string filters
described in the conventions file, the numeric enum value was never part of
this contract and two spellings of one filter must not disagree. Responses
always use the kebab key in `kind`.

## Content model

An item is a **typed object**, never a JSON string:

```json
{
  "arabicTitle": "عنوان",
  "englishTitle": "Title",
  "arabicBody": "نص",
  "englishBody": "Body",
  "imageKey": "landing/hero.png",
  "iconKey": "landing/icon.png",
  "target": { "type": "services", "serviceId": null },
  "serviceId": null
}
```

Per kind:

- `banners` — required Arabic and English title plus `imageKey`; optional
  bilingual body and optional `target`.
- `quick-links` — required Arabic and English label plus `target`; optional
  `iconKey`.
- `featured-services` — required `serviceId` only. Featured entries use the live
  catalog name, icon and caregiver type and must not carry editorial overrides
  (`arabicTitle`, `englishTitle`, `arabicBody`, `englishBody`, `imageKey`,
  `iconKey`, `target`), and their ids cannot repeat inside one section.

Locked rules, applied to every create/update:

- Optional copy is supplied in both languages or omitted in both.
- Caps: titles/labels ≤ 200 characters, bodies ≤ 4000, image/icon keys ≤ 500.
- At most 10 items per section. Drafts may be empty; a published section needs
  at least 1 item.
- `serviceId` is only allowed on featured entries.
- `target.type` is exactly `services` (whole catalog, no `serviceId`) or
  `service` (one non-empty `serviceId`), compared ordinally. There are no
  arbitrary URLs, routes or extra parameters.
- Item order is the array order; there is no per-item `displayOrder`.
- Section `displayOrder` is a non-negative integer set by create/update or by
  `reorder`, and reads return ascending order.

A rejected item shape returns `400 Cms.FamilyHome.SectionInvalid`.

The stored payload is capped at 40,000 characters by the
`cms.family_home_sections.items_json` column, which is tighter than ten maximal
items; a payload over that cap is refused by the domain (`400
Cms.FamilyHome.SectionInvalid`). Stored JSON keeps text verbatim (Arabic is
stored literally, not as `\uXXXX` escapes) so the Admin `search` filter can
match it.

## Lifecycle, twins and history

`Draft → Published → Archived`, plus `isVisible`.

- `PUT` on a draft edits it in place. `PUT` on a published row creates (or
  updates) its **draft twin**, linked by `sourceId` = the published id, and
  returns the twin with `200`; the published row is untouched until the twin is
  published.
- `POST /{id}/publish` publishes the draft, appends an immutable `Published`
  history row (actor, UTC) and archives the previously published section of the
  same kind. Only one published section per kind exists at a time.
- Visibility changes append a `VisibilityChanged` row; deletions append a
  `Deleted` row (recording the kind key and, for a twin, the published id) that
  survives the deleted section.
- Everything is optimistic-concurrency guarded by a `version` token. Every
  Admin read (and every successful write) returns the current `version`.
  `expectedVersion` semantics: `0` means "no precondition, use the current
  version"; a positive value must equal the current version of the row the edit
  applies to (otherwise `409 Cms.FamilyHome.StaleVersion`); a negative value is
  malformed (`400 Cms.FamilyHome.SectionInvalid`). For a published source row
  the precondition names the twin when a twin exists, otherwise the published
  row being forked.
- Archived rows are immutable history: `PUT`, `visibility` and `publish` return
  `409 Cms.FamilyHome.ImmutableHistory`.
- Only drafts can be deleted (`204`). Published and archived sections return
  `409 Cms.FamilyHome.PublishedHistoryConflict`, and deleting a draft twin never
  touches the published row it came from.

## Requests

List filters (Admin only):

- `status` — `Draft`, `Published` or `Archived`, case-insensitive. An unknown
  value returns `400 Cms.FamilyHome.SectionInvalid`.
- `kind` — the kebab kind key. An unknown value returns `400
  Cms.FamilyHome.InvalidKind`.
- `search` — case-insensitive substring over the stored item copy (Arabic or
  English). There is no title column: the copy lives inside the items.
- `page` (default 1, minimum 1) and `pageSize` (default 20, clamped to 1–100).

Create body:

```json
{
  "kind": "banners",
  "items": [ { "arabicTitle": "عنوان", "englishTitle": "Title", "imageKey": "landing/hero.png" } ],
  "displayOrder": 0,
  "isVisible": true
}
```

Update body: `items`, `displayOrder`, `isVisible`, `expectedVersion`.
Visibility body: `{ "value": false, "expectedVersion": 0 }`.
Publish body: `{ "expectedVersion": 0 }`.
Reorder body: `{ "items": [ { "id": "<section-guid>", "displayOrder": 0 } ] }` —
each entry must name an existing section exactly once with `displayOrder >= 0`,
otherwise `400 Cms.FamilyHome.ReorderInvalid`. Reordering bumps the version of
every listed section.

### Public media keys

`imageKey`/`iconKey` name an existing **public** file. A key is rejected with
`400 Cms.FamilyHome.SectionInvalid` when it

- starts with `private/` (that folder is the private storage sibling and is
  never served as family home media),
- normalizes outside the effective public storage root
  (`Storage:Local:RootPath`, default `<app base>/sanad-files`),
- or does not exist on disk.

Create and update validate the keys in the request; **publish re-validates the
stored keys**, so a draft written before its asset moved cannot go live.

## Responses

Detail and list items share one shape:

```json
{
  "id": "01900000-0000-7000-8000-000000000001",
  "kind": "quick-links",
  "version": 2,
  "status": "Draft",
  "displayOrder": 0,
  "isVisible": true,
  "sourceId": "01900000-0000-7000-8000-000000000002",
  "items": [ { "arabicTitle": "الخدمات", "englishTitle": "Services",
               "target": { "type": "services", "serviceId": null } } ],
  "createdOnUtc": "2026-10-21T10:00:00Z",
  "updatedOnUtc": "2026-10-21T10:05:00Z",
  "publishedOnUtc": null
}
```

`sourceId` is `null` for a standalone draft or a published row and set on a
draft twin. `status` is the enum name (`Draft`/`Published`/`Archived`).
Unspecified item fields serialize as `null`.

If a stored section payload cannot be parsed, the read **fails closed**: the
detail returns `400 Cms.FamilyHome.Incomplete`, and the list fails the same way
instead of reporting an empty section.

## Publish validation

Publishing validates what is stored, not just the last request:

- `400 Cms.FamilyHome.Incomplete` when the draft has no items, a banner has no
  image key, a quick link has no target, a featured entry has no catalog id, or
  a referenced catalog service is missing or inactive.
- `400 Cms.FamilyHome.SectionInvalid` when the stored payload breaks a content
  rule or a stored media key is no longer a valid public key.
- Publishing an already-published row returns `400
  Cms.FamilyHome.SectionInvalid` (Landing parity); an archived row returns `409
  Cms.FamilyHome.ImmutableHistory`; an unknown id returns `404
  Cms.FamilyHome.SectionNotFound`.

A reference that becomes missing or inactive *after* publication does not fail
the Family read: the affected item is omitted there, as described in
`docs/app/families/home.md`.

## Errors

| Code | Status | Meaning |
|---|---|---|
| `Cms.FamilyHome.SectionInvalid` | 400 | Content/shape violation, bad media key, malformed `expectedVersion`, unknown `status` token |
| `Cms.FamilyHome.InvalidKind` | 400 | Unknown section kind token |
| `Cms.FamilyHome.Incomplete` | 400 | Cannot publish, or stored content cannot be read |
| `Cms.FamilyHome.ReorderInvalid` | 400 | Empty, duplicated, unknown or negative reorder entry |
| `Cms.FamilyHome.SectionNotFound` | 404 | Unknown section id |
| `Cms.FamilyHome.StaleVersion` | 409 | `expectedVersion` no longer matches (or a concurrent write won) |
| `Cms.FamilyHome.PublishedHistoryConflict` | 409 | Published or archived section cannot be deleted |
| `Cms.FamilyHome.ImmutableHistory` | 409 | Archived section cannot be edited, published or re-hidden |

The status codes are mapped locally by the two family home controllers; the
shared `ResultProblemDetailsMapper` and the shared policy files are unchanged.
`401`/`403` come from the normal authentication/authorization pipeline.

## Adapter

`tools/Invoke-E05LocalHttpVerification.ps1` drives these routes and the Family
read against a loopback target with guarded INSERT-only fixtures (identities,
one family, synthetic catalog services, disposable sections and a synthetic
public file under `landing/`). It refuses to run when a module migration is
pending, so the phase migration must be applied by the release owner before its
HTTP checks can execute.
