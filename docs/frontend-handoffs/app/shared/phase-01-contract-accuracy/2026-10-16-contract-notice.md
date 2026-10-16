# Shared App contract notice — FUR-01 auth shapes and unread-count convention

Date: 2026-10-16 (Africa/Cairo). Phase: FUR-01.
Purpose: implementation delivery (local; not deployed).
Audience: App shared — Family, Caregiver and Elderly clients (convention notice only).
Communication state: ready for owner forwarding; receipt not confirmed.

No mobile frontend was audited. No frontend repository was inspected. This
notice covers only the shared auth/unread-count wire conventions that mobile
clients also consume; it is not a mobile code audit.

## Baseline and authority

- Original intake and issue IDs: `FR-20261016-30` (auth payloads), `FR-20261016-31` (typed IDs), `FR-20261016-32` (enums) insofar as they touch shared auth/unread-count shapes.
- Canonical plan/approved contract: [FUR-01 contract](../../../../internal/work/portal-api-reconciliation/contracts/fur-01-contract-accuracy.md) (owner-approved 2026-10-16).
- Source revision: intake `4b65369d760c8af6b2242ebd2022a6cb10057580`; FUR-01 local delta uncommitted. Full evidence in [delivery report](../../../../internal/work/portal-api-reconciliation/reports/fur-01-contract-accuracy.md).
- Deployment: not deployed. Behavior below is existing runtime truth, unchanged by FUR-01; only its documentation/examples were corrected locally.

## What is correct and should remain

| Issue | Correct behavior/usage | Evidence |
| --- | --- | --- |
| FR-20261016-30 | Register sends `arabicFullName`, `englishFullName`, `email`, `phoneNumber`, `password`, `accountType`. Verify sends `verificationRequestId` + `code`; resend sends `verificationRequestId`. Never send `email`/`purpose` payloads. | `docs/auth/registration-and-verification.md` |
| FR-20261016-31 | Response IDs such as `userId`, `verificationRequestId`, and `deviceSessionId` are wrappers (`{ "value": "<guid>" }`); request GUIDs stay plain GUIDs. `X-Device-Session-Id` carries the inner GUID, not a JSON object. | `docs/auth/refresh-and-sessions.md`; `PortalAuthExampleContractTests` |
| FR-20261016-30 | Restricted login returns verification flags with null session/refresh values and **no** verification request IDs. Resume-signup recovery is the separate FUR-02 contract, not promised here. | `docs/auth/email-password-login.md` |
| FR-20261016-31 | Unread count is a bare JSON integer (`3`, or `0`), never `{ "count": 3 }`. | `docs/app/notifications.md`; `PortalRemainingResponseContractTests.UnreadCount` |

## What needs correction

| Issue | Classification | Current truth and expected frontend action | Backend responsibility / phase | Evidence |
| --- | --- | --- | --- | --- |
| FR-20261016-30/31 | Stale examples (fixed locally) | Client/Postman request shapes were already right; the auth guide and Postman response examples were stale (plain-GUID examples where the API returns wrappers; firstName/lastName/phone and email/purpose
payloads). Shared models must use wrapper IDs for `userId`/`verificationRequestId`/`deviceSessionId` and the literal
`attemptesRemaining` spelling on verify responses. Guide literals are now verified against runtime serialization by `PortalLiteralExampleConformanceTests`. | FUR-01 — implemented/tested locally; not published/deployed | `docs/auth/registration-and-verification.md`; `docs/postman/Sanad.Auth.postman_collection.json` |

## Decisions and evidence requested

- Owner decisions: none for this topic.
- Frontend evidence: none required before release; after the accepted-phase release, shared models can be rechecked against the published examples.
- Acceptance: no new app behavior is introduced; existing login/refresh/unread-count handling stays as-is.
- Configuration/migrations: none.
- Next action: owner returns the worker delivery report to the planning Mastermind for review.
