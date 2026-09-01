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

## Policy `CmsContent`

Requires an authenticated user with `access_type` = `Normal` and `account_type` = `SuperAdmin` or `ContentAdmin`. Applied to splash-screen admin write routes.

## Policy `CaregiversAdmin`

Requires an authenticated user with `access_type` = `Normal` and `account_type` = `SuperAdmin` or `ContentAdmin`. Applied to:

- Caregiver lookup admin routes (`/api/v1/admin/lookups/...`)
- Caregiver review routes (`/api/v1/admin/caregivers/...`): paged list, detail, approve/reject/request-correction/suspend/reactivate, certificate verify/reject/revoke, certificate file download

## Policy `CaregiverAccess`

Requires an authenticated user with:

- Claim `access_type` = `Normal`
- Claim `account_type` = `MedicalCaregiver` or `CompanionCaregiver`

Applied to all caregiver self-service routes (`/api/v1/caregiver/...`): profile bootstrap/read/update, selections, pricing, schedules, availability, certificates, and submit/resubmit.

The caregiver type is derived from the `account_type` claim at bootstrap (`MedicalCaregiver` → Medical, `CompanionCaregiver` → Companion) and is fixed for the profile. A Family/Elderly/admin account receives `403`, as does any Restricted verification token.

## Policy `FamilyAccess`

Requires an authenticated user with:

- Claim `access_type` = `Normal`
- Claim `account_type` = `Family`

Applied to all family self-service routes (`/api/v1/family/...`): family bootstrap/read/rename, elderly dependent management and photos, and family-member invitations. A caregiver/Elderly/admin account or any Restricted verification token receives `403`.

Within the family, authorization is role-based (Owner / Editor / Viewer) and enforced by the Families module; `403 Families.*.AccessDenied` / `Families.Family.NotOwner` are returned for role violations. See `docs/app/families/overview.md`.

## Account types

| Value | Name |
|---|---|
| `1` | Family |
| `2` | MedicalCaregiver |
| `3` | CompanionCaregiver |
| `4` | Elderly |
| `5` | SuperAdmin |
| `6` | ContentAdmin |
| `7` | SupportAdmin |

Elderly cannot self-register and cannot share an identity with another account type.

## Restricted versus Normal

| | Restricted | Normal |
|---|---|---|
| When | PendingVerification email/password login | Active user |
| Access token | 15 minutes | 15 minutes |
| Refresh token | No | Yes, 30 days |
| DeviceSession | No | Yes |
| Password change / sessions | No | Yes |
| Caregiver onboarding (`CaregiverAccess`) | No (403) | Yes, for caregiver accounts |
| Verify / resend | Yes, those routes are anonymous | Yes |

## Header

Current-session logout also requires:

```text
X-Device-Session-Id: <guid>
```

The user id always comes from JWT `sub`, never from the request body.
