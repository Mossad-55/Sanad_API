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

Post creation body:

```json
{
  "titleArabic": "نصيحة يومية",
  "titleEnglish": "Daily care tip",
  "contentArabic": "تذكّر شرب الماء.",
  "contentEnglish": "Remember to drink water.",
  "imageUrl": null,
  "isAnonymous": false
}
```

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
`{ "value": 5 }`; the caller’s rating is created or updated. `GET
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
