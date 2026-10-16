# Care Home Owner portal — FUR-02 verification continuity — signup recovery and safe resend

Date: 2026-10-16 (Africa/Cairo). Phase: FUR-02.
Purpose: implementation delivery (local; not deployed).
Audience: Care Home Owner portal (Admin audience = Admin portal + Care Home Owner portal).
Communication state: ready for owner forwarding; receipt not confirmed.

No frontend repository was inspected. No frontend code defect is claimed.
This notice communicates the approved backend contract and what owner-portal
clients must change after release; nothing below is deployed.

## Baseline and authority

- Original intake and issue IDs: `FR-20261016-01` (phone OTP resend failure) and `FR-20261016-12` (lost signup verification IDs/recovery).
- Canonical plan/approved contract: [FUR-02 contract](../../../../internal/work/portal-api-reconciliation/contracts/fur-02-verification-continuity.md) (owner-approved 2026-10-16).
- Source revision and relevant local delta: intake `ba88f3c63120110dbf5e84744aaa46abbed01ad9`; FUR-02 local delta uncommitted. See [delivery report](../../../../internal/work/portal-api-reconciliation/reports/fur-02-worker-delivery.md).
- Deployment revision: not deployed. Behavior below is implemented and tested locally only.
- Evidence limits: isolated unit tests with fake providers plus one-winner concurrency and migration proof against a validated disposable local PostgreSQL target (`localhost:5432/sanad_fur02_verify`, created for this phase, since removed). No production, VPS, or real provider runs.
- Frontend repository inspected: no.

## What is correct and should remain

| Issue | Correct behavior/usage | Evidence |
| --- | --- | --- |
| FR-20261016-12 | Registration still returns both wrapper request IDs on success (`201`); verify still takes a plain-GUID `verificationRequestId` plus `code`; six-digit OTP, five-minute lifetime, five attempts, and account-type rules are unchanged. Keep current signup calls. | `docs/auth/registration-and-verification.md` |
| FR-20261016-01 | Resend still takes only `verificationRequestId` and still returns the replacement ID plus expiry (`200`). Keep the existing call shape. | `docs/auth/registration-and-verification.md`; `docs/postman/Sanad.Auth.postman_collection.json` |

## What needs correction or backend work

| Issue | Classification | Current truth and expected frontend action | Backend responsibility / phase | Evidence |
| --- | --- | --- | --- | --- |
| FR-20261016-12 | Former frontend assumption (lost signup IDs) resolved by new backend capability | A client that lost the registration response no longer needs to re-register: log in with the signup password and read the new nullable `emailVerificationRequestId`, `emailVerificationRequestExpiresOnUtc`, `phoneVerificationRequestId`, `phoneVerificationRequestExpiresOnUtc` on the restricted (`accessType` 2) login response, then continue with verify/resend. Verified channels return null. This was a missing backend capability, not a proven client bug. | FUR-02 — implemented/tested locally; not published/deployed | `docs/auth/email-password-login.md`; `LoginCommandHandler` |
| FR-20261016-01 | Backend reliability risk (resend failure ordering) fixed; client retry rule changes | Resend now accepts `Pending` **and** `Expired` requests and sends the code before changing state. A provider failure returns `502 Identity.Verification.ProviderDeliveryFailed` with no state change: retry with the **same** request ID after the 60-second cooldown. An `Expired` ID (including after an expired-code `verify` attempt) is resendable; `Verified`/`Invalidated` stay `400`. Concurrent resends persist exactly one replacement; the loser gets `409 Identity.Verification.RequestSuperseded` and must re-login to recover the winning ID (a losing call may already have sent an unusable code). | FUR-02 — implemented/tested locally; not published/deployed | `docs/auth/registration-and-verification.md`; `docs/auth/errors.md` |

## Implementation connection

| Issue | Contract | Implementation/revision | Verification | Deployment | Remaining action |
| --- | --- | --- | --- | --- | --- |
| FR-20261016-01/-12 | [FUR-02 contract](../../../../internal/work/portal-api-reconciliation/contracts/fur-02-verification-continuity.md) | Additive restricted-login recovery fields; send-first resend with stable 502 mapping; one Identity concurrency migration `20261016133316_AddVerificationRequestVersion`; local delta uncommitted | Focused Identity/API tests plus disposable-PostgreSQL one-winner concurrency and clean-migration proof; strict build zero warnings/errors; architecture 1/1 | Not deployed | Mastermind review → accepted-phase release → owner portal adopts recovery IDs and the resend retry rule |

Future-work links may point to a plan now; add actual implementation/evidence only
when it exists. A passing test is not a deployment receipt.

## Decisions and evidence requested

- Owner decisions: none for this topic.
- Frontend evidence: after release, confirm the owner portal reads the four nullable recovery fields from restricted login, retries resend with the same ID on 502, and treats 409-superseded as recover-via-login rather than as a generic error.
- Acceptance: no new endpoint; existing register/verify/resend shapes unchanged; restricted token still cannot call normal-access routes.
- Configuration/migrations: one Identity migration (`20261016133316_AddVerificationRequestVersion`, additive `version` column); no configuration change; no restart/package change beyond the normal release.
- Next action: owner returns the worker delivery report to the planning Mastermind for review.
