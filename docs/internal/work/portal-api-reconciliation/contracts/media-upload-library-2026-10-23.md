# Storage-backed library video and bounded media correctness — contract (2026-10-23)

Scope authority: the [active execution brief](../worker-prompts/media-care-records-payments-2026-10-23.md)
(W3 Storage/library lane), the [testing contract](media-care-records-testing-2026-10-23.md),
and the Owner-locked requirement 3/D6 (actual media creation uploads bytes; an
external playback URL is not a substitute). This document records the
implemented behavior, the exact changed paths, the migration name, the
storage-interface disposition and the deliberately unfixed findings. All
executable checks were **authored and NOT RUN**: W3 has no runtime slot
(Mastermind owns the only build/test/API/DB/storage slot).

## Locked rules implemented

1. **Upload, do not link.** Library video content is playable only from bytes
   uploaded through the API. The admin create/update form never accepted and
   still does not accept a video file; a dedicated attach route stores the MP4
   in private storage and persists only a storage reference.
2. **D6 — no external playback.** An existing externally linked `videoUrl`
   (including the reported `https://example.com/videos/quick-healthy-meal`
   row) is retained as legacy admin metadata and is never fetched or served by
   the API. The public detail advertises a playback address only when approved
   bytes exist **and** the caller is entitled; otherwise `videoUrl` is `null`.
   No arbitrary URL is ever fetched.
3. **One neutral playback outcome.** Every failure of the playback route —
   unknown id, not published, not a video, no uploaded bytes, unreadable
   object, missing premium entitlement — returns the same
   `Cms.LibraryContent.NotPublished` (404), so the route cannot be used to
   probe entitlement or stored-file state.
4. **Video invariants unchanged where they still bind.** Video content requires
   a positive `durationSeconds`, must not carry article sections, and
   `videoUrl` — when supplied — must still be a well-formed absolute http(s)
   URL of ≤500 characters. Video content no longer requires `videoUrl`.

## Endpoints

### Admin attach/remove — `api/v1/admin/library` (`CmsContent` policy)

| Method | Route | Success | Notes |
|---|---|---|---|
| POST | `/api/v1/admin/library/content/{id}/video` | `200` + `LibraryAdminContentResponse` | `multipart/form-data`, single `file` part. Allowed on `Draft` and `Published`. |
| DELETE | `/api/v1/admin/library/content/{id}/video` | `204` | Idempotent when nothing is attached. |

Why dedicated routes rather than extending create/update: `CreateContent`/
`UpdateContent` are positional multipart signatures already consumed by
existing callers and tests; a new file part would have changed them. The
attach route keeps the existing contract intact and makes replace/remove
explicit.

| Situation | Code | Status |
|---|---|---|
| Unknown content id | `Cms.LibraryContent.NotFound` | 404 |
| Target status `Archived` | `Cms.LibraryContent.InvalidOperation` | 409 |
| Target type `Article` (attach or remove) | `Cms.LibraryContent.Invalid` — `errors.videoStorageKey` | 400 |
| Missing/empty part | `Storage.File.Empty` | 400 |
| Part over the cap | `Storage.File.TooLarge` | 400 |
| Declared type not `video/mp4`, or bytes without the container marker | `Storage.File.UnsupportedType` | 400 |
| Safe but unpersistable reference (over 500 chars, blank) | `Cms.LibraryContent.Invalid` — `errors.videoStorageKey` | 400 |

Upload mechanics:

- Cap 50 MB = `52_428_800` bytes; `[RequestSizeLimit(52_428_800 + 102_400)]`
  leaves a small multipart-envelope allowance so a file exactly at the limit
  still binds.
- Declared `video/mp4` is required **and** the actual bytes must carry the ISO
  base-media `ftyp` marker at offset 4 (`bytes[4..8] == "ftyp"`), matching the
  established chat-video check. A declared type alone is never trusted.
- Stored with `IFileStorage.SavePrivateAsync(..., "library-video", 52_428_800)`
  ⇒ private root, outside `/files`; the storage key is never returned to app
  consumers, only to admins as `videoStorageKey`.
- Ordering: bytes are written first; the reference is persisted second. If the
  attach step fails, the freshly written object is deleted (no orphan
  reference). On replace, the superseded object is deleted only **after** the
  new reference is persisted — a delete failure can leave an unreferenced file
  but never content pointing at missing bytes.
- Remove: the reference is cleared and persisted first, then the object is
  deleted — playback stops immediately even if the file cleanup fails.
  `videoUrl` metadata is untouched by attach and remove.

### App playback — `api/v1/library` (authenticated)

| Method | Route | Success | Notes |
|---|---|---|---|
| GET | `/api/v1/library/content/{id}/video` | `200` `FileStreamResult` (`video/mp4`, `enableRangeProcessing: true`) | Publication, identity and the premium entitlement are re-checked by the query. |

- The handler resolves a `Published`, `Video`-typed row that `HasStoredVideo`;
  the premium gate reuses `LibraryPremiumAccess.HasPremiumContentAccessAsync`
  (identical rule to the detail read). Any failure → `Cms.LibraryContent.NotPublished`.
- The controller opens the stored object with `IFileStorage.OpenReadAsync`; a
  storage miss or read failure is reported as the same 404 rather than a 500 or
  a distinct code.
- Range support lets a player seek, exactly like the chat-video reads.
- Anonymous callers get the pre-existing `401` from the `[Authorize]` route.

### Public detail contract

`LibraryContentDetailResponse.VideoUrl` is produced by `LibraryVideoUrls.For(id)`
→ the relative path `/api/v1/library/content/{id}/video`, and only when
`canAccess && HasStoredVideo`. It is `null` for articles, for videos whose
bytes were never uploaded or were detached, and for unentitled premium readers.
The list item shape is unchanged (no `videoUrl` there — as before).

## Domain and persistence

- `LibraryContent` (`Domain/Library/LibraryContent.cs`):
  - `MaximumVideoStorageKeyLength = 500`, new nullable `VideoStorageKey`
    (column `video_storage_key`, `varchar(500)`), derived `HasStoredVideo`.
  - `VideoUrl` documented as legacy administrator metadata, retained and still
    normalized when supplied.
  - `ApplyTypeRules`: video requires positive duration, rejects sections,
    `NormalizeVideoUrl(videoUrl)` optional, `NormalizeVideoStorageKey` optional;
    article rejects a URL **and** a stored key and clears both.
  - `AttachVideo(storageKey)` / `RemoveVideo()`: check order is Archived →
    Type → key, so lifecycle wins over type, and each raises the stable domain
    messages the handlers map to `Cms.LibraryContent.InvalidOperation` /
    `Cms.LibraryContent.Invalid`.
  - `Update` preserves an existing video reference unless the effective type
    stops being `Video` (articles can never address one) — the reference
    survives ordinary metadata edits.
- `LibraryContentFieldDetail.FromDomainException` now maps the
  `videoStorageKey` domain messages to the `videoStorageKey` field path, so an
  article/typeless violation is a field-level `Cms.LibraryContent.Invalid`.
- `LibraryContentConfiguration`: `video_storage_key` mapping +
  `Ignore(HasStoredVideo)` (derived property, not a column).
- Migration pair (files only — **nothing applied**):
  - `20261023114500_AddLibraryContentVideoStorageKey` (`src/Modules/Cms/Infrastructure/Sanad.Modules.Cms.Infrastructure/Persistence/Migrations/`),
    additive: adds the nullable `video_storage_key` column; no other schema
    change; predecessor `20261022230000_AddComplaintReviewsAndSubscriptionRefundRequests`.
  - `.Designer.cs` + `CmsDbContextModelSnapshot.cs` updated to match. W3 is the
    sole CMS migration/snapshot author for this slice.
- `LibraryContentValidation`: video `videoUrl` is validated only when supplied
  (blank/omitted is now valid).

## Storage interface disposition (proposed before editing, as required)

**No `IFileStorage` amendment is needed.** The interface already exposes
`SavePrivateAsync(stream, contentType, length, folder, maximumBytes, ct)`
and `OpenReadAsync(key, ct)`, and `video/mp4` is already an accepted private
type. The new code uses those members unchanged; no interface churn was
introduced while other lanes consume the abstraction.

## Bounded media correctness in W3 scope

Upload signature/size hardening (buffered, signature verified against the
actual bytes before anything reaches disk; caps at 2 MiB):

| Controller | Change |
|---|---|
| `AdminSplashScreensController` | `SaveSplashImageAsync`: empty ⇒ `Storage.File.Empty`, over 2 MiB ⇒ `TooLarge`, declared type without a matching JPEG/PNG/WebP signature ⇒ `UnsupportedType`. |
| `AdminAssessmentsController` | `SaveTierImageAsync`: same three checks. |
| `AdminLookupsController` | `SaveIconAsync`: same three checks; folder hoisted to `services`. |

Byte-range support added (`enableRangeProcessing: true`) so PDFs/photos/reports
can be fetched in ranges:

| Controller | Route family |
|---|---|
| `CareHomeProfileMediaController` | facility profile media |
| `CareHomeRoomMediaController` | room media |
| `FamilyReportsController` | family visit/medical report files |
| `CaregiverBookingsController` | caregiver medical report file |

`AdminLandingController`: `ValidateLandingImagePath` is now applied by
`CreateSection` and `UpdateSection`. A blank `imagePath` stays valid ("no
image"); anything else must be a public `landing/…` asset of this storage root
— rejects `private/…`, traversal (`..`), foreign/external and any path outside
the storage root with `Cms.Landing.Media.InvalidKey` (400), and a
well-formed-but-missing object with `Cms.Landing.Media.NotFound` (404). The
persisted key is normalized (lowercase, canonical separators). This is the same
public-prefix rule the landing media delete route already enforced.

Note for the Mastermind: `Cms.Landing.Media.InvalidKey` and
`Cms.Landing.Media.NotFound` are **not** in the shared
`ResultProblemDetailsMapper` table. The new controller checks emit their status
directly, so behavior is correct; any handler-emitted `MediaKeyInvalid`
(existing landing commands) still falls to the mapper default (400), which is
the intended status. No mapper amendment is required for this slice.

## Tests authored — NOT RUN

| File | Covers |
|---|---|
| `tests/Sanad.UnitTests/Cms/LibraryVideoStorageTests.cs` | Model pins the private `video_storage_key` column and leaves the derived flag unmapped; video creatable without an external URL; supplied URL still validated / blank URL stored as absent; domain rejects a stored reference on articles; `AttachVideo` rejects a reference outside the private area, an overlong/blank one; attach persists for draft and published; attach/remove refuse archived and non-video; `RemoveVideo` clears bytes and keeps legacy metadata; metadata update keeps uploaded bytes; detail exposes the playback route only with bytes **and** entitlement; video query returns the stored key only for a published entitled video; handler lifecycle/type failure mapping. |
| `tests/Sanad.UnitTests/API/AdminLibraryVideoControllerTests.cs` | Upload stores private bytes and returns the new reference; replace deletes the superseded object only after the new reference is stored; non-MP4 declared types, MP4 declaration without `ftyp`, missing/empty/oversize refused; unknown id 404 without uploading; archived 409 and deletes the fresh object; article 400 and deletes only the fresh object; failed attach keeps the existing object; delete detaches then deletes; delete without bytes is a no-op; delete maps unknown/article/archived. |
| `tests/Sanad.UnitTests/API/LibraryVideoReadControllerTests.cs` | Published video with bytes streams the stored object with range support; detail and playback routes agree and never expose the storage key; unverified subject ⇒ unauthorized; detail hides the route without entitlement or bytes; premium bytes require entitlement; unknown/draft/archived return the same 404; unreadable or missing bytes fail like unpublished. |
| `tests/Sanad.UnitTests/API/MediaUploadHardeningTests.cs` | Splash / assessment-tier / service-icon uploads: declared type without a matching signature refused, empty and oversize refused, real PNG/JPEG accepted (and cleanup on handler failure); landing section create refuses a non-public reference and a missing key while accepting an existing one; landing update applies the same rules; family report photo, caregiver medical report photo, care-home room media and profile media each serve with range support. |
| `tests/Sanad.UnitTests/Cms/LibraryTests.cs` (amended, Owner-approved) | The two superseded assertions that encoded the pre-D6 rule — a premium video's `videoUrl` being surfaced to the entitled caller — now assert `null`, matching the new contract. Every amendment is listed inside that file's own history; no other assertion changed. |

No other existing test encoded the pre-hardening splash/tier/icon upload
behavior (declared-MIME trust) and no test asserted `EnableRangeProcessing`
anywhere, so the hardening supersedes no further assertions.

## Bounded review correction in progress; follow-up findings

The W3 handback reported declared-MIME trust on three remaining upload paths.
Mastermind has sent a bounded correction to the same W3 session to verify bytes
against the already-supported image types and add no-storage-write regressions:

- `AuthController` identity document upload.
- `CaregiverController` certificate/identity upload.
- `FamilyController.SavePrivatePhotoAsync`.
- `CareHomeProfileMediaController.Upload` passes the raw `file.ContentType`
  through; the handler already validates JPEG/PNG by signature with a 5 MiB cap,
  so this remains a metadata consistency note, not an unvalidated-byte path.

Recommended (not implemented): a shared upload guard plus, for video-capable
reads that still lack it, the same `enableRangeProcessing` treatment; and a
publish-time media guard for library video (currently publishing a video with
no bytes is allowed and simply advertises no playback URL).

## Postman amendments for the Mastermind (shared collections are Mastermind-owned)

Three routes have been added to the shared Postman collections so the contract
mapping stays complete:

1. `docs/postman/admins/Sanad.Admin.postman_collection.json` — Library CMS
   folder: `POST {{baseUrl}}/api/v1/admin/library/content/{{libraryContentId}}/video`
   (`form-data`, `file` part, an MP4), and
   `DELETE {{baseUrl}}/api/v1/admin/library/content/{{libraryContentId}}/video`.
2. `docs/postman/app/Sanad.App.Library.postman_collection.json` —
   `GET {{baseUrl}}/api/v1/library/content/{{libraryContentId}}/video` with the
   bearer token, expecting the MP4 (or the pinned 404).

Admin upload/remove and App playback requests now document the approved storage
and entitlement rules. Route/Postman parity is pending the coordinated final
gate. No new error code was introduced, so no shared mapping change is needed.

## Documentation

`docs/app/library/README.md` was updated within the pinned W3 manifest
(upload/playback routes, optional `videoUrl` as legacy metadata, playback
consumer contract, `videoStorageKey`, playback 404 semantics, error-code table).
`docs/admin/library.md` does not exist in this repository, so there is no admin
guide to update.

## NOT RUN limits

- No build, test execution, HTTP host, migration application, PostgreSQL,
  storage root or provider operation was performed by this lane; every test
  above is authored only.
- The controller tests use recording/dispatching doubles; they prove route
  binding, status mapping and storage-call ordering, not real disk I/O,
  PostgreSQL or player behavior.
- Real-host playback (range negotiation, streaming of a genuine MP4), the real
  private storage root and migration application are runtime-slot checks.
- No real replacement video content was supplied for the reported R1 row; the
  legacy `example.com` link therefore still does not play, by design.
