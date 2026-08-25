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

## Runtime selection

| Config | Sender |
|---|---|
| SMTP not configured | `DevelopmentEmailSender` |
| SMTP configured | `SmtpEmailSender` |
| SMS Misr not configured | `DevelopmentSmsSender` |
| SMS Misr configured | `SmsMisrSmsSender` |

## Example

```bash
export ASPNETCORE_ENVIRONMENT=Development
export ConnectionStrings__IdentityDatabase="Host=localhost;Port=5432;Database=sanad_identity;Username=REPLACE_ME;Password=REPLACE_ME"
export Identity__Jwt__Issuer="sanad-api"
export Identity__Jwt__Audience="sanad-clients"
export Identity__Jwt__SigningKey="REPLACE_WITH_AT_LEAST_32_UTF8_BYTES"
```
