# Claims and policies

## JWT

Issuer, audience, and signing key come from:

```text
Identity__Jwt__Issuer
Identity__Jwt__Audience
Identity__Jwt__SigningKey
```

The signing key must contain at least 32 UTF-8 bytes.

Inbound JWT claim remapping is disabled. `sub` stays `sub`.

## Claims

| Claim | Meaning |
|---|---|
| `sub` | User id |
| `access_type` | `Normal` or `RestrictedVerification` |
| `account_type` | Present on Normal tokens for the user's account roles |

JSON login `accessType` is the enum number: `1` Normal, `2` RestrictedVerification. The JWT claim is the enum name.

## Policy `NormalAccess`

Defined in `AuthorizationPolicies.NormalAccess`.

Requires:

- Authenticated user
- Claim `access_type` = `Normal`

Applied to:

- `POST /api/v1/auth/password/change`
- `POST /api/v1/auth/sessions/logout`
- `POST /api/v1/auth/sessions/logout-all`
- `GET /api/v1/auth/sessions`
- `DELETE /api/v1/auth/sessions/{sessionId}`

A restricted verification token calling those routes receives `403`.

A missing or invalid bearer token receives `401`.

## Account types

| Value | Name |
|---|---|
| `1` | Family |
| `2` | MedicalCaregiver |
| `3` | CompanionCaregiver |
| `4` | Elderly |

Elderly cannot self-register and cannot share an identity with another account type.

## Restricted versus Normal

| | Restricted | Normal |
|---|---|---|
| When | PendingVerification email/password login | Active user |
| Access token | 15 minutes | 15 minutes |
| Refresh token | No | Yes, 30 days |
| DeviceSession | No | Yes |
| Password change / sessions | No | Yes |
| Verify / resend | Yes, those routes are anonymous | Yes |

## Header

Current-session logout also requires:

```text
X-Device-Session-Id: <guid>
```

The user id always comes from JWT `sub`, never from the request body.
