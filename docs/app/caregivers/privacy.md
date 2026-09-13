# Caregiver privacy preferences

Caregiver visibility and privacy toggles live under `/api/v1/caregiver/privacy`. They control how the caregiver appears in discovery and how optional data is surfaced.

## Access

All routes require the `CaregiverAccess` policy (Normal JWT, `MedicalCaregiver` or `CompanionCaregiver` account). See `overview.md`.

- Family JWT → bare 403 (policy Forbid, no code)
- Unauthenticated → 401

## GET `/api/v1/caregiver/privacy`

Returns the caller's current visibility preferences.

```http
GET /api/v1/caregiver/privacy
Authorization: Bearer {{caregiverToken}}
```

`200` response:

```json
{
  "showProfile": true,
  "showRating": true,
  "showPhone": false,
  "shareLocation": false
}
```

Defaults (for new caregivers and pre-migration rows):

- `showProfile` = true
- `showRating` = true
- `showPhone` = false
- `shareLocation` = false

| HTTP | `code` |
|---|---|
| 401 | — |
| 403 | — (bare policy Forbid) |
| 404 | `Caregivers.Onboarding.NotFound` |

## PUT `/api/v1/caregiver/privacy`

Full replace of the caller's visibility preferences.

```http
PUT /api/v1/caregiver/privacy
Authorization: Bearer {{caregiverToken}}
Content-Type: application/json

{
  "showProfile": false,
  "showRating": true,
  "showPhone": false,
  "shareLocation": false
}
```

`204 No Content` on success.

Rules:

- All four booleans are required; the endpoint is a full replace, not partial.
- Allowed in any status that allows profile edits today (Onboarding, NeedsCorrection, Active, etc). The update only changes the visibility flags and `updated_on_utc`; it does not change status or availability.
- `showProfile = false` hides the caregiver from discovery search (`GET /api/v1/caregivers`). The other three toggles are stored-only for now (rating = Phase I, phone not exposed anywhere yet, location = Phase G).

| HTTP | `code` |
|---|---|
| 400 | `Api.Validation.Failed` |
| 401 | — |
| 403 | — (bare policy Forbid) |
| 404 | `Caregivers.Onboarding.NotFound` |

## Enforcement

- Discovery search (`Discovery` folder query handler behind `GET /api/v1/caregivers`) EXCLUDES caregivers with `ShowProfile = false`. The filter is implemented in `CaregiversDbContext.SearchActiveCaregiversAsync` via `COALESCE(c.show_profile, true) = true`.
- The other three toggles are stored-only for now (rating = Phase I, phone not exposed anywhere yet, location = Phase G) — say exactly this in the docs.

## Storage

`VisibilityPreferences` is a value object in `Caregivers.Domain` with value equality. Persistence is table-split onto `caregivers`:

- `show_profile` bool default true
- `show_rating` bool default true
- `show_phone` bool default false
- `share_location` bool default false

No migration file is committed by the worker; the owner generates the migration from the EF configuration.

## Postman

See `Sanad.App.Caregiver` collection: folder `07. Privacy` with `GET privacy` and `PUT privacy` requests.

## Bruno

- `collections/Sanad/caregiver/10-privacy-get.bru` — seq 10, GET privacy, bearer `{{jwt}}`, asserts 200 + `showProfile` eq true.
- Logout requests keep logout last: `08-logout-owner.bru` seq 8→9, `09-logout-caregiver.bru` seq 9→11 if needed.
