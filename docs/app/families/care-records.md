# Care-record creates (family and caregiver)

Two authenticated creates add an entry to a dependent's care-record timeline:

| Method | Route | Policy | Source |
|---|---|---|---|
| `POST` | `/api/v1/family/dependents/{dependentId}/care-records` | `FamilyAccess` (Owner/Editor) | `Family` |
| `POST` | `/api/v1/caregiver/care-records` | `CaregiverAccess` | `Caregiver` |

Both derive the source, dependent ownership and author from the authenticated
caller and the route; they are never accepted from the body. Success is `201`
with the created `CareRecordResponse`.

## Request body

```json
{
  "hospitalName": "Approved test fixture",
  "visitDate": "2026-10-20",
  "details": "Disposable care-record fixture"
}
```

- `hospitalName` is required, trimmed, and at most 200 characters.
- `visitDate` is a calendar date (`YYYY-MM-DD`).
- `details` is required, trimmed plain text, and at most 4,000 characters.
- The caregiver create also takes `dependentId` in the body (a selector only);
  the caregiver's booking scope is revalidated at write, so an unrelated
  dependent is `404 Families.CareRecords.DependentNotFound` or
  `403 Families.CareRecords.AccessDenied`.

## Response

```json
{
  "id": "0198e4a0-4444-7777-8888-000000000004",
  "dependentId": "0198e2c1-1111-7777-8888-000000000001",
  "source": 1,
  "sourceNameAr": "العائلة",
  "sourceNameEn": "Family",
  "authorUserId": "0198e2c0-0000-7777-8888-000000000000",
  "hospitalName": "Approved test fixture",
  "visitDate": "2026-10-20",
  "details": "Disposable care-record fixture",
  "createdOnUtc": "2026-10-25T09:00:00Z",
  "updatedOnUtc": null,
  "version": 1
}
```

### Reading `source`

`source` is the numeric wire value of the record's origin — `1` = Family,
`2` = CareHome, `3` = Caregiver. It is always server-derived: a create body
never sets it. `sourceNameAr`/`sourceNameEn` are the bilingual display names
for that value, for presentation only and never authorization keys.

The timeline read above (`GET /api/v1/family/dependents/{dependentId}/care-records`)
accepts an optional `source` filter in either form: the enum name (`Family`,
`CareHome`, `Caregiver`, case-insensitive) or its number (`1`, `2`, `3`).
Omitting it returns all three sources combined. Any other value — or bad paging
or reversed date filters — is `400 Families.CareRecords.InvalidQuery` before the
read runs. The elderly and admin care-record reads apply the same filter rule.

## Retry safety (`Idempotency-Key`)

The header is optional on both creates. Without it, the request behaves exactly
as before.

- The trimmed value must be 1–128 printable ASCII characters, otherwise
  `400 Families.CareRecords.IdempotencyKeyInvalid`.
- Records are scoped to the authenticated actor, the operation
  (`family.care-records.create` or `caregiver.care-records.create`) and the key.
  The key row commits in the same transaction as the care record, so a rejected
  attempt never stores a key.
- The fingerprint covers `dependentId`, `hospitalName`, `visitDate` and
  `details`. Server-derived values (source, author, timestamps, version) are
  excluded.
- Same key + same fingerprint replays the stored `201` status and body
  byte-for-byte with no second record and no second audit entry.
- Same key + different fingerprint is `409
  Families.CareRecords.IdempotencyConflict`.
- Concurrent duplicates execute exactly once through the unique
  `(actor_id, operation, idempotency_key)` index; the loser replays the winner.
- Rows are retained for 7 days; no purge worker runs in this slice.

## Errors

| Code | HTTP | When |
|---|---|---|
| `Families.CareRecords.Invalid` | 400 | A field is empty, too long, or otherwise invalid. |
| `Families.CareRecords.InvalidQuery` | 400 | Bad paging, an unknown `source` filter value, or reversed date filters. |
| `Families.CareRecords.IdempotencyKeyInvalid` | 400 | The supplied `Idempotency-Key` is not 1–128 printable ASCII characters after trimming. |
| `Families.CareRecords.DependentNotFound` | 404 | The dependent does not exist or is outside the caller's scope. |
| `Families.CareRecords.AccessDenied` | 403 | The caller may not write this dependent's records. |
| `Families.CareRecords.IdempotencyConflict` | 409 | The same key was already used with a different request. |

`401` means unauthenticated; `403` also covers a caller outside the route's
access policy.
