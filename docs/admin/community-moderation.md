# Community moderation API

All routes in this guide require the existing `CommunityModeration` policy:
an authenticated normal `ContentAdmin` or `SuperAdmin` account. Moderation is
limited to Community data. These endpoints do not suspend Identity accounts,
change login/session state, or affect another module.

## Queue and post detail

`GET /api/v1/admin/community/posts` returns a page of all Community posts,
newest first with a stable ID tie-break. Query parameters are optional:

| Parameter | Meaning |
| --- | --- |
| `status` | `Draft`, `PendingReview`, `Published`, `Rejected`, or `Archived` |
| `reportStatus` | `Open`, `ActionTaken`, `Dismissed`, or `None` (no retained reports) |
| `search` | Case-insensitive match across Arabic/English title and content |
| `page` | 1-based page; values below 1 are treated as 1 |
| `pageSize` | Defaults to 25; capped at 100 |

Response shape:

```json
{
  "items": [],
  "page": 1,
  "pageSize": 25,
  "totalCount": 0,
  "counters": {
    "pendingReviewCount": 0,
    "publishedCount": 0,
    "rejectedCount": 0,
    "openReportCount": 0,
    "authorsWithOpenReportsCount": 0,
    "activeRestrictionCount": 0
  }
}
```

Each item includes bilingual post content, image/category data, anonymity and
status, timestamps, report totals, and the author's ID and Arabic/English
display names. Contact details are never returned. `GET
/api/v1/admin/community/posts/{postId}` returns the same post details together
with retained reports and chronological publish/rejection history. Unknown
post IDs return 404. Invalid status filters return 400.

## Publish and reject

- `POST /api/v1/admin/community/posts/{postId}/publish` keeps the existing
  publish behavior and returns 204. Only `PendingReview` posts can be
  published.
- `POST /api/v1/admin/community/posts/{postId}/reject` returns 204 and
  requires this JSON body:

  ```json
  { "reasonCode": "Spam", "note": "Optional context" }
  ```

  Accepted reason codes are `CommunityGuidelines`, `Spam`, `Duplicate`,
  `UnsafeContent`, and `Other`. `Other` requires a note. Notes are trimmed
  plain text, at most 1000 characters. Rejection stores moderator ID, reason,
  note, and UTC decision time. Malformed codes/notes return 400; unknown posts
  return 404; a post outside `PendingReview` returns 409.

## Resolve reports

`POST /api/v1/admin/community/reports/{reportId}/resolve` resolves one open
report:

```json
{ "resolution": "ActionTaken", "note": "Reviewed" }
```

`resolution` is `ActionTaken` or `Dismissed`; `note` is optional, trimmed
plain text up to 1000 characters. The API records moderator and UTC time.
Resolving a report does not itself reject/publish a post or restrict an
author. Unknown report IDs return 404; an already resolved report returns
409; invalid input returns 400. Reports are retained and cannot be deleted.

## Community-only author restrictions

Create a restriction with
`POST /api/v1/admin/community/authors/{authorId}/restrictions`:

```json
{
  "duration": "Days7",
  "reasonCode": "Spam",
  "note": "Optional context"
}
```

`duration` is `Hours24`, `Days7`, or `Days30`. The required closed-set
`reasonCode` uses `CommunityGuidelines`, `Spam`, `Duplicate`, `UnsafeContent`,
or `Other`; `Other` requires a note. The response records author, moderator,
start/expiry times, reason, note, and revocation state. At most one active
restriction is allowed per author. An already restricted author returns 409;
invalid input returns 400.

An active restriction blocks creation of Community posts, comments, and
replies until expiry. It does not hide or alter existing content, block edits
or deletes, or block other interactions. Expiry is checked by the write
commands using UTC; no background job is used.

Revoke early with
`POST /api/v1/admin/community/restrictions/{restrictionId}/revoke`:

```json
{ "reason": "Appeal accepted" }
```

A nonblank reason up to 1000 characters is required. Revocation retains the
original action and records moderator, UTC time, and reason. Unknown IDs
return 404, a previously revoked restriction returns 409, and invalid reasons
return 400. Expired/revoked records remain part of moderation history.
