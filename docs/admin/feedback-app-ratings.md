# App rating investigation (admin read)

`GET /api/v1/admin/feedback/app-ratings` is a read-only, paginated list of the
app ratings users submitted through the authenticated app surface (see
[`docs/app/feedback.md`](../app/feedback.md)). It exists so Support and Super
Admins can investigate rating feedback during a refund or support
investigation; it never creates, edits, or deletes stored ratings. Authored
Postman examples for the Feedback admin surface live in
[`docs/postman/admins/Ahlena.Admin.Feedback.postman_collection.json`](../postman/admins/Ahlena.Admin.Feedback.postman_collection.json).

## Authorization

`OperationalReadAdmin` — Super Admin and Support Admin on a normal token only.
Content Admin, Finance Admin, and every app audience are denied, as for the
other operational reads. Unauthenticated callers receive `401`; authenticated
callers outside the policy receive `403`.

## Request

```text
GET /api/v1/admin/feedback/app-ratings?page=1&pageSize=20
```

Both parameters are optional query values: `page` defaults to `1`,
`pageSize` to `20`. Out-of-range values are clamped silently, following the
other paged admin reads: a `page` below `1` is treated as `1`, and a
`pageSize` below `1` or above `100` falls back to `20` (`100` itself is
accepted).

## Response

`200` with `{ items, page, pageSize, totalCount }`, newest submissions first
(ordered by creation time, then id, so equal timestamps stay stable):

```json
{
  "items": [
    {
      "id": "0198e2c2-4444-7777-8888-000000000001",
      "userId": "0198e2c2-4444-7777-8888-000000000002",
      "rating": 5,
      "comment": "The medication reminders are clear.",
      "deviceInfo": "Android",
      "appVersion": "1.4.0",
      "createdOnUtc": "2026-10-20T09:00:00Z"
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 1
}
```

Each item projects exactly the columns the submission stored: `id`, the
internal `userId`, `rating` (1–5), the optional `comment`, `deviceInfo`,
`appVersion`, and `createdOnUtc`. No name, phone, email, session, or contact
data is joined in; the internal user id is the sole attribution. An empty
database returns `200` with `"items": []` and `"totalCount": 0`.

Bad binding input behaves like the other queries: framework validation
rejections use the field-array shape, and there is no domain `code` on this
route (see
[`docs/admin/api-contract-conventions.md`](api-contract-conventions.md)).
