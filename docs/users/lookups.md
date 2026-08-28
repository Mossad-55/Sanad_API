# Lookups (app / public)

Active lookup lists for caregiver onboarding dropdowns. All anonymous, active-only, `200` with `[]` when empty.

Strongly typed ids are returned as `{ "id": { "value": "<guid>" }, ... }`.

## Services
`GET /api/v1/lookups/services` — active services with icons. See `docs/users/service-lookups.md`.

## Languages
`GET /api/v1/lookups/languages` — ordered by `code`.
```json
[
  { "id": { "value": "…" }, "code": "ar", "arabicName": "العربية", "englishName": "Arabic" }
]
```

## Governorates
`GET /api/v1/lookups/governorates` — ordered by English name.
```json
[
  { "id": { "value": "…" }, "arabicName": "البحيرة", "englishName": "Beheira" }
]
```

## Cities
`GET /api/v1/lookups/cities?governorateId={governorateGuid}` — cities of one governorate.

The `governorateId` query parameter is **required**. Returns a city only when **both** the city and its governorate are active; `200` with `[]` if the governorate is inactive or has no active cities. Ordered by English name.
```json
[
  { "id": { "value": "…" }, "arabicName": "دمنهور", "englishName": "Damanhur" }
]
```

## Areas
`GET /api/v1/lookups/areas?cityId={cityGuid}` — areas of one city (service locations the caregiver selects).

The `cityId` query parameter is **required**. Returns an area only when the **full chain** (area + city + governorate) is active; deactivating the parent city or governorate hides the area here even though the area itself remains active. Ordered by English name.
```json
[
  { "id": { "value": "…" }, "arabicName": "مركز دمنهور", "englishName": "Damanhur Markaz" }
]
```

Caregivers select **Area** ids only (never city or governorate).

Inactive records never appear here. Admin management (including inactive records) lives under `/api/v1/admin/lookups/...`:

- Services: `docs/admin/service-lookups.md`
- Languages & governorates: `docs/admin/lookups-languages-governorates.md`
- Cities & areas: `docs/admin/lookups-cities-areas.md`
