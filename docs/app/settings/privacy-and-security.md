# Privacy & Security (app / settings)

The settings screen shared by the app's authenticated accounts; it is PURE REUSE — password change and device sessions come from Identity (Auth), while the two legal pages and the Help Center are CMS surfaces (SET-12 legal documents, SET-13 help center) served by their own signed-in endpoints. They are NOT splash screens.

Development base URL: https://localhost:7296

## Endpoint map

| Screen action | Endpoint | Documented in |
|---|---|---|
| Change password (F13) | POST /api/v1/auth/password/change | docs/auth/password-reset-and-change.md |
| List device sessions (F12) | GET /api/v1/auth/sessions | docs/auth/refresh-and-sessions.md |
| Log out this device (F12) | POST /api/v1/auth/sessions/logout | docs/auth/refresh-and-sessions.md |
| Log out everywhere (F12) | POST /api/v1/auth/sessions/logout-all | docs/auth/refresh-and-sessions.md |
| Revoke one session (F12) | DELETE /api/v1/auth/sessions/{sessionId} | docs/auth/refresh-and-sessions.md |
| Privacy Policy page (F14) | GET /api/v1/legal/privacy-policy | docs/app/settings/legal.md |
| Terms & Conditions (F15) | GET /api/v1/legal/terms | docs/app/settings/legal.md |
| Help Center (FAQs + contact card) | GET /api/v1/help-center | docs/app/settings/help-support.md |

## Change password (F13)

Normal JWT only; restricted verification tokens get 403. Success 204.

Body: { "currentPassword": "...", "newPassword": "..." }

Rules:
- Current password must be correct
- New password must differ from the current password
- Success revokes every DeviceSession, including the current one
- The client must log in again

| HTTP | `code` |
|---|---|
| 401 | `Identity.Password.InvalidCurrentPassword` |
| 400 | `Identity.Password.NewPasswordMustDiffer` |
| 404 | `Identity.Password.UserNotFound` |

```bash
curl -sS -o /dev/null -w "%{http_code}\n" \
  https://localhost:7296/api/v1/auth/password/change \
  -H "Authorization: Bearer ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "currentPassword": "Password1234",
    "newPassword": "NewPassword1234"
  }'
```

## Device sessions (F12)

### List sessions — GET, Normal JWT, 200

Returns non-revoked, non-expired sessions with device name, platform, app version, created, expiry and last rotation, and that no token hashes are returned.

```json
{
  "sessions": [
    {
      "deviceSessionId": { "value": "…" },
      "deviceName": "…",
      "platform": "…",
      "appVersion": "…",
      "createdOnUtc": "…",
      "expiresOnUtc": "…",
      "lastRotatedOnUtc": "…"
    }
  ]
}
```

Maximum five active sessions; the user must remove one, the API never picks.

### Log out this device — POST

Normal JWT plus `X-Device-Session-Id`. Success `204`. A missing or invalid header returns `400` with `Api.Auth.InvalidDeviceSessionHeader`.

### Log out everywhere — POST /api/v1/auth/sessions/logout-all

Normal JWT. Success `204`. Revokes every non-revoked session for the current user.

### Revoke one session — DELETE /api/v1/auth/sessions/{sessionId}

Normal JWT. Success `204`. Revokes one owned session. A foreign or missing session returns `404` so ownership is not leaked.

| HTTP | `code` |
|---|---|
| 404 | `Identity.Sessions.SessionNotFound` |
| 404 | `Identity.Sessions.SessionNotOwned` |
| 404 | `Identity.Sessions.UserNotFound` |

Revocation is idempotent. Maximum five active sessions. The user must remove an old session; the API does not pick one.

## Privacy Policy (F14) and Terms & Conditions (F15)

Both pages are admin-managed **legal documents** (SET-12) — not splash screens. `GET /api/v1/legal/privacy-policy` and `GET /api/v1/legal/terms` require the app JWT; the audience is derived from the token (Family, MedicalCaregiver, CompanionCaregiver, Elderly) and can never be overridden by the client. Each call returns exactly the current `Published` version for that token's audience and type, with bilingual ordered sections. There is no fallback: if that audience has no publication the API returns `404 Cms.Legal.NotPublished`, and admin account types get `403 Cms.Content.UnsupportedAudience`. Full contract: `docs/app/settings/legal.md`; admin authoring and publishing: `docs/admin/legal-content.md`.

The Terms agreement is captured in the registration flow; on this screen both pages are VIEW-ONLY.

## Help Center (support contact + FAQs)

`GET /api/v1/help-center` (same JWT) returns the active FAQs for the token's audience ordered by `displayOrder`, plus the optional global support contact card (`supportPhone`, `supportEmail`) shown as “Contact us”. Before any content is configured it is a `200` with empty FAQs and a `null` contact — it never blocks the screen. Contract: `docs/app/settings/help-support.md`; admin management: `docs/admin/help-center.md`.

## Not on this screen (yet)

- Two-factor authentication (decision D7 — not built)
- Caregiver profile/ratings visibility toggles (planned, not implemented)

## Related docs

- docs/auth/password-reset-and-change.md
- docs/auth/refresh-and-sessions.md
- docs/app/settings/legal.md
- docs/app/settings/help-support.md
- docs/admin/legal-content.md
- docs/admin/help-center.md
