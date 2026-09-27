# Admin Elderly help requests

Operational help-request access uses `ElderlyHelpRequestOperational`: a
Normal JWT with `account_type` `SuperAdmin` or `SupportAdmin`. ContentAdmin,
Family, Elderly, and restricted-verification tokens are denied. Sensitive
reads write an operational audit row before querying and return safe projected
fields only; deleted Families are filtered out.

## Routes

| Method and route | Contract |
|---|---|
| `GET /api/v1/admin/elderly/help-requests?status=&elderlyId=&page=1&pageSize=20` | Paged last-year list; `pageSize` is bounded 1–100 and may filter by status or Elderly profile ID. |
| `GET /api/v1/admin/elderly/help-requests/{requestId}` | Safe request detail. |
| `GET /api/v1/admin/elderly/help-requests/{requestId}/history` | Chronological append-only history. |
| `GET /api/v1/admin/elderly/help-requests/aggregate` | Counts grouped by persisted status for the last-year, non-deleted-Family population. |
| `POST /api/v1/admin/elderly/help-requests/{requestId}/{action}` | Status action; action is `accepted`, `started`, `resolved`, `rejected`, `cancelled`, or `reopened`. |

The `aggregate` route must be matched before a GUID detail route by clients that
build route templates. The request projection contains IDs, selected catalog
keys, optional custom text, status, and UTC timestamps. History contains the
action, resulting status, optional reason, actor user ID, and UTC occurrence
time.

## Status actions and reasons

The lifecycle is:

`Pending → Accepted → InProgress → Resolved`

`Pending` may also become `Rejected` or `Cancelled`. A resolved or rejected
request may be `Reopened`; reopening persists `Reopened` and returns the live
request to `InProgress`. Every operator `reject`, `resolve`, and `reopen`
action requires a nonblank reason of at most 500 characters. Operator
`cancel` also requires the same bounded reason. Invalid transitions return
`400 Families.HelpRequest.InvalidOperation`.

Availability is one year for request and history reads. There is no delete or
export route. Push/email delivery is deferred to Notifications/Events and SMS
is excluded; this surface is operational state/history, not a notification
delivery console.

## CMS sentence-builder catalog

CMS content authoring is separate from operational help-request access:

- `GET /api/v1/admin/sentence-builder/catalog`
- `GET /api/v1/admin/sentence-builder/catalog/{revisionId}`
- `POST /api/v1/admin/sentence-builder/catalog`
- `POST /api/v1/admin/sentence-builder/catalog/{revisionId}/activate`
- `POST /api/v1/admin/sentence-builder/catalog/{revisionId}/deactivate`

These routes use `CmsContent` and accept Normal `SuperAdmin` or `ContentAdmin`
tokens. Categories are `Actor=1`, `Action=2`, `Need=3`, `Qualifier=4`.
Create/upsert adds an immutable revision and leaves at most one active revision
per stable key/category; activation changes active state without mutating old
revision content. Labels are bilingual and each is limited to 200 characters;
stable keys are limited to 120 characters and display order must be nonnegative.
