# E04 — Platform controls and public business status

**Status: Owner-approved direction — 2026-10-19.** This contract records the
Owner's accepted recommendation. Its implementation manifest and Worker brief
must be prepared and pinned before dispatch; release/deployment remain separate
Owner gates.

## Product input and current evidence

- The Owner confirms there are no current screens/instructions for this feature;
  E04 is a new backend capability, not a screen-reconciliation task.
- The roadmap calls for platform active/maintenance and payment-mode enforcement
  plus public business status. Settings CRUD by itself is not sufficient.
- Current Care Home maintenance blocks apply to rooms/beds, not global platform
  availability.
- Existing booking payment methods include Card, Wallet, and ApplePay in an
  enum; the inspected Paymob intent mapping supports Card and Wallet only. E04
  must not claim ApplePay is available without a separately verified provider
  integration.

## Objective

Give SuperAdmin a small, auditable way to put the API's new customer booking and
payment-start operations into maintenance, control currently supported payment
methods, and let clients read a safe public status. Do not build a general
application-settings registry or an Admin frontend.

## Proposed scope

1. **Platform state:** `Operational` or `Maintenance`, with an optional
   bilingual public maintenance message. Persist the current state and an
   append-only change record containing actor, UTC time, before/after values,
   and optional internal reason. Use optimistic concurrency so stale Admin
   updates cannot silently overwrite a newer control state.
2. **Admin API:** authenticated SuperAdmin-only read/update endpoints for the
   platform state and the Card/Wallet availability switches. Do not expose or
   edit gateway secrets or integration IDs.
3. **Public status API:** anonymous read endpoint returns only the public state,
   approved bilingual message, and currently available supported payment
   methods. It must not expose internal reasons, credentials, provider
   configuration, or infrastructure details.
4. **Maintenance enforcement:** while in Maintenance, reject new customer
   booking checkouts and creation of new payment intents for caregiver bookings,
   Care Home bookings, and subscription/plan-change payments. Enforce this in
   the backend business path, not only in a client-facing status response.
5. **Payment-mode enforcement:** when Card or Wallet is disabled, reject new
   payment-intent requests for that method server-side. Only expose a method as
   available when it is enabled and its existing provider integration is
   configured. ApplePay remains unavailable in E04 unless a separately approved
   and verified integration is added.

## Explicit exceptions and unchanged behavior

- Public health/status, authentication, SuperAdmin control access, and verified
  payment-provider callbacks remain available during Maintenance.
- Already-created payment intents and their callbacks continue to be processed;
  retrying/retrieving an existing intent does not create a new one and is not
  blocked solely by Maintenance.
- Existing booking/subscription reads, cancellations, care-delivery lifecycle,
  refunds, and administrative operations are not globally blocked by this
  control. E04 gates only the named new checkout/payment-start operations.
- Existing local resource-level Care Home maintenance behavior is unchanged.
- Platform fees/taxes, payment provider choice, secrets, migrations outside the
  approved E04 persistence change, and production data are out of scope.

## Proposed API behavior

Exact paths and DTO names are provisional until the Worker file/API contract
manifest is reviewed:

- `GET /api/v1/platform/status` — anonymous sanitized public status.
- `GET /api/v1/admin/platform-controls` — SuperAdmin readback of editable
  controls and version.
- `PUT /api/v1/admin/platform-controls` — SuperAdmin update with expected
  version; successful changes append an audit record.
- Maintenance-blocked new business actions return a stable documented conflict
  (proposed `503 Platform.Maintenance`); a disabled method returns a stable
  documented client error (proposed `400 Platform.PaymentMethodUnavailable`).
  The final status/error mapping must match established API conventions and be
  included in focused OpenAPI and regression tests.

## Persistence and safety proposal

Use one small database-backed control record plus append-only audit history; do
not create a general settings registry. The read-only architecture check found
an existing `FinanceDbContext` and `finance` schema used for shared platform
charge rules and caregiver payout policies, already reachable by the API and
the booking/subscription application layers. Recommendation: add the E04 control
and audit tables there; do not add a new database context/module. Seed a
deterministic initial `Operational` state through the E04 migration. If the
control record cannot be read, new gated operations fail closed with a generic
service-unavailable response; no request may bypass the gate by assuming
Operational.

## Acceptance criteria

- Anonymous status accurately returns only the approved public state, message,
  and enabled/configured supported methods; Admin-only fields and reasons never
  appear publicly.
- Only SuperAdmin can read/update controls; unauthenticated and other roles are
  denied. Successful updates persist and append exactly attributable audit
  history; stale-version writes are rejected without overwriting current state.
- Operational state preserves existing behavior. Maintenance blocks each
  listed new checkout/payment-intent operation and no unlisted existing booking,
  care-delivery, cancellation, refund, or admin lifecycle operation.
- Card/Wallet availability is enforced server-side across the listed intent
  paths. Disabled/unconfigured methods cannot create provider intents; an
  existing intent callback still settles idempotently. ApplePay is not advertised
  or accepted as supported by E04.
- Required tests cover state transitions, role denial, audit/concurrency,
  maintenance enforcement and exceptions, payment-mode enforcement, provider
  callback continuity, and sanitized status/OpenAPI. Required build/test results
  and every NOT RUN check are recorded; no result is inferred.
- Deployment, production changes, and release receipt require the existing
  separate explicit Owner authorization after review and verification.

## Exclusions and gates

- No frontend/UI implementation, general application configuration registry,
  gateway-secret editing, new payment provider/method, provider call, or data
  repair.
- This draft does not authorize implementation, migration application, commit,
  push, publish, or deployment. Contract approval and release authorization are
  separate gates.

## Owner approval required

This direction is Owner-approved. The Mastermind will save the phase tracker and
Worker brief with the pinned revision and exact file manifest, then obtain the
Owner-selected Worker provider/model before dispatch. No Worker starts before
those dispatch materials are saved and the agreed pre-dispatch commit/push gate
is satisfied. Release/deployment still require separate explicit authorization.
