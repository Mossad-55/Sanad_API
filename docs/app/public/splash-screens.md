# Splash screens (app / public)

Shared onboarding splash for **Family, Medical Caregiver, Companion Caregiver, and Elderly**. There is no per-role audience. Every published screen is returned to every app.

**Route:** `GET /api/v1/splash-screens`  
**Auth:** none (`AllowAnonymous`)  
**Success:** `200`  
**Empty list:** `200` with `[]` — the client continues to the next flow.

Draft screens are never returned.

## Example

```http
GET /api/v1/splash-screens
```

```json
[
  {
    "id": { "value": "01900000-0000-7000-8000-000000000001" },
    "arabicTitle": "مرحبا",
    "englishTitle": "Welcome",
    "arabicDescription": "وصف قصير",
    "englishDescription": "Short description",
    "arabicButtonText": "التالي",
    "englishButtonText": "Next",
    "imagePath": "splash/welcome.png",
    "backgroundColor": "#1A73E8",
    "displayOrder": 0
  }
]
```

`id` is a strongly typed id: JSON object `{ "value": "<guid>" }`.

`imagePath` is a storage **key** such as `splash/{guid}.jpg`.  
The image is publicly readable at:

```text
GET {baseUrl}/files/{imagePath}

`backgroundColor` is `#RRGGBB`.

Order is `displayOrder` ascending.

## Errors

This GET has no business 4xx for “no screens”. Validation failures use `Api.Validation.Failed` (`400`) if they occur.

Admin create/publish lives under `/api/v1/admin/splash-screens` and is documented in `docs/admin/splash-screens.md`.

## Privacy Policy and Terms & Conditions are NOT splash screens

Legal pages are dedicated CMS content (SET-12), not published splash
screens. The app renders them on the **Privacy & Security** settings screen
from the signed-in routes:

```text
GET /api/v1/legal/privacy-policy
GET /api/v1/legal/terms
```

Both require a normal app JWT; the audience is derived from the token and
cannot be overridden. See `docs/app/settings/legal.md` (app) and
`docs/admin/legal-content.md` (admin). The carousel on this page
(`GET /api/v1/splash-screens`) stays anonymous and is unrelated to legal
content.
