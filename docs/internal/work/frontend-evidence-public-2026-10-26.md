# Frontend evidence — public/anonymous surfaces (2026-10-26)

Companion evidence for frontend-report item 4 (public legal route: frontend
reports `404`, backend doc says `200`). **Read-only**: no auth/login/session, no
DB write, no provider/financial call, no config/runtime change, no restart, no
source edit. This file is new and independent; the two handoff docs
(`frontend-report-2026-10-26.md`, `frontend-response-2026-10-26.md`) were **not**
edited.

- Captured: **2026-10-26 ~15:36 UTC** (server `Date` headers below).
- Rehearsal host: `72.62.92.144`; API `Staging`, manifest revision
  `2bccdb3e5b59c65ba1cad91abd640bbb7fb92b81`.
- Loopback: `http://127.0.0.1:5001` (Kestrel, reached via read-only SSH on the
  host; the port is **not** exposed publicly — a direct request to
  `72.62.92.144:5001` times out).
- Public: `http://72.62.92.144:8091` (nginx 1.24.0 → Kestrel).
- Every request was a plain `GET`, **no `Authorization` header, no cookie, no
  login**, redirects not followed.

---

## 1. Source route table — every anonymous/public API endpoint

Enumerated from the API controllers (`src/API/Sanad.API/Controllers`). "Policy" is
the authorization attribute that actually governs the action; `[AllowAnonymous]`
is action-level unless it is on the class (marked "class").

| Method | Exact route template | Policy / auth | Controller |
|---|---|---|---|
| GET | `/api/v1/public/legal/{documentType}` | `[AllowAnonymous]` (class) | `PublicLegalController` |
| GET | `/api/v1/deployment/info` | `[AllowAnonymous]` | `DeploymentInfoController` |
| GET | `/api/v1/platform/status` | `[AllowAnonymous]` | `PlatformStatusController` |
| GET | `/api/v1/landing/sections` | `[AllowAnonymous]` | `LandingController` |
| GET | `/api/v1/landing/stats` | `[AllowAnonymous]` | `LandingController` |
| GET | `/api/v1/site-settings` | `[AllowAnonymous]` | `PublicSiteSettingsController` |
| GET | `/api/v1/splash-screens` | `[AllowAnonymous]` | `SplashScreensController` |
| GET | `/api/v1/elderly/welcome?language=en|ar` | `[AllowAnonymous]` (class) | `ElderlyWelcomeController` |
| GET | `/api/v1/support/contact` | `[AllowAnonymous]` | `SupportController` |
| GET | `/api/v1/lookups/services` | `[AllowAnonymous]` | `LookupsController` |
| GET | `/api/v1/lookups/languages` | `[AllowAnonymous]` | `LookupsController` |
| GET | `/api/v1/lookups/governorates` | `[AllowAnonymous]` | `LookupsController` |
| GET | `/api/v1/lookups/cities?governorateId=` | `[AllowAnonymous]` | `LookupsController` |
| GET | `/api/v1/lookups/areas?cityId=` | `[AllowAnonymous]` | `LookupsController` |
| GET | `/api/v1/lookups/specializations` | `[AllowAnonymous]` | `LookupsController` |
| GET | `/api/v1/lookups/professional-titles` | `[AllowAnonymous]` | `LookupsController` |
| GET | `/api/v1/lookups/academic-degrees` | `[AllowAnonymous]` | `LookupsController` |
| GET | `/api/v1/lookups/banks` | `[AllowAnonymous]` | `LookupsController` |
| GET | `/api/v1/care-homes/discovery` | `[AllowAnonymous]` (class) | `CareHomeDiscoveryController` |
| GET | `/api/v1/care-homes/discovery/{careHomeId:guid}` | `[AllowAnonymous]` (class) | `CareHomeDiscoveryController` |
| GET | `/api/v1/care-homes/facilities/{facilityId:guid}/visit-availability` | `[AllowAnonymous]` | `CareHomeVisitsController` |
| GET | `/api/v1/care-homes/discovery/{careHomeId:guid}/media/{mediaId:guid}` | `[AllowAnonymous]` | `CareHomeProfileMediaController` |
| GET | `/api/v1/care-homes/discovery/{careHomeId:guid}/room-types/{roomTypeId:guid}/media/{mediaId:guid}/file` | `[AllowAnonymous]` | `CareHomeRoomMediaController` |
| GET | `/api/v1/care-homes/discovery/{careHomeId:guid}/rooms/{roomId:guid}/media/{mediaId:guid}/file` | `[AllowAnonymous]` | `CareHomeRoomMediaController` |
| POST | `/api/v1/partner-contact` | `[AllowAnonymous]` (class) | `PartnerContactController` |
| POST | `/api/v1/auth/register` | `[AllowAnonymous]` | `AuthController` |
| POST | `/api/v1/auth/verification/verify` | `[AllowAnonymous]` | `AuthController` |
| POST | `/api/v1/auth/verification/resend` | `[AllowAnonymous]` | `AuthController` |
| POST | `/api/v1/auth/login` | `[AllowAnonymous]` | `AuthController` |
| POST | `/api/v1/auth/refresh` | `[AllowAnonymous]` | `AuthController` |
| POST | `/api/v1/auth/elderly/request-otp` | `[AllowAnonymous]` | `AuthController` |
| POST | `/api/v1/auth/elderly/verify-otp` | `[AllowAnonymous]` | `AuthController` |
| POST | `/api/v1/auth/password/reset/request` | `[AllowAnonymous]` | `AuthController` |
| POST | `/api/v1/auth/password/reset` | `[AllowAnonymous]` | `AuthController` |
| POST | `/api/v1/payments/webhooks/paymob` | `[AllowAnonymous]` (class) + provider HMAC | `PaymobWebhookController` |

Notes that matter for item 4:

- The **public** legal surface is **only**
  `/api/v1/public/legal/{documentType}`. `documentType` is `1` (PrivacyPolicy)
  or `2` (TermsAndConditions); the enum **name** is accepted case-insensitively
  (`PrivacyPolicy`, `TermsAndConditions`). Any other value (including
  `privacy-policy`, `privacy`, `terms`, `0`, `3`) is **`400`**
  `Cms.Content.InvalidDocumentType` — never a fallback.
- The **authenticated** legal surface is a different route:
  `/api/v1/legal/privacy-policy` and `/api/v1/legal/terms`, both under
  `[Authorize(Policy = NormalAccess)]` → **`401` without a token**. There is no
  `/api/v1/legal/{documentType}` route, so numeric/name calls there are `404`.
- There is **no** route for `/`, `/health`, `/healthz`, `/privacy`, `/terms`,
  `/about`, `/privacy-policy`, `/terms-and-conditions` or `/legal` in source.

---

## 2. Observed status — loopback (`127.0.0.1:5001`) vs public (`:8091`)

Both paths returned **identical status codes** for every probe. (Only the
`Server` header and `traceId` differ: `Kestrel` vs `nginx/1.24.0 (Ubuntu)`.)

| Path | 127.0.0.1:5001 | 72.62.92.144:8091 |
|---|---|---|
| `/` | **404** | **404** |
| `/api/v1/deployment/info` | **200** | **200** |
| `/api/v1/public/legal/1` | **200** | **200** |
| `/api/v1/public/legal/2` | **200** | **200** |
| `/api/v1/public/legal/PrivacyPolicy` | 200 | 200 |
| `/api/v1/public/legal/TermsAndConditions` | 200 | 200 |
| `/api/v1/public/legal/privacy-policy` | 400 | 400 |
| `/api/v1/public/legal/privacy` | — | 400 |
| `/api/v1/public/legal/terms` | — | 400 |
| `/api/v1/public/legal/0` | — | 400 |
| `/api/v1/public/legal/3` | — | 400 |
| `/api/v1/public/legal` | — | 404 |
| `/api/v1/legal/1` | 404 | 404 |
| `/api/v1/legal/privacy` | 404 | 404 |
| `/api/v1/legal/PrivacyPolicy` | 404 | 404 |
| `/api/v1/legal/terms` | **401** | **401** |
| `/api/v1/legal/privacy-policy` | — | 401 |
| `/privacy` | 404 | 404 |
| `/terms` | 404 | 404 |
| `/about` | 404 | 404 |
| `/privacy-policy` | 404 | 404 |
| `/terms-and-conditions` | 404 | 404 |
| `/legal` | 404 | 404 |
| `/api/v1/public/privacy-policy` | — | 404 |
| `/api/v1/public/terms` | — | 404 |
| `/api/v1/privacy-policy` | — | 404 |
| `/api/v1/terms` | — | 404 |
| `/api/v1/deployment-info` | — | 404 |
| `/api/v1/platform/status` | **200** | **200** |
| `/api/v1/site-settings` | **200** | **200** |
| `/health` | 404 | 404 |
| `/healthz` | 404 | 404 |

*("—" = not probed on that path; probed set was run on both where it matters.
Both hosts were swept for the primary rows; the extra 404/400 variants were
swept on the public path and are route-table-derived.)*

### 2.1 Exact raw responses (public path)

`GET http://72.62.92.144:8091/` — **404**:

```
HTTP/1.1 404 Not Found
Server: nginx/1.24.0 (Ubuntu)
Date: Mon, 26 Oct 2026 15:36:26 GMT
Content-Type: application/problem+json
Transfer-Encoding: chunked
Connection: keep-alive

{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.5","title":"Not Found","status":404,"traceId":"00-83db4303f2c591ad5dca79500ffd7193-4f1a32371efab2fb-00"}
```

`GET /api/v1/public/legal/1` — **200** (DEMO placeholder content):

```
HTTP/1.1 200 OK
Server: nginx/1.24.0 (Ubuntu)
Date: Mon, 26 Oct 2026 15:36:27 GMT
Content-Type: application/json; charset=utf-8

{"id":{"value":"01a17542-46e5-7920-979f-fe8bdda0735e"},"documentType":1,"audience":0,"version":1,"status":2,"createdOnUtc":"2026-10-25T22:50:40.229956Z","updatedOnUtc":"2026-10-25T22:50:40.317Z","publishedOnUtc":"2026-10-25T22:50:40.317Z","sections":[{"sectionType":1,"displayOrder":1,"arabicTitle":"نص تجريبي - ليس نصًا قانونيًا","englishTitle":"DEMO - not legal text","arabicDescription":"هذا نص تجريبي لأغراض العرض فقط ولا يمثل سياسة خصوصية فعلية.","englishDescription":"SANAD DEMO placeholder. Not legal text.","arabicBullets":[],"englishBullets":[]},{"sectionType":2,"displayOrder":2,...}]}
```

`GET /api/v1/public/legal/2` — **200** (DEMO placeholder content):
id `01a17542-4746-7ec3-bf05-c3910b512839`, `englishTitle` `"DEMO - not legal
text"`, `englishDescription` `"SANAD DEMO terms placeholder. Not legal text."`.

`GET /api/v1/legal/terms` (no token) — **401**:

```
HTTP/1.1 401 Unauthorized
Content-Type: application/problem+json; charset=utf-8
WWW-Authenticate: Bearer

{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.2","title":"Unauthorized","status":401,"traceId":"00-d7d1a5d49540618759613cc3f6ceb2b9-a5a95f4368f170d7-00"}
```

`GET /api/v1/public/legal/privacy-policy` — **400**:

```
HTTP/1.1 400 Bad Request
Content-Type: application/problem+json; charset=utf-8

{"type":"https://httpstatuses.com/400","title":"Bad Request","status":400,"detail":"The request could not be completed.","instance":"/api/v1/public/legal/privacy-policy","code":"Cms.Content.InvalidDocumentType","traceId":"0HNPJ2IKOCM1H:00000001"}
```

`GET /api/v1/deployment/info` — **200**:

```
{"application":"Sanad.API","environment":"Staging","manifestRevision":"2bccdb3e5b59c65ba1cad91abd640bbb7fb92b81","requiredMigrations":{...9 contexts...},"appliedMigrations":{...same 9...},"databaseReachable":true}
```

`GET /api/v1/platform/status` — **200**:

```
{"state":1,"maintenanceMessageArabic":null,"maintenanceMessageEnglish":null,"availablePaymentMethods":[1,2],"subscriptionCardEnrollmentAvailable":true}
```

---

## 3. Key findings (404 vs 200)

1. **`/` is `404` on BOTH loopback and public** — there is no root/index route in
   source, and nginx proxies straight to Kestrel with no static landing page.
   This **corrects** the phrasing in `frontend-response-2026-10-26.md` item 4
   ("the public surface currently reads 200 for the DEMO page"): the `200` DEMO
   payload is **not** at `/`; it is the body of the **public legal route**.
2. **The DEMO/placeholder page is `/api/v1/public/legal/1` and `/2`** — `200`
   with bilingual "DEMO - not legal text" / "SANAD DEMO placeholder. Not legal
   text." sections. So the backend `200` is real but it is a *placeholder legal
   document*, not a marketing/legal page.
3. **The real public legal route is `/api/v1/public/legal/{documentType}`**
   with `documentType` ∈ `{1,2}` (or `PrivacyPolicy`/`TermsAndConditions`).
   Anything else at that segment is `400`, and `/api/v1/public/legal` alone is
   `404`.
4. **No source route exists** for `/privacy`, `/terms`, `/about`,
   `/privacy-policy`, `/terms-and-conditions`, `/legal`, `/health`, `/healthz`
   — all `404` on both hosts. If the frontend's failing request targets one of
   these, the `404` is expected and is **not** a deployment defect.
5. **The authenticated legal paths are different and return `401`, not `404`,**
   when unauthenticated: `/api/v1/legal/privacy-policy`, `/api/v1/legal/terms`.
   Numeric/other-segment calls under `/api/v1/legal/...` are `404` because that
   controller only declares those two literal routes.
6. **Anonymous 200 surfaces confirmed live** (no token): `deployment/info`,
   `platform/status`, `site-settings`, `public/legal/1`, `public/legal/2`.
7. **nginx is transparent**: status codes are identical loopback vs public, so a
   loopback `404` is not a proxy/rewrite artifact — the API itself has no such
   route.

---

## 4. What the frontend must send us

To close item 4 without ambiguity, send us **one** reproduction with all four
fields: (a) the **exact request route** — the full path **including** the
`documentType` segment and any query string, from the actual failing call, not a
summary (e.g. is it `/api/v1/public/legal/1`, `/api/v1/legal/privacy-policy`, or
something else?); (b) the **environment** it was sent to — base URL/host and port
(the rehearsal is `http://72.62.92.144:8091` public / `http://127.0.0.1:5001`
loopback), and the API revision if known; (c) the **UTC timestamp** of the
attempt; and (d) the **raw response** — status line, full headers, and the raw
body verbatim (a browser/network-panel "copy as cURL" plus "copy response" is
ideal), so we can compare it byte-for-byte against the observed `200` DEMO
payload on `/api/v1/public/legal/1|2` and the `404` on `/` and the non-existent
paths above. Until that exact route is in hand, a `404` on a path we do not
declare, or an expected `400`/`401` on the routes that do exist, cannot be
distinguished from a genuine deployment fault.

---

## 5. Rules honored / limitations

- Read-only only: anonymous `GET`s (and status-only probes) plus read-only source
  reads. No `Authorization` header, cookie or login was ever sent; no POST was
  issued against any mutating route; no DB write, provider call, config/runtime
  change or restart.
- The loopback port `5001` is firewall-closed externally; loopback probes were run
  from the host over the established read-only SSH access (`curl` to
  `127.0.0.1:5001`). The public probes were run directly from the workstation.
- Status codes and the DEMO body reflect the deployed revision `2bccdb3e` at
  capture time; they go stale after any release.
- No handoff/report doc and no source file was modified by this task.
