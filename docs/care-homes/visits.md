# Care-home visits

Care-home visits are separate from care-giver home-visit bookings. They are free, one-hour appointments for a prospective Family or for an elderly resident currently staying at the selected facility.

## Family and public routes

| Method and route | Access | Purpose |
|---|---|---|
| `GET /api/v1/care-homes/facilities/{facilityId}/visit-availability?date=YYYY-MM-DD&visitorCount=n` | Anonymous; only publicly eligible facilities are returned | Lists available Cairo-local one-hour slots and remaining visitor capacity |
| `GET /api/v1/family/care-home-visits` | `FamilyAccess` | List visits for the caller's active Family |
| `POST /api/v1/family/care-home-visits` | `FamilyAccess` | Request a visit |
| `GET /api/v1/family/care-home-visits/{visitId}` | `FamilyAccess` | Read a visit owned by the caller's Family |
| `POST /api/v1/family/care-home-visits/{visitId}/cancel` | `FamilyAccess` | Cancel before the visit starts |
| `POST /api/v1/family/care-home-visits/{visitId}/reschedule` | `FamilyAccess` | Ask for a new slot; the facility must approve it |

The create request includes `facilityId`, `kind` (`Prospective` or `Resident`),
`elderlyId` (null for prospective visits), `visitorName`, `visitorPhone`, an
exact `visitorCount`, and a UTC-offset `startsAt`. A resident visit requires an
elderly ID belonging to the active Family and an eligible accepted, paid stay
at that facility on the visit date. Reschedule accepts
`{ "startsAt": "2026-11-12T08:00:00Z" }`. Family and owner cancellation accept
`{ "reason": "Schedule changed" }`; the reason may be null. Each family read
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

Settings requests contain `visitorCapacity`, arrays of `{ "day": 1, "startTime": "09:00", "endTime": "17:00" }` for `operatingHours` and `visitingWindows` (`day` is the numeric `DayOfWeek`, 1 = Monday; string names are rejected with 400), and closure entries `{ "date": "2026-11-10", "reason": "Holiday" }`. A stale settings version conflicts with 409 `CareHomes.Visits.Conflict`. Settings that would make an already-reserved future appointment invalid or exceed capacity are rejected. Failed writes leave settings and version unchanged. Owner settings reads return 200 with `version` 0 and capacity 0 before the first save.

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
rejection sets `approve` to false and supplies a nonblank reason. The Postman
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
`visitId`, `facilityId`, `familyId`, nullable `elderlyId`, `kind`
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

Migration `20261008154517_AddCareHomeVisits` adds `care_homes.visit_settings` and `care_homes.visits`, with schedule JSONB, capacity/version, lifecycle timestamps/actors, indexes, and facility/self-reschedule foreign keys. It is generated and has not been applied to any database. No destructive reset or deployment is part of this change.

Postman examples are authored contracts, not evidence of runtime execution. Bruno was not run: no approved disposable API fixture state was prepared for visits. Focused unit tests cover visit lifecycle, Cairo slots, reservation capacity behavior, authorization metadata, and route contracts; database-backed capacity races and HTTP middleware/model binding remain unverified. The Admin operational read routes were authored with focused unit tests and an authored local HTTP adapter; their runtime HTTP proof is pending and is recorded in the F6 worker brief.
