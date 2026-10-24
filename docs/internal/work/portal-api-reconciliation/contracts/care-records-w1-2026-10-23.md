# Care Records — W1 Portal/API Contract (2026-10-23)

Worker: W1 care-records lane. Status: implemented, ready for Mastermind review.
Base: HEAD `9433c1d` (uncommitted). All checks authored but **not run** (build/test
runtime slot is Mastermind-owned).

## Scope locked

Three authoring sources over one aggregate (`families.care_records`):

| Source | Who | Read | Write |
|---|---|---|---|
| `Family` | Family Owner/Editor | — | Family-source CRUD only |
| `CareHome` | Facility owner (CareHomes module) | — | CareHome-source CRUD, own facility |
| `Caregiver` | Caregiver (medical + companion, no distinction) | own-source | create + full CRUD own-source, window-gated |

Readers: Family (any member role) reads all three sources of an owned dependent;
Elderly reads own combined timeline (read-only); Admin
(`OperationalReadAdmin` = SuperAdmin + SupportAdmin) reads list/detail only with a
payload-free read audit.

## Routes

Family (policy `FamilyAccess`):

- `GET  /api/v1/family/dependents/{dependentId}/care-records`
- `GET  /api/v1/family/dependents/{dependentId}/care-records/{recordId}`
- `POST /api/v1/family/dependents/{dependentId}/care-records` → 201
- `PUT  /api/v1/family/dependents/{dependentId}/care-records/{recordId}`
- `DELETE /api/v1/family/dependents/{dependentId}/care-records/{recordId}?expectedVersion=n` → 204

Caregiver (policy `CaregiverAccess`):

- `GET  /api/v1/caregiver/care-records/recipients` (picker: names only)
- `GET  /api/v1/caregiver/care-records?dependentId=…`
- `GET  /api/v1/caregiver/care-records/{recordId}`
- `POST /api/v1/caregiver/care-records` (body carries `dependentId` as selector only) → 201
- `PUT  /api/v1/caregiver/care-records/{recordId}`
- `DELETE /api/v1/caregiver/care-records/{recordId}?expectedVersion=n` → 204

CareHome (policy `CareHomeOwnerAccess`):

- `GET  /api/v1/care-homes/residents/mine` (accepted residents, names only)
- `GET  /api/v1/care-homes/residents/mine/{residentId}/care-records`
- `GET  /api/v1/care-homes/residents/mine/{residentId}/care-records/{recordId}`
- `POST /api/v1/care-homes/residents/mine/{residentId}/care-records` → 201
- `PUT  /api/v1/care-homes/residents/mine/{residentId}/care-records/{recordId}`
- `DELETE /api/v1/care-homes/residents/mine/{residentId}/care-records/{recordId}?expectedVersion=n` → 204

Elderly (policy `ElderlyAccess`, read-only):

- `GET /api/v1/elderly/care-records` — self-scoped; the route accepts **no** dependent id.

Admin (policy `OperationalReadAdmin`, read-only):

- `GET /api/v1/admin/care-records`
- `GET /api/v1/admin/care-records/{recordId}`

Deletes take `expectedVersion` from the query string (no delete body).

## Envelope and fields

List envelope (all five portals): `{ items, page, pageSize, totalCount, totalPages }`.
Item (`CareRecordResponse`): `id, dependentId, source ("Family"|"CareHome"|"Caregiver" —
enum NAME, never numeric), sourceNameAr, sourceNameEn, authorUserId, hospitalName,
visitDate (date-only), details, createdOnUtc, updatedOnUtc?, version`.

Query contract (identical across portals): filter `source` (name, case-insensitive;
unknown = 400), inclusive `visitDateFrom`/`visitDateTo` (reversed = 400), `search`
(trimmed ≤ 100 chars, matches hospital names + picker names only — **never** medical
details), `page` (default 1), `pageSize` (default 20, max 100). Ordering pinned:
`visitDate DESC, createdOnUtc DESC, id DESC`.

Creation/edit contract: `hospitalName` required, ≤ 200 after trim; `details` required,
≤ 4000 after trim, plain text (control characters other than tab/newline/CR rejected);
`visitDate` date-only; edits echo `expectedVersion` (optimistic concurrency, `version`
field). Attribution (source, family, caregiver, facility, dependent) is
server-derived; it is immutable and never trusted from the caller.

## Authorization semantics

- Caregiver window, revalidated on **every** read and write: a booking for the exact
  dependent that is `Confirmed` or `InProgress`, or `Completed` with
  `completedOnUtc >= utcNow - 30d` (boundary inclusive). The picker's result is a
  suggestion only — a permission revoked between picker GET and create POST fails the
  create (403).
- CareHome scope, rechecked per action: resident must exist in Families (unknown →
  404 `DependentNotFound`) and hold an `Accepted` CareHomes booking at the caller's
  facility (known but foreign → 403). Facility is resolved from the authenticated
  owner, never from the request.
- Family: any member role reads; Owner/Editor write family-source records only. A
  caregiver/care-home-source record under an authorized dependent is 403 (role/source
  denial), never 404. A known record id outside every authorized scope is 404
  (`NotFound`).

## Error codes (pinned precedence: 400 → 403 → 404 → 409)

| Code | HTTP | Meaning |
|---|---|---|
| `Families.CareRecords.InvalidQuery` | 400 | bad paging/search/source/range |
| `Families.CareRecords.Invalid` | 400 | field/length/plain-text violation |
| `Families.CareRecords.AccessDenied` | 403 | foreign dependent, wrong source, expired window |
| `Families.CareRecords.DependentNotFound` | 404 | dependent out of every authorized scope |
| `Families.CareRecords.NotFound` | 404 | known id outside authorized scope |
| `Families.CareRecords.ConcurrencyConflict` | 409 | stale `expectedVersion` |

## Audit

`families.care_record_audits` is append-only (enforced in `FamiliesDbContext.SaveChanges`)
and payload-free: authoring rows (`Created`/`Updated`/`Deleted`) carry
`recordId` + `source` (deletion attribution outlives the record row); admin reads emit
`AdminListed` (no record id, no source) / `AdminDetailRead` (record id, no source) with
actor, timestamp and `correlationId` (≤ 200 chars). No visit content is ever audited.

## Persistence

Tables `families.care_records` + `families.care_record_audits`; migration
`20261023131500_AddCareRecords` (+ Designer, snapshot updated). `care_records.elderly_id`
carries an explicit FK to `families.elderlies(id)` with `DeleteBehavior.Restrict`
(conventional referential integrity, mirroring `MedicalReportConfiguration` — care
records are medical data and never cascade-delete). Keyed indexes:
`elderly_id`, `(elderly_id, source)`, `visit_date`, `author_facility_id`; audit:
`(elderly_id, occurred)`, `(actor, occurred)`, `occurred`, `record_id`. Audit rows keep
id/duplicate-free plain columns (no FK semantics — AdminListed rows intentionally have
no dependent binding and no FK relation).

## Shared integration status (Mastermind-owned)

1. **`ResultProblemDetailsMapper` — integrated**: Mastermind added the following
   status-code entries; the safe unknown-code default no longer masks these results:
   - `Families.CareRecords.AccessDenied` → 403
   - `Families.CareRecords.DependentNotFound` → 404
   - `Families.CareRecords.NotFound` → 404
   - `Families.CareRecords.InvalidQuery` → 400
   - `Families.CareRecords.Invalid` → 400
   - `Families.CareRecords.ConcurrencyConflict` → 409
2. **Postman — integrated**: the Family Operations, Caregiver Operations,
   Care-Home Owner, Elderly and Admin Families collections now include the five
   surfaces' routes without removing existing folders. Delete examples use the
   query-string `expectedVersion` form; all five JSON collections parsed
   successfully. This is contract documentation, not live HTTP evidence.

No DI, policy, Program or assembly-scan change is needed: handlers/validators are
picked up by the existing Families registration, and all five controllers register
through the existing controller discovery.

These routes are API contract coverage only; this repository contains no consuming
mobile/Admin dashboard source to verify screen behavior. Consumer-screen checks
remain NOT RUN until those builds/devices are available.

## Verification (authored, NOT run — Mastermind owns the build/test slot)

- `tests/Sanad.UnitTests/Families/CareRecordDomainTests.cs` — aggregate invariants.
- `tests/Sanad.UnitTests/Families/CareRecordHandlerTests.cs` — role gating, caregiver
  grace boundary (inclusive), revoked-between-picker-and-create, elderly self-scope,
  care-home facility scope, admin payload-free audits, paging/filter/search, 409s.
- `tests/Sanad.UnitTests/API/CareRecordsPortalControllersTests.cs` — policy wiring per
  controller, read-only admin/elderly surfaces, 400-before-auth source parse,
  version-from-query delete contract, coded-mapper status mapping, plus in-process
  HTTP-host family-route binding/success and anonymous/wrong-role denial checks.
  The HTTP fake replaces MediatR: it proves transport/auth contracts, not persisted
  data, real PostgreSQL, the other portals' successful HTTP requests, or consumer UI.
