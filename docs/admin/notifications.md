# Admin notification inspection

The Admin operational views provide read-only inspection and aggregate counts
for durable notification records created within the rolling last year. Every
view requires a Normal `SuperAdmin` or `SupportAdmin` under
`AdminNotificationOperationalRead`; `ContentAdmin` is denied. Each request
persists a payload-free operational access audit before executing its query.
If audit persistence fails, the request returns no notification data.

## Routes

| Route | Query parameters | Result |
|---|---|---|
| `GET /api/v1/admin/notifications` | `page` (default 1), `pageSize` (default 20, maximum 100); optional exact `category`, `type`, `startDate`, and `endDate` | Paged `items`, `page`, `pageSize`, `hasMore` |
| `GET /api/v1/admin/notifications/{notificationId}` | UUID path value | One notification metadata record; a missing or out-of-window record returns `404 Notifications.AdminNotification.NotFound` |
| `GET /api/v1/admin/notifications/timeline` | Required UTC calendar `startDate` and `endDate`; `page` (default 1), `pageSize` (default 20, maximum 100) | Chronological page with `items`, `page`, `pageSize`, `hasMore` |
| `GET /api/v1/admin/notifications/aggregate` | Optional `startDate` and `endDate` | `totalCount`, `readCount`, `unreadCount`, and `categoryCounts` |

List and timeline page and page size values must each be from 1 through 100. List category
and type filters are exact matches. Dates use `YYYY-MM-DD` UTC calendar days;
date filtering includes the complete start and end days. Timeline requires both
dates and the inclusive range may contain at most 31 calendar days. Aggregate
accepts either date independently; the same range ordering and 31-day limit is
checked when both are supplied. Invalid ranges or paging values return a
validation failure.

```http
GET /api/v1/admin/notifications?page=1&pageSize=20&category=ElderlySos&type=SosCreated&startDate=2026-09-01&endDate=2026-09-30
Authorization: Bearer <normal-admin-token>
X-Correlation-ID: admin-notification-list

GET /api/v1/admin/notifications/6ab3d866-3a6f-4c09-8d9c-75d46cc24de1
Authorization: Bearer <normal-admin-token>
X-Correlation-ID: admin-notification-detail

GET /api/v1/admin/notifications/timeline?startDate=2026-09-01&endDate=2026-09-30&page=1&pageSize=20
Authorization: Bearer <normal-admin-token>
X-Correlation-ID: admin-notification-timeline

GET /api/v1/admin/notifications/aggregate?startDate=2026-09-01&endDate=2026-09-30
Authorization: Bearer <normal-admin-token>
X-Correlation-ID: admin-notification-aggregate
```

List, detail, and timeline records project only notification ID, category,
type, creation time, and read time. No recipient identity, title, body, or
destination is returned. Pagination responses include `items`, `page`,
`pageSize`, and `hasMore`; detail returns one record. Aggregate contains counts
only, for example:

```json
{
  "totalCount": 42,
  "readCount": 30,
  "unreadCount": 12,
  "categoryCounts": [
    { "category": "ElderlySos", "count": 8 }
  ]
}
```

All four views apply the rolling one-year creation-time cutoff. Older records
are unavailable through these reads but are not physically purged by this
capability. Authentication failures return 401; authenticated roles outside
the policy, including `ContentAdmin`, receive 403. Detail for an unknown or
out-of-window ID returns `404 Notifications.AdminNotification.NotFound`.

The existing Families operational access-audit store records each read. No
schema change is required. The approved seven-year audit retention duration is
not enforced by these routes.

**Needs Owner Verification:** deleted-recipient mapping, the complete
notification category/event inventory, event-specific timezone behavior,
physical notification purge and retention beyond the one-year read window, and
enforcement of seven-year audit retention. Provider delivery, outbox,
scheduling, and retries are outside these operational views.
