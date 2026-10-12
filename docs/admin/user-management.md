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
