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

## Paymob webhook

- `POST /api/v1/payments/webhooks/paymob` is anonymous (`[AllowAnonymous]`).
- HMAC-SHA512 over Paymob `obj` fields in dashboard order (`PaymobHmacCalculator`).
- Signature may arrive as `?hmac=`, JSON body `hmac`, or header `X-Paymob-Hmac`. Comparison is hex-normalized and constant-time.
- If `Paymob__HmacSecret` is unset the endpoint returns `503`.
- A valid HMAC for an unknown booking returns `200` so Paymob stops retrying.

## Subscription payment boundary

- Family subscription payment-intent routes require the normal family policy and
  family Owner role; admin plan authoring/list/detail routes require the normal
  Super Admin `SubscriptionPlanAdmin` policy.
- The server calculates amount, currency, tax, discount, plan, and renewal
  terms. Clients cannot choose a provider subscription plan ID or override the
  Paymob mapping.
- Initial Card enrollment sends a provider `subscription_plan_id` only when the
  local plan has a positive `paymobSubscriptionPlanId` and the Card 3DS
  integration is configured. Wallet and renewal intents remain manual.
- Subscription callbacks use body `hmac` over `{trigger_type}for{subscription_data.id}`;
  accepted trigger spellings are `CREATED`, `Subscription Created`, `Successful
  Transaction`, `Failed Transaction`, and `Failed Overdue Transaction`.
- Subscription renewal callbacks require top-level `paymob_request_id`; it is
  recorded as the durable provider event/idempotency identity.
  `subscription_data.id` is authoritative through the shared provider identity
  registry. Callback ledger rows are append-only and protect against duplicate,
  out-of-order, and cross-target provider identity reuse. A successful callback
  after the seven-day grace terminal boundary is ledgered and acknowledged with
  `200` without changing renewal state.

## Swagger

`/swagger` is Development only. Production must not serve it.

## Logging

Do not log OTPs, refresh tokens, SMTP passwords, or SMS Misr passwords.
