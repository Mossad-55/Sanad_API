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

Settings requests contain `visitorCapacity`, arrays of `{ "day": "Monday", "startTime": "09:00", "endTime": "17:00" }` for `operatingHours` and `visitingWindows`, and closure entries `{ "date": "2026-11-10", "reason": "Holiday" }`. A stale settings version conflicts. Settings that would make an already-reserved future appointment invalid or exceed capacity are rejected.

The complete settings request also contains `expectedVersion`; for example:

```json
{
  "expectedVersion": 1,
  "visitorCapacity": 4,
  "operatingHours": [{ "day": "Monday", "startTime": "09:00", "endTime": "17:00" }],
  "visitingWindows": [{ "day": "Monday", "startTime": "10:00", "endTime": "16:00" }],
  "closures": []
}
```

The owner decision request is `{ "approve": true, "reason": null }` to approve;
rejection sets `approve` to false and supplies a nonblank reason. The Postman
Family and Owner visit folders contain authored examples for these routes;
examples do not claim runtime execution.

When a reschedule is requested, the approved original remains active and continues to reserve capacity. Approval of the replacement changes both records in one transaction; rejection, expiry, or cancellation leaves the original appointment intact. Either the Family or facility owner may cancel until the appointment start time.

## Persistence and verification limits

Migration `20261008154517_AddCareHomeVisits` adds `care_homes.visit_settings` and `care_homes.visits`, with schedule JSONB, capacity/version, lifecycle timestamps/actors, indexes, and facility/self-reschedule foreign keys. It is generated and has not been applied to any database. No destructive reset or deployment is part of this change.

Postman examples are authored contracts, not evidence of runtime execution. Bruno was not run: no approved disposable API fixture state was prepared for visits. Focused unit tests cover visit lifecycle, Cairo slots, reservation capacity behavior, authorization metadata, and route contracts; database-backed capacity races and HTTP middleware/model binding remain unverified.
