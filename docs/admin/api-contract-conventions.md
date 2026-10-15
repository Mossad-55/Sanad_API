# Admin API contract conventions

Shared metadata conventions for the Admin/portal surface, published in the
generated OpenAPI document (`/openapi/v1.json`). Wire formats are unchanged:
these conventions only describe what the API already returns.

## Numeric enums

Integer-valued enums are returned as JSON numbers. Their schemas publish the
actual CLR numeric values in `enum` plus the matching names in `x-enumNames`
(and in the description), derived from the enum definitions — for example
`UserStatus` `1 = PendingVerification, 2 = Active, 3 = Suspended,
4 = Blocked`, and `AdminUserActionType` `1 = Suspended, 2 = Reactivated,
3 = Blocked, 4 = SessionsRevoked, 5 = PasswordReset, 6 = AccountTypeChanged,
7 = Created, 8 = Anonymized`. Nullable enums keep their nullability; string
enums stay strings.

String-valued query filters (for example admin user `accountType`/`status`
and wellness-tip `status`) accept case-insensitive names and defined numeric
strings; see each resource guide for the accepted names.

## IDs

Strongly typed IDs serialize as wrapper objects with a required non-null
`value`, for example `{ "value": "0198e2c2-4444-7777-8888-000000000001" }`.
Flattened `Guid` DTO properties stay plain strings. Do not invent a wrapper
where the route returns a string, and do not flatten existing wrappers.

## Required query parameters

Parent/date parameters that the business requires are marked required in
OpenAPI. Omitted or empty GUID parents fail validation (`400
Api.Validation.Failed`); malformed GUIDs fail model binding (`400`); unknown
but well-formed parents return an empty `200` list on the city/area reads. An
omitted license-expiration `through` date returns `400
CareHomes.Admin.InvalidQuery`; a valid date returns the filtered page (empty
when nothing expires in range).

## Success envelopes and statuses

Paged admin reads return `{ items, page, pageSize, totalCount }`, except the
notification list/timeline views, which return `{ items, page, pageSize,
hasMore }`. Bare JSON arrays are returned where the handler returns a list
(check-in disputes, charge-rule history, city/area lookups, categories).

Admin creation returns `201`. The current platform-charge rule returns `200`
with a `null` body before the first rule exists. Unset resources (elderly
welcome, unknown user/notification/facility/category) return their existing
`404` codes. Controllers return the handler DTO directly, never a
`Result<T>` envelope.

## Community category update

`PUT /api/v1/admin/community/categories/{categoryId}` takes a body without
`Id` (`arabicName`, `englishName`, `isActive`); the route ID is authoritative.
A legacy body that still carries an `Id` is accepted and that value is
ignored.
