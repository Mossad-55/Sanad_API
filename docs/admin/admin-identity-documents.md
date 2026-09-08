# Admin National ID review

Admin review of National ID (front + back) lives under `/api/v1/admin/identity-documents/...` and requires policy **`CaregiversAdmin`**: authenticated, `access_type` = `Normal`, `account_type` = `SuperAdmin` or `ContentAdmin`. The same admins who review caregiver certificates review Family / Medical / Companion National IDs.

This is the **only** way to read the scans. Files stay in private storage. List and detail never include paths or URLs. User self-service (`GET`/`PUT /api/v1/auth/identity-document`) never returns files.

National ID is **not** a caregiver certificate. Certificate verify/reject/revoke stay on `/api/v1/admin/caregivers/{id}/certificates/...`.

## Status values

| Value | Name |
|---|---|
| `1` | Pending |
| `2` | Verified |
| `3` | Rejected |
| `4` | Revoked |

Domain rules (already on `User`):

| Action | Valid from | Document | User |
|---|---|---|---|
| Verify | Pending | → Verified | unchanged |
| Reject | Pending | → Rejected, reason stored | unchanged (user can `PUT` new images) |
| Revoke | Verified | → Revoked, reason stored | → **Blocked**; every DeviceSession is revoked |

Reject is “photo unclear / try again”. Revoke is fraud / wrong person — same severity as blocking the identity, stronger than caregiver certificate revoke (which suspends the caregiver profile, not the login).

`User.Activate` still does **not** require a Verified National ID.

## List (paged)

```http
GET /api/v1/admin/identity-documents?page=1&pageSize=10&status=1
Authorization: Bearer {{accessToken}}
```

Query (all optional):

- `page` (default 1), `pageSize` (default 10, max 100)
- `status` — `1` Pending, `2` Verified, `3` Rejected, `4` Revoked

Empty database: `200` with `totalCount: 0` and `items: []`. Invalid paging: `400 Api.Validation.Failed`.

Response items include names, phone, email, account types, user status, verification status, review reason, and timestamps. **No file URLs.**

## Detail

```http
GET /api/v1/admin/identity-documents/{userId}
```

- `404 Identity.IdentityDocument.UserNotFound`
- `404 Identity.IdentityDocument.NotFound` — user exists but has not uploaded
- `409 Identity.IdentityDocument.UnsupportedAccountType` — Elderly / admin-only identity

## Download files

```http
GET /api/v1/admin/identity-documents/{userId}/front
GET /api/v1/admin/identity-documents/{userId}/back
```

Streams the private image (`image/jpeg` / `image/png` / `image/webp`). Download name `national-id-{userId}-{front|back}.<ext>`.

- `404 Storage.File.NotFound` — stored file missing
- Same user/document 404/409 as detail

## Review actions

All return `204` on success. Reason-bearing actions take `{ "reason": "…" }` (required, ≤ 500 chars).

| Action | Route | Body |
|---|---|---|
| Verify | `POST /{userId}/verify` | none |
| Reject | `POST /{userId}/reject` | `{ "reason": "…" }` |
| Revoke | `POST /{userId}/revoke` | `{ "reason": "…" }` |

- `409 Identity.IdentityDocument.InvalidOperation` — wrong current status (e.g. verify a Verified document, revoke a Pending one)
- `400 Api.Validation.Failed` — missing/overlong reason

After reject, the user keeps a Normal login and replaces both images with `PUT /api/v1/auth/identity-document` (status returns to Pending). After revoke the user is Blocked and cannot log in or re-upload until a later admin-unblock slice.
