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

Post ratings are unique per post/user; check-ins are unique per post/user/local
date. Interactions are persisted in the `community` schema. The SQL mappings use
PostgreSQL-compatible `timestamp with time zone`, `date`, `time`, `text`, and
`CURRENT_TIMESTAMP` types/defaults.

Community interactions are not financial or medically authoritative records.
Run state-changing checks only against disposable fixtures.
