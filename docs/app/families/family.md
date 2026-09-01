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
      "joinedOnUtc": "2026-09-01T09:00:00Z"
    }
  ]
}
```

- `role`: `1` Owner, `2` Editor, `3` Viewer.
- `relationshipType`: see the table in `overview.md`. The bootstrapped owner member uses `99` (Other).
- Members grow when invitations are accepted — see `invitations.md`.

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
| 403 | `Families.Family.NotOwner` | Rename by a non-owner |
| 404 | `Families.Family.NotFound` | Family not bootstrapped / not a member |
| 409 | `Families.Family.AlreadyExists` | Duplicate bootstrap |
