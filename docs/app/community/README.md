# Community

Community routes are authenticated and live under `/api/v1/community`. Read
routes expose published posts; new posts enter moderation as `PendingReview`.
Write identities come from the JWT and cannot be supplied by the client.

- [Posts, interactions, ratings, check-ins, recommendations, uploads and moderation](writes.md)
- [Postman Community collection](../../postman/app/Ahlena.App.Community.postman_collection.json)
- [Postman moderation collection](../../postman/admins/Ahlena.Admin.Community.postman_collection.json)

Comments and replies enforce author ownership for edits/deletes. Run stateful
requests only with a disposable fixture and a content moderator token where
noted.

## Authors and anonymity

Every post, comment and reply carries `displayName` (the author's English full
name, Arabic as fallback, `null` when the identity row is missing), `avatarUrl`
and `authorId`. `avatarUrl` is the author's avatar **download path**
(`/api/v1/community/authors/{authorId}/photo`) when the author has a stored
avatar, and `null` otherwise; it never carries the stored private storage key.

```text
GET /api/v1/community/authors/{authorId}/photo   → 200 image bytes | 404
```

The route (the S3 approved photo route, refund decision lock #32) requires a
Normal JWT and streams the image bytes only. An unknown author, an author with no
stored avatar and an **anonymous** author all answer the same generic `404`
(`Community.Author.Photo.NotFound`). Anonymity is resolved server-side from the
stored `is_anonymous` flag of the author's posts — the request never carries (and
cannot weaken) an anonymity hint.

`isAnonymous` is unchanged on posts and is now also on comments and replies.
An anonymous post carries no author identity at all — `authorId`,
`displayName` and `avatarUrl` are all `null`, for every caller, including the
post's own author and administrators — and a comment or reply written by the
author of an anonymous post inside that post's own thread is anonymous too:
`authorId`, `displayName` and `avatarUrl` are all `null`. The API never
substitutes a placeholder name, and `authorId` here is the stored id, not a
name. The full contract is in
[Posts, interactions, ratings, check-ins, recommendations, uploads and moderation](writes.md).

## Viewer capabilities

Every post response — the feed, post detail, recommendations, favorites and the
create/update responses — carries `isOwn`, `canEdit` and `canDelete` for the
authenticated caller: `isOwn` when the caller is the stored author, `canEdit`
for the caller's own `PendingReview`/`Published` post, and `canDelete` for the
caller's own non-`Deleted` post. Another viewer gets `false` for all three, and
anonymity changes no flag. The flags grant nothing — the mutation routes stay
authoritative — and they never encode Admin moderation powers. See
[Viewer capabilities](writes.md#viewer-capabilities-isown-canedit-candelete).

Every post also carries `viewsCount`, the stored lifetime view counter. Record a
view with

```text
POST /api/v1/community/posts/{postId}/view   → 200 { "postId": "…", "viewsCount": 3 }
```

The route uses the post-detail read rule (`401` unauthenticated; `404` for an
unknown, non-published or blocked-author post). The author never increments the
counter, and every other viewer counts at most once per post for its lifetime; a
repeat call returns the current count. The counter is monotonic and never
negative. See [Post views](writes.md#post-views).

Comments and replies carry `isOwn`, `canEdit`, `canDelete` and `isLiked` for the
caller, mirroring the real handlers: a comment can be edited by its author and
deleted only while it has no replies, and replies always report
`canEdit`/`canDelete` as `false` because the API exposes no reply mutation route.
See [Comment and reply viewer state](writes.md#comment-and-reply-viewer-state).

## Blocking an author (one-way, hide-posts-only)

A caller can hide an author's **posts** from their own feeds. The relation is
Community-owned; no chat block is read or written.

```text
POST   /api/v1/community/blocks            { "authorId": "…" }   → 204
DELETE /api/v1/community/blocks/{authorId}                      → 204
GET    /api/v1/community/blocks?page=&pageSize=                  → 200
```

Blocking is idempotent (a repeat succeeds without a duplicate row), a self-block
is `400 Community.Block.Invalid`, and an unknown target author is the generic
privacy `404`. Unblocking an author that was not blocked also succeeds. The
`404` body is generic, but a known target returns `204` and an unknown one
`404`, so the route does distinguish a registered account id from an
unregistered one — the same accepted Chat block design.

The filter applies to **post reads only** — the feed, post detail,
recommendations and favorites. It is composed inside the query, before the
paging, so a page never shrinks silently. Comments, replies, likes, ratings,
check-ins and every other interaction are unchanged: a blocked author can still
be commented on, and the caller can still comment.

**Anonymity interaction.** The filter is silent and keyed on the stored author
id, so a caller who blocked an author also stops seeing that author's
**anonymous** posts. No response carries the blocked author's id or name and the
anonymous-post contract (`displayName`/`avatarUrl`/`authorId` all `null`) is
unchanged — but the filtering is **observable across a block/unblock
transition**: a caller who has seen an anonymous post can, in principle, block
candidate authors and watch it disappear to infer who wrote it. Do not use
"block and observe" to unmask an anonymous author. The full
contract is in
[Posts, interactions, ratings, check-ins, recommendations, uploads and moderation](writes.md).
