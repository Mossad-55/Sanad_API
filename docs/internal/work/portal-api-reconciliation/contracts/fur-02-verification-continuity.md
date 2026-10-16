# FUR-02 — Verification continuity (approved contract)

Date: 2026-10-16 (Africa/Cairo).

Status: **Contract approved by owner on 2026-10-16; implementation not started.**
The owner approved the six choices in section 8 after read-only source,
contract, test and persistence inspection. The expiry clarifications below
preserve the approved goal: a user who lost signup IDs can request a fresh
code after the original expires, including after `verify` marks it `Expired`.
This is not an implementation report or deployment evidence.

## 1. Problem and evidence boundary

The current registration flow creates and saves email and phone verification
requests, then sends both codes. It returns both request IDs only when the
handler completes successfully. The current resend flow invalidates the
provided pending request, creates and saves a replacement, and calls the
provider afterwards. A provider exception can therefore leave the old request
invalidated and the replacement unsent; the caller may receive no replacement
ID. The SMS adapter throws for a non-success HTTP/provider result. The exact
frontend-reported provider exception has not been reproduced, and configured
SMS values were observed only as presence, not as proof of valid credentials or
provider availability.

The restricted password-login response includes the user ID, restricted access
token, expiry and email/phone verification flags, but no verification request
IDs. The token deliberately has no account-type or session claims. Existing
verification and resend routes are anonymous and use a request ID as the
bearer of the operation. No existing route was found that recovers pending
signup request IDs from a restricted identity. `UpdateMyProfile` has a separate
normal-access flow that can create new contact-verification requests; it is not
the signup-recovery capability.

No fixed OTP, force-verification endpoint, provider bypass, production seed,
or real provider test is part of this phase.

## 2. Recommended bounded contract

### 2.1 Recovery without a new endpoint

Keep `POST /api/v1/auth/login` and its existing restricted-login behavior. For a
password-authenticated user whose status is `PendingVerification`, add the
following nullable fields to `LoginResponse`:

- `emailVerificationRequestId`
- `emailVerificationRequestExpiresOnUtc`
- `phoneVerificationRequestId`
- `phoneVerificationRequestExpiresOnUtc`

For each purpose, select the newest request linked to the authenticated user
whose purpose is the corresponding signup verification and whose status is
`Pending` or `Expired`, ordered by creation time and then ID for deterministic
ties. An expired pending request, or a request already marked `Expired` by
`verify`, is returned with its actual expiry so the client can call resend;
the expired code cannot verify.
Return null for a verified channel. The
existing `emailVerified`, `phoneVerified`, access type, token fields and null
refresh/session fields remain unchanged. Existing clients that ignore additive
JSON fields remain compatible. A client that lost the registration response
can log in with its password, receive the restricted token and request IDs, then
use the existing verify/resend routes.

The response must never return another user's request ID, a reset-password or
elderly-login request ID, OTP material, target secrets beyond the existing
login contract, or a normal-access token. If no resendable request exists for an
unverified channel, its recovery fields are null; the API does not manufacture
an OTP or silently mark the channel verified. The request ID fields retain the
existing response ID shape, `{ "value": "...guid..." }`; request bodies use
plain GUID strings. Active login and elderly OTP login return null for the
four additive fields.

### 2.2 Retry-safe resend ordering

For the existing `POST /api/v1/auth/verification/resend` request shape and
response shape:

1. Load the requested request. Accept `Pending` or `Expired` for resend;
   preserve not-found, superseded and cooldown checks. `Verified` and
   `Invalidated` requests remain non-resendable. The `Expired` allowance is
   necessary because an expired-code verification attempt persists that status.
2. Generate a replacement code in memory and call the selected fake/provider
   sender before invalidating or saving any verification-request state.
3. If the sender fails or times out, leave the requested request and all
   persisted verification state unchanged. Return
   `Identity.Verification.ProviderDeliveryFailed` with HTTP 502 and the public
   message "Verification code delivery is temporarily unavailable. Please try
   again." without provider credentials, response bodies, OTPs or raw
   exception text. If the HTTP request is cancelled, preserve state and
   propagate cancellation without promising an HTTP response. The caller can
   retry with the same request ID after the normal cooldown rule permits it.
4. Only after a successful send, transition the old request to `Invalidated`
   from `Pending` or `Expired`, and persist one
   replacement request. Return the existing `ResendOtpResponse` containing the
   replacement request ID and expiry. Supersession compares the latest
   `Pending` or `Expired` request for the same user, purpose and target.

This is retry-safe persisted state, not an exactly-once provider guarantee. A
provider may accept a message and then fail before the API observes success;
the contract keeps the old request usable in that ambiguous case. That tradeoff
must be documented rather than hidden. The provider call must remain fake in
automated tests.

The mapped delivery error must be distinct from validation,
cooldown, superseded and unauthorized verification errors. Do not map a
provider failure to success or to a client-fixable 400.

### 2.3 Registration failure compatibility

Do not change the registration request or response names, status code, OTP
format, five-minute lifetime, channel purposes or dual-request creation in this
phase. Registration/provider failure behavior must be made explicit in tests:
the persisted user and pending requests remain recoverable through password
login, and the new login fields provide the IDs if the original 201 response
was not delivered. If the owner instead wants registration to roll back on a
provider failure, that is a separate policy choice and must replace this
recommendation before implementation.

## 3. Permissions and security boundary

- `register`, `login`, `verification/verify` and `verification/resend` retain
  their current anonymous route authorization for wire compatibility.
- Recovery IDs are disclosed only after successful password login for the same
  user. The restricted token remains `RestrictedVerification`, without account
  claims, refresh token, device session or normal-access permissions.
- No existing normal-access endpoint becomes callable by a restricted token.
- Verification purposes exposed by recovery are exactly `VerifyEmail` and
  `VerifyPhone`; reset-password and elderly-login requests remain outside this
  recovery response.
- Request-ID possession remains the existing verify/resend authorization model;
  FUR-02 does not broaden it into an administrator or public lookup facility.

## 4. Expiry, cooldown and concurrency

The current values remain the approved contract: six-digit code, five-minute
request lifetime, five failed attempts, and a 60-second resend cooldown based
on the request's persisted creation time. A provider failure does not alter the
request's creation time, expiry, attempts or status. A successful resend starts
a new five-minute lifetime on the replacement and invalidates the old request.

The worker must make the persistence result deterministic for concurrent resend
calls on the same request. Exactly one replacement is persisted; a losing
concurrent call returns HTTP 409 `Identity.Verification.RequestSuperseded` and
never invalidates the winner. A losing call may already have sent a code; that
code is not accepted, and the client can recover the winning request ID through
password login. The worker must prove the
chosen mechanism against PostgreSQL semantics, not only InMemory. An in-process
lock alone is insufficient for multiple API instances.

The owner approved the first mechanism:

1. **Approved for this phase:** add a PostgreSQL-safe optimistic concurrency
   marker to `VerificationRequest` (with the required migration), translate
   a conflict into the documented 409, and keep the provider call outside the
   database transaction. This prevents two successful saves from replacing the
   same request, while acknowledging the provider ambiguity above. Both
   `Pending` and `Expired` resend must update the old row's concurrency marker
   in the same atomic save as the replacement insert.

A durable encrypted delivery outbox is outside FUR-02 and would require its
own contract for payload protection, retries, retention and provider idempotency.

## 5. Fixture-first verification and tests

Use fake email/SMS senders with deterministic success, throw, cancellation and
provider-ambiguous failure behavior. Never contact SMS Misr, seed/reset the
Production-mode VPS, or put OTPs/secrets/raw provider logs in evidence.

Required focused coverage includes:

- registration creates both pending requests and returns the existing IDs on
  provider success;
- a registration send failure leaves the persisted user/requests recoverable;
- pending password login returns restricted access and the correct latest
  email/phone request IDs and expiry values;
- verified channels return null recovery IDs and foreign-user IDs never leak;
- resend success invalidates exactly the old request, persists one replacement,
  returns its ID and sends through the expected channel;
- provider rejection/exception/timeout leaves the old request pending,
  unchanged and retryable, with the stable mapped error; request cancellation
  also preserves state;
- cooldown, expired-request recovery (including after `verify` marks it
  `Expired`), superseded, verified/invalidated, unknown-ID, invalid-code and
  maximum-attempt behavior are covered; `Verified` and `Invalidated` remain
  non-resendable;
- concurrent resend behavior is covered on a disposable PostgreSQL target,
  with InMemory tests clearly
  labeled as logic-only;
- the restricted token cannot access normal-access endpoints and the additive
  login JSON remains compatible with existing response examples/OpenAPI.

Before any stateful API test, the worker must identify actors/states, verify a
disposable target and fake-provider configuration, reuse or create the smallest
isolated fixture, and read back user/request ownership, status, expiry and
dependencies. No production fixture write is authorized.

## 6. Documentation and Postman

Update the existing authentication/verification feature guide and Postman
folder only for verified behavior: additive restricted-login recovery fields,
the unchanged verify/resend request shapes, the provider-delivery error and
cooldown/expiry lifecycle. Examples must not contain real credentials, tokens,
OTP values or production identifiers. No Bruno work is planned.

Add a dated Admin/App shared handoff only if it helps the owner communicate the
contract: App means Family/Caregiver/Elderly clients; Admin means the two web
portals. It must distinguish the former frontend assumption (lost signup IDs)
from the backend reliability risk, and must not claim frontend code was
inspected or that the phase is deployed.

## 7. Configuration, migration and release disclosure

The approved concurrency marker requires one Identity migration. The migration
identifier is assigned by the worker and reported before execution against any
target. No configuration change is approved.
Neither option changes CORS, storage, key-ring, JWT signing configuration or
provider credentials. Existing SMS/email configuration is preserved; provider
availability is not asserted.

The worker must disclose the exact migration identifier, required restart or
configuration changes and any new package/runtime dependency before running
stateful tests. No migration is applied to Production as part of planning or
testing. Any release remains subject to the normal accepted-phase review and
release preflight.

## 8. Acceptance gate and owner decisions

FUR-02 is accepted only when the source, focused tests, PostgreSQL concurrency
evidence where required, OpenAPI/guide/Postman examples and saved worker report
all agree with one locked choice. The phase is not Done until its applicable
release is verified, or deployment is explicitly N/A for a docs-only outcome.

Owner approved:

1. the additive-login recovery approach instead of a new recovery endpoint;
2. send-first resend ordering and its documented at-least-once provider
   boundary;
3. the PostgreSQL concurrency marker and Identity migration;
4. provider failure mapping to HTTP 502 and the stable public error code above;
5. keeping registration's persisted user/requests recoverable after send
   failure, with no rollback policy change;
6. the listed fixture-first tests, docs/Postman scope and exclusions.

The worker must complete the entire phase and submit a saved report for
Mastermind review. Approval of this contract does not make the phase Done.
