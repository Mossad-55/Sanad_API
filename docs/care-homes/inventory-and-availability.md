# Care Homes inventory and availability

HC-TASK-020 adds owner managed inventory for a facility. These routes are under
`/api/v1/care-homes/inventory` and require the authenticated account to be the
`CareHomeOwner` who owns the facility. The response for inventory mutations is
the complete `CareHomeInventoryDto`: `facilityId`, the optimistic concurrency
`version`, and arrays named `roomTypes`, `rooms`, `beds`, and `maintenance`.

The inventory, assignment, maintenance, and availability implementation is
committed on `main`. The checklist records the completed focused/full test and
disposable Bruno evidence. Fresh-database migration application was not part of
the HC-023 closeout and remains explicitly unverified; it is not a current
blocker. This guide describes the API contract and is not itself runtime evidence.

## Read and manage inventory

`GET /api/v1/care-homes/inventory/mine` returns the owner's current inventory.

Room types use `POST /mine/room-types` and `PUT /mine/room-types/{id}`:

```json
{
  "expectedVersion": 1,
  "arabicName": "غرفة مشتركة",
  "englishName": "Shared room",
  "monthlyPriceEgp": 10000,
  "allocationMode": 1,
  "arabicDescription": "",
  "englishDescription": "Shared accommodation"
}
```

`allocationMode` is `1` Shared, `2` Private, or `3` Suite. The monthly price
is in EGP. Archive or restore a room type with
`POST /mine/room-types/{id}/archive?expectedVersion=1&restore=false`.

Create a room with `POST /mine/rooms`:

```json
{ "expectedVersion": 2, "roomTypeId": "<room-type-id>", "roomNumber": "101" }
```

Rename it with `PUT /mine/rooms/{id}` using the same request shape, and archive
or restore it with `POST /mine/rooms/{id}/archive?expectedVersion=2&restore=false`.
Room numbers are unique within a facility. A room number can be changed only
when the room and its beds have no active hold or stay. Archiving retains the
stable asset ID.

Create or rename a bed with `POST /mine/beds` or `PUT /mine/beds/{id}`:

```json
{ "expectedVersion": 3, "roomId": "<room-id>", "label": "A" }
```

Archive or restore a bed with
`POST /mine/beds/{id}/archive?expectedVersion=3&restore=false`.

All owner mutations require `expectedVersion`; stale versions return the
standard conflict result. Invalid input returns a Care Homes inventory error,
including `CareHomes.Inventory.Invalid`, `InvalidTarget`, `RoomNumberTaken`,
`ActiveOccupancy`, or `InvalidRange` as applicable.

## Maintenance and derived availability

Create a maintenance block with `POST /mine/maintenance`:

```json
{
  "expectedVersion": 4,
  "target": 2,
  "targetId": "<bed-id>",
  "startDate": "2027-01-10",
  "endDate": "2027-01-12",
  "reason": "Planned repair"
}
```

`target` is `1` for a room or `2` for a bed. Dates are Egypt-local calendar
dates with a start-inclusive, end-exclusive range. The end date must be after
the start date. A block is rejected when it overlaps an active hold or stay,
or an existing block on the same room/bed relationship. A room block also
conflicts with a block on one of its beds, and vice versa.

Owners may amend a block with `PUT /mine/maintenance/{maintenanceId}` using
`{ "expectedVersion": n, "startDate": "YYYY-MM-DD", "endDate": "YYYY-MM-DD", "reason": "..." }`,
or cancel it with `POST /mine/maintenance/{maintenanceId}/cancel` and
`{ "expectedVersion": n }`. Only blocks whose existing start date is strictly
after the current Cairo-local date may be amended/cancelled, and an amended
start date must also remain in the future. Blocks starting today or earlier are
immutable. Both operations recheck the facility version, active occupancy and
room/bed maintenance overlaps. Cancellation is retained in inventory readback
with `isCancelled`, `cancelledOnUtc`, and `cancelledBy`; it is not a hard delete.
Cancelled blocks no longer reduce availability. Invalid date ranges return
`400 CareHomes.Inventory.InvalidRange`; a block that is no longer eligible
returns `409 CareHomes.Inventory.InvalidState`; occupancy and maintenance
overlaps return stable `409` inventory conflict codes.

Query the owner's availability with `POST /mine/availability` or an Admin
operator's availability with `POST /admin/{facilityId}/availability`:

```json
{ "startDate": "2027-01-01", "endDate": "2027-01-20" }
```

Each result contains `roomTypeId`, `availableRooms`, `availableBeds`,
`totalRooms`, and `totalBeds`. Archived assets, maintenance blocks, and
overlapping occupancy are excluded. Shared allocation consumes beds; Private
and Suite allocation consume whole rooms. The registered
`CareHomeBookingOccupancyProvider` supplies active checkout holds, paid
decision holds, and accepted stays from Care Homes bookings. Before physical
assignment, occupancy is counted against the room type; after assignment,
Shared stays occupy their bed and Private/Suite stays occupy their room. An
actual check-out shortens occupancy to its Egypt-local calendar date when
applicable. The inventory API consumes these intervals for owner/Admin
availability and maintenance-conflict checks. HC-023 transfers append
date-effective assignment history; the provider emits occupancy segments on
both sides of each Cairo effective date rather than treating a future move as
already current. Transfer destination conflicts are serialized by the existing
facility/room-type reservation guard. Booking creation remains owned by
HC-TASK-032.

Authentication failures are `401`; accounts outside the owner policy receive
`403`. Unknown facilities or assets return the relevant inventory not-found
result.
