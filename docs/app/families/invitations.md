# Family member invitations

Owners and Editors invite other **Family-account users** to share management of a family. Invitees are addressed by **email only** (never phone). The recipient must already have a Sanad account with a Family role; otherwise no invitation is created and no email is sent.

All routes require policy `FamilyAccess`.

## Rules

- **Recipient must be registered**: an email with no account → `409 Families.Invitation.RecipientNotRegistered` (no email sent).
- **Recipient must have a Family account**: an existing account without the Family role (e.g. caregiver) → `409 Families.Invitation.RecipientMissingFamilyAccount`.
- **Cannot invite yourself** → `409 Families.Invitation.CannotInviteYourself`.
- **Already a member** → `409 Families.Invitation.AlreadyMember`.
- **One pending invitation per recipient per family**: a second invite while one is pending → `409 Families.Invitation.PendingInvitationExists`.
- **Roles**: invitees join as **Editor** (`2`) or **Viewer** (`3`). Owner (`1`) is never assignable → `400 Families.Invitation.InvalidRole`.
- **Permissions**: create = Owner/Editor (`403 Families.Invitation.AccessDenied` for Viewer); accept/decline = the invited user only; revoke = family Owner only.
- Invitations are **single use** and **expire after 7 days**.

## Token and deep link

On creation the API generates an opaque, random 32-byte token. Only its **SHA-256 hash** is stored; the plaintext is sent exactly once, in the invitation email.

The email contains a mobile **deep link** (there is no web page):

```text
sanad://family/invite?token=<opaque-token>
```

The base is configuration value `App:InviteBaseUrl` (`sanad://family/invite` in development). The mobile app registers the `sanad://` scheme, opens on the invitation screen, reads `token`, and calls accept or decline. Tapping the link without the app installed does nothing by design — recipients always already have the app (they must be registered). The same pending invitations are also listed in-app (`GET .../invitations`), so the email is a convenience shortcut, not the only entry point.

The token is authenticated at accept/decline time against the caller's JWT: **only the invited user's own Family session** can answer it (`403 Families.Invitation.NotInvitee` otherwise).

Email delivery is best-effort: the invitation is persisted first; an SMTP failure never rolls it back. In development the link is written to the server log instead of being emailed.

## Create an invitation

Owner/Editor.

```http
POST /api/v1/family/invitations
Authorization: Bearer {{familyToken}}
Content-Type: application/json

{
  "email": "sister@example.com",
  "role": 2,
  "relationshipType": 6
}
```

- `role`: `2` Editor, `3` Viewer.
- `relationshipType`: see the table in `overview.md` (e.g. `6` Sister).

- `201` — `FamilyInvitationResponse` (does **not** include the token; the token is only in the email).
- `404 Families.Invitation.FamilyNotFound` — caller has no family.
- `403 Families.Invitation.AccessDenied` — Viewer.
- `400 Families.Invitation.InvalidRole` — role is Owner or invalid.
- `409` — `RecipientNotRegistered` / `RecipientMissingFamilyAccount` / `CannotInviteYourself` / `AlreadyMember` / `PendingInvitationExists`.

## List my pending invitations

The authenticated user sees invitations addressed to them that are still pending and not expired.

```http
GET /api/v1/family/invitations
Authorization: Bearer {{familyToken}}
```

- `200` — array of `FamilyInvitationResponse`, newest first. This powers the in-app invitation inbox (used when arriving without a deep link).

`FamilyInvitationResponse`:

```json
{
  "id": "…uuid…",
  "familyId": "…uuid…",
  "familyName": "The Nasr Family",
  "invitedEmail": "sister@example.com",
  "role": 2,
  "relationshipType": 6,
  "status": 1,
  "createdOnUtc": "2026-09-01T10:00:00Z",
  "expiresOnUtc": "2026-09-08T10:00:00Z"
}
```

`status`: `1` Pending, `2` Accepted, `3` Declined, `4` Revoked, `5` Expired. (The list endpoint returns only Pending.)

## Accept an invitation

The invited user calls this with the token from the deep link (or from the invitation inbox flow). On success the user is added to the family as a `FamilyMember` with the invited role and relationship.

```http
POST /api/v1/family/invitations/accept
Authorization: Bearer {{familyToken}}
Content-Type: application/json

{
  "token": "<opaque-token>"
}
```

- `204` — accepted; the user is now a member.
- `404 Families.Invitation.NotFound` — unknown token.
- `403 Families.Invitation.NotInvitee` — the caller is not the invited user.
- `409 Families.Invitation.NotPending` — already answered or revoked.
- `409 Families.Invitation.Expired` — past the 7-day window (the invitation is marked Expired).
- `409 Families.Invitation.AlreadyMember` — already in the family.

## Decline an invitation

Same request shape; the invitation is marked Declined and no membership is created.

```http
POST /api/v1/family/invitations/decline
Authorization: Bearer {{familyToken}}
Content-Type: application/json

{
  "token": "<opaque-token>"
}
```

- `204` — declined.
- `404 Families.Invitation.NotFound`, `403 Families.Invitation.NotInvitee`, `409 Families.Invitation.NotPending` / `Expired` as with accept.

## Revoke an invitation

Family Owner only, by invitation id (the owner sees the id in their own invitation management views).

```http
DELETE /api/v1/family/invitations/{invitationId}
Authorization: Bearer {{familyToken}}
```

- `204` — revoked.
- `404 Families.Invitation.NotFound` — no such invitation.
- `403 Families.Invitation.AccessDenied` — caller is not the owner of the invitation's family.
- `409 Families.Invitation.NotPending` — already decided/revoked.

## Invitation states

```text
                 ┌──────────── accept ───────────▶ Accepted (member added)
Pending ─────────┼──────────── decline ──────────▶ Declined
                 ├──────────── owner revoke ─────▶ Revoked
                 └──────────── 7 days elapse ───▶ Expired
```

## Error catalog (this surface)

| HTTP | code | When |
|---|---|---|
| 400 | `Families.Invitation.InvalidRole` | Role is Owner or out of range |
| 400 | `Families.Invitation.InvalidToken` | Empty token |
| 403 | `Families.Invitation.AccessDenied` | Viewer creating, or non-owner revoking |
| 403 | `Families.Invitation.NotInvitee` | Someone other than the invitee answers |
| 404 | `Families.Invitation.FamilyNotFound` | Caller has no family |
| 404 | `Families.Invitation.NotFound` | Unknown invitation/token |
| 404 | `Identity.User.EmailNotFound` | (mapped to RecipientNotRegistered at the families layer) |
| 409 | `Families.Invitation.RecipientNotRegistered` | No account for the email |
| 409 | `Families.Invitation.RecipientMissingFamilyAccount` | Account lacks the Family role |
| 409 | `Families.Invitation.CannotInviteYourself` | Inviting your own email |
| 409 | `Families.Invitation.AlreadyMember` | Recipient is already in the family |
| 409 | `Families.Invitation.PendingInvitationExists` | A pending invite already exists |
| 409 | `Families.Invitation.NotPending` | Already answered/revoked |
| 409 | `Families.Invitation.Expired` | Past 7-day expiry |
