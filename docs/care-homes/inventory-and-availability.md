# Care Homes inventory and availability

HC-TASK-020 adds owner managed inventory for a facility. These routes are under
`/api/v1/care-homes/inventory` and require the authenticated account to be the
`CareHomeOwner` who owns the facility. The response for inventory mutations is
the complete `CareHomeInventoryDto`: `facilityId`, the optimistic concurrency
`version`, and arrays named `roomTypes`, `rooms`, `beds`, and `maintenance`.

The additive Care Homes inventory migration and its disposable-fixture evidence
are recorded in the task checklist. The examples below describe the source
contract; this page does not claim execution of a test, build, or Bruno gate.

## Calendar-month stay period

`CareHomeCalendarPeriod.CalculateEndDate` calculates one calendar month from a
start date. It returns the same day in the next calendar month when that day
exists, and otherwise clamps the result to the final day of the next month:

- January 31 returns February 28 in a non-leap year and February 29 in a leap
  year; March 31 returns April 30.
- A date that exists in the next month, such as October 15, returns the 15th of
  the next month.
- A December start crosses the year boundary while preserving the calendar day.

This helper does not define booking interval inclusivity, booking or payment
behavior, extensions, or renewal. Care Homes stays do not renew automatically;
those lifecycle and payment contracts remain with their assigned tasks.

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

Query the owner's availability with `POST /mine/availability` or an Admin
operator's availability with `POST /admin/{facilityId}/availability`:

```json
{ "startDate": "2027-01-01", "endDate": "2027-01-20" }
```

Each result contains `roomTypeId`, `availableRooms`, `availableBeds`,
`totalRooms`, and `totalBeds`. Archived assets, maintenance blocks, and
overlapping occupancy are excluded. Shared allocation consumes beds; Private
and Suite allocation consume whole rooms. The source exposes
`ICareHomeOccupancyProvider`; its current implementation is empty until the
later booking slices supply active hold/stay intervals. Booking creation and
facility room/bed assignment remain HC-TASK-032/034.

Authentication failures are `401`; accounts outside the owner policy receive
`403`. Unknown facilities or assets return the relevant inventory not-found
result.
