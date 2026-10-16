# Shared App verification continuity notice — FUR-02 signup recovery and safe resend

Date: 2026-10-16 (Africa/Cairo). Phase: FUR-02.
Purpose: implementation delivery (local; not deployed).
Audience: App shared — Family, Caregiver and Elderly clients (convention notice only).
Communication state: ready for owner forwarding; receipt not confirmed.

No mobile frontend was audited. No frontend repository was inspected. This
notice covers only the shared auth verification wire changes that mobile
clients also consume; it is not a mobile code audit. Elderly SMS-OTP login
itself is unchanged (its login returns null recovery fields).

## Baseline and authority

- Original intake and issue IDs: `FR-20261016-01` (phone OTP resend failure) and `FR-20261016-12` (lost signup verification IDs/recovery) insofar as they touch shared resend behavior and signup recovery.
- Canonical plan/approved contract: [FUR-02 contract](../../../../internal/work/portal-api-reconciliation/contracts/fur-02-verification-continuity.md) (owner-approved 2026-10-16).
- Source revision and relevant local delta: intake `ba88f3c63120110dbf5e84744aaa46abbed01ad9`; FUR-02 local delta uncommitted. Full evidence in [delivery report](../../../../internal/work/portal-api-reconciliation/reports/fur-02-worker-delivery.md).
- Deployment: not deployed. Behavior below is existing-plus-new local truth, not a released change.
- Frontend repository inspected: no.

## What is correct and should remain

| Issue | Correct behavior/usage | Evidence |
| --- | --- | --- |
| FR-20261016-12 | Register sends the documented fields and returns wrapper IDs; verify sends `verificationRequestId` + `code`. Never send `email`/`purpose` payloads. Request GUIDs stay plain; response IDs stay `{ "value": guid }` wrappers. | `docs/auth/registration-and-verification.md` |
| FR-20261016-01 | Resend sends `verificationRequestId` and returns the replacement ID plus expiry; the call shape is unchanged. | `docs/auth/registration-and-verification.md` |
| FR-20261016-12 | Restricted login keeps null refresh/session values, verification flags, and the `RestrictedVerification` token without account claims. Shared models must keep the wrapper-ID convention. | `docs/auth/email-password-login.md`; `PortalAuthExampleContractTests` |

## What needs correction

| Issue | Classification | Current truth and expected frontend action | Backend responsibility / phase | Evidence |
| --- | --- | --- | --- | --- |
| FR-20261016-12 | Former frontend assumption (lost signup IDs) resolved by new backend capability | After release, a pending-verification login also returns nullable `emailVerificationRequestId`, `emailVerificationRequestExpiresOnUtc`, `phoneVerificationRequestId`, `phoneVerificationRequestExpiresOnUtc` for unverified channels (verified channels stay null; active login stays null). A client that lost the registration response can resume with password login instead of re-registering. No mobile screen was audited, so no client bug is claimed. | FUR-02 — implemented/tested locally; not published/deployed | `docs/auth/email-password-login.md` |
| FR-20261016-01 | Backend reliability risk (resend failure ordering) fixed; client retry rule changes | Resend accepts `Pending` and `Expired` IDs and returns `502 Identity.Verification.ProviderDeliveryFailed` without state change when the provider fails: retry the **same** ID after the 60-second cooldown. A concurrent-resend loser gets `409 Identity.Verification.RequestSuperseded`; recover the current ID with password login. A losing call may already have sent an unusable code — always use the latest ID received. | FUR-02 — implemented/tested locally; not published/deployed | `docs/auth/registration-and-verification.md`; `docs/auth/errors.md` |

## Decisions and evidence requested

- Owner decisions: none for this topic.
- Frontend evidence: none required before release; after the accepted-phase release, shared models can add the four nullable recovery fields and the resend retry rule.
- Acceptance: no new endpoint; no mobile behavior change beyond the documented additive fields and error codes; elderly OTP login unchanged.
- Configuration/migrations: none for clients (one additive Identity migration server-side; no client configuration change).
- Next action: owner returns the worker delivery report to the planning Mastermind for review.
