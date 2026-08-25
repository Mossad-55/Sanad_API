# Auth error catalog

Failed Auth commands return Problem Details. The application error code is in `code`.

```json
{
  "type": "https://httpstatuses.com/401",
  "title": "Unauthorized",
  "status": 401,
  "detail": "Authentication failed.",
  "instance": "/api/v1/auth/login",
  "code": "Identity.Login.InvalidCredentials",
  "traceId": "00-..."
}
```

Validation failures are also Problem Details. `detail` is a safe public message. It does not include OTPs, tokens, or secrets.

## Status map

| `code` | HTTP |
|---|---|
| `Identity.Registration.EmailAlreadyInUse` | 409 |
| `Identity.Registration.PhoneAlreadyInUse` | 409 |
| `Identity.Registration.UnsupportedAccountType` | 400 |
| `Identity.Verification.RequestNotFound` | 401 |
| `Identity.Verification.RequestNotPending` | 400 |
| `Identity.Verification.RequestExpired` | 401 |
| `Identity.Verification.InvalidCode` | 401 |
| `Identity.Verification.UnsupportedPurpose` | 400 |
| `Identity.Verification.UserNotFound` | 401 |
| `Identity.Verification.ResendRequestNotFound` | 404 |
| `Identity.Verification.ResendRequestNotPending` | 400 |
| `Identity.Verification.RequestSuperseded` | 409 |
| `Identity.Verification.ResendCooldownActive` | 409 |
| `Identity.Login.InvalidCredentials` | 401 |
| `Identity.Login.UserSuspended` | 403 |
| `Identity.Login.UserBlocked` | 403 |
| `Identity.Login.SessionLimitReached` | 409 |
| `Identity.Refresh.SessionNotFound` | 401 |
| `Identity.Refresh.SessionRevoked` | 401 |
| `Identity.Refresh.SessionExpired` | 401 |
| `Identity.Refresh.UserNotFound` | 401 |
| `Identity.Refresh.UserNotActive` | 403 |
| `Identity.Refresh.ReuseDetected` | 401 |
| `Identity.ElderlyLogin.OtpVerificationFailed` | 401 |
| `Identity.ElderlyLogin.SessionLimitReached` | 409 |
| `Identity.Password.UserNotFound` | 401 |
| `Identity.Password.UserNotActive` | 403 |
| `Identity.Password.UserHasNoPassword` | 400 |
| `Identity.Password.InvalidCurrentPassword` | 401 |
| `Identity.Password.OtpVerificationFailed` | 401 |
| `Identity.Password.PendingRequestNotFound` | 401 |
| `Identity.Password.NewPasswordMustDiffer` | 400 |
| `Identity.Sessions.SessionNotFound` | 404 |
| `Identity.Sessions.SessionNotOwned` | 404 |
| `Identity.Sessions.UserNotFound` | 404 |
| `Api.Auth.InvalidDeviceSessionHeader` | 400 |

Unmapped application errors become `400`.

## Safe public details

| HTTP | `detail` |
|---|---|
| 401 | Authentication failed. |
| 403 | The requested operation is not allowed. |
| 404 | The requested resource was not found. |
| 409 | The request conflicts with the current state. |
| Other 400 | The request could not be completed. |

Clients should branch on `status` and `code`, not on `detail`.
