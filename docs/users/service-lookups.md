# Service lookups (app / public)

The list of active caregiver **Services** shown during onboarding and filtering.

**Route:** `GET /api/v1/lookups/services`  
**Auth:** none (`AllowAnonymous`)  
**Success:** `200`  
**Empty list:** `200` with `[]`.

Inactive services are never returned. Results are ordered by Arabic name.

## Example

```http
GET /api/v1/lookups/services
```

```json
[
  {
    "id": { "value": "01900000-0000-7000-8000-000000000001" },
    "arabicName": "طبيب",
    "englishName": "Doctor",
    "iconPath": "services/01900000-0000-7000-8000-000000000001.png",
    "caregiverType": 1
  }
]
```

`id` is a strongly typed id: JSON object `{ "value": "<guid>" }`.  
`caregiverType`: `1` Medical, `2` Companion.  
`iconPath` is a storage key; the icon is publicly readable at:

```text
GET {baseUrl}/files/{iconPath}
```

Admin create/rename/activate lives under `/api/v1/admin/lookups/services` — see `docs/admin/service-lookups.md`.