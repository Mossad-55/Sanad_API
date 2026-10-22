# Community

Community routes are authenticated and live under `/api/v1/community`. Read
routes expose published posts; new posts enter moderation as `PendingReview`.
Write identities come from the JWT and cannot be supplied by the client.

- [Posts, interactions, ratings, check-ins, recommendations, uploads and moderation](writes.md)
- [Postman Community collection](../../postman/app/Sanad.App.Community.postman_collection.json)
- [Postman moderation collection](../../postman/admins/Sanad.Admin.Community.postman_collection.json)

Comments and replies enforce author ownership for edits/deletes. Run stateful
requests only with a disposable fixture and a content moderator token where
noted.

## Authors and anonymity

Every post, comment and reply carries `displayName` (the author's English full
name, Arabic as fallback, `null` when the identity row is missing) and
`avatarUrl` (always `null` today — Community has no publicly servable avatar).

`isAnonymous` is unchanged on posts and is now also on comments and replies.
An anonymous post carries no author identity at all, and a comment or reply
written by the author of an anonymous post inside that post's own thread is
anonymous too: `authorId`, `displayName` and `avatarUrl` are all `null`. The
API never substitutes a placeholder name. The full contract is in
[Posts, interactions, ratings, check-ins, recommendations, uploads and moderation](writes.md).

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
