# Community post management — API behavior guide (2026-10-23)

Scope authority: the [active execution brief](../worker-prompts/media-care-records-payments-2026-10-23.md)
(Owner-locked "community post edit/delete" addition) and the
[testing contract](media-care-records-testing-2026-10-23.md). This guide
documents the implemented behavior of the Community post-management feature
and the author-photo privacy alignment across posts, comments and replies.
W2 Community lane; all executable checks were authored and are NOT RUN here
(one coordinated runtime slot, Mastermind-owned).

## Locked product rules

1. An authenticated post author can edit and delete THEIR OWN post only.
   Another user's post cannot be modified or deleted through the app
   endpoints, including by forging an author id: the server derives the
   acting author from the authentication claim, the ownership check compares
   it against the stored immutable `Post.AuthorId`, and no request field can
   reach either.
2. Administrators holding the existing `CommunityModeration` policy can
   delete any post through the Admin surface. That permission does not
   include editing, re-attribution or impersonation: the Admin controller has
   NO edit route at all (asserted by tests).
3. Deletion hides the post from public reads and blocks new child content,
   while retaining the row, its comments/replies and the referenced media.
   There is no cascading hard deletion of users, media files, or unrelated
   posts.
4. Editing never bypasses moderation: a published post that is edited returns
   to `PendingReview` and its publication metadata (`PublishedBy`,
   `PublishedOnUtc`) is cleared, so a moderator must re-approve the new
   content.
5. The conservative community anonymity rule (D7) is preserved and extended
   consistently: every author-photo projection (post, comment, reply) returns
   `null` exactly where the authorized author-photo download route would
   deny. There is no caller-avatar fallback.

## Policy membership (actual, from `DependencyInjection.cs`)

`CommunityModeration` = authenticated `Normal` access token with account type
`SuperAdmin` or `ContentAdmin`. `SupportAdmin` is deliberately NOT included.
This controller-level policy is unchanged.

## Endpoints

### App surface — `api/v1/community` (authenticated)

| Method | Route | Success | Authored-by rules |
|---|---|---|---|
| PUT | `/api/v1/community/posts/{postId}` | `200` + `CommunityPostResponse` | Author of the post only |
| DELETE | `/api/v1/community/posts/{postId}` | `204` | Author of the post only |

- The acting author is always the JWT claim value. The request body has no
  author field; a forged author/user id cannot widen the scope.
- PUT replaces the display fields: `titleArabic`, `titleEnglish`,
  `contentArabic`, `contentEnglish` (both languages required, same length
  limits as creation: titles ≤ 200, contents required), `imageUrl`
  (optional), `isAnonymous` (author's choice), `categoryId` (optional, must
  be an active category). The route id owns `postId`; the claim owns the
  actor. Author identity is immutable — a post's author can never be changed.
- PUT of a `Published` post returns it to `PendingReview` (re-moderation);
  PUT of a `PendingReview` post stays `PendingReview`. A `Rejected` or
  `Archived` post is not editable. A `Deleted` post answers 404 to its own
  author too.
- DELETE marks the post `Deleted` and records `DeletedBy` (the author id) and
  `DeletedOnUtc` for audit. A repeat delete is 404. Comments, replies and the
  referenced image asset are NOT removed.

### Admin surface — `api/v1/admin/community` (CommunityModeration policy)

| Method | Route | Success | Rules |
|---|---|---|---|
| DELETE | `/api/v1/admin/community/posts/{postId}` | `204` | Deletes ANY post; `DeletedBy` records the moderator |

- Unknown or already-deleted post → 404 (first deletion's audit is never
  overwritten).
- No Admin edit/impersonation route exists or may be added under this
  permission.

## Status codes (pinned)

| Situation | Code | Status |
|---|---|---|
| Unknown post id, another author's post, already-deleted post (app and admin routes) | `Community.PostNotFound` | 404 |
| Reaction mutation (like/unlike/favorite/comment-like) against a deleted or otherwise non-published parent | `Community.PostNotFound` (like/favorite) or false → `NotFound()`; check-in `Community.PostNotFound` | 404 |
| Rating against a deleted or otherwise non-published parent | false → `NotFound()` (pre-existing guard) | 404 |
| Editing a `Rejected`/`Archived` post | `Community.InvalidModerationOperation` | 409 |
| Stale concurrent write (edit or delete raced another lifecycle transition) | `Community.InvalidModerationOperation` | 409 — client reloads and retries |
| Invalid image reference (external URL, unknown upload, foreign private asset) | `Community.Image.Invalid` | 400 |
| Invalid edit fields (missing/oversized titles or contents) | `Community.InvalidPostFields` | 400 |
| Unknown category / inactive category on edit | `Community.InvalidCategory` / `Community.CategoryInactive` | 400 / 409 (existing shared mappings) |

All codes reuse the shared `ResultProblemDetailsMapper` table — no mapper
amendment was needed for this feature.

The foreign/missing/deleted choice (404 rather than 403) follows the
Community module's own established privacy convention: a post that is not
yours is indistinguishable from one that does not exist, matching the
blocked-author detail behavior (`GetPostQuery`) and comment deletion
(`Forbidden → 404` in `CommentsController`).

## Concurrency and races

`Post.Version` is a long concurrency token (the module's established
`CommunityPostReport.Version` convention). Every lifecycle transition
(publish, reject, author edit, author delete, moderator delete) increments it.
A stale write — notably an author edit racing an Admin delete — answers 409
and never resurrects the deleted post or loses the deletion. The stored
deletion audit of the first deleter stays intact.

Both app and Admin routes accept the client retry pattern: reload the post
(gone → 404) or the feed after a 409.

## Deleted-post visibility and children

- Feed, search, date filters (`GET /posts`), detail (`GET /posts/{id}`),
  favorites (`GET /favorites`) and recommendations all read published posts
  only; `Deleted` is excluded everywhere, including raw-SQL recommendations.
- Comment and reply reads (`GET /posts/{id}/comments`,
  `GET /comments/{id}/replies`) and their counts exclude children and
  descendants of a deleted post — the thread disappears with the post.
- New comments and replies against a deleted parent (or against a comment
  whose parent post is deleted) fail with `Community.PostNotFound` (404).
- Reactions stop with the post lifecycle (bounded Mastermind correction,
  same day: the earlier limitation below is resolved). Every mutation that
  starts or continues a reaction verifies the parent post is still
  `Published` before persisting:
  - `LikePostCommandHandler` and `ToggleFavoritePostCommandHandler` reject a
    non-published (incl. `Deleted`) parent with `Community.PostNotFound`
    (404), persisting nothing.
  - `UnlikePostCommandHandler` rejects the same way and keeps the retained
    like row: the deletion contract has NO interaction-cleanup semantics, so
    the undo of a pre-deletion like is simply stopped — the retained
    interaction keeps its deletion-time state (no decrement, no removal).
  - `LikeCommunityCommentCommandHandler` resolves the comment's parent post
    and rejects the toggle while that parent is not `Published` (incl.
    deleted); the retained comment like row likewise stays.
  - `AddRatingCommandHandler` and `AddCheckInCommandHandler` already guarded
    published-only parents before this correction; the deleted parent hits
    the same existing rejection (rating: `Community.PostNotFound`-equivalent
    false → wire 404; check-in: `Community.PostNotFound` 404). No code
    change was needed there; regression tests pin the behavior.
  - Handler-level and endpoint-level regressions for all of the above live
    in the two W2 test files (NOT RUN — no runtime slot).

## Image references (uploads only)

- A post's `imageUrl` (create or edit) must be the authorized upload read
  route `/api/v1/community/uploads/images/{imageId}/file`, must reference an
  existing `CommunityImage`, and must be either the author's own upload or
  already public (attached to a published post) — the same visibility the
  upload read route grants strangers.
- Arbitrary external URLs (any non-upload value) and another user's private
  upload are rejected (`Community.Image.Invalid`, 400).
- Clearing the image (null) is always allowed.
- Legacy compatibility: an EDIT that keeps the stored `imageUrl` byte-for-byte
  is not re-validated, so pre-existing posts with historical external
  references remain editable in every other field without touching the image;
  changing the value to a non-upload reference is rejected.
- Deleting a post NEVER deletes the `CommunityImage` row or its stored bytes:
  another record may share the asset. Media cleanup is a separate authorized
  storage operation (W3 lane).

## Author-photo privacy alignment (R2/D7)

The shared batched `GetCommunityAuthorProfilesQuery` handler now publishes
`avatarUrl` only for authors the download route would actually serve:

1. at least one `Published`, non-anonymous post, AND
2. no anonymous post ever (any status, conservative), AND
3. the identity row carries a stored avatar (existing behavior).

Consumers: post feed/detail/recommendations/favorites, comment projections
and reply projections — one predicate, no 200-with-404-URL mismatch, no
caller-avatar fallback. Consequences worth knowing:

- An author whose only published posts are deleted loses the avatar path on
  the next read (matching the route's 404).
- Deleting your own anonymous posts does NOT restore the avatar: the
  conservative rule keys on anonymous-post history, and rows are retained
  after deletion.
- Display names follow the existing item-level anonymity rules and are
  unchanged (item anonymity hides name+id; the photo suppresses only the
  avatar path per the rules above).
- The photo route itself (`GET /api/v1/community/authors/{authorId}/photo`)
  is unchanged. Two existing projection tests were updated because they
  encoded the pre-alignment behavior (advertising an avatar URL the route
  would deny for an author with no Community post history).

## Anatomy of the change (exact paths)

- App routes: `src/API/Sanad.API/Controllers/CommunityPostsController.cs`
  (PUT + DELETE `posts/{postId}`), Admin deletion:
  `AdminCommunityPostsController.cs` (DELETE `{postId}`).
- New handlers/commands (only under Posts/, as scoped):
  `src/Modules/Community/Application/Sanad.Modules.Community.Application/Posts/UpdatePostCommands.cs`.
- Image-reference enforcement: `Posts/CreatePostCommandHandler.cs` +
  `Uploads/CommunityImageUrls.cs` (`TryParseImageId`).
- Deleted hiding/child blocking:
  `Comments/GetCommentsQueryHandler.cs`, `Comments/GetPostCommentsQuery.cs`,
  `Comments/GetCommentRepliesQuery.cs`,
  `Comments/CreatePostCommentCommandHandler.cs`,
  `Comments/CreateReplyCommandHandler.cs`.
- Photo alignment: `Posts/GetPostsQuery.cs`
  (`GetCommunityAuthorProfilesQueryHandler`).
- Reaction-stop correction (bounded, same day):
  `Posts/LikePostCommandHandler.cs`, `Posts/UnlikePostCommandHandler.cs`,
  `Posts/ToggleFavoritePostCommandHandler.cs` (new still-`Published` guards),
  `Comments/LikeCommunityCommentCommand.cs` (parent-post resolution +
  guard). `Ratings/AddRatingCommandHandler.cs` and
  `CheckIns/AddCheckInCommandHandler.cs` were verified as already guarded —
  no code change, tests only.
- Domain: `Posts/Post.cs` (`PostStatus.Deleted`, `Update`, `Delete`,
  `Version`, `DeletedBy`, `DeletedOnUtc`).
- Persistence: `Configurations/PostConfiguration.cs` + additive migration
  `20261023150000_AddPostDeletionAudit` (adds `Version bigint NOT NULL
  DEFAULT 0`, `DeletedBy uuid NULL`, `DeletedOnUtc timestamptz NULL` on
  `community."Posts"`; no other schema change) + snapshot update. W2 is the
  sole Community migration/snapshot author for this addition.
- Tests (authored, NOT RUN): `tests/Sanad.UnitTests/Community/CommunityPostOwnershipTests.cs`
  and `tests/Sanad.UnitTests/API/CommunityPostManagementApiTests.cs`
  (ownership/forged-id, moderation deletion, race guard, deleted hiding,
  child blocking, media retention, image authorization, photo alignment,
  route/policy reflection, no-admin-edit assertion, plus the reaction-stop
  regressions for like/unlike/favorite/comment-like and the pinned
  rating/check-in deleted-parent behavior). The API suite also authors real-host
  HTTP checks for app PUT claim/route binding, moderator DELETE attribution, and
  SupportAdmin/Family denial; these use a capturing sender, so they do not prove
  persistence. The dual-PostgreSQL-context race belongs to the runtime slot.
- Previously required Postman/shared-mapper amendments: none needed — this
  feature reuses existing codes and the Mastermind-owned Postman mapping
  covers route inventory updates (PUT/DELETE app post, DELETE admin post).

## NOT RUN limits (authored only)

- No build, test run, HTTP host, migration application, PostgreSQL, storage
  or provider operation was performed by this lane (no runtime slot) — for
  the original feature and the bounded corrections alike; every listed test is
  authored only and NOT RUN.
- The real-host HTTP tests exercise route binding/authorization only; they do
  not establish persistence, PostgreSQL concurrency, or consumer-screen behavior.
- The race test uses two SQLite contexts; the contract's separation with
  separate PostgreSQL contexts runs in the runtime slot.
- Consumer-screen verification (mobile/admin) is outside API scope.
