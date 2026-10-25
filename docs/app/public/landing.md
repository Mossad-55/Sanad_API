# Landing (app / public)

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
Testimonial and FAQ references are resolved live on this read (see below).

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
    "displayOrder": 0,
    "testimonials": null,
    "faqs": null
  }
]
```

`id` is a strongly typed id: JSON object `{ "value": "<guid>" }`. Nullable
fields serialize as `null` when empty.

## Section shape (all 11 stored types)

`type` is one of the closed registry keys: `hero`, `partner-logos`, `about`,
`features`, `activity-cards`, `steps`, `dashboard-promo`, `testimonials`,
`faq`, `contact-form`, `footer`. (`stats` is virtual and never listed.)

Every one of the 11 stored types returns the **same** envelope: the bilingual
copy (`arabicTitle` / `englishTitle`, plus optional `arabicBody` /
`englishBody`), the optional `imagePath` storage key, `displayOrder`, the two
resolved-ref arrays, and the type-specific `contentJson`. There is no nested
projection per type: `contentJson` is returned **raw**, exactly as the editor
stored it (raw-vs-typed is a settled implementation choice). The server
interprets it only where a type has a defined meaning:

| `type` | `contentJson` |
|---|---|
| `hero` | free-form; publishing a hero also requires `imagePath` |
| `partner-logos` | free-form |
| `about` | free-form |
| `features` | free-form |
| `activity-cards` | free-form |
| `steps` | free-form |
| `dashboard-promo` | free-form |
| `testimonials` | `{ "refs": [...] }` — resolved live into `testimonials` |
| `faq` | `{ "refs": [...] }` — resolved live into `faqs` |
| `contact-form` | field configuration; submissions use the shared support flow |
| `footer` | free-form |

"Free-form" means the API stores and returns the JSON string unchanged; it does
not reshape or validate the inner schema.

**Public image access.** `imagePath` is a raw storage key (at most 500
characters), not a URL. The static file middleware serves every storage file
publicly at `/files/<imagePath>` — anonymous, matching the `AllowAnonymous`
landing routes — so a client composes the display URL from the key as
`{baseUrl}/files/{imagePath}`. Only `/files/private/*` is guarded away with a
`404`; there is no per-file authorization on public storage keys, so nothing
private should ever be stored under one. Each section returns at most one
`imagePath`; resolved testimonial/FAQ refs are ordered by their stored ref
order and hidden or dangling refs are silently dropped (never an error).

**Bilingual nested text.** `contentJson` is a plain JSON **string**, validated
by length only (≤ 8000 characters) and transported verbatim. Sections carry
editable text as legacy plain strings over the same envelope
(`arabicTitle`/`englishTitle`/`arabicBody`/`englishBody`) **and** — where the
editor chooses, inside `contentJson` — as nested bilingual text objects such
as `{ "ar": "…", "en": "…" }`. Both forms transport unchanged: a nested
`{ar,en}` object arrives inside `contentJson` exactly as authored, and the
client picks the language. The server interprets only `refs` (testimonials/faq
above); it never resolves, normalizes or translates nested text. A resolved
nested-text projection does not exist — adding one would be a new product
decision, out of scope. Any option/choice values inside `contentJson` are
authored AR/EN strings; nothing is auto-translated.

`testimonials` and `faqs` resolve only for their own section type. A
`testimonials` section returns its resolved items in `testimonials` (possibly
`[]` when every ref is dangling or hidden) and an empty `faqs`; a `faq` section
is the mirror image. Every other section type returns `null` for both.

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

### Anonymous partner contact

A visitor **without an account** (a partner enquiry, or any anonymous
enquiry) uses the public partner-contact route instead — the destination is
the same admin support inbox, but the submission is stored as request data on
its own aggregate, because a support ticket always belongs to a signed-in
user:

```http
POST /api/v1/partner-contact
```

Anonymous. Body `{name, company?, phone?, companyType?, city?, email,
subject?, message}` — `phone` (optional, ≤ 32 characters, digits with an
optional leading `+`, at least one digit, and common separators), `companyType` (optional free
text, ≤ 100) and `city` (optional free text, ≤ 120) are additive fields with
the same rules as the existing fields; omitted or blank is stored as `null`.
Plus the required `Idempotency-Key` header; at most 5 submissions per hour
per caller.
Success `200` with the receipt; `400` validation, `409` idempotency conflict,
`429` rate limited. Full contract: [partner contact](partner-contact.md).

## Get live stats

**Route:** `GET /api/v1/landing/stats`
**Success:** `200`

Computed per request (no caching in v1; counts only, no PII):
families exclude soft-deleted rows; caregivers count `Active` rows by type;
facilities count `Approved` rows.

`familiesCount`, `medicalCaregiversCount`, `companionCaregiversCount` and
`approvedFacilitiesCount` are **live counts of real rows** — nothing here is a
stored or hard-coded number, and a store with no matching rows returns `0`
rather than a placeholder. `totalCaregiversCount` is the sum of the two
caregiver counters. The client may serve these reads behind its 60-second ISR
window; the endpoint itself stays per-request and has no revalidation webhook.

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
