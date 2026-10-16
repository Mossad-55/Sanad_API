# Registration and verification

Family, Medical Caregiver, Companion Caregiver, and CareHomeOwner accounts
register with email + password, then verify email and phone.

Elderly cannot call `POST /register`.

Development base URL:

```text
https://localhost:7296
```

## POST `/api/v1/auth/register`

Anonymous. Success `201`. Duplicate email or phone `409`.

```json
{
  "arabicFullName": "أحمد حسن",
  "englishFullName": "Ahmed Hassan",
  "email": "ahmed@example.com",
  "phoneNumber": "+201001234567",
  "password": "Password1ab",
  "accountType": 1
}
```

`accountType`: `1` Family, `2` MedicalCaregiver, `3` CompanionCaregiver, `8` CareHomeOwner. Care-home owners verify the email and phone identities before receiving a normal token; OTP is not a login method. Values such as Elderly (`4`) and all Admin account types remain unavailable for public registration and are rejected by request validation with `400 Api.Validation.Failed` (including an `AccountType` validation error).

Password: 10–128 characters, at least one uppercase, one lowercase, and one number.

Creates:

- User `PendingVerification`
- Email OTP
- SMS OTP

Register success `201` returns wrapper IDs (literal wire shape):

```json
{
  "userId": { "value": "0198e2c2-4444-7777-8888-000000000010" },
  "emailVerificationRequestId": { "value": "0198e2c2-4444-7777-8888-000000000011" },
  "phoneVerificationRequestId": { "value": "0198e2c2-4444-7777-8888-000000000012" }
}
```

Request GUIDs stay plain GUIDs while response IDs are `{ "value": guid }`
wrappers. Restricted login returns verification flags but no verification
request IDs; losing signup state needs the separately contracted FUR-02
recovery flow, which is not promised here.

```bash
curl -k -sS https://localhost:7296/api/v1/auth/register \
  -H 'Content-Type: application/json' \
  -d '{"arabicFullName":"أحمد حسن","englishFullName":"Ahmed Hassan","email":"ahmed@example.com","phoneNumber":"+201001234567","password":"Password1ab","accountType":1}'
```

Do **not** send avatar or National ID files on this request. The body is JSON only. After email + phone verification and a Normal login, use [Avatar](avatar.md) and [National ID (identity document)](identity-document.md).

## POST `/api/v1/auth/verification/verify`

Anonymous. Success `200`.

```json
{
  "verificationRequestId": "0198e2c2-4444-7777-8888-000000000001",
  "code": "123456"
}
```

Request fields are a plain GUID plus `code`; do not send `email` or `purpose`
payloads. Success example (literal wire shape; the `attemptesRemaining`
spelling is the current public field name):

```json
{
  "userId": { "value": "0198e2c2-4444-7777-8888-000000000010" },
  "emailVerified": true,
  "phoneVerified": true,
  "normalAccessAllowed": true,
  "attemptesRemaining": 5
}
```

Verifying a code marks that channel verified, then the handler checks both
channels: when email and phone are both verified and the user is still
`PendingVerification`, the user activates. Either supported channel — email or
phone — can therefore be the final verification that activates the account;
there is no email-only or phone-only activation rule.

Unknown, expired, or invalid codes return `401`.

## POST `/api/v1/auth/verification/resend`

Anonymous. Success `200`.

```json
{
  "verificationRequestId": "0198e2c2-4444-7777-8888-000000000001"
}
```

The request carries only the pending `verificationRequestId` (plain GUID).
The response returns the replacement request ID and its expiry:

```json
{
  "verificationRequestId": { "value": "0198e2c2-4444-7777-8888-000000000002" },
  "expiresOnUtc": "2026-10-16T12:15:00Z"
}
```

Missing pending request `404`. Already verified `400`. Cooldown `409`.

Resend supersedes the previous pending request.

## Avatar

Not part of register or verify. After a Normal login:

```text
PUT /api/v1/auth/avatar
```

See [avatar.md](avatar.md).

## National ID

Not part of register or verify. The mobile app may collect front/back photos on any screen, then:

```text
PUT /api/v1/auth/identity-document
```

with a Normal JWT. See [identity-document.md](identity-document.md).
