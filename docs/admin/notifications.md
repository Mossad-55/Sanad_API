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

## Delivery state

Admin notification inspection continues to read the in-app notification store.
Push delivery is tracked separately in `notifications.push_outbox` with
`Pending`, `Processing`, `Sent`, `Suppressed`, or `Failed` status, attempt count,
next-attempt time, a sanitized last error code, and the FCM message ID when
available. Device tokens and message bodies are not included in the Admin
inspection responses. There is no Admin push-delivery route in Lane A.

Email delivery is tracked separately in `notifications.email_outbox`. Each row
stores the recipient, subject, plain-text body, optional HTML snapshot, stable
idempotency key, status, attempt count, next-attempt time, and delivery timestamps.
The processor sends HTML and plain text as `multipart/alternative` when an HTML
snapshot exists. Recipients and email bodies are not exposed by the Admin
notification inspection routes.

## Generated copy and language

These inspection routes project metadata only, so the recipient-facing `title`
and `body` of a notification are not exposed here. Those fields are
server-generated display copy chosen from the recipient's persisted UI language
(Arabic is the default; English when the recipient persists `2`), and a fan-out
to several recipients reads every recipient's language in one batch lookup.
Generated copy never repeats a raw identifier — ids belong to the structured
destination pair and to the push payload. Stored rows are not rewritten.

The admin notices an administrator triggers — account status change and password
reset — localize their in-app row the same way. Their account-status email keeps
the English subject, body, and idempotency key it already carried, so previously
sent mail and existing outbox rows are unchanged. A support-reply notification
keeps the administrator-authored body byte for byte and localizes only its title.

## Self-service notification preferences (Admin and facility-owner accounts)

Preference management is a separate, self-service surface from the read-only
inspection routes above. Both routes require only `NormalAccess`, which is
satisfied by any authenticated account whose token carries the
`AccessType = Normal` claim — the policy checks no `AccountType`. `SuperAdmin`,
`SupportAdmin`, `ContentAdmin`, `FinanceAdmin`, and `CareHomeOwner` therefore
reach it exactly as a Family, caregiver, or elderly account does. Each caller
reads and writes **only their own** user row; there is no admin
"manage another user's preferences" route, and no bulk or per-facility variant.
Self-service edits can change push eligibility and, for the wellness categories,
whether an in-app row is created at all, so callers that also read the
inspection routes above should expect them to be unrelated surfaces.

| Route | Request | Result |
|---|---|---|
| `GET /api/v1/account/notification-preferences` | none | `200` with the caller's ten booleans |
| `PUT /api/v1/account/notification-preferences` | Full-replacement body carrying the same ten fields | `200` with the stored ten booleans |

`PUT` is a full replacement: its **eight required fields** are always written,
and its **two nullable fields** carry preserve/compatibility semantics instead —
an omitted `helpRequestAlerts` keeps its stored value, and an omitted
`chatMessages` follows the chat compatibility rule below. `401` is returned when
no valid Normal JWT is supplied, `404` when the caller's user row is missing,
and `409` when the account is blocked.

### Settable fields and where they take effect

All ten booleans are settable by every account type; the request shape does not
vary by role. What differs is whether a given role can *receive* a push-keyed
notification that consults the field. The mapping is the push fan-out policy;
fields with no mapping are stored but never consulted.

| Field | Push key | Admin / facility-owner relevance (current source) |
|---|---|---|
| `systemNotifications` | `SystemNotifications` | Facility review events (`CareHomes`/`OnboardingStatus`) reach the facility owner and, on submission and correction events, every active `SuperAdmin`/`SupportAdmin`. |
| `bookingUpdates` | `BookingUpdates` | Care-home booking events (`CareHomes` with `Booking*` types) reach the facility owner, the affected Family user, or, for refund-failed and dispute-opened events, active `SuperAdmin`/`SupportAdmin`. |
| `helpRequestAlerts` | `HelpRequestAlerts` | SOS and help-request alerts also target active `SupportAdmin` users, alongside eligible Family members and the assigned caregiver. |
| `medicationReminders` | `MedicationReminders` | Medication-late alerts also target active `SupportAdmin` users. |
| `checkInAlerts` | `CheckInAlerts` | Negative check-in alerts target eligible Family members only; no admin or facility-owner recipient. |
| `chatMessages` | `ChatMessages` | Any chat participant, including an admin or facility-owner account, when that account is in the conversation. |
| `communityNotifications` | `CommunityNotifications` | Mapping exists for own-post published/rejected, but no producer emits that category today. |
| `familyActivityAlerts` | — | No push mapping; stored only. |
| `newOrders` | — | No push mapping; stored only. |
| `messagesFromFamilies` | `ChatMessages` (legacy, see below) | Legacy chat field; retained for existing consumers. |

Support-reply (`Support`/`SupportReply`) and admin account-status/password
notices (`Identity`/`AdminUser*`) have no push mapping, so they are in-app only
regardless of any preference.

For SOS, help-request, and medication alerts, the Family and caregiver
recipients are filtered by preference *inside the recipient lookup*, so an
ineligible Family or caregiver is removed **before** the notification row is
created; the `SupportAdmin` recipients are added without that filter and are
gated only immediately before push delivery.

### Defaults, unused fields, and the chat compatibility rule

A new account's preferences all default to enabled. `helpRequestAlerts`
defaults to enabled for rows that predate the field.

`familyActivityAlerts` and `newOrders` are settable and persisted but have **no
push mapping and no other consumer** — clearing either has no effect. Treat them
as reserved/legacy, not as live switches.

`messagesFromFamilies` is a legacy field: `chatMessages` is the canonical
chat-push preference. For rows that predate `chatMessages`, the stored
`messagesFromFamilies` value was adopted as the initial `chatMessages` value.
On update, an explicit `chatMessages` wins; if it is omitted and
`messagesFromFamilies` changes from its stored value, the new
`messagesFromFamilies` value is mirrored to `chatMessages`; otherwise the stored
`chatMessages` is preserved, so omission alone never resets a chat opt-out.
Legacy `messagesFromFamilies` notification rows are mapped to the `ChatMessages`
key so their opt-outs keep working. New integrations should use `chatMessages`.

### What a disabled preference does

A disabled preference does **not** behave the same for every producer, so do not
read it as a universal "push off, inbox unaffected" switch.

- **Suppression at selection (wellness categories).** For check-in alerts,
  help requests, SOS, and medication-late alerts, the Family and caregiver
  recipients are selected by preference inside the recipient lookup
  (`GetActiveUsersWithCheckInAlertsQuery.cs:20-22,29,33`), so an ineligible
  Family or caregiver never becomes a recipient — **no in-app row is created for
  them at all** and their preference also suppresses the push. The
  `SupportAdmin` recipients of these categories are added without the
  preference filter and are gated only before push delivery.
- **Suppression at delivery only (care-home owner/admin and chat).** Facility
  review and booking events select the owner and active `SuperAdmin`/
  `SupportAdmin` recipients without consulting preferences, and the email-outbox
  row is written unconditionally — so the in-app row and the email are produced
  regardless of the preference, and only the push is suppressed. Chat rows are
  likewise created for the conversation participant regardless of the preference,
  which is re-checked immediately before each chat push send.

In both cases preferences never *initiate* push, email, or SMS. Push outbox rows
are created only for recipients that have a registered device; a row is then
either suppressed at delivery (preference disabled, or no device at send time)
or sent.

**Needs Owner Verification:** the exact set of roles the future Admin portal and
facility-owner portal surfaces will expose these fields to. This guide records
only what the current endpoints and fan-out policy permit.

## OpenAPI contract

All four views publish their actual success schemas (`200` page envelopes
with `hasMore` for list/timeline, the metadata record for detail, and the
count aggregate). Unknown or out-of-window detail ids keep `404
Notifications.AdminNotification.NotFound`. Shared conventions are in
[api-contract-conventions](api-contract-conventions.md).
