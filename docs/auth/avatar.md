# Avatar

Family, Medical Caregiver, and Companion Caregiver upload a profile photo **after** email + phone verification and a **Normal** login. The mobile app can call this from any screen.

This is **not** part of `POST /api/v1/auth/register`. Register stays JSON (names, email, phone, password, account type).

Elderly cannot upload here. National ID stays on `PUT /api/v1/auth/identity-document`. Caregiver professional certificates stay on `POST /api/v1/caregiver/certificates`.

Development base URL:

```text
https://localhost:7296
```

Use `curl -k`. Requires policy `NormalAccess`. Restricted tokens receive `403`. Missing/invalid bearer tokens receive `401`.

Files are stored in **private** storage (folder `avatars`). Responses never include paths or URLs. The owner downloads the image with GET on this same path.

Allowed types: `image/jpeg` (`image/jpg` accepted), `image/png`, `image/webp`. PDF is rejected. Maximum **5 MB**.

## PUT `/api/v1/auth/avatar`

Normal JWT. Multipart field **`file`**. Success `204`. Replaces a previous avatar and deletes the old private file.

```bash
curl -k -sS https://localhost:7296/api/v1/auth/avatar \
  -X PUT \
  -H 'Authorization: Bearer REPLACE_ACCESS_TOKEN' \
  -F 'file=@avatar.jpg;type=image/jpeg'
```

| HTTP | `code` |
|---|---|
| 400 | `Storage.File.Empty` / `TooLarge` / `UnsupportedType` |
| 401 | missing/invalid token |
| 403 | restricted token |
| 404 | `Identity.Avatar.UserNotFound` |
| 409 | `Identity.Avatar.UnsupportedAccountType` |
| 409 | `Identity.Avatar.InvalidOperation` — blocked user |

## GET `/api/v1/auth/avatar`

Normal JWT. Streams the image. Missing avatar is `404 Identity.Avatar.NotFound` (not an empty JSON body).

```bash
curl -k -sS https://localhost:7296/api/v1/auth/avatar \
  -H 'Authorization: Bearer REPLACE_ACCESS_TOKEN' \
  -o avatar.jpg
```

## Suggested client flow

```text
POST /api/v1/auth/register          (no avatar)
POST /api/v1/auth/verification/verify
POST /api/v1/auth/login             (Normal JWT)
PUT  /api/v1/auth/avatar            (file)
GET  /api/v1/auth/avatar            (display)
```
