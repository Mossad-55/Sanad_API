# Selections (services, languages, areas) and pricing

All routes require the `CaregiverAccess` policy. Every save returns the full `CaregiverProfileResponse` (see `profile.md`).

## Bulk replace selections

One endpoint sets the complete multi-select state for the services/languages/areas screen. Send the **full desired set**; the server diffs and applies only the adds/removes. Re-saving the same set is a no-op.

```http
PUT /api/v1/caregiver/selections
Content-Type: application/json
Authorization: Bearer {{caregiverToken}}

{
  "serviceIds": ["guid", "guid"],
  "languageIds": ["guid"],
  "areaIds": ["guid"]
}
```

Rules:

- Duplicate ids in an array are ignored.
- **Services** must exist, be active, and match the caregiver type (a Medical caregiver cannot select a Companion service). Minimum 1 to submit; an **Active** caregiver cannot remove his last service.
- **Languages** must exist and be active (languages are type-neutral). Minimum 1 to submit; Active caregiver cannot remove the last language.
- **Areas** must exist and be active with the **full active parent chain** (area → city → governorate all active). Maximum **10** areas; minimum 1 to submit; Active caregiver cannot remove the last area.
- Empty arrays are allowed while onboarding (saving partial progress); submission enforces minimums.

Errors:

- `404 Caregivers.Onboarding.NotFound` — no profile.
- `404 Caregivers.Lookups.NotFound` — any referenced id does not exist.
- `409 Caregivers.Onboarding.InactiveLookup` — a referenced service/language/area is inactive, a service is for the other caregiver type, or the area's city/governorate chain is inactive.

> Lookup ids come from the anonymous catalog endpoints: `GET /api/v1/lookups/services`, `/languages`, `/governorates`, `/cities?governorateId=`, `/areas?cityId=`, `/specializations`, `/professional-titles`, `/academic-degrees` (see `docs/app/public/lookups.md`).

## Medical pricing

Medical caregiver accounts only. Four prices, all required, all greater than zero, at most two decimal places.

```http
PUT /api/v1/caregiver/pricing/medical
Content-Type: application/json

{
  "homeVisitPrice": 150.00,
  "eightHourShiftPrice": 500.00,
  "twelveHourShiftPrice": 700.50,
  "twentyFourHourShiftPrice": 1200.00
}
```

Validation failures return `400 Api.Validation.Failed` with field messages (e.g. "Home Visit price must be greater than zero.", "… cannot have more than two decimal places."). A Companion account receives `409 Caregivers.Onboarding.WrongCaregiverType`.

## Companion pricing

Companion caregiver accounts only. Three prices, same >0 / ≤2-decimal rules.

```http
PUT /api/v1/caregiver/pricing/companion
Content-Type: application/json

{
  "hourlyPrice": 40.00,
  "eightHourDayPrice": 300.00,
  "overnightPrice": 450.00
}
```

A Medical account receives `409 Caregivers.Onboarding.WrongCaregiverType`.

## Response excerpt

The response includes the persisted arrays and pricing objects:

```json
{
  "serviceIds": ["…"],
  "languageIds": ["…"],
  "areaIds": ["…"],
  "medicalPricing": {
    "homeVisitPrice": 150.00,
    "eightHourShiftPrice": 500.00,
    "twelveHourShiftPrice": 700.50,
    "twentyFourHourShiftPrice": 1200.00
  },
  "companionPricing": null
}
```

Pricing is part of the submit readiness checklist (see `overview.md`).
