# Landing (app / public) — Phase 1 reads

Public landing content. Editorial copy is CMS-managed; every displayed number
is either managed copy or a live aggregation — no displayed number is stored.

**Auth:** none (`AllowAnonymous`) on both routes below.

## Get published sections

**Route:** `GET /api/v1/landing/sections`
**Success:** `200`
**Empty list:** `200` with `[]` — the client renders the default landing.

Returns published, visible sections ordered by `displayOrder` ascending.
Draft and hidden sections are never returned. The virtual `stats` key is never
listed here; live counters come from `GET /api/v1/landing/stats` below.
(Testimonial/FAQ reference resolution and dangling-ref skipping land in
Phase 3; contact intake and history/audit land in later phases.)

```http
GET /api/v1/landing/sections
```

```json
[
  {
    "id": { "value": "01900000-0000-7000-8000-000000000001" },
    "type": "about",
    "arabicTitle": "من نحن",
    "englishTitle": "About",
    "arabicBody": "نص",
    "englishBody": "Body",
    "contentJson": null,
    "imagePath": null,
    "displayOrder": 0
  }
]
```

`id` is a strongly typed id: JSON object `{ "value": "<guid>" }`.
`type` is one of the closed registry keys: `hero`, `partner-logos`, `about`,
`features`, `activity-cards`, `steps`, `dashboard-promo`, `testimonials`,
`faq`, `contact-form`, `footer`. (`stats` is virtual and never listed.)
Nullable fields serialize as `null` when empty.

## Get live stats

**Route:** `GET /api/v1/landing/stats`
**Success:** `200`

Computed per request (no caching in v1; counts only, no PII):
families exclude soft-deleted rows; caregivers count `Active` rows by type;
facilities count `Approved` rows.

```http
GET /api/v1/landing/stats
```

```json
{
  "familiesCount": 12,
  "medicalCaregiversCount": 5,
  "companionCaregiversCount": 7,
  "approvedFacilitiesCount": 3,
  "totalCaregiversCount": 12
}
```

## Errors

Neither GET has a business 4xx for “no content”. Validation failures use
`Api.Validation.Failed` (`400`) if they occur.

## Preview (deferred)

Token-gated draft preview
(`GET /api/v1/landing/sections/preview?token=`, 401 on bad/expired token)
is deferred to Phase 2 by owner decision 2026-10-12 and has no route in v1.
