# Caregiver Discovery (family app)

Browsing of **Active** caregivers for the booking flow: search cards, the caregiver public profile, and a price quote.

All routes live under `/api/v1/caregivers...`.

> **Access changed (`9ae68cc`):** these routes are **no longer anonymous** — they require a **Normal JWT** (any account type: Family, Medical/Companion caregiver, Elderly, admin). A missing or invalid token receives `401`; a Restricted-verification token receives `403`.

## Endpoints

| Method | Route | Description |
|---|---|---|
| `GET` | `/api/v1/caregivers` | Paged caregiver search |
| `GET` | `/api/v1/caregivers/{caregiverId}` | Public profile: identity header, specialization, services, languages, areas, weekly schedule, pricing, verified certificate names |
| `GET` | `/api/v1/caregivers/{caregiverId}/quote?shiftType=&startTime=&endTime=` | Server-side price quote — the exact engine checkout uses |

## Search query parameters

| Param | Type | Notes |
|---|---|---|
| `search` | string? | Case-insensitive match on the caregiver's Arabic/English full name. |
| `type` | int? | `1` Medical, `2` Companion. |
| `gender` | int? | `1` Male, `2` Female. |
| `governorateId` | UUID? | Location scope filter. |
| `cityId` | UUID? | Location scope filter. |
| `areaId` | UUID? | Only caregivers with a selection in that area. |
| `specializationId` | UUID? | Specialization filter. |
| `availability` | int? | `1` Available, `2` Unavailable. |
| `minPrice` / `maxPrice` | decimal? | Price range filter. |
| `minRating` | decimal? | Minimum rating filter. |
| `minExperienceYears` | int? | Minimum years of experience. |
| `page` / `pageSize` | int | Defaults `page=1`, `pageSize=10`. Response carries the total count. |

Only caregivers in **Active** status are returned. Every response field maps directly to database state — absent values are `null` / `[]` (no synthetic fallbacks).

## Quote

`GET /api/v1/caregivers/{caregiverId}/quote?shiftType=1&startTime=10:00&endTime=12:00`

Returns the caregiver's base fee for the requested product plus the 15% platform fee and the total (`EGP`). Hourly bookings are priced from the exact time window; fixed products use the stored product price.

## Error Catalog

| Code | HTTP | When |
|---|---|---|
| `Caregivers.Discovery.CaregiverNotFound` | 404 | Caregiver id unknown. |
| `Caregivers.Discovery.QuoteNotAvailable` | 409 | Pricing not configured for the product, or product/caregiver-type mismatch. |
