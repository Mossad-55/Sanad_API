# Account self-edit

A logged-in user views and updates their own profile. Both routes require a Normal JWT and act on the caller only (`UserId` comes from the JWT, never from the body).

Development base URL:

```text
https://localhost:7296
```

## GET `/api/v1/account`

Returns the caller's profile. The account type is display-only.

```bash
curl -sS https://localhost:7296/api/v1/account \
  -H "Authorization: Bearer ACCESS_TOKEN"
```

`200` response:

```json
{
  "arabicFullName": "محمد أحمد",
  "englishFullName": "Mohamed Ahmed",
  "email": "user@example.com",
  "phoneNumber": "+201001234567",
  "avatarUrl": null,
  "accountType": 1,
  "emailVerified": true,
  "phoneVerified": true
}
```

- `accountType`: `1` Family, `2` Medical Caregiver, `3` Companion Caregiver, `4` Elderly
- `email` is `null` for Elderly users registered without an email
- `avatarUrl` is `null` until an avatar is uploaded via `PUT /api/v1/auth/avatar`; the stored path is returned as-is and the image is fetched via `GET /api/v1/auth/avatar`

| HTTP | `code` |
|---|---|
| 404 | `Identity.Account.UserNotFound` |

## Account choices and switcher

These routes require a normal JWT. They expose regular account types owned by
the authenticated user and validate a dashboard selection; they do not issue a
new JWT or create a new session. Account types are `1` Family, `2` Medical
Caregiver, and `3` Companion Caregiver. Elderly (`4`) is not selectable here.

### GET `/api/v1/account/accounts`

Returns the caller's owned regular account types.

```http
GET /api/v1/account/accounts
Authorization: Bearer ACCESS_TOKEN
```

`200` response: `{ "accounts": [1, 2] }`.

### POST `/api/v1/account/accounts`

Adds one regular account type. After a successful `200`, refresh the current
session because the response sets `refreshRequired` to `true`.

```http
POST /api/v1/account/accounts
Authorization: Bearer ACCESS_TOKEN
Content-Type: application/json

{ "accountType": 2 }
```

Possible outcomes include `400` validation/unsupported type, `403` restricted
access, `404 Identity.Account.UserNotFound`, and `409` for an existing account,
caregiver-type exclusivity, retained caregiver profile, or invalid account state.

### POST `/api/v1/account/switch`

Validates an already-owned regular account and returns the requested dashboard
selection. It performs no token issuance, session swap, or authorization-role
narrowing; the current JWT remains authoritative.

```http
POST /api/v1/account/switch
Authorization: Bearer ACCESS_TOKEN
Content-Type: application/json

{ "accountType": 2 }
```

`200` returns `{ "accountType": 2 }`. Invalid types return `400`; an unknown
user returns `404 Identity.Account.UserNotFound`; an unowned type returns `409
Identity.Account.AccountNotOwned`; and an invalid account state returns `409
Identity.Account.InvalidOperation`.

## PUT `/api/v1/account`

Partial update of the caller's names, email, and phone. Any field omitted (or `null`) is left unchanged. Role/account type is never accepted — a `role` field in the body is ignored.

```bash
curl -sS -X PUT https://localhost:7296/api/v1/account \
  -H "Authorization: Bearer ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "arabicFullName": "محمد أحمد",
    "englishFullName": "Mohamed Ahmed",
    "email": "new@example.com",
    "phoneNumber": "+201001234567"
  }'
```

Partial update: send only the fields you want to change; omitted/null fields are untouched.

`200` response:

```json
{
  "emailVerified": false,
  "phoneVerified": true,
  "emailVerificationRequestId": "…uuid…",
  "phoneVerificationRequestId": null
}
```

Rules:

- Names follow the registration policy: 2–200 characters, Arabic and English updated independently
- Email must be valid, max 256 characters; phone must be E.164
- A changed email/phone is marked UNVERIFIED and the account returns to pending verification until the OTP is completed
- Changing an email/phone saves a fresh OTP request and sends the code; the client completes it with the existing `POST /api/v1/auth/verification/verify` (resend with `POST /api/v1/auth/verification/resend`)
- Submitting the current email/phone is a no-op (no new OTP)
- Blocked users cannot update their profile

| HTTP | `code` |
|---|---|
| 400 | `Api.Validation.Failed` |
| 404 | `Identity.Account.UserNotFound` |
| 409 | `Identity.Registration.EmailAlreadyInUse` |
| 409 | `Identity.Registration.PhoneAlreadyInUse` |
| 409 | `Identity.Account.InvalidOperation` |

## GET `/api/v1/account/language`

Returns the caller's UI language preference.

```bash
curl -sS https://localhost:7296/api/v1/account/language \
  -H "Authorization: Bearer ACCESS_TOKEN"
```

`200` response:

```json
{
  "uiLanguage": 1
}
```

- `uiLanguage`: `1` Arabic (default), `2` English
- The preference is stored per user; it is not a request-culture switcher and is separate from the caregiver Language lookup

| HTTP | `code` |
|---|---|
| 404 | `Identity.Account.UserNotFound` |

## PUT `/api/v1/account/language`

Updates the caller's UI language preference.

```bash
curl -sS -X PUT https://localhost:7296/api/v1/account/language \
  -H "Authorization: Bearer ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "uiLanguage": 2
  }'
```

`200` response:

```json
{
  "uiLanguage": 2
}
```

Rules:

- `uiLanguage` accepts `1` Arabic (default) or `2` English; any other value fails validation
- Submitting the current language is a no-op
- The preference is persisted per user and applies to the caller only (`UserId` comes from the JWT, never from the body)
- Blocked users cannot update their preference

| HTTP | `code` |
|---|---|
| 400 | `Api.Validation.Failed` |
| 404 | `Identity.Account.UserNotFound` |
| 409 | `Identity.Account.InvalidOperation` |

## GET `/api/v1/account/notification-preferences`

Returns the caller's notification preferences (screen F11).

```bash
curl -sS https://localhost:7296/api/v1/account/notification-preferences \
  -H "Authorization: Bearer ACCESS_TOKEN"
```

`200` response:

```json
{
  "checkInAlerts": true,
  "medicationReminders": true,
  "bookingUpdates": true,
  "communityNotifications": true,
  "familyActivityAlerts": true,
  "newOrders": true,
  "messagesFromFamilies": true,
  "systemNotifications": true
}
```

- All eight toggles default to ON for existing and new users
- One superset object holds every toggle; each client surfaces the subset relevant to the signed-in role
- Delivery is email and in-app only (no SMS)
- Storage only: the preference is persisted per user; delivery of notifications comes later

| HTTP | `code` |
|---|---|
| 404 | `Identity.Account.UserNotFound` |

## PUT `/api/v1/account/notification-preferences`

Full replace of the caller's notification preferences.

```bash
curl -sS -X PUT https://localhost:7296/api/v1/account/notification-preferences \
  -H "Authorization: Bearer ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "checkInAlerts": true,
    "medicationReminders": false,
    "bookingUpdates": true,
    "communityNotifications": false,
    "familyActivityAlerts": true,
    "newOrders": false,
    "messagesFromFamilies": true,
    "systemNotifications": false
  }'
```

`200` response:

```json
{
  "checkInAlerts": true,
  "medicationReminders": false,
  "bookingUpdates": true,
  "communityNotifications": false,
  "familyActivityAlerts": true,
  "newOrders": false,
  "messagesFromFamilies": true,
  "systemNotifications": false
}
```

Rules:

- All eight fields are required; omitting any field fails validation (full replace, not partial update)
- Submitting the current combination is a no-op
- The preference is persisted per user and applies to the caller only (`UserId` comes from the JWT, never from the body)
- Blocked users cannot update their preference
- Storage only: no notification is delivered by this endpoint (delivery comes later)

| HTTP | `code` |
|---|---|
| 400 | `Api.Validation.Failed` |
| 404 | `Identity.Account.UserNotFound` |
| 409 | `Identity.Account.InvalidOperation` |

## DELETE `/api/v1/account`

Self-service deletion of the caller's account. The endpoint uses `NormalAccess` policy so that both caregiver and family users reach the handler and receive coded errors instead of bare policy forbids.

```bash
curl -sS -X DELETE https://localhost:7296/api/v1/account \
  -H "Authorization: Bearer ACCESS_TOKEN"
```

`204 No Content` on success.

### Caregiver self-delete (SET-8d)

Two modes:

| Caller | What happens |
|---|---|
| **PURE caregiver** (no Family account) | Full deletion: Identity account blocked + PII-scrubbed (anonymize & retain), all device sessions revoked, caregiver profile `Deactivated`. |
| **HYBRID** (Family + caregiver) | Caregiver side only: profile `Deactivated`, caregiver account types removed, Family side and sessions stay alive. |

D11 guard: `409 Identity.Account.ActiveBookingExists` while any booking is `PendingCaregiverApproval` / `Confirmed` / `InProgress`.

### Family-only self-delete (SET-15)

For users WITHOUT a caregiver account:

- Elderly dependent → `409 Identity.Account.ElderlyManagedByFamily` ("Elderly accounts are managed by the family.")
- Owner of an active family → `409 Identity.Account.OwnershipTransferRequired` ("Transfer family ownership before deleting your account.")
- Otherwise: leave all families (via `IFamilyAccountGateway.LeaveFamiliesForSelfDeletionAsync` which fails without changes if the user is OWNER of any active family) → `User.AnonymizeAndDeactivate` → revoke all active sessions → `204`.

Anonymize & retain everywhere — nothing is hard-deleted.

### Errors

| HTTP | `code` | Cause |
|---|---|---|
| 401 | — | Missing/invalid JWT |
| 403 | `Identity.Account.CaregiverOnly` | Caller has no caregiver and no Family account (e.g. admin) |
| 404 | `Identity.Account.UserNotFound` | No Identity user for token subject |
| 404 | `Identity.Account.CaregiverProfileNotFound` | Caregiver account type present but no profile row |
| 409 | `Identity.Account.ActiveBookingExists` | D11 guard |
| 409 | `Identity.Account.ElderlyManagedByFamily` | Elderly accounts are managed by the family |
| 409 | `Identity.Account.OwnershipTransferRequired` | Transfer family ownership before deleting your account |

### Bruno

- `collections/Sanad/account-delete-family/` — family self-delete negative tests: `01-login-owner.bru`, `02-delete-as-owner-conflict.bru` (DELETE → assert 409 + code `Identity.Account.OwnershipTransferRequired` — guard-protected, mutates nothing), `03-logout-owner.bru`.
- Existing `set-8d-account-delete` folder covers caregiver paths.
