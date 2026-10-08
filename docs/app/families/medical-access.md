# Dependent medical-access grants

Family owners and editors may create or revoke a grant for a dependent in their
family. Family members may read the grant list. Each grant names exactly one
recipient (`granteeUserId`), and the recipient must be an active Medical
caregiver account. The API uses the authenticated family member and route
dependent ID; caller-supplied actor/dependent IDs are not trusted.

- `POST /api/v1/family/dependents/{dependentId}/medical-access-grants`
- `GET /api/v1/family/dependents/{dependentId}/medical-access-grants`
- `GET /api/v1/family/dependents/{dependentId}/medical-access-grants/{grantId}`
- `DELETE /api/v1/family/dependents/{dependentId}/medical-access-grants/{grantId}`

Create accepts `grantType` (`Full`, `Limited`, or `Emergency`), permission
booleans, required `granteeUserId`, optional UTC `expiresOnUtc`, and optional
notes (maximum 500 characters):

```json
{
  "granteeUserId": "0198e2c2-2222-7777-8888-000000000002",
  "grantType": "Limited",
  "canViewRecords": true,
  "canEditRecords": false,
  "canShareWithOthers": false,
  "expiresOnUtc": "2026-12-31T23:59:59Z",
  "notes": "Assigned medical follow-up"
}
```

Editing or sharing requires view permission. Multiple grants are allowed for a
dependent, including separate grants to separate named medical caregivers; a
duplicate active grant for the same dependent and grantee is rejected. Delete
is a revocation: the grant and revocation actor/time remain available in
history, and the response is `204`.

Unknown/foreign dependents are hidden as `Families.AccessDenied`; unknown grants
return `MedicalAccess.GrantNotFound`; a non-caregiver or non-medical/inactive
grantee returns `MedicalAccess.InvalidGrant`. These endpoints do not themselves
expose medical records; downstream record access must check the named grantee,
grant permissions, expiry, and revocation state.

## Recipient picker

`GET /api/v1/family/dependents/{dependentId}/medical-access-recipients?search=&page=1&pageSize=20`
lists the caregivers the family may grant to, for the existing grant creation
endpoint. Any family member may read it (grant creation itself still requires
owner/editor permission); unknown/foreign dependents return
`Families.AccessDenied`.

Eligibility reuses the grant rule exactly: active Medical caregivers only
(`Type == Medical`, `Status == Active`, matched by user id). No other users
are listed, and no medical-record content is returned. Each item carries the
`userId` required by the grant creation endpoint:

```json
{
  "items": [
    {
      "caregiverId": "0198e2c2-3333-7777-8888-000000000001",
      "userId": "0198e2c2-2222-7777-8888-000000000002",
      "arabicFullName": "…",
      "englishFullName": "…",
      "avatarUrl": null,
      "caregiverType": "Medical",
      "specializationId": "…",
      "specializationArabicName": "…",
      "specializationEnglishName": "…",
      "hasActiveGrant": false
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 1,
  "totalPages": 1
}
```

`hasActiveGrant` is true when a non-revoked, non-expired grant already exists
for this dependent and grantee. `search` matches Arabic/English names
(case-insensitive, trimmed, optional); `page` is at least 1 and `pageSize` is
clamped to 1–100.
