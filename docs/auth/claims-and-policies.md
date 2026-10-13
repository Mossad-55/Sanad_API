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
| `sid` | Session-bound Normal tokens only: the issuing `DeviceSessionId` (GUID `D`). Absent on legacy Normal tokens and never present on Restricted tokens |

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
- `GET /api/v1/auth/avatar`
- `PUT /api/v1/auth/avatar`
- `GET /api/v1/auth/identity-document`
- `PUT /api/v1/auth/identity-document`
- `GET /api/v1/caregivers` and `GET /api/v1/caregivers/{caregiverId}` and `GET /api/v1/caregivers/{caregiverId}/quote` — caregiver discovery (search, public profile, quote); any Normal account role may browse

A restricted verification token calling those routes receives `403`.

A missing or invalid bearer token receives `401`.

Session-bound Normal tokens (`sid`) are validated on every authenticated
request: a revoked/expired session, a session owned by another user, or a
non-Active account fails authentication (`401`) even before expiry. Legacy
Normal tokens without `sid` still pass `NormalAccess` until their natural
15-minute expiry; sensitive Chat surfaces reject them (see below).

## Policy `SessionBoundAccess`

Defined in `AuthorizationPolicies.SessionBoundAccess`.

Requires:

- Authenticated user
- Claim `access_type` = `Normal`
- Claim `sid` present (verified by JWT validation against the live session row)

Applied to:

- All `/api/v1/chat/...` app routes (`ChatController`)
- `/hubs/chat` (`ChatHub`)

A legacy unbound Normal token calling Chat receives `401/403`; refresh to a
session-bound token. Other `NormalAccess` surfaces keep the documented rollout
window and do not require `sid` yet.

National ID upload additionally rejects Elderly and admin-only identities with `409 Identity.IdentityDocument.UnsupportedAccountType`. Family, Medical Caregiver, and Companion Caregiver may call it.

## Policy `CmsContent`

Requires an authenticated user with `access_type` = `Normal` and `account_type` = `SuperAdmin` or `ContentAdmin`. Applied to splash-screen admin write routes.

## Policy `CaregiversAdmin`

Requires an authenticated user with `access_type` = `Normal` and `account_type` = `SuperAdmin` or `ContentAdmin`. Applied to:

- Caregiver lookup admin routes (`/api/v1/admin/lookups/...`)
- National ID admin review (`/api/v1/admin/identity-documents/...`): paged list, detail, front/back download, verify/reject/revoke

## Policy `CaregiverReviewAdmin`

Requires an authenticated user with `access_type` = `Normal` and `account_type` = `SuperAdmin` or `SupportAdmin`. Applied to caregiver review routes (`/api/v1/admin/caregivers/...`): paged list, detail, approve/reject/request-correction/suspend/reactivate, certificate verify/reject/revoke, and certificate file download. ContentAdmin remains limited to CMS and caregiver lookup responsibilities and receives `403` for these operational review routes.

## Policy `PayoutOperationalAdmin`

Requires an authenticated user with `access_type` = `Normal` and `account_type` = `SuperAdmin` or `FinanceAdmin`. Reserved for caregiver payout operations: payout-policy administration, payout-account verification, and manual payout transfer recording. `SupportAdmin` and `ContentAdmin` receive `403` on these routes; existing policies and endpoint behavior are unchanged.

## Policy `CaregiverAccess`

Requires an authenticated user with:

- Claim `access_type` = `Normal`
- Claim `account_type` = `MedicalCaregiver` or `CompanionCaregiver`

Applied to all caregiver self-service routes (`/api/v1/caregiver/...`): profile bootstrap/read/update, selections, pricing, schedules, availability, certificates, and submit/resubmit.

The caregiver type is derived from the `account_type` claim at bootstrap (`MedicalCaregiver` → Medical, `CompanionCaregiver` → Companion) and is fixed for the profile. A Family/Elderly/admin account receives `403`, as does any Restricted verification token.

National ID is **not** a caregiver certificate. Caregivers upload it with `PUT /api/v1/auth/identity-document` (`NormalAccess`), not under `/caregiver/certificates`.

## Policy `FamilyAccess`

Requires an authenticated user with:

- Claim `access_type` = `Normal`
- Claim `account_type` = `Family`

Applied to all family self-service routes (`/api/v1/family/...`): family bootstrap/read/rename, elderly dependent management and photos, and family-member invitations. A caregiver/Elderly/admin account or any Restricted verification token receives `403`.

Within the family, authorization is role-based (Owner / Editor / Viewer) and enforced by the Families module; `403 Families.*.AccessDenied` / `Families.Family.NotOwner` are returned for role violations. See `docs/app/families/overview.md`.

## Policy `CareHomeOwnerAccess`

Requires an authenticated user with `access_type` = `Normal` and `account_type`
= `CareHomeOwner`. It is reserved for Care homes owner self-service endpoints;
facility persistence and routes are still being implemented. Verification OTP
is for account verification only, not login.

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
| `8` | CareHomeOwner |
| `9` | FinanceAdmin |

Elderly cannot self-register and cannot share an identity with another account type. FinanceAdmin cannot self-register; the account is provisioned by the configuration-gated `FinanceAdminSeed` seeder. Like other administrative accounts, FinanceAdmin cannot be combined with another account type on the same identity.

## Restricted versus Normal

| | Restricted | Normal |
|---|---|---|
| When | PendingVerification email or phone/password login | Active user |
| Access token | 15 minutes | 15 minutes, session-bound (`sid`) after session creation |
| Refresh token | No | Yes, 30 days, bound rotation per device session |
| DeviceSession | No | Yes |
| Password change / sessions | No | Yes |
| National ID (`/auth/identity-document`) | No (403) | Yes, for Family / Medical / Companion |
| Caregiver onboarding (`CaregiverAccess`) | No (403) | Yes, for caregiver accounts |
| Verify / resend | Yes, those routes are anonymous | Yes |

## Header

Current-session logout accepts the verified JWT `sid` first and falls back to:

```text
X-Device-Session-Id: <guid>
```

The user id always comes from JWT `sub`, never from the request body. Chat hub
connections never read this header: they carry the verified JWT `sid` in
connection metadata.
