# Shared App verification continuity notice — FUR-02 signup recovery and safe resend

Date: 2026-10-16 (Africa/Cairo). Phase: FUR-02.
Purpose: deployed backend contract notice; client adoption unconfirmed.
Audience: App shared — Family, Caregiver and Elderly clients (convention notice only).
Communication state: ready for owner forwarding; receipt not confirmed.

No mobile frontend was audited. No frontend repository was inspected. This
notice covers only the shared auth verification wire changes that mobile
clients also consume; it is not a mobile code audit. Elderly SMS-OTP login
itself is unchanged (its login returns null recovery fields).

## Baseline and authority

- Original intake and issue IDs: `FR-20261016-01` (phone OTP resend failure) and `FR-20261016-12` (lost signup verification IDs/recovery) insofar as they touch shared resend behavior and signup recovery.
- Canonical plan/approved contract: [FUR-02 contract](../../../../internal/work/portal-api-reconciliation/contracts/fur-02-verification-continuity.md) (owner-approved 2026-10-16).
- Source revision: implementation `5bde52383071a2c20d17e3059038f729c1b64a70`; exact deployed package `5cd3526b53e8555d3233810b8a024fcb6aa0322e`. Full evidence in [delivery report](../../../../internal/work/portal-api-reconciliation/reports/fur-02-worker-delivery.md) and [deployment receipt](../../../../internal/work/portal-api-reconciliation/reports/fur-02-deployment.md). No real-provider delivery was exercised.
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
| FR-20261016-12 | Former frontend assumption (lost signup IDs) resolved by new backend capability | A pending-verification login also returns nullable `emailVerificationRequestId`, `emailVerificationRequestExpiresOnUtc`, `phoneVerificationRequestId`, `phoneVerificationRequestExpiresOnUtc` for unverified channels (verified channels stay null; active login stays null). A client that lost the registration response can resume with password login instead of re-registering. No mobile screen was audited, so no client bug is claimed. | FUR-02 — backend deployed; client adoption unconfirmed | `docs/auth/email-password-login.md` |
| FR-20261016-01 | Backend reliability risk (resend failure ordering) fixed; client retry rule changes | Resend accepts `Pending` and `Expired` IDs and returns `502 Identity.Verification.ProviderDeliveryFailed` without state change when the provider fails: retry the **same** ID after the 60-second cooldown. A concurrent-resend loser gets `409 Identity.Verification.RequestSuperseded`; recover the current ID with password login. A losing call may already have sent an unusable code — always use the latest ID received. | FUR-02 — backend deployed; client adoption unconfirmed | `docs/auth/registration-and-verification.md`; `docs/auth/errors.md` |

## Decisions and evidence requested

- Owner decisions: none for this topic.
- Frontend evidence: shared models can add the four nullable recovery fields and the resend retry rule; confirm behavior in the relevant clients. No mobile frontend was inspected.
- Acceptance: no new endpoint; no mobile behavior change beyond the documented additive fields and error codes; elderly OTP login unchanged.
- Configuration/migrations: none for clients (one additive Identity migration server-side; no client configuration change).
- Next action: owner forwards this notice and requests client adoption evidence; the [deployment receipt](../../../../internal/work/portal-api-reconciliation/reports/fur-02-deployment.md) proves only the backend release.
