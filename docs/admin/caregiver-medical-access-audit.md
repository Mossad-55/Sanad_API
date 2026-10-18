# Caregiver medical-access decision audit (Admin)

SuperAdmin and SupportAdmin only (Normal JWT). Read-only paged audit of Family
decisions to grant or revoke medical access for a named caregiver.

- `GET /api/v1/admin/caregivers/medical-access-audit`

Filters (all optional): `fromUtc`, `throughUtc`, `actorUserId`, `dependentId`,
`granteeUserId`, `action` (`CreateGrant`/`RevokeGrant`), `decision`
(`Allowed`/`Denied`). With no time filters, the newest 30 days are returned.
If one time bound is supplied, the other is required. Range must not exceed 12
months. Bounds are UTC, inclusive lower and exclusive upper.

Paging: `page` (default 1), `pageSize` (default 50, max 100). Sort:
`occurredOnUtc` descending, then audit ID descending. Returns the standard
paging envelope `{ items, page, pageSize, totalCount, totalPages }`.

Each item contains only audit metadata: audit ID, occurredOnUtc, actorUserId,
dependentId, granteeUserId, grantId, action, decision, reasonCode, grantType,
permission booleans, expiresOnUtc, correlationId. No medical content, grant
notes, names, email, phone, IP, or request/response bodies are exposed.

Retention: 12 calendar months (UTC); expired rows are excluded from reads even
if cleanup is delayed. Cleanup deletes only this audit table.