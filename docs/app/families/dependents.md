# Elderly dependents

Dependents are the elderly people a family cares for. Adding a dependent **provisions an Elderly Identity login server-side**: the API creates an Identity user with no email and no password, `status = Active`, and a verified phone, so the dependent can immediately log in with SMS OTP (see `docs/auth/elderly-sms-login.md`). The family never sees or sets a password.

All routes require policy `FamilyAccess`. Permission matrix:

| Action | Owner | Editor | Viewer |
|---|---|---|---|
| Add / update / remove dependent, set photo | ✅ | ✅ | ❌ `403` |
| List / get dependents, download photo | ✅ | ✅ | ✅ |

## Dependent model

`DependentResponse`:

```json
{
  "id": "…uuid…",
  "familyId": "…uuid…",
  "identityUserId": "…uuid…",
  "arabicFullName": "سعيد نصر",
  "englishFullName": "Saeed Nasr",
  "gender": 1,
  "relationshipType": 7,
  "dateOfBirth": "1948-07-20",
  "hasPhoto": true,
  "detailedAddress": "12 Nile Street, Damanhur",
  "healthNotes": "Diabetes type 2; takes metformin.",
  "createdOnUtc": "2026-09-01T09:05:00Z"
}
```

- `gender`: `1` Male, `2` Female.
- `relationshipType`: the dependent's relationship **to the family** (e.g. `7` Grandfather means the dependent is the family member's grandfather). Values: see the `relationshipType` table in `overview.md` (`1` Father, `2` Mother, `7` Grandfather, `8` Grandmother, `15` Spouse, `99` Other, …). Required on add and update.
- `dateOfBirth`: `YYYY-MM-DD`, must not be in the future.
- `detailedAddress`: optional, ≤ 500 characters.
- `healthNotes`: optional, ≤ 2000 characters.
- `hasPhoto`: boolean only. **The photo path/URL is never exposed**; photos are private and reachable solely through the authorized download route.
- `identityUserId` is the Elderly Identity account; it links to SMS OTP login.

## Add a dependent

`multipart/form-data`. The photo is optional.

```http
POST /api/v1/family/dependents
Authorization: Bearer {{familyToken}}
Content-Type: multipart/form-data

  arabicFullName: سعيد نصر
  englishFullName: Saeed Nasr
  phoneNumber: +201007654321
  gender: 1
  relationshipType: 7
  dateOfBirth: 1948-07-20
  detailedAddress: 12 Nile Street, Damanhur     (optional)
  healthNotes: Diabetes type 2                   (optional)
  photo: <binary image, optional>
```

- `201` — `DependentResponse`.
- `404 Families.Elderly.FamilyNotFound` — family not bootstrapped.
- `403 Families.Elderly.AccessDenied` — Viewer role.
- `400 Families.Elderly.InvalidProfile` — invalid name/phone/gender/relationshipType/date, or address/notes over their limits.
- `409 Families.Elderly.PhoneBelongsToNonElderly` — the phone belongs to a family/caregiver/admin account.
- `409 Families.Elderly.PhoneLinkedToAnotherFamily` — an elderly identity for this phone is already linked to a family.
- `409 Identity.Elderly.PhoneAlreadyInUse` — identity-level phone conflict.
- `400 Storage.File.*` — photo missing/empty, over 5 MB, or not `image/jpeg|png|webp`.

**Phone resolution rules (one elderly → one family):**

1. No Identity user has this phone → an Elderly Identity user is created (Active, phone verified), then linked.
2. An **elderly** Identity user exists and is not linked to any family → it is re-linked (used for add-after-remove transfers).
3. An elderly Identity user already linked to a family → `409 PhoneLinkedToAnotherFamily`.
4. A non-elderly Identity user has this phone → `409 PhoneBelongsToNonElderly`.

If the Families write fails after an identity was created, the created identity is rolled back automatically (best-effort compensation). If the upload succeeds but the command fails, the orphaned photo file is deleted.

## List dependents

Any member.

```http
GET /api/v1/family/dependents
Authorization: Bearer {{familyToken}}
```

- `200` — array of `DependentResponse`, ordered by creation time.
- `404 Families.Elderly.FamilyNotFound` — family not bootstrapped.

## Get one dependent

Any member. Dependents are scoped to the caller's family; an id from another family returns `404` (existence is never leaked across families).

```http
GET /api/v1/family/dependents/{dependentId}
Authorization: Bearer {{familyToken}}
```

- `200` — `DependentResponse`.
- `404 Families.Elderly.NotFound` — not in this family.

## Update a dependent

Owner/Editor only. JSON. Profile fields only; the photo is managed by its own route and is untouched here.

```http
PUT /api/v1/family/dependents/{dependentId}
Authorization: Bearer {{familyToken}}
Content-Type: application/json

{
  "relationshipType": 7,
  "arabicFullName": "سعيد نصر",
  "englishFullName": "Saeed Nasser",
  "gender": 1,
  "dateOfBirth": "1948-07-20",
  "detailedAddress": "18 Corniche Road, Damanhur",
  "healthNotes": "Diabetes; penicillin allergy."
}
```

- `200` — updated `DependentResponse`.
- `404 Families.Elderly.NotFound` — not in this family.
- `403 Families.Elderly.AccessDenied` — Viewer.
- `400 Families.Elderly.InvalidProfile` — validation failure (including invalid `relationshipType` or future date of birth).

## Remove a dependent

Owner/Editor only.

```http
DELETE /api/v1/family/dependents/{dependentId}
Authorization: Bearer {{familyToken}}
```

- `204` — removed.
- `404 Families.Elderly.NotFound` — not in this family.
- `403 Families.Elderly.AccessDenied` — Viewer.

Effect: the `families.elderlies` row and the stored photo file are deleted (hard delete). The **Elderly Identity user remains** — the person can still log in by SMS OTP, and the phone can be re-added (linked) later by this or another family.

## Dependent photo

Photos are stored in a **private** folder (`elderly-photos`) under the `<root>-private` storage area that the public `/files` static endpoint never serves. Allowed types: `image/jpeg`, `image/png`, `image/webp`. Maximum size **5 MB**.

### Upload / replace (Owner/Editor)

`multipart/form-data`, single `photo` file field.

```http
PUT /api/v1/family/dependents/{dependentId}/photo
Authorization: Bearer {{familyToken}}
Content-Type: multipart/form-data

  photo: <binary image>
```

- `200` — updated `DependentResponse` (`hasPhoto: true`).
- `404 Families.Elderly.NotFound` — not in this family.
- `403 Families.Elderly.AccessDenied` — Viewer.
- `400 Storage.File.Empty|TooLarge|UnsupportedType`.

Replacing a photo deletes the previous file after a successful save. If the command fails after upload, the orphaned file is deleted.

### Download (any member)

```http
GET /api/v1/family/dependents/{dependentId}/photo
Authorization: Bearer {{familyToken}}
```

- `200` — the image bytes (`image/jpeg|png|webp`) with a generated file name (`dependent-{id}.jpg|png|webp`).
- `404 Families.Elderly.NotFound` — not in this family, or no photo uploaded.
- `404 Storage.File.NotFound` — the stored file is missing.

The stream is only ever returned to a member of the family that owns the dependent.

## Error catalog (this surface)

| HTTP | code | When |
|---|---|---|
| 400 | `Families.Elderly.InvalidProfile` | Invalid field values / limits (including invalid `relationshipType`) |
| 400 | `Storage.File.Empty` / `TooLarge` / `UnsupportedType` | Photo problems |
| 403 | `Families.Elderly.AccessDenied` | Viewer attempts a manage action |
| 404 | `Families.Elderly.FamilyNotFound` | Family not bootstrapped |
| 404 | `Families.Elderly.NotFound` | Dependent not in caller's family / no photo |
| 404 | `Storage.File.NotFound` | Stored photo missing |
| 409 | `Families.Elderly.PhoneLinkedToAnotherFamily` | Phone already linked elsewhere |
| 409 | `Families.Elderly.PhoneBelongsToNonElderly` | Phone belongs to a non-elderly account |
| 409 | `Identity.Elderly.PhoneAlreadyInUse` | Identity phone conflict |
