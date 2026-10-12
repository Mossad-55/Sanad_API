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

## Testimonials and FAQ (resolved refs)

`testimonials` and `faq` sections carry curated references in `contentJson`:

```json
{ "refs": [{ "kind": "caregiver", "id": "<guid>" }, { "kind": "family", "id": "<guid>" }] }
{ "refs": [{ "id": "<faq-guid>" }] }
```

The public read resolves them live, in ref order:

- Testimonials resolve to `{kind, refId, stars (1–5), text, arabicName,
  englishName}` from real caregiver/family ratings. Names come from the
  rater's family; anonymous or unresolvable names fall back to
  “عائلة موثقة” / “Verified family”. Refs may carry `"hidden": true` to
  suppress them without deleting.
- FAQ refs resolve live from HelpCenter entries (active entries only),
  returning `{refId, arabicQuestion, englishQuestion, arabicAnswer,
  englishAnswer}` — never copied text.

Dangling refs (missing/inactive targets, unknown kinds, malformed JSON)
resolve as skipped — never `500`. The section still returns `200` with the
resolved items only. Admins see a per-section dangling-ref warning count on
the admin list.

## Contact form (shared support flow)

`contact-form` sections carry field configuration in `contentJson`
(`requiredFields`, inbox note). Submission uses the real, shared flow —
there is no separate landing ticket table:

```http
POST /api/v1/support/contact
```

Authenticated (app JWT). Body `{subject (3–150 chars), message (10–2000
chars)}`. Success `200` with the ticket receipt; validation `400`,
unauthenticated `401`. See the request in the Postman “04. Landing” folder.

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
