# Elderly profile

The signed-in Elderly app can read the profile of the dependent linked to the
authenticated Elderly Identity. The API resolves that dependent from the JWT
identity; clients do not provide an elderly or dependent ID.

## Read my profile

```http
GET /api/v1/elderly/profile
Authorization: Bearer {{elderlyJwt}}
```

Authorization requires a Normal Elderly JWT. The response is `200` with:

```json
{
  "id": "…uuid…",
  "arabicFullName": "سعيد نصر",
  "englishFullName": "Saeed Nasr",
  "age": 78,
  "hasPhoto": true,
  "photoUrl": "/api/v1/elderly/profile/photo",
  "timeZoneId": "Africa/Cairo",
  "latestAssessment": null,
  "updatedOnUtc": "2026-09-25T12:30:00Z"
}
```

- `age` is computed from the stored date of birth using the current date in
  the Elderly profile's IANA `timeZoneId`, not the server UTC date.
- `latestAssessment` is nullable and contains the newest linked completed
  assessment when one exists.
- This self-service response intentionally does not expose `detailedAddress`
  or `healthNotes`.
- `photoUrl` is the authorized API path, not a public file URL. It is returned
  even when `hasPhoto` is false; requesting it without a stored photo returns
  `404 Families.Elderly.NotFound`.

Errors include `401` for a missing/invalid JWT and `404 Families.Elderly.NotFound`
when the authenticated identity is not linked to an Elderly dependent.

## Read my profile photo

```http
GET /api/v1/elderly/profile/photo
Authorization: Bearer {{elderlyJwt}}
```

The same Normal Elderly authorization and identity-only resolution apply. A
successful response is `200` image bytes (`image/jpeg`, `image/png`, or
`image/webp`) with a generated `elderly-{id}` filename. There is no public
static-file alternative.

## Timezone ownership

The Family Owner sets the linked dependent's timezone. Existing rows are
backfilled by the generated `20260925235151_AddElderlyProfileTimeZone`
migration to `Africa/Cairo`; the migration is generated and inspected but is
not applied by this documentation change. New dependents use the configured
default, `ElderlyProfile:DefaultTimeZoneId`, whose default is also
`Africa/Cairo`.

```http
PUT /api/v1/family/dependents/{dependentId}/timezone
Authorization: Bearer {{familyToken}}
Content-Type: application/json

{ "timeZoneId": "Africa/Cairo" }
```

Only the Family Owner may change it. Editors and Viewers receive `403
Families.Elderly.AccessDenied`; an invalid or non-IANA identifier returns
`400 Families.Elderly.InvalidProfile`; a dependent outside the family returns
`404 Families.Elderly.NotFound`. A successful response is `200`:

```json
{
  "dependentId": "…uuid…",
  "timeZoneId": "Africa/Cairo",
  "updatedOnUtc": "2026-09-25T12:30:00Z"
}
```

Family dependent list/get/create/update responses also include `timeZoneId`.
The emergency-contact flow and Admin Elderly profile inspection are separate
open gaps and are not provided by these routes.
