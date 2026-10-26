# Care-home care records (resident timeline)

The facility-owner dashboard writes `CareHome`-source care records for its
accepted residents. They are part of the one care-record aggregate shared with
the family and caregiver surfaces: a record created here is read-only for the
family and the elderly, who see it inside their combined timelines. The family
and caregiver create contracts are documented in
[family care records](../app/families/care-records.md); this page covers the
care-home side.

## Routes

| Method and route | Access | Purpose |
|---|---|---|
| `GET /api/v1/care-homes/residents/mine` | `CareHomeOwnerAccess` | Accepted residents of the caller's facility, paged bilingual names only (no medical data) |
| `GET /api/v1/care-homes/residents/mine/{residentId}/care-records` | `CareHomeOwnerAccess` | Own `CareHome`-source records of one accepted resident |
| `POST /api/v1/care-homes/residents/mine/{residentId}/care-records` | `CareHomeOwnerAccess` | Create a `CareHome`-source record; success `201` |
| `GET /api/v1/care-homes/residents/mine/{residentId}/care-records/{recordId}` | `CareHomeOwnerAccess` | Read one own record |
| `PUT /api/v1/care-homes/residents/mine/{residentId}/care-records/{recordId}` | `CareHomeOwnerAccess` | Versioned update of own record; stale version is `409` |
| `DELETE /api/v1/care-homes/residents/mine/{residentId}/care-records/{recordId}?expectedVersion=n` | `CareHomeOwnerAccess` | Versioned delete of own record; success `204` |

Scope, locked per the D2/D3 decisions: the facility owner only (no staff
role). The facility is resolved server-side from the authenticated owner —
never from the request — and every action rechecks the resident's
accepted-stay scope against the CareHomes module. A known dependent that is
not an accepted resident of the caller's facility is
`403 Families.CareRecords.AccessDenied`; an unknown one is
`404 Families.CareRecords.DependentNotFound` (no existence disclosure).

## Create contract

`POST /api/v1/care-homes/residents/mine/{residentId}/care-records` → `201`.
The source (`CareHome`), facility attribution and author id are all derived
server-side; nothing in the body sets them.

Request:

```json
{
  "hospitalName": "Approved test fixture",
  "visitDate": "2026-10-20",
  "details": "Disposable care-home care-record fixture"
}
```

- `hospitalName` is required, trimmed, and at most 200 characters.
- `visitDate` is a calendar date (`YYYY-MM-DD`).
- `details` is required, trimmed plain text, and at most 4,000 characters.

Unlike the family and caregiver creates, this surface takes no
`Idempotency-Key` header; there is no replay layer on the care-home create.

Response (`201`, the created `CareRecordResponse`):

```json
{
  "id": "0198e4a0-4444-7777-8888-000000000007",
  "dependentId": "0198e2c1-1111-7777-8888-000000000001",
  "source": 2,
  "sourceNameAr": "دار الرعاية",
  "sourceNameEn": "Care Home",
  "authorUserId": "0198e2c0-0000-7777-8888-000000000003",
  "hospitalName": "Approved test fixture",
  "visitDate": "2026-10-20",
  "details": "Disposable care-home care-record fixture",
  "createdOnUtc": "2026-10-26T09:00:00Z",
  "updatedOnUtc": null,
  "version": 1
}
```

### Errors

| Code | HTTP | When |
|---|---|---|
| `Families.CareRecords.Invalid` | 400 | A field is empty, too long, or otherwise invalid. |
| `Families.CareRecords.InvalidQuery` | 400 | Bad paging, an unknown `source` filter value, or reversed date filters (list reads). |
| `Families.CareRecords.DependentNotFound` | 404 | The resident id is not a known dependent. |
| `Families.CareRecords.AccessDenied` | 403 | The dependent exists but has no accepted booking at the caller's facility. |

`401` covers an unauthenticated call or an authenticated user that owns no
facility row.

## Where a created record surfaces (app reads)

The record is written once and read through the shared aggregate by three
surfaces:

- **Family app —** `GET /api/v1/family/dependents/{dependentId}/care-records`
  (`FamilyAccess`): the dependent's combined timeline across all three
  sources. The care-home record appears with `source: 2` /
  `sourceNameEn: "Care Home"`. `sourceNameAr`/`sourceNameEn` are display
  names only.
- **Elderly app —** `GET /api/v1/elderly/care-records` (`ElderlyAccess`, read
  only): the authenticated elderly user's own combined timeline across all
  three sources, resolved from the token — no alternate elderly id.
- **Care-home dashboard —** the own-source list/detail reads in the table
  above, scoped to this facility only.

On both app reads the optional `source` filter accepts the enum name
(`CareHome`, case-insensitive) or its number (`2`); omitting it returns all
sources combined. Search examines `hospitalName` only, never details.
