# Lookups (app / public)

Active lookup lists for caregiver onboarding dropdowns. All anonymous, active-only, `200` with `[]` when empty.

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

## Services
`GET /api/v1/lookups/services` — active services with icons; see `docs/users/service-lookups.md`.

Inactive records never appear here. Admin management (including inactive records) lives under `/api/v1/admin/lookups/...` — see `docs/admin/lookups-languages-governorates.md`.