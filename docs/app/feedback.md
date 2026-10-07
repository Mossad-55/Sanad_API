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
Unauthenticated callers receive `401`; invalid input is returned through the
standard validation/problem-details contract. Feedback is stored in the
`identity` schema by the `AddFeedback` migration.
