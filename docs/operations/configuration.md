# Configuration reference

Use environment variables. ASP.NET Core maps `__` to nested keys.

Do not put secrets in `appsettings.json` or in Git.

## Required

| Variable | Purpose |
|---|---|
| `ConnectionStrings__IdentityDatabase` | PostgreSQL connection for Identity |
| `Identity__Jwt__Issuer` | JWT issuer |
| `Identity__Jwt__Audience` | JWT audience |
| `Identity__Jwt__SigningKey` | HMAC key, at least 32 UTF-8 bytes |

The host will not start if JWT is missing or the signing key is too short.

EF design-time migrations also read `ConnectionStrings__IdentityDatabase`.

## Local file storage

| Variable | Default | Purpose |
|---|---|---|
| `Storage__Local__RootPath` | `{appBase}/sanad-files` | Disk root for uploaded files |

On Hostinger use `/var/sanad/files`.  
Public URL path is `/files/{key}`. Do not commit uploaded files.

## Optional SMTP

Enabled only when `Host` and `FromAddress` are set.

| Variable | Default | Purpose |
|---|---|---|
| `Identity__Email__Smtp__Host` | | SMTP host |
| `Identity__Email__Smtp__Port` | `587` | SMTP port |
| `Identity__Email__Smtp__UseSsl` | `true` | TLS |
| `Identity__Email__Smtp__Username` | | Optional SMTP user |
| `Identity__Email__Smtp__Password` | | Optional SMTP password |
| `Identity__Email__Smtp__FromAddress` | | From address |
| `Identity__Email__Smtp__FromName` | `Sanad Care` | From display name |

## Family invitations (deep link)

| Variable | Default | Meaning |
|---|---|---|
| `App__InviteBaseUrl` | `sanad://family/invite` (Development) | Base of the mobile deep link sent in invitation emails; the API appends `?token=<opaque-token>` |

The deep link opens the mobile app's invitation screen; there is no web page. See `docs/app/families/invitations.md`.

## Optional SMS Misr

Enabled when `Username`, `Password`, and `Sender` are set.

| Variable | Default | Purpose |
|---|---|---|
| `Identity__Sms__SmsMisr__Username` | | Account user |
| `Identity__Sms__SmsMisr__Password` | | Account password |
| `Identity__Sms__SmsMisr__Sender` | | Sender token |
| `Identity__Sms__SmsMisr__Template` | | OTP template token |
| `Identity__Sms__SmsMisr__Environment` | `2` | `2` test, `1` live |
| `Identity__Sms__SmsMisr__BaseUrl` | `https://smsmisr.com` | API host |

- No template → `POST /api/SMS/`
- Template set → `POST /api/OTP/`
- Test senders usually have no template. Leave `Template` unset.

## Optional Paymob (online payments)

Enabled when `SecretKey` is set. Otherwise `DevelopmentPaymobClient` serves payment intents with dev orders (`dev-…` / instant fake refunds) — no real gateway calls.

| Variable | Default | Purpose |
|---|---|---|
| `Paymob__BaseUrl` | `https://accept.paymob.com` | API host. Must **not** end in `/api` — the client appends `/v1/intention/` and `/api/acceptance/...` itself. |
| `Paymob__SecretKey` | | Dashboard secret key (`sk_test_…` / `sk_live_…`); authorises intention and refund calls (`Authorization: Token …`). |
| `Paymob__PublicKey` | | Dashboard public key (`pk_…`); handed to the mobile SDK together with the intent `clientSecret`. |
| `Paymob__HmacSecret` | | HMAC-SHA512 secret verifying webhook callbacks; if unset the webhook endpoint answers `503`. |
| `Paymob__CardIntegrationId` | | Card integration id (Dashboard → Developers → Payment Integrations). |
| `Paymob__WalletIntegrationId` | | Mobile-wallet integration id (Vodafone/Etisalat/Orange). |
| `Paymob__WebhookUrl` | | Public webhook URL; sent as the intention `notification_url` and must also be registered on each integration's *transaction processed callback* in the dashboard. |

- Test-mode secret keys only pair with test-mode integration ids — mixing modes fails with `404 Integration ID/Name does not exist`.
- See `docs/app/families/bookings.md` for the payment flow and `POST /api/v1/payments/webhooks/paymob` for the callback contract.

## Runtime selection

| Config | Sender |
|---|---|
| SMTP not configured | `DevelopmentEmailSender` |
| SMTP configured | `SmtpEmailSender` |
| SMS Misr not configured | `DevelopmentSmsSender` |
| SMS Misr configured | `SmsMisrSmsSender` |
| Paymob not configured | `DevelopmentPaymobClient` |
| Paymob configured | `PaymobClient` |

## Example

```bash
export ASPNETCORE_ENVIRONMENT=Development
export ConnectionStrings__IdentityDatabase="Host=localhost;Port=5432;Database=sanad_identity;Username=REPLACE_ME;Password=REPLACE_ME"
export Identity__Jwt__Issuer="sanad-api"
export Identity__Jwt__Audience="sanad-clients"
export Identity__Jwt__SigningKey="REPLACE_WITH_AT_LEAST_32_UTF8_BYTES"
```
