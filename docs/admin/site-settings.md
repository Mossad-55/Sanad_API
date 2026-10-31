# Admin site settings

Authoring surface for the **one** site-settings row: the editable navbar links
and the SEO defaults served by the anonymous public read
`GET /api/v1/site-settings` (see `docs/app/public/site-settings.md`).

**Base:** `/api/v1/admin/site-settings`
**Policy:** `CmsContent`
Requires a **normal** JWT (`access_type` = `Normal`) and `account_type` of
`SuperAdmin` or `ContentAdmin`. Other roles receive **403**; missing/invalid
token → **401**.

There is exactly one settings row. Its key is fixed, so the first write
creates it and every later write updates that same row — no second row, no
draft/publish workflow, and no history surface.

## Read the settings

```text
GET /api/v1/admin/site-settings
```

**200** returns the current document:

```json
{
  "navbarLinks": [
    {
      "key": "library",
      "arabicLabel": "المكتبة",
      "englishLabel": "Library",
      "url": "/library"
    }
  ],
  "seo": {
    "arabicTitle": "أهلنا",
    "englishTitle": "Ahlena",
    "arabicDescription": "وصف",
    "englishDescription": "Description"
  },
  "createdOnUtc": "2026-10-22T09:00:00Z",
  "updatedOnUtc": "2026-10-22T09:00:00Z"
}
```

**404** `Cms.SiteSettings.NotFound` before anything has been authored — there
is no row to read yet.

`navbarLinks` is the authored list in the authored order; that order is the
navbar order. `seo` is `null` when no SEO value was authored, and each of its
members is `null` when that member was not authored.

## Update the settings

```text
PUT /api/v1/admin/site-settings
```

The request replaces the whole document. An omitted `navbarLinks` means "no
navbar links"; an omitted `seo` means "no SEO defaults". Nothing is kept
implicitly, generated or translated.

```json
{
  "navbarLinks": [
    {
      "key": "library",
      "arabicLabel": "المكتبة",
      "englishLabel": "Library",
      "url": "/library"
    },
    {
      "key": "about",
      "arabicLabel": "من نحن",
      "englishLabel": "About",
      "url": "https://ahlena.example/about"
    }
  ],
  "seo": {
    "arabicTitle": "أهلنا",
    "englishTitle": "Ahlena",
    "arabicDescription": "وصف",
    "englishDescription": "Description"
  }
}
```

**200** returns the stored document (the same shape as the read).
**400** `Cms.SiteSettings.Invalid` when the content is unusable — nothing is
stored and the current row is unchanged. **400** `Api.Validation.Failed` when
the request shape itself is out of bounds (the body names the offending
fields). **409** `Cms.SiteSettings.Conflict` when another writer created the
row first; read the row and retry the update.

### Authoring rules

- **Bilingual links.** Every link needs a `key`, an `arabicLabel`, an
  `englishLabel` and a `url`. A link with only one language is refused.
- **Link key.** Up to 60 characters, letters/digits/`-`/`_`/`.` only, and
  unique inside the document (case-insensitive).
- **Link labels.** Up to 120 characters each.
- **Link target.** Either a root-relative app route (`/library`) or an absolute
  `http`/`https` url. `javascript:`, `data:` and every other scheme are
  refused, and so are protocol-relative targets (`//host/path`).
- **Link count.** At most 20 links per document.
- **SEO defaults.** Titles up to 200 characters, descriptions up to 500
  characters each. A whitespace-only value is stored as absent.

Values are stored as authored apart from surrounding whitespace being trimmed;
a whitespace-only value becomes absent. Nothing is generated, translated or
back-filled.
