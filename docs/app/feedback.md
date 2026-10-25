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
- **Not public.** No app-facing route exposes ratings, and nothing here is
  returned to the submitting client; admins read stored ratings only through
  the separate admin surface below.
- **Read by Admin only, for rating investigation.** There is no public or
  owner-facing read route. A paginated admin read exists at
  `GET /api/v1/admin/feedback/app-ratings` for Super Admin and Support Admin
  (normal JWT); it returns the stored columns only (id, internal user id,
  rating, comment, device info, app version, creation time) — no name, phone,
  or contact data is joined in. See
  [`docs/admin/feedback-app-ratings.md`](../admin/feedback-app-ratings.md).
