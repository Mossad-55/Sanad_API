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
  "accountType": 1,
  "emailVerified": true,
  "phoneVerified": true
}
```

- `accountType`: `1` Family, `2` Medical Caregiver, `3` Companion Caregiver, `4` Elderly
- `email` is `null` for Elderly users registered without an email

| HTTP | `code` |
|---|---|
| 404 | `Identity.Account.UserNotFound` |

## PUT `/api/v1/account`

Partial update of the caller's names, email, and phone. Any field omitted (or `null`) is left unchanged. Role/account type is never accepted — a `role` field in the body is ignored.

```bash
curl -sS -X PUT https://localhost:7296/api/v1/account \
  -H "Authorization: Bearer ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "email": "new@example.com"
  }'
```

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
