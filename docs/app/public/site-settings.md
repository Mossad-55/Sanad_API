# Site settings (app / public)

The published navbar links and the SEO defaults that an app client renders.
The content is CMS-managed (see `docs/admin/site-settings.md`); this route only
serves what an administrator authored.

**Auth:** none (`AllowAnonymous`).

## Get site settings

**Route:** `GET /api/v1/site-settings`
**Success:** `200`

```http
GET /api/v1/site-settings
```

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

`navbarLinks` is the authored list in the authored order — that order is the
navbar order. Each link carries its stable `key`, its bilingual labels and its
navigation target.

`seo` is `null` when no SEO default was authored; each of its members is
`null` when that member was not authored.

**Empty store:** `200` with `navbarLinks: []` and `seo: null` — the read is
honest before anything is authored and never invents content.

The read exposes only the published settings content: no identity, no
timestamps and no authoring metadata.
