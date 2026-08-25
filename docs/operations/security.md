# Security notes

## Secrets

Never commit or paste into chat:

- JWT signing keys
- Database passwords
- SMTP passwords
- SMS Misr username, password, sender, or template
- Access tokens, refresh tokens, or OTP plaintext
- `.env` files

Use environment variables. User-secrets are not used in this repository. Identity migrations read the connection string from the environment, not from user-secrets.

## Tokens

- Access token: 15 minutes
- Refresh token: opaque, hashed at rest, 30 days
- Restricted token: 15 minutes, no refresh
- Refresh reuse revokes every session for that user
- Password reset and password change revoke every session

## OTP

- Six ASCII digits
- Hash-only storage
- Five-minute lifetime
- Five failed attempts
- 60-second resend cooldown
- The API never returns the raw code

## Input

- Phone numbers: exact ASCII E.164
- OTP codes: exact six ASCII digits
- Unicode digits and trailing newlines are rejected

## Authorization

- Policy `NormalAccess` requires JWT `access_type=Normal`
- Restricted tokens cannot change passwords or manage sessions
- User id comes from JWT `sub`
- Controllers stay thin and do not contain business rules

## Providers

- SMTP and SMS Misr credentials stay on the server
- Development senders are used when credentials are missing
- SMS Misr test senders should not set `Template`
- Use `Identity__Sms__SmsMisr__Environment=2` until live sending is approved

## Swagger

`/swagger` is Development only. Production must not serve it.

## Logging

Do not log OTPs, refresh tokens, SMTP passwords, or SMS Misr passwords.
