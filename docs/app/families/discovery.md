# Caregiver Discovery (family app)

Browsing of **Active** caregivers for the booking flow: search cards, the caregiver public profile, and a price quote.

All routes live under `/api/v1/caregivers...`.

> **Access changed (`9ae68cc`):** these routes are **no longer anonymous** — they require a **Normal JWT** (any account type: Family, Medical/Companion caregiver, Elderly, admin). A missing or invalid token receives `401`; a Restricted-verification token receives `403`.

## Endpoints

| Method | Route | Description |
|---|---|---|
| `GET` | `/api/v1/caregivers` | Paged caregiver search |
| `GET` | `/api/v1/caregivers/{caregiverId}` | Public profile: identity header, specialization, services, languages, areas, weekly schedule, pricing, verified certificate names |
| `GET` | `/api/v1/caregivers/{caregiverId}/photo` | Authorized avatar **download** (the S3 approved photo route, refund decision lock #32). Streams the image bytes only; the stored private `avatar_url` key is never returned. Non-discoverable or avatar-less caregivers return a generic `404` (`Caregivers.Photo.NotFound`). |
| `GET` | `/api/v1/caregivers/{caregiverId}/quote?shiftType=&startTime=&endTime=` | Server-side price quote — the exact engine checkout uses |

## Search query parameters

| Param | Type | Notes |
|---|---|---|
| `search` | string? | Case-insensitive match on the caregiver's Arabic/English full name. |
| `type` | int? | `1` Medical, `2` Companion. |
| `gender` | int? | `1` Male, `2` Female. |
| `governorateId` | UUID? | Location scope filter. Returns only caregivers with a working-area selection in the governorate; unset means no governorate restriction. |
| `cityId` | UUID? | Location scope filter. Returns only caregivers with a working-area selection in the city; unset means no city restriction. |
| `areaId` | UUID? | Only caregivers with a selection in that area. |
| `specializationId` | UUID? | Specialization filter. |
| `availability` | int? | `1` Available, `2` Unavailable. |
| `minPrice` / `maxPrice` | decimal? | Price range filter. |
| `minRating` | decimal? | Minimum rating filter. |
| `minExperienceYears` | int? | Minimum years of experience. |
| `page` / `pageSize` | int | Defaults `page=1`, `pageSize=10`. Response carries the total count. |

Only caregivers in **Active** status are returned. Every response field maps directly to database state — absent values are `null` / `[]` (no synthetic fallbacks).

**Avatar field (`avatarUrl`):** on the search cards and the public profile, `avatarUrl` is **only the authorized download path** `/api/v1/caregivers/{caregiverId}/photo`, or `null` when the caregiver has no stored avatar. It is never the stored Identity `avatar_url` key — the image bytes are served exclusively by the authorized photo route, which re-checks discoverability (Active + profile not hidden) and answers the generic `404` (`Caregivers.Photo.NotFound`) otherwise. A caregiver who hides their profile after publication therefore leaves a stale path on cached surfaces that resolves to that sanctioned generic `404`.

## Quote

`GET /api/v1/caregivers/{caregiverId}/quote?shiftType=1&startTime=10:00&endTime=12:00`

Returns the caregiver's base fee for the requested product, independent platform-fee and tax rates/amounts, the shared charge-rule version, and the total (`EGP`). Hourly bookings are priced from the exact time window; fixed products use the stored product price. If no effective shared Finance rule exists, the quote is unavailable rather than defaulting to zero.

## Error Catalog

| Code | HTTP | When |
|---|---|---|
| `Caregivers.Discovery.CaregiverNotFound` | 404 | Caregiver id is unknown or the caregiver is not active (profile and quote). |
| `Caregivers.Discovery.QuoteNotAvailable` | 409 | Pricing not configured for the product, or product/caregiver-type mismatch. |
| `Caregivers.Photo.NotFound` | 404 | Unknown, non-active or hidden caregiver, or a caregiver with no stored avatar / unreadable avatar file. The same generic 404 covers all cases. |
