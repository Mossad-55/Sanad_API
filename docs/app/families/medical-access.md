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

## What a caregiver can read today (route → field → authorization)

| Route | Fields | Authorization |
|---|---|---|
| `GET /api/v1/caregiver/bookings/{bookingId}` | `elderly.healthNotes` (free text the family stored on the dependent row) | `CaregiverAccess`; the booking must belong to the calling caregiver, otherwise `404 Bookings.NotFound`. |
| `GET /api/v1/caregiver/bookings/medical-reports/{reportId}/photo` | the medical report photo bytes | `CaregiverAccess`; the **submitting** caregiver only (`403 Reports.Medical.AccessDenied` otherwise), and only when the report carries a photo (`404 Reports.Medical.PhotoNotFound`). |
| `GET /api/v1/caregiver/bookings/{bookingId}/emergency-contact` | `{ name, relationship, phoneNumber }` | `CaregiverAccess`; the booking must belong to the calling caregiver (`404 Bookings.NotFound`), and the visit must be live — `InProgress`, or `Confirmed` and not yet started — otherwise `403 Bookings.EmergencyContactNotAllowed`. `null` when the dependent stores no contact. |
| `GET /api/v1/caregiver/bookings/{bookingId}/visit-history` | the visit/medical report items of this booking | `CaregiverAccess`; the booking must belong to the calling caregiver (`404 Bookings.NotFound`). `null` when no report was recorded. |
| `GET /api/v1/caregiver/dependents/{dependentId}/medical-profile` | the structured medical profile | `CaregiverAccess` **plus an active medical-access grant** to the caller. See below. |
| `GET /api/v1/family/dependents/{dependentId}/medical-profile` | the structured medical profile | `FamilyAccess` plus family membership — **family only**. |
| `GET /api/v1/family/dependents/{dependentId}/medications` | the medication rows | `FamilyAccess` plus family membership — **family only**. |

### Caregiver medical-profile read (grant-gated)

`GET /api/v1/caregiver/dependents/{dependentId}/medical-profile` is the one
caregiver route that consumes a grant. The gate is the **persisted grant row**,
re-evaluated on **every read** — never cached:

- an **active** grant for the dependent (`revokedOnUtc` is null and any
  `expiresOnUtc` is in the future) **whose `granteeUserId` is the caller**, and
- carrying `canViewRecords`.

A missing grant, a revoked or expired grant, a grant to a different grantee, or a
grant without view permission is `403 MedicalAccess.RecordAccessDenied`; an
unknown dependent is `404 MedicalAccess.NotFound`. The response is the exact
family read shape; this route does **not** relax the family-scoped read, and it
never exposes medication rows. Revoking a grant takes effect on the next read.

The grant rows record the family's decision (grantee, type, permissions, expiry,
revocation) and the audit records the decision. A caregiver-facing
medical-record read was a separate Owner-level authorization decision; that
decision is the grant-gated route above — no other caregiver route reads a
medical profile, and nothing in a caregiver booking response contains a medical
profile, a medication, or an emergency contact outside the live-window route.

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

## Decision audit (Admin-only)

E03 adds an append-only Admin decision audit of the grant create/revoke
operations above. It records only authorization decisions at those operations;
it does **not** add a caregiver medical-record read endpoint and does not change
grant semantics. The audit is readable only via
`GET /api/v1/admin/caregivers/medical-access-audit` by SuperAdmin/SupportAdmin.
