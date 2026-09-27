# Admin Elderly SOS operations

Admin SOS operations are read/status-management routes under
`/api/v1/admin/elderly/sos`. They require a `Normal` JWT whose
`account_type` is `SuperAdmin` or `SupportAdmin` (`ElderlySosOperational`).
Family, Caregiver, Elderly, ContentAdmin, restricted-verification, and
unauthenticated requests are not authorized.

## Routes

| Method | Route | Behavior |
|---|---|---|
| GET | `/api/v1/admin/elderly/sos?page=1&pageSize=20&status=&elderlyId=` | Paged operational list; optional status and Elderly filters |
| GET | `/api/v1/admin/elderly/sos/{sosId}` | Operational detail |
| GET | `/api/v1/admin/elderly/sos/{sosId}/history` | Chronological append-only history |
| POST | `/api/v1/admin/elderly/sos/{sosId}/status` | Apply an allowed lifecycle action |

The status request body is:

```json
{ "action": "Acknowledged" }
```

The API enum values are `Open = 1`, `Acknowledged = 2`, `Resolved = 3`, and
`Cancelled = 4`; history actions are `Created`, `Acknowledged`, `Resolved`,
and `Cancelled`. `Open` may be acknowledged, resolved, or cancelled;
`Acknowledged` may be resolved or cancelled; terminal states cannot transition.

`page` defaults to `1`, and `pageSize` defaults to `20` and is clamped to
`1..100`. A correlation ID is accepted from `X-Correlation-ID` and otherwise
uses the server trace identifier.

## Privacy, audit, and retention

Every Admin list, detail, history, and status operation writes an operational
audit row before the read or status change. Admin projections exclude SOS rows
whose linked Family has been deleted. Such rows, and unknown IDs, return an
operational `404 Families.Sos.NotFound` response rather than disclosing their
existence. Coordinates are returned only for the 30-day visibility window and
are null thereafter; stored coordinates are consent-gated and rounded to three
decimal places.

The exact Admin export contract, physical purge schedule, location-retention
policy, and field-level audit/export disclosure are **Needs Owner
Verification**. No SMS, server dialer, push/email provider, outbox, or
scheduler delivery is claimed by these routes.
