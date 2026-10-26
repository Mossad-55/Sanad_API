# Care-home visits

Care-home visits are separate from care-giver home-visit bookings. They are free, one-hour appointments for a prospective Family or for an elderly resident currently staying at the selected facility.

## Family and public routes

| Method and route | Access | Purpose |
|---|---|---|
| `GET /api/v1/care-homes/facilities/{facilityId}/visit-availability?date=YYYY-MM-DD&visitorCount=n` | Anonymous; only publicly eligible facilities are returned | Lists available Cairo-local one-hour slots and remaining visitor capacity |
| `GET /api/v1/family/care-home-visits` | `FamilyAccess` | List visits for the caller's active Family |
| `POST /api/v1/family/care-home-visits` | `FamilyAccess` | Request a visit |
| `GET /api/v1/family/care-home-visits/{visitId}` | `FamilyAccess` | Read a visit owned by the caller's Family |
| `GET /api/v1/family/care-home-visits/eligible-residents?facilityId=&startsAt=` | `FamilyAccess` | List residents of the caller's Family who are eligible for a resident visit at the requested facility and date |
| `POST /api/v1/family/care-home-visits/{visitId}/cancel` | `FamilyAccess` | Cancel before the visit starts |
| `POST /api/v1/family/care-home-visits/{visitId}/reschedule` | `FamilyAccess` | Ask for a new slot; the facility must approve it |

The create request includes `facilityId`, `kind` (`Prospective` or `Resident`),
`elderlyId` (null for prospective visits), `visitorName`, `visitorPhone`, an
exact `visitorCount`, and a UTC-offset `startsAt`. A resident visit requires an
elderly ID belonging to the active Family and an eligible accepted, paid stay
at that facility on the visit date. Reschedule accepts
`{ "startsAt": "2026-11-12T08:00:00Z" }`. Family and owner cancellation accept
`{ "reason": "Schedule changed" }`; the reason may be null. Reason length
limits (port-round-2 clarification): a decision or cancellation reason is
capped at **1000** characters after trimming — an over-long one is rejected
with `409 CareHomes.Visits.InvalidState`. These two caps are deliberately NOT
published as OpenAPI `maxLength`: the domain measures the **trimmed (consumed)
text**, so a raw `maxLength` would wrongly reject accepted whitespace-padded
values, and the approve path ignores the decision reason entirely. The
visit-settings closure `reason` is the one exception: it is measured on the
raw value **before** trimming, so its request schema publishes the exact
`maxLength: 500` and an over-long closure reason is refused with a 400
request-validation error before the handler runs; the domain rejects raw >500
exactly the same way. Each family read
and mutation is scoped to the caller's active Family.

Create request example:

```json
{
  "facilityId": "00000000-0000-0000-0000-000000000000",
  "kind": "Prospective",
  "elderlyId": null,
  "visitorName": "Family contact",
  "visitorPhone": "+201000000000",
  "visitorCount": 2,
  "startsAt": "2026-11-10T08:00:00Z"
}
```

For a `Resident` visit, `elderlyId` must belong to the caller's Family and the resident must have an accepted, paid, not-yet-checked-out booking at that facility on the visit date. Prospective visits omit the elderly ID. Visitor count is an exact positive integer, including groups above three.

### Optional retry header (`Idempotency-Key`)

`POST /api/v1/family/care-home-visits` accepts an **optional** `Idempotency-Key` header that makes client retries safe:

- **Absent, blank, or whitespace-only header** → the legacy behavior, byte-identical to before the header existed: every legal create makes a new row; no key is stored. Existing clients need no change.
- **Same key + identical create content** (facility, kind, elderly, visitor name/phone, count, start time; names/phones compared trimmed) → the stored visit is returned with `200` instead of creating a second row. This holds both for sequential retries and for concurrent retries. The response reflects the visit's current persisted state (a replay after approval returns the approved state); the visit id is the replayed original.
- **Same key + any create-input difference** → `409 CareHomes.Visits.IdempotencyConflict`. Nothing is stored.
- Keys are **scoped per Family**: the same key sent by another Family is a different scope, not a conflict.
- Length bound: a trimmed key longer than 200 characters is rejected with `400 CareHomes.Visits.InvalidRequest` (the same ≤200 bound the partner-contact surface pins). The key is stored on the visit row and is never returned by any read.
- Reschedule, cancel, and decision routes do not accept or use the header.

The dedupe runs inside the per-facility create serialization, so the same-key retry of one facility replays the original create; a raced retry (same or different facility) is resolved by the outside-guard catch on the unique `(family_id, idempotency_key)` store index, which replays (200) or conflicts (409).

The eligible-residents read returns the same resident set the create path accepts, in deterministic order by Arabic name, then English name, then elderly id:
`{ "elderlyId": "...", "arabicName": "...", "englishName": "...", "careHomeBookingId": "..." }`. One booking per resident is returned (the deterministic first match after ordering by Arabic name, English name, elderly id, then booking id); other eligible bookings for the same resident are not returned.
A missing or malformed `facilityId` or `startsAt` is rejected by ASP.NET model binding as a framework `400`; only an all-zero `facilityId` (`00000000-…`) or a default `startsAt` (`0001-01-01…`) reaches the handler and returns `400 CareHomes.Visits.InvalidRequest`. The caller must belong to an active Family or the route returns `404`. A resident is eligible only when the caller's Family has an accepted, paid, not-yet-checked-out booking at that facility whose `StartDate <=` the Cairo-local date of `startsAt` `< EndDate`. The read filters by the requested facility only and does not re-assert that the facility is approved or publicly eligible; it returns the caller's own residents for that facility and discloses no other family's data. The create path additionally requires an approved, publicly eligible facility and returns `CareHomes.Discovery.NotFound` otherwise.

### Response shapes and OpenAPI parity

Every successful family or owner visit read or mutation returns the stored
visit object: `id`, `facilityId`, `familyId`, `kind` (`1` Prospective, `2`
Resident), nullable `elderlyId`, `visitorName`, `visitorPhone`,
`visitorCount`, UTC `startsAt`/`endsAt`, numeric `status`, nullable
`rescheduleOfVisitId`, `decisionExpiresAt`, nullable decision facts
(`decidedByUserId`, `decidedAt`, `decisionReason`), nullable cancellation
facts (`cancelledByUserId`, `cancelledAt`, `cancellationReason`), and
`createdAt`. All family and owner visit routes publish their typed success
schemas and failure statuses in `/openapi/v1.json`. Every `4xx` entry
documents the ProblemDetails schema — the generator's `[ApiController]`
client-error mapping adds it even to status-only annotations, and OpenAPI
has a single response object per status. OpenAPI therefore cannot express
the empty-body variants: at runtime the guards documented here answer some
family `400`/`404` cases with no body (a caller whose account resolves to no
active Family gets an empty `404` before the handler runs; a Prospective
create with an `elderlyId`, or a Resident create whose `elderlyId` is not in
the Family, answers a bodyless `400`/`404`), handler-scoped failures return
the coded ProblemDetails form, and the JWT challenge/policy denial answer
`401`/`403` with empty bodies even though the document shows ProblemDetails.
Consumers must accept both shapes on these statuses. The documented codes:
`400 CareHomes.Visits.InvalidRequest`, `404 CareHomes.Visits.NotFound`
(`CareHomes.Discovery.NotFound` when the public availability or create check
rejects the facility), and `409 CareHomes.Visits.InvalidState` /
`CareHomes.Visits.Conflict` / `CareHomes.Visits.CapacityConflict` /
`CareHomes.Visits.IdempotencyConflict` as ProblemDetails.

Availability slots use the `Africa/Cairo` time zone (Windows fallback `Egypt Standard Time`) and return UTC-offset timestamps plus remaining capacity. Slots must fit both the facility's operating hours and its family visiting windows; facility closure dates suppress all slots. Ambiguous or invalid Cairo-local DST times are omitted. Requests need at least 24 hours' notice. Pending requests hold visitor capacity and expire after 24 hours if the facility has not decided.

## Facility-owner routes

All routes require `CareHomeOwnerAccess` and are scoped to the owner's facility:

| Method and route | Purpose |
|---|---|
| `GET /api/v1/care-homes/facilities/mine/visit-settings` | Read versioned capacity, operating hours, visiting windows, and closure dates |
| `PUT /api/v1/care-homes/facilities/mine/visit-settings` | Replace schedule settings using `expectedVersion` |
| `GET /api/v1/care-homes/visits/mine` | List facility visits |
| `POST /api/v1/care-homes/visits/mine/{visitId}/decision` | Approve or reject a pending request; rejection requires a reason |
| `POST /api/v1/care-homes/visits/mine/{visitId}/cancel` | Cancel before the visit starts |

Settings requests contain `visitorCapacity`, arrays of `{ "day": 1, "startTime": "09:00", "endTime": "17:00" }` for `operatingHours` and `visitingWindows` (`day` is the numeric `DayOfWeek`, 1 = Monday; string names are rejected with 400), and closure entries `{ "date": "2026-11-10", "reason": "Holiday" }` (an over-long closure reason — the supplied value is measured **before** any trimming, at more than **500** characters — or a missing closure date makes the request invalid: the first save (no stored settings yet) answers 400 `CareHomes.Visits.InvalidRequest`, while a failed `Update` of existing settings answers 409 `CareHomes.Visits.Conflict`, the same mapped code a stale `expectedVersion` yields; trimming to ≤500 happens only afterwards when valid closures are normalized). The closure `reason` cap is measured on the raw value before trim and is published exactly as `maxLength: 500` on the request schema, so an over-long closure reason (raw >500) is refused with a plain 400 request-validation error before any state check — the domain rejects the identical set of values, so no previously accepted request changes outcome. A stale settings version still conflicts with 409 `CareHomes.Visits.Conflict`. Settings that would make an already-reserved future appointment invalid or exceed capacity are rejected. Failed writes leave settings and version unchanged. Owner settings reads return 200 with `version` 0 and capacity 0 before the first save.

The complete settings request also contains `expectedVersion`; for example:

```json
{
  "expectedVersion": 1,
  "visitorCapacity": 4,
  "operatingHours": [{ "day": 1, "startTime": "09:00", "endTime": "17:00" }],
  "visitingWindows": [{ "day": 1, "startTime": "10:00", "endTime": "16:00" }],
  "closures": []
}
```

Verified PORTAL-05 on an isolated HTTP host with real handlers: Draft and Approved facilities read and save settings; cross-owner settings stay isolated; capacity below 1 returns 400 `CareHomes.Visits.InvalidRequest`.

The owner decision request is `{ "approve": true, "reason": null }` to approve;
rejection sets `approve` to false and supplies a nonblank reason capped at
**1000** characters after trimming (an over-long rejection reason returns
`409 CareHomes.Visits.InvalidState`). The Postman
Family and Owner visit folders contain authored examples for these routes;
examples do not claim runtime execution.

When a reschedule is requested, the approved original remains active and continues to reserve capacity. Approval of the replacement changes both records in one transaction; rejection, expiry, or cancellation leaves the original appointment intact. Either the Family or facility owner may cancel until the appointment start time.

## Admin read (operational register)

The operational Admin register is read-only. It uses the same policy as the
sibling Admin Care Homes bookings read, `CareHomesOperationalAdmin`: Normal
`SuperAdmin (5)` or `SupportAdmin (7)` only. `ContentAdmin`, `FinanceAdmin`,
owners, Family accounts, caregivers and anonymous callers are denied
(`403`/`401`). There is **no** Admin scheduling, approval, rejection,
cancellation, reschedule or override route for visits; the family and owner
routes above stay the only mutation paths, and the Admin read never widens
`CareHomeOwnerAccess` or `FamilyAccess`.

| Method and route | Purpose |
|---|---|
| `GET /api/v1/admin/care-homes/visits` | Paged visit register |
| `GET /api/v1/admin/care-homes/visits/{visitId}` | Single visit with its recorded facts |

List query parameters: `facilityId`, `status`, `from`, `to`, `page`, `pageSize`.

- `facilityId` is an exact facility id.
- `status` is a numeric `CareHomeVisitStatus`.
- `from`/`to` are inclusive `YYYY-MM-DD` **UTC** days matched against the
  scheduled window: a visit is in range when its `endsAtUtc` is after the start
  of `from` and its `startsAtUtc` is before the end of `to`.
- `page` is clamped to at least `1` and `pageSize` defaults to `25` and is
  clamped to `1–100`; neither is an error.
- The only rejected inputs are an empty (`00000000-…`) `facilityId`, an
  undefined `status`, and an inverted window (`to` before `from`), each
  returning `400 CareHomes.Admin.InvalidQuery`.

The page is ordered newest-first by scheduled start and then visit id, and
returns `page`, `pageSize`, `totalCount` and `items`. Each item contains
`visitId`, `facilityId`, nullable `facilityName`, `familyId`, nullable `familyName`, nullable `elderlyId`, `kind`
(`1` Prospective, `2` Resident), `status` (`1` Pending, `2` Approved,
`3` Rejected, `4` FamilyCancelled, `5` FacilityCancelled, `6` Expired,
`7` ReschedulePending, `8` Rescheduled), `startsAtUtc`, `endsAtUtc`, nullable
`rescheduleOfVisitId`, the recorded decision facts (`decidedByUserId`,
`decidedOnUtc`, nullable `decisionReason`), the recorded cancellation facts
(`cancelledByUserId`, `cancelledOnUtc`, nullable `cancellationReason`) and
`createdOnUtc`.

The detail route returns the same fields plus `updatedOnUtc`. An unknown or
empty `visitId` returns `404 CareHomes.Visits.NotFound`.

The register deliberately omits `visitorName` and `visitorPhone` (visitor PII),
`visitorCount`, the requesting `familyUserId`, the pending-decision expiry
(`decisionExpiresOnUtc`) and the row `version`. It reads persisted facts only,
derives no event history or timeline, and mutates nothing.

## Persistence and verification limits

Migration `20261008154517_AddCareHomeVisits` adds `care_homes.visit_settings` and `care_homes.visits`, with schedule JSONB, capacity/version, lifecycle timestamps/actors, indexes, and facility/self-reschedule foreign keys. Additive migration `20261022121500_AddCareHomeVisitIdempotency` (predecessor: CareHomes head `20261020210910_AddCareHomeRatingModeration`) adds the nullable `care_homes.visits.idempotency_key` column (varchar(200)) and the unique `IX_visits_family_id_idempotency_key` index; legacy rows (null key) and reschedule rows stay outside the uniqueness via NULLS-DISTINCT semantics and read/act unchanged. Both migrations are generated files only — neither has been applied to any database, and no destructive reset or deployment is part of this change.

Postman examples are authored contracts, not evidence of runtime execution. Bruno was not run: no approved disposable API fixture state was prepared for visits. Focused unit tests cover visit lifecycle, Cairo slots, reservation capacity behavior, authorization metadata, and route contracts; database-backed capacity races and HTTP middleware/model binding remain unverified. The Admin operational read routes were authored with focused unit tests and an authored local HTTP adapter; their runtime HTTP proof is pending and is recorded in the F6 worker brief.
