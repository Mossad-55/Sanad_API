# SMS Misr VPS configuration and verification

This runbook records the working SMS Misr setup for the Ahlena API and the checks to repeat after configuring a VPS. Never commit the SMS Misr username, password, sender token, or OTP.

## Configure the sender

1. In the SMS Misr dashboard, open **Sender IDs**.
2. Use **Copy token** for the sender that matches the selected provider environment. The API setting named `Identity__Sms__SmsMisr__Sender` must contain this token, not the visible Sender ID label (for example, `Test Sender`). A rejected sender token/label produces provider response `1904`; the API then surfaces the failed send as HTTP 500.
3. Set these environment variables in the VPS service manager or secret store:

   | Variable | Value |
   |---|---|
   | `Identity__Sms__SmsMisr__Username` | SMS Misr account username |
   | `Identity__Sms__SmsMisr__Password` | SMS Misr account password |
   | `Identity__Sms__SmsMisr__Sender` | Sender **token** copied from Sender IDs |
   | `Identity__Sms__SmsMisr__Environment` | `2` for test; `1` for live |
   | `Identity__Sms__SmsMisr__Template` | Leave unset for the SMS API; set the approved template token only when using SMS Misr's OTP API |

   Keep secrets in the VPS secret store or a root-only environment file. Do not place them in `appsettings*.json`, deployment scripts, shell history, or Git. Restart the API after changing environment values; ASP.NET Core reads them at process startup.

For a systemd deployment, add an `EnvironmentFile=` entry for the API service's protected environment file, then run `systemctl daemon-reload` and restart that service. Use the actual service name configured on the VPS. For Docker or another service manager, set the same variable names through its secret/environment mechanism and recreate the API container/process.

## Test mode

- Keep `Identity__Sms__SmsMisr__Environment=2` while validating the provider test account. Test requests appear in the SMS Misr dashboard reports; they are not evidence of delivery to a handset.
- With `Template` unset, the API calls `POST https://smsmisr.com/api/SMS/` and expects provider success code `1901`.
- With an approved `Template` configured, the API calls `POST https://smsmisr.com/api/OTP/` and expects provider success code `4901`.
- A successful API resend returns HTTP 200 with a replacement `verificationRequestId` and expiry. This response is returned only after the provider success code is accepted. Verify the corresponding recent row in SMS Misr Reports, without copying OTPs into logs or deployment notes.

## End-to-end resend check

Use a pending SMS verification request from a test account. The resend route is:

```http
POST /api/v1/auth/verification/resend
Content-Type: application/json

{"verificationRequestId":"<pending-verification-request-id>"}
```

Resend has a 60-second cooldown. A `409` cooldown response means the request was not sent again yet. A `500` after the provider call means SMS Misr did not return the success code; inspect the API server log for the sanitized provider code and correct the sender token/environment/template configuration before retrying. Do not repeatedly resend while the cooldown is active.

For `1904`, copy the sender **token** from the dashboard again and update the VPS secret. Do not substitute the display name for the token. Use environment `1` only after the live sender and account are approved for live use.
