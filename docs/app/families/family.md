# Family bootstrap and profile

These routes create and manage the authenticated user's family. All require policy `FamilyAccess`.

## Bootstrap the family

A family is created once. The authenticated Family user becomes the Owner and the first family member.

```http
POST /api/v1/family
Authorization: Bearer {{familyToken}}
Content-Type: application/json

{
  "name": "The Nasr Family"
}
```

The body is optional; sending `{}` or no name defaults the family name to `"My Family"`.

- `201` — `FamilyResponse` (see below).
- `409 Families.Family.AlreadyExists` — the user already owns a family.

## Get the family

Any family member (Owner, Editor, Viewer) can read the family they belong to.

```http
GET /api/v1/family
Authorization: Bearer {{familyToken}}
```

- `200` — `FamilyResponse`.
- `404 Families.Family.NotFound` — the user owns no family and is not a member of any (bootstrap first).

Member management is owner-only; all members can view the family and a member's details.

`FamilyResponse`:

```json
{
  "id": "…uuid…",
  "name": "The Nasr Family",
  "ownerUserId": "…uuid…",
  "createdOnUtc": "2026-09-01T09:00:00Z",
  "members": [
    {
      "userId": "…uuid…",
      "role": 1,
      "relationshipType": 99,
      "addedByUserId": "…uuid…",
      "joinedOnUtc": "2026-09-01T09:00:00Z",
      "arabicFullName": "أحمد محمد النصر",
      "englishFullName": "Ahmed Mohamed El-Nasr",
      "email": "ahmed@example.com"
    }
  ]
}
```

- `role`: `1` Owner, `2` Editor, `3` Viewer.
- `relationshipType`: see the table in `overview.md`. The bootstrapped owner member uses `99` (Other).
- `arabicFullName` / `englishFullName` / `email` are enrichment sourced from the identity gateway (not stored by the Families module). When a profile lookup misses, names fall back to `""` and `email` to `null`; the member row is never dropped.
- Members grow when invitations are accepted — see `invitations.md`.

## Members

Member `id`s are the member's own identity `UserId`.

### Get a member's details

Any family member (Owner, Editor, Viewer).

```http
GET /api/v1/family/members/{memberId}
Authorization: Bearer {{familyToken}}
```

- `200` — `FamilyMemberResponse` (enriched):

```json
{
  "userId": "…uuid…",
  "role": 1,
  "relationshipType": 99,
  "addedByUserId": "…uuid…",
  "joinedOnUtc": "2026-09-01T09:00:00Z",
  "arabicFullName": "أحمد محمد النصر",
  "englishFullName": "Ahmed Mohamed El-Nasr",
  "email": "ahmed@example.com"
}
```

- `404 Families.Family.NotFound` — caller has no family.
- `403 Families.Family.AccessDenied` — caller is not a member of the family.
- `404 Families.Family.MemberNotFound` — no such member in the family.

### Change a member's role

Owner only. The Owner role is transfer-only and can never be assigned here.

```http
PUT /api/v1/family/members/{memberId}/role
Authorization: Bearer {{familyToken}}
Content-Type: application/json

{
  "role": 2
}
```

- `204` — role updated.
- `400` — `role` is Owner or out of the enum range.
- `403 Families.Family.NotOwner` — an Editor/Viewer tried to change a role.
- `404 Families.Family.NotFound` — caller has no family.
- `404 Families.Family.MemberNotFound` — no such member.
- `409 Families.Family.OwnerProtected` — attempting to change the owner's role.

### Remove a member

Owner only.

```http
DELETE /api/v1/family/members/{memberId}
Authorization: Bearer {{familyToken}}
```

- `204` — member removed.
- `403 Families.Family.NotOwner` — an Editor/Viewer tried to remove a member.
- `404 Families.Family.NotFound` — caller has no family.
- `404 Families.Family.MemberNotFound` — no such member.
- `409 Families.Family.OwnerProtected` — attempting to remove the owner.

## Rename the family

Owner only.

```http
PUT /api/v1/family/name
Authorization: Bearer {{familyToken}}
Content-Type: application/json

{
  "name": "The Nasr Household"
}
```

- `200` — updated `FamilyResponse`.
- `404 Families.Family.NotFound` — no family for this user.
- `403 Families.Family.NotOwner` — an Editor/Viewer tried to rename.
- `400 Families.Family.InvalidName` — empty or longer than 100 characters.

## Error catalog (this surface)

| HTTP | code | When |
|---|---|---|
| 400 | `Families.Family.InvalidName` | Blank or >100-char family name |
| 403 | `Families.Family.NotOwner` | Rename / role change / removal by a non-owner |
| 403 | `Families.Family.AccessDenied` | Member detail read by a non-member |
| 404 | `Families.Family.NotFound` | Family not bootstrapped / not a member |
| 404 | `Families.Family.MemberNotFound` | Member id not in the family |
| 409 | `Families.Family.AlreadyExists` | Duplicate bootstrap |
| 409 | `Families.Family.OwnerProtected` | Owner role change / owner removal |
