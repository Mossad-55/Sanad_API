# Registration and verification

Family, Medical Caregiver, and Companion Caregiver register with email + password, then verify email and phone.

Elderly cannot call `POST /register`.

Development base URL:

```text
https://localhost:7296
```

## POST `/api/v1/auth/register`

Anonymous. Success `201`. Duplicate email or phone `409`.

```json
{
  "firstName": "Ahmed",
  "lastName": "Hassan",
  "email": "ahmed@example.com",
  "phone": "+201001234567",
  "password": "Password1ab",
  "accountType": 1
}
```

`accountType`: `1` Family, `2` MedicalCaregiver, `3` CompanionCaregiver. Other values `400 Identity.Registration.UnsupportedAccountType`.

Password: 10–128 characters, at least one uppercase, one lowercase, and one number.

Creates:

- User `PendingVerification`
- Email OTP
- SMS OTP

```bash
curl -k -sS https://localhost:7296/api/v1/auth/register \
  -H 'Content-Type: application/json' \
  -d '{"firstName":"Ahmed","lastName":"Hassan","email":"ahmed@example.com","phone":"+201001234567","password":"Password1ab","accountType":1}'
```

Do **not** send National ID photos on this request. The body is JSON only. After email + phone verification and a Normal login, use [National ID (identity document)](identity-document.md).

## POST `/api/v1/auth/verification/verify`

Anonymous. Success `200`.

```json
{
  "email": "ahmed@example.com",
  "purpose": 1,
  "code": "123456"
}
```

`purpose`: `1` Email, `2` Phone.

Email verification does not activate the user. Phone verification on a pending user with both channels verified calls `User.Activate`.

Unknown, expired, or invalid codes return `401`. Wrong purpose for a pending request returns `400`.

## POST `/api/v1/auth/verification/resend`

Anonymous. Success `200`.

```json
{
  "email": "ahmed@example.com",
  "purpose": 1
}
```

Missing pending request `404`. Already verified `400`. Cooldown `409`.

Resend supersedes the previous pending request of the same purpose.

## National ID

Not part of register or verify. The mobile app may collect front/back photos on any screen, then:

```text
PUT /api/v1/auth/identity-document
```

with a Normal JWT. See [identity-document.md](identity-document.md).
