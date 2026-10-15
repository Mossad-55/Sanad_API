# SuperAdmin user management

Phase 1 provides read-only user and active-session views. All routes require a
Normal-access `SuperAdmin` account. SupportAdmin, ContentAdmin, and FinanceAdmin
accounts are denied.

## List users

`GET /api/v1/admin/users`

Query parameters: `search` (Arabic or English name, email, or phone; case
insensitive), `accountType`, `status`, `sort` (`Newest`, `Oldest`, or `Name`),
`page`, and `pageSize`. Page and pageSize are clamped to 1–100. Invalid enum
filters or sort values return 400 `Identity.Admin.InvalidQuery`. Name sorting is
Arabic name ascending, then English name ascending.

The response is `{ items, page, pageSize, totalCount }`. Each item includes the
user id, names, email, phone, status, account types, creation time, and
`isVerified`. `isVerified` is false only while status is
`PendingVerification`.

## User detail

`GET /api/v1/admin/users/{id}`

Returns profile fields, status, account types, creation time, `isVerified`,
active sessions, and `linked`. Unknown ids return 404 `Identity.User.NotFound`
without revealing account type. For CareHomeOwner, `linked` contains
`facilityId` and `facilityStatus`; for every other account type it is `{}`.

## Admin active sessions

`GET /api/v1/admin/users/sessions`

Returns paged active sessions belonging to SuperAdmin, SupportAdmin,
ContentAdmin, and FinanceAdmin accounts. Optional filters are `accountType` and
`userId`; sort supports `Newest`, `Oldest`, and `Name`, with the same page and
pageSize clamping as the user list. Revoked or expired sessions are excluded.
Session fields include `deviceId`, device name, platform, app version, creation
and expiration times, and `lastSeenUtc`.

There is no request-tracking timestamp in Identity. `lastSeenUtc` is therefore
the device session's `LastRotatedOnUtc` value (the last refresh-token rotation),
not the time of the user's last API request. It is null if that session has
never rotated its token.

## Suspend, reactivate, and block

`POST /api/v1/admin/users/{id}/suspend` and
`POST /api/v1/admin/users/{id}/block` require `{ "reason": "..." }` and
return the updated status plus the number of sessions revoked. Suspension and
blocking revoke every active device session. `POST
/api/v1/admin/users/{id}/reactivate` changes a suspended or blocked account to
Active; no sessions are restored. Status notices are queued as best-effort
in-app and email notifications after the identity mutation succeeds.

Reactivation requires activation readiness: the account must hold at least one
account record and a verified phone; a non-Elderly-only account additionally
needs an email, a verified email, and a password. Elderly-only accounts never
acquire email/password requirements. A freshly created admin is
`PendingVerification` with unverified contacts, so suspending or blocking that
account and then reactivating it returns `409
Identity.Admin.ActivationNotReady` with the safe detail "The user does not
meet the activation requirements. Complete the required verification and
account setup before reactivation." The target keeps its status, reason,
timestamps, and contact flags; no success audit entry or notice is created,
previously revoked sessions stay revoked, and no new login session is issued.
The pending account must complete the normal email/SMS OTP verification flow
first; admins cannot force-verify contacts or force the account Active. A
previously verified suspended or blocked user still reactivates to Active with
200, a cleared status reason, exactly one `Reactivated` audit entry, and one
success notice. Repeating reactivation against an Active account stays `409
Identity.Admin.NotSuspended`.

The seeded SuperAdmin and the acting SuperAdmin cannot be suspended or blocked.
The final active SuperAdmin cannot be blocked. Repeated or invalid transitions
return the documented `Identity.Admin.*` conflict codes; unknown users return
`Identity.User.NotFound`.

## Session revocation

`POST /api/v1/admin/users/{id}/sessions/revoke` returns 204 and revokes all
active sessions. To revoke one session, send `{ "sessionId": "..." }`.
Unknown users and sessions return `Identity.User.NotFound`.

## Password reset, account type, and admin creation

`POST /api/v1/admin/users/{id}/password-reset` returns a temporary password
exactly once and revokes all active sessions. Treat the response as a secret;
do not store or log it. The seeded SuperAdmin cannot be reset through this API.

`PUT /api/v1/admin/users/{id}/account-type` accepts an `accountType` and an
optional `reason` of up to 1000 characters. It accepts an account type of
`SuperAdmin`, `SupportAdmin`, `ContentAdmin`, or `FinanceAdmin`. Only
administrative-to-administrative changes are allowed. The seeded SuperAdmin
cannot be changed, and demoting the final active SuperAdmin is rejected.

`POST /api/v1/admin/users` accepts the existing registration fields
(`arabicFullName`, `englishFullName`, `email`, `phoneNumber`, `password`, and
`accountType`) and returns the existing registration response containing the
user id and email/phone verification request ids. Only administrative account
types are accepted. Verification codes are delivered through the existing
email/SMS OTP flow; the new account stays `PendingVerification` and cannot be
used as an administrator until the normal verification flow completes. Admins
cannot force-verify the account.

## Anonymization and audit

`POST /api/v1/admin/users/{id}/anonymize` requires a non-empty `reason` and
scrubs names, email, phone, and avatar data, blocks the account, and revokes
active sessions while retaining account aggregates. The seeded SuperAdmin,
the acting SuperAdmin, and the final active SuperAdmin are protected. Repeating
anonymization returns `409 Identity.Admin.AlreadyAnonymized`.

`GET /api/v1/admin/users/audit` returns a paged append-only admin-action feed.
Filters: `actorUserId`, `targetUserId`, `action`, `fromUtc`, and `toUtc`.
Sorting supports `Newest`, `Oldest`, and `Name`; Name sorts by the acting
administrator's Arabic name then English name. Mutations record actor, target,
action, optional reason, and UTC timestamp. Reads do not create audit entries.

## OpenAPI contract

All ten reads/actions above publish their actual success schemas: the four
GETs return `200` envelopes/records (`{ items, page, pageSize, totalCount }`
for list/sessions/audit; full detail with active sessions and facility link),
the six actions return `200` with `AdminUserStatusResponse`
(`{ userId, status, revokedSessionCount }`), `ResetAdminUserPasswordResponse`
(`{ userId, temporaryPassword, revokedSessionCount }`),
`AdminUserAccountTypeResponse`, or `RegisterUserResponse` — except
create-admin, which returns `201` with `{ userId, emailVerificationRequestId,
phoneVerificationRequestId }` (ID wrappers stay `{ "value": "…" }`). Unknown
users return `404 Identity.User.NotFound`. Shared enum/ID conventions are in
[api-contract-conventions](api-contract-conventions.md).
