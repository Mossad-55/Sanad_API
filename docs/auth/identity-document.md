# National ID (identity document)

Family, Medical Caregiver, and Companion Caregiver upload National ID **front and back** on this endpoint. The mobile app can call it from any screen (Family registration step, caregiver documents/certificates step, or later).

This is **not** part of `POST /api/v1/auth/register`. Register stays JSON (names, email, phone, password, account type). Collect the photos on the device, finish email + phone OTP, log in with a **Normal** token, then call this API.

Elderly cannot upload here. Professional certificates (practice license, graduation) stay on `POST /api/v1/caregiver/certificates`. National ID belongs to the User, not the Caregiver aggregate.

Development base URL:

```text
https://localhost:7296
```

Use `curl -k` against that host. `http://localhost:5235` redirects `307` to HTTPS.

Requires policy `NormalAccess`. Restricted verification tokens receive framework `403`. Missing or invalid bearer tokens receive framework `401`.

Files are stored in **private** storage (folder `identity-documents`, sibling root that `GET /files/{key}` never serves). Responses never include paths or URLs. Admin download/review is a later slice; until then status stays `Pending`.

Allowed content types: `image/jpeg` (`image/jpg` is accepted and stored as jpeg), `image/png`, `image/webp`. PDF is rejected. Maximum **5 MB per image**. The PUT request size limit is **10 MB** (two files).

`User.Activate` does **not** require a National ID. Caregiver `POST /submit` does **not** require it in this slice.

## Status values

| Value | Name |
|---|---|
| `1` | Pending |
| `2` | Verified |
| `3` | Rejected |
| `4` | Revoked |

## GET `/api/v1/auth/identity-document`

Normal JWT. Success `200`.

When nothing has been uploaded:

```json
{
  "uploaded": false,
  "verificationStatus": null,
  "reviewReason": null
}
```

When a document exists:

```json
{
  "uploaded": true,
  "verificationStatus": 1,
  "reviewReason": null
}
```

```bash
curl -k -sS https://localhost:7296/api/v1/auth/identity-document \
  -H 'Authorization: Bearer REPLACE_ACCESS_TOKEN'
```

Missing document is **not** `404`. `404 Identity.IdentityDocument.UserNotFound` means the JWT `sub` does not match a user.

| HTTP | `code` |
|---|---|
| 401 | missing/invalid token (framework) |
| 403 | restricted token (framework) |
| 404 | `Identity.IdentityDocument.UserNotFound` |
| 409 | `Identity.IdentityDocument.UnsupportedAccountType` |

## PUT `/api/v1/auth/identity-document`

Normal JWT. Multipart fields **`front`** and **`back`** (both required). Success `200` with the same JSON shape as GET (`uploaded: true`, status `Pending`).

First call creates the document (`Pending`). Later calls replace both images, return status to `Pending`, and delete the previous private files.

If the user is **Active**, a replace also:

- sets User status to `PendingVerification`
- revokes every DeviceSession (reason `National ID was replaced.`)

The client must log in again after that replace. First upload on an Active user does **not** change User status and does **not** revoke sessions.

```bash
curl -k -sS https://localhost:7296/api/v1/auth/identity-document \
  -X PUT \
  -H 'Authorization: Bearer REPLACE_ACCESS_TOKEN' \
  -F 'front=@front.jpg;type=image/jpeg' \
  -F 'back=@back.jpg;type=image/jpeg'
```

JSON bodies in other Auth curls stay in **single quotes**. Multipart uses `-F` as above.

Compensation: if the command fails after the files are saved, both new files are deleted. If the back image fails validation after the front was saved, the front file is deleted.

| HTTP | `code` |
|---|---|
| 400 | `Storage.File.Empty` — missing `front` or `back` |
| 400 | `Storage.File.TooLarge` — a file exceeds 5 MB |
| 400 | `Storage.File.UnsupportedType` — not jpeg/png/webp |
| 401 | missing/invalid token (framework) |
| 403 | restricted token (framework) |
| 404 | `Identity.IdentityDocument.UserNotFound` |
| 409 | `Identity.IdentityDocument.UnsupportedAccountType` — Elderly or admin-only identity |
| 409 | `Identity.IdentityDocument.InvalidOperation` — blocked user, or Domain rejected the mutation |

## Suggested client flow

```text
POST /api/v1/auth/register          (no ID files)
POST /api/v1/auth/verification/verify  (email, then phone)
POST /api/v1/auth/login             (Normal JWT)
PUT  /api/v1/auth/identity-document (front + back)
GET  /api/v1/auth/identity-document (status)
```
