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

## Viewer capabilities (`isOwn`, `canEdit`, `canDelete`)

Every surface that returns a post — the feed, post detail, recommendations,
favorites and the create/update responses — carries three additive booleans
describing the **authenticated caller's own** app rights over that post:

| Field | `true` when |
| --- | --- |
| `isOwn` | the authenticated caller is the stored author |
| `canEdit` | `isOwn` **and** the post is `PendingReview` or `Published` |
| `canDelete` | `isOwn` **and** the post is not `Deleted` |

- A caller who is not the author gets `false` for all three. This includes a
  moderator reading through the app surface: the flags describe app
  ownership only and **never encode moderation powers**, which stay on the
  separate `/api/v1/admin/community` routes.
- Anonymity changes nothing. An anonymous post's author still sees their own
  `true` flags while `displayName` and `avatarUrl` stay `null` and no author
  id is published.
- The flags are a **read snapshot**. They authorize nothing: `PUT
  /api/v1/community/posts/{postId}` and `DELETE
  /api/v1/community/posts/{postId}` keep enforcing ownership, status and
  concurrent-state checks server-side, and a stale or foreign request still
  answers the same `404`/`409` it did before.
- The read surfaces return `Published` posts only. Editing a published post
  returns it to `PendingReview` for re-moderation, so the edited post
  **disappears from the feed, detail, recommendations and favorites** until a
  moderator re-approves it. Deletion removes it from every read the same way.
  The flags never widen that visibility and expose no unpublished post.

## Author fields and anonymity

Post, comment and reply responses carry two additive author fields:

| Field | Type | Meaning |
| --- | --- | --- |
| `displayName` | string or `null` | The author's English full name, with the Arabic full name as fallback. `null` when the identity row is missing or when both names are blank — never a placeholder such as "Anonymous". |
| `avatarUrl` | string or `null` | The author's avatar **download path** — `/api/v1/community/authors/{authorId}/photo` — when the identity row carries a stored avatar, `null` otherwise. It is a path the API resolves, never the stored Identity private storage key. An anonymous author resolves to `null` (the route would answer `404` for them). Fetch the bytes with `GET /api/v1/community/authors/{authorId}/photo` (see [Authors and anonymity](README.md#authors-and-anonymity)). |

The feed, post detail, recommendations, favorites, comments and replies each
resolve the display names with **one batched identity lookup per page**; a
per-row lookup is never performed, and the ids of anonymous authors are never
looked up at all.

### Anonymity

- `isAnonymous` on a post is the stored flag and is unchanged.
- An **anonymous post carries no author identity at all**: `displayName` and
  `avatarUrl` are `null`, and the post response has no author id field.
- Comments and replies now carry `isAnonymous` too. Community stores
  anonymity on the post only, so a comment or a reply is anonymous exactly
  when it is written by the author of an anonymous post **inside that post's
  own thread** — the one place where the post author would otherwise be
  identified through their own words. Such a response carries
  `authorId: null`, `displayName: null`, `avatarUrl: null` and
  `isAnonymous: true`. Every other comment or reply is not anonymous and
  carries its author id and display name.
- `authorId` on comments and replies is now **nullable**; it is `null` exactly
  when `isAnonymous` is `true`. It remains the ownership handle, and the
  edit/delete endpoints keep enforcing ownership server-side.

Render anonymity as **null identity** — no name and no avatar. The API never
substitutes a placeholder name for an anonymous author.

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

Comment and reply responses carry the author display fields and the anonymity
rules described in
[Author fields and anonymity](#author-fields-and-anonymity): `displayName`,
`avatarUrl`, `isAnonymous`, and a nullable `authorId`.

## Blocking an author (one-way, hide-posts-only)

The caller can hide an author's **posts** from their own Community reads. The
relation is Community-owned: no chat block is read or written, and it never
denies a comment, a reply, a like, a rating, a check-in or any other
interaction.

```text
POST   /api/v1/community/blocks
DELETE /api/v1/community/blocks/{authorId}
GET    /api/v1/community/blocks?page=&pageSize=
```

All three routes require an authenticated account (a normal JWT); the viewer is
the authenticated identity and the target is named by **author id only**.

| Route | Body / query | Success | Failures |
|---|---|---|---|
| `POST /api/v1/community/blocks` | `{ "authorId": "…" }` | `204` | `400 Community.Block.Invalid` (self-block, or an empty id); `404` with code `Community.Block.NotFound` (unknown target author — the generic 404 body; a known target returns `204` and an unknown one `404`, so this route **does** distinguish a registered account id from an unregistered one, exactly like the accepted Chat block design) |
| `DELETE /api/v1/community/blocks/{authorId}` | — | `204` (always, even when nothing was blocked) | — |
| `GET /api/v1/community/blocks?page=&pageSize=` | `page` (≥ 1), `pageSize` (1–100) | `200` with a bare list of `{ authorId, displayName, avatarUrl, blockedOnUtc }`, most-recent block first | — |

- **Block is idempotent.** Blocking an already-blocked author succeeds again
  (`204`) and never creates a duplicate row; the unique index on
  `(blocker, blocked author)` is the database backstop.
- **Unblock is idempotent.** Removing a relation that does not exist still
  succeeds (`204`); unblock is the caller's own action and never reveals
  whether a block existed. Removing the relation restores the author's posts.
- **The list is per caller.** It never lists an author blocked by anyone else.
  `displayName` is the author's English full name with the Arabic full name as
  fallback and is `null` when the identity row is missing; `avatarUrl` is
  the author's avatar download path (`/api/v1/community/authors/{authorId}/photo`)
  when the identity row carries a stored avatar, `null` otherwise — never the
  stored private key. The names are
  resolved with **one batched identity lookup per page**, never one per row.
- **Only post reads are filtered.** The blocked author's posts are excluded from
  the posts feed, post detail, recommendations and favorites. The predicate is
  composed inside the query, before the ordering and the paging, so a page
  never shrinks silently. A blocked author's post detail answers the same
  not-found shape as an unknown or unpublished post (`404`).
- **Comments and interactions are not denied.** Comments, replies, likes,
  ratings and check-ins keep their current behaviour for a blocked author, and
  the caller can still comment on that author's posts.

**Anonymity interaction (honest statement).** The filter is silent and keyed on
the **stored author id**. A caller who blocked an author therefore also stops
seeing that author's **anonymous** posts, because the filter cannot know that
the post was written anonymously and deliberately does not look. No response
carries the blocked author's id or name and the anonymous-post contract
(`displayName`, `avatarUrl` and `authorId` all `null`) is unchanged — but the
filtering is **observable across a block/unblock transition**: a caller who has
seen an anonymous post can, in principle, block candidate authors and watch
the post disappear to infer who wrote it. That inference channel is under the
caller's own control, so the app must not use "block and observe" to unmask an
anonymous author.

**Accepted tech debt — the favourites read.** The favourites read resolves the
blocked authors' published post ids with one constant extra query and compares
them as an id array. The array grows with the number of published posts the
blocked authors have, so a viewer who blocks a prolific author makes that
query carry more ids. This is recorded as accepted, documented tech debt: the
alternative shapes either shrink a page silently or need a raw-SQL read.

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
