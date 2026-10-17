# Community API

All Community API routes require an authenticated account. Public in this
feature means available to any authenticated account; anonymous requests are
rejected with `401`. The API overwrites
author/user IDs in write commands with the authenticated identity; clients must
not choose another author.

## Posts and moderation

`GET /api/v1/community/posts` and
`GET /api/v1/community/posts/{postId}` return published posts only. `POST
/api/v1/community/posts` creates a post in `PendingReview`; the author cannot
publish it directly. Content moderators with the `ContentAdmin` or
`SuperAdmin` role use:

- `POST /api/v1/admin/community/posts/{postId}/publish`
- `POST /api/v1/admin/community/posts/{postId}/reject`

Both moderation routes return `204` on success and require the
`CommunityModeration` policy. Rejected/pending posts are not visible through
the public read routes. Unknown posts return the standard Community not-found
problem response.

Rejection requires a closed-set `reasonCode` and accepts an optional
plain-text `note` (maximum 1000 characters). Codes are `CommunityGuidelines`,
`Spam`, `Duplicate`, `UnsafeContent`, and `Other`; `Other` requires a note.
Unknown codes, a missing required note, or an overlong note return 400. The
decision records the moderator and UTC time and appears in admin moderation
history.

An authenticated Community user can report a **published** post they did not
author with `POST /api/v1/community/posts/{postId}/reports`:

```json
{ "reasonCode": "Spam", "note": "Optional context" }
```

Report reason codes are `Spam`, `HarassmentOrAbuse`,
`HarmfulOrUnsafeContent`, `MisleadingOrFalseInformation`, and `Other`.
`Other` requires a note; all notes are trimmed plain text up to 1000
characters. Unknown posts return 404; own-post or non-published targets
return 409; invalid codes, missing required notes, or overlong notes return
400. One report is retained per reporter/post pair; repeating the request
returns the existing report. Reporting does not hide the post from the feed.
There is no report deletion or post hide/restore action in this phase.

An active Community restriction prevents its author from creating posts,
comments, and replies. The restriction expires after the selected 24-hour,
7-day, or 30-day duration, or earlier if revoked by a moderator. It does not
change account login or access to other modules, existing content, or other
Community interactions. A restricted write returns the Community error
`Community.AuthorRestricted`.

Post creation body (all fields except `imageUrl`, `isAnonymous`, and
`categoryId` are required):

```json
{
  "titleArabic": "نصيحة يومية",
  "titleEnglish": "Daily care tip",
  "contentArabic": "تذكّر شرب الماء.",
  "contentEnglish": "Remember to drink water.",
  "imageUrl": null,
  "isAnonymous": false,
  "categoryId": null
}
```

`categoryId` is optional. When supplied it must reference an existing
**active** category: unknown ids return `400` `Community.InvalidCategory`;
inactive categories return `409` `Community.CategoryInactive`.

## Feed, filters, sort, and per-user state

`GET /api/v1/community/posts` supports (all optional, combinable):

- `search=` — case-insensitive match against Arabic/English titles and
  contents.
- `categoryId=` — only posts in that category.
- `fromDate=` / `toDate=` — inclusive bounds on the UTC creation timestamp.
- `sort=Newest|Oldest|MostLiked` (case-insensitive; default `Newest`;
  `MostLiked` breaks ties by recency then id). Any other value returns `400`
  with code `Community.InvalidSort`. `fromDate` after `toDate` returns `400`
  with code `Community.InvalidDateRange`.
- `page=` (minimum 1) and `pageSize=` (1–100). The bare-list envelope is
  unchanged: no wrapper object, no `totalCount`.

List, detail, and recommendations items carry these **additive** per-user
fields computed for the authenticated caller: `categoryId`,
`categoryArabicName`, `categoryEnglishName`, `isLiked`, `isFavorited`, and
`currentUserRating` (integer 1–5, `null` when the caller never rated the
post). On the recommendations feed `isLiked`/`isFavorited` are always
`false` and `currentUserRating` is always `null` because already-interacted
posts are excluded from recommendations by definition. The favorite toggle
endpoint is unchanged.

`GET /api/v1/community/favorites?page=&pageSize=` returns the caller's
favorited published posts (same post shape and per-user fields as the feed),
newest-favorited-first. Untoggling a favorite removes the post from the list
on the next call.

## Categories

Community categories are a bilingual, admin-managed lookup used to classify
posts. The public list is available to any authenticated account:

`GET /api/v1/community/categories` → `200` with a bare list of active
categories only, ordered by creation time then id:

```json
[
  { "id": "0198e2c2-4444-7777-8888-000000000001", "arabicName": "نصائح صحية", "englishName": "Health tips" }
]
```

Administrators with the `CommunityModeration` policy (`SuperAdmin` or
`ContentAdmin`) manage the full lookup under
`/api/v1/admin/community/categories`:

- `GET` — list all categories (including inactive) with `isActive`,
  `createdOnUtc`, and `updatedOnUtc`.
- `POST` — create `{ "arabicName": "…", "englishName": "…", "isActive": true }`
  → `201` with the created item. Both names are required (1–200 chars,
  trimmed); invalid names return `400` `Community.Category.Invalid`.
- `PUT /{categoryId}` — update names and `isActive` → `200`. The request
  body carries only `{ "arabicName": "…", "englishName": "…", "isActive": true }`;
  the route id is authoritative and any legacy body `Id` is ignored. Unknown
  ids return `404` `Community.CategoryNotFound`.
- `DELETE /{categoryId}` — hard delete → `204`. Unknown ids return `404`
  `Community.CategoryNotFound`; a category referenced by posts returns `409`
  `Community.CategoryInUse` (posts never lose their category implicitly
  through delete).

Deactivating a category hides it from the public list immediately; existing
posts keep their stored category.

## Check-ins and ratings

Published posts may receive one check-in per user/post/local date:

`POST /api/v1/community/posts/{postId}/check-in`

```json
{
  "answer": true,
  "timeZoneId": "Africa/Cairo",
  "localDate": "2026-10-05",
  "answeredAtLocalTime": "14:30:00"
}
```

The server validates the IANA time zone and stores the corresponding UTC
timestamp. A repeated local-date submission updates that user’s row. Invalid
time zones and dates return `Community.CheckIn.Invalid`.

`POST /api/v1/community/posts/{postId}/rating` accepts `{ "value": 1 }` through
`{ "value": 5 }`; `value` is an integer and is range-validated server-side
(out-of-range or non-integer values return `400` `Community.Rating.Invalid`;
there is no legacy `ratingValue` support). The caller's rating is created or
updated. `GET
/api/v1/community/posts/{postId}/rating/average` returns the current average
and count.

Comment updates and deletes are restricted to the comment author. A comment
whose author does not match the caller is returned as not found. Post likes,
comment likes, and favorites are stored as per-user interactions, so repeating a
like is idempotent, unlike removes only the caller’s like, and favorite toggles
only the caller’s saved state. A database uniqueness constraint protects each
target/user/interaction combination.

```text
POST   /api/v1/community/posts/{postId}/like
DELETE /api/v1/community/posts/{postId}/like
POST   /api/v1/community/posts/{postId}/favorite
POST   /api/v1/community/posts/{postId}/comments
GET    /api/v1/community/posts/{postId}/comments
PUT    /api/v1/community/comments/{commentId}
DELETE /api/v1/community/comments/{commentId}
POST   /api/v1/community/comments/{commentId}/like
POST   /api/v1/community/comments/{commentId}/replies
GET    /api/v1/community/comments/{commentId}/replies
GET    /api/v1/community/help-request-catalog
```

Post ratings are unique per post/user; check-ins are unique per post/user/local
date. Interactions are persisted in the `community` schema. The SQL mappings use
PostgreSQL-compatible `timestamp with time zone`, `date`, `time`, `text`, and
`CURRENT_TIMESTAMP` types/defaults.

## Recommendations

`GET /api/v1/community/recommendations?page=1&pageSize=10` returns published
posts only, using the same post shape as the posts feed. Ranking is
personalized from the caller's own existing signals — posts they liked,
favorited, commented on, rated, or checked in to determine affinity authors;
no new tracking, profiles, or ML are involved:

1. Published posts by affinity authors that the caller has not interacted
   with, newest first (the caller's own posts never count as affinity).
2. All other published posts the caller has not interacted with, newest
   first; engagement counts break recency ties and post id breaks the rest,
   so pages are deterministic.

Posts the caller already interacted with are excluded. With no interactions,
the affinity set is empty and the result equals the newest-first published
feed. Like the posts feed, the response is a bare list with `page` (minimum
1) and `pageSize` (1–100); no `totalCount` is returned. Anonymous posts are
included with their anonymity preserved. Ranking is computed in a single
database query over the existing interaction tables (covered by SQLite-backed
focused tests); the caller's history is never materialized into memory.

Community interactions are not financial or medically authoritative records.
Run state-changing checks only against disposable fixtures.

## Image uploads

`POST /api/v1/community/uploads/images` accepts one image as `multipart/form-data`
with a `file` field. Any authenticated account may upload. The response carries
the stable image id and the URL to attach when creating a post:

```json
{
  "imageId": "0198e2c2-4444-7777-8888-000000000001",
  "imageUrl": "/api/v1/community/uploads/images/0198e2c2-4444-7777-8888-000000000001/file",
  "contentType": "image/jpeg",
  "sizeBytes": 184320
}
```

Validation: `image/jpeg`, `image/png`, or `image/webp` only; the declared
content type must match the file's magic bytes (the submitted filename and
content type alone are not trusted); size is bounded by the public storage
limit (`Storage:Local:MaxBytes`, default 2 MB, mirrored by the request cap).
Empty, oversized, unsupported, or signature-mismatched files return `400`
(`Storage.File.*` or `Community.Image.Invalid`); unauthenticated callers
receive `401`.

Storage behavior: files are saved with a generated safe key under private
storage and are never served through the static `/files` host, so an uploaded
image cannot be opened publicly before its post is approved — unlike post
bodies, image bytes stay gated until publication. Each upload is recorded
(`community.CommunityImages`) with uploader and time. Uploading never creates
or publishes a post, and the existing moderation still applies in full when a
post is created: the post starts `PendingReview` and its image becomes
visible to readers only after a moderator publishes it.

`GET /api/v1/community/uploads/images/{imageId}/file` serves the bytes with
`Cache-Control: no-store`. It allows the uploader's own images, any image for
content moderators (`CommunityModeration`: SuperAdmin or ContentAdmin, for
pending-post preview), and images attached to a published post for any
authenticated caller. Anything else — including unapproved or rejected-post
images for strangers — returns `404` without revealing whether the image
exists; unauthenticated callers receive `401`, and unknown ids return `404`.

Orphan retention: uploaded images that are never attached to a post are
retained; no cleanup subsystem exists. The upload table makes orphans
queryable (stored keys never referenced by a post) for a future admin
cleanup, if ever needed.
