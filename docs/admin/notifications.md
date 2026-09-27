# Admin notification inspection

`GET /api/v1/admin/notifications?page=1&pageSize=20` provides a paged,
read-only view of durable notification rows created within the last year.
Page size is 1–100. The response contains notification ID, category, type,
creation time, and read time. It intentionally omits recipient identity, title,
body, destination data, counts, and recipient/family profile details.

```http
GET /api/v1/admin/notifications?page=1&pageSize=20
Authorization: Bearer <normal-admin-token>
X-Correlation-ID: admin-notification-review
```

```json
{
  "items": [
    {
      "id": "6ab3d866-3a6f-4c09-8d9c-75d46cc24de1",
      "category": "ElderlySos",
      "type": "SosCreated",
      "createdOnUtc": "2026-09-27T14:30:00Z",
      "readOnUtc": null
    }
  ],
  "page": 1,
  "pageSize": 20,
  "hasMore": false
}
```

`page` is 1–100000; invalid paging returns validation failure. Missing/invalid
authentication returns 401, and an authenticated role outside this policy
returns 403. Audit persistence failure fails closed and returns no notification
data.

Access requires a Normal `SuperAdmin` or `SupportAdmin` token under
`AdminNotificationOperationalRead`; `ContentAdmin` is not authorized. Each
request writes an immutable, payload-free operational access audit before the
notification query. If audit persistence fails, the request fails without
returning notification data. No schema change was needed; the existing
Families operational access audit store is used.

The approved audit retention duration is seven years, but this route does not
add or enforce an audit purge/retention worker. Notification rows older than
one year are excluded from this read but are not physically purged here.

**Needs Owner Verification:** the operational table/filter shape, detail,
timeline, and aggregate routes; aggregate metrics; recipient, payload, and
destination visibility; mapping notifications to deleted Families or other
deleted recipients; enforcement of the approved seven-year audit retention;
and notification retention beyond the existing one-year read availability.
Older rows are excluded at read time; this route does not purge them. Provider
delivery, outbox, scheduling, and production mutation are outside this
capability.
