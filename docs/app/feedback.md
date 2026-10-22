# App feedback

`POST /api/v1/feedback/app-rating` requires an authenticated account. The
server replaces the request `userId` with the authenticated identity, so a
caller cannot submit feedback on behalf of another user.

```json
{
  "rating": 5,
  "comment": "The medication reminders are clear.",
  "deviceInfo": "Android",
  "appVersion": "1.0.0"
}
```

`rating` must be between 1 and 5. The comment is optional. A valid submission
returns `200` with `{ "success": true, "message": "Rating submitted successfully" }`.
Unauthenticated callers receive `401`; an out-of-range `rating` is rejected with
`400 Feedback.InvalidRating`, and other invalid input is returned through the
standard validation/problem-details contract. Feedback is stored in the
`identity` schema by the `AddFeedback` migration.

## Rating policy

- **Each submission creates a new rating.** The route always inserts a new
  record with its own id; a repeated submission never updates or replaces an
  earlier rating — there is no update-in-place path.
- **Attributed and retained.** Every rating is stored against the signed-in
  account (the request `userId` is replaced by the authenticated identity) and
  is kept; there is no update or deletion endpoint.
- **Authenticated only — no anonymous option.** The route requires an app JWT;
  a caller without a valid token receives `401`. Anonymous ratings are not
  accepted or stored.
- **Not public.** No read route exposes ratings, and nothing here is returned to
  any client.
- **No admin surface today.** There is no admin list or detail route for
  ratings; the stored rows are telemetry and are only readable directly from
  storage. An admin surface would be a new product decision.
