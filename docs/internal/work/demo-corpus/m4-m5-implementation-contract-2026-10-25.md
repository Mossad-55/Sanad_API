# M4–M5 implementation contract (pinned 2026-10-25)

Companion to the active brief
[full-demo-corpus-2026-10-25.md](full-demo-corpus-2026-10-25.md), which owns
scope, task state and acceptance. This file records the reviewed design so the
M4–M5 worker does not re-derive it. Scope approval is not release approval.

Investigation session: `ses_e8bfdb7aeffeGmASgAWQMSc1p4`. Correction ownership:
Mastermind accepts the defaults below; a worker that finds a genuine conflict
reports it instead of inventing policy.

## M4 — optional `Idempotency-Key` on four creates

Routes (exactly these, no others):

1. `POST /api/v1/family/bookings/checkout` — Families context.
2. `POST /api/v1/family/care-home-bookings/checkout` — CareHomes context.
3. `POST /api/v1/family/dependents/{dependentId}/care-records` — Families.
4. `POST /api/v1/caregiver/care-records` — Families.

### Shared kit (new, `Sanad.BuildingBlocks.Application/Idempotency/`)

- `IdempotencyKey(string Value)` + `TryParse(string? raw, out key, out error)`:
  absent/whitespace → `(null, null)`; trim; empty or > 128 → error; every char
  `0x20..0x7E` else error. `MaximumLength = 128`.
- `IdempotencyFingerprint.Compute(string operation, IEnumerable<KeyValuePair<string,string?>> fields)`:
  ordered `key\0value\0…`, UTF-8, SHA-256, lowercase hex; `null` → `<null>`;
  callers trim values. GUID `D` lowercase, dates `yyyy-MM-dd`, times `HH:mm`,
  enums by name, decimals invariant.
- `IdempotencyRequest(IdempotencyKey Key, string Fingerprint)`.
- `IdempotentOutcome<T>(T Value, int StatusCode, string? ReplayBody)` — non-null
  `ReplayBody` means replay-as-is.
- `IIdempotentResponseSnapshotter.Snapshot<T>(T response)`.

Fingerprint fields, in order (server-derived inputs such as `UtcNow`,
`CurrentDate`, correlation id and the care-home intake resolution are excluded):

| Operation constant | Fields |
|---|---|
| `family.bookings.checkout` | `elderlyId, caregiverId, shiftType, bookingDate, startTime, endTime, serviceAddress, specialInstructions, bookingAreaId, bookingServiceId` |
| `family.care-home-bookings.checkout` | `elderlyId, facilityId, roomTypeId, requestedStartDate, careNeedsNotes, responsibleContactName, responsibleContactPhone, responsibleContactRelationship` |
| `family.care-records.create` | `dependentId` (route), `hospitalName, visitDate, details` |
| `caregiver.care-records.create` | `dependentId` (body), `hospitalName, visitDate, details` |

API edge: `src/API/Sanad.API/Idempotency/MvcIdempotentResponseSnapshotter.cs`
over MVC `JsonOptions` (register in `src/API/Sanad.API/DependencyInjection.cs`
next to the other gateways). `ApiControllerBase` gains `IdempotentCreated` /
`IdempotentOk`: fresh → normal result; replay → `ContentResult` with the stored
body, `application/json; charset=utf-8`, stored status. No `isReplay` field on
the wire.

Parity rule: absent header = byte-identical current behavior. Key parsing
happens once in each controller; invalid → coded 400. Rejected attempts
(400/403/404/409) never write a row, so the key stays retryable.

### Storage and flow

Per writing context, `idempotency_requests` with `id` (PK), `actor_id`,
`operation` varchar(64), `idempotency_key` varchar(128), `fingerprint` char(64),
`resource_id` uuid null, `response_status`, `response_body` text (exact wire
JSON — not `jsonb`), `created_on_utc`; unique
`(actor_id, operation, idempotency_key)`, index on `created_on_utc`. No FK to
the business row. Entity + `IEntityTypeConfiguration` per module; DbSet added to
`IFamiliesDbContext`/`FamiliesDbContext` and `ICareHomesDbContext`/`CareHomesDbContext`.
`actor_id` is the `UserId`, not the family.

Handler order (all four): normalize → **authorization** (and
`PlatformControlGate` for checkouts) → idempotency lookup → replay (stored
status+body, no new hold/record/audit/provider call) or coded 409 on fingerprint
mismatch → existing business path with the idempotency row added in the **same**
`SaveChangesAsync`.

Concurrency: unique index decides; on `DbUpdateException` with SQLSTATE `23505`
detach the added business entity/entities and the idempotency row, re-read the
winner, replay or 409. Add a shared
`PostgresErrors.IsUniqueViolation(DbUpdateException, string? expectedIndex)`
(check `PostgresException.SqlState`, keep a message fallback); do **not** copy
the visits catch that matches any unique violation. Care-home checkout does the
lookup inside its existing serializable + advisory-lock guard (a pre-lock
snapshot can miss the winner); its unique-violation catch sits outside the guard.

Known behavior to document, not change: the caregiver care-record replay
re-runs the booking-window authorization, so a replay after the window closed
answers `403 Families.CareRecords.AccessDenied`; a replay is historical, not a
new offer; platform maintenance does not 503 a confirmed replay.

Codes (add explicitly to `ResultProblemDetailsMapper`; unmapped codes fall back
to 400):

| Code | Status |
|---|---|
| `Bookings.IdempotencyKeyInvalid` | 400 |
| `Bookings.IdempotencyConflict` | 409 |
| `CareHomes.Bookings.IdempotencyKeyInvalid` | 400 |
| `CareHomes.Bookings.IdempotencyConflict` | 409 |
| `Families.CareRecords.IdempotencyKeyInvalid` | 400 |
| `Families.CareRecords.IdempotencyConflict` | 409 |
| `Caregivers.Availability.InvalidQuery` | 400 (M5) |
| `Bookings.AvailabilityConflict` | 409 (M5) |

Retention is documentation-only: no expiry logic in code, no purge worker;
`created_on_utc` is indexed for a future purge.

### Per-endpoint anchors (current code)

- Family bookings checkout: `FamilyBookingsController.CreateCheckout`
  (`src/API/Sanad.API/Controllers/FamilyBookingsController.cs:110-144`), handler
  `CreateBookingCheckoutCommandHandler`
  (`src/Modules/Families/Application/.../Bookings/BookingCommands.cs:58-252`,
  single `SaveChangesAsync` at ~237, no explicit transaction, no provider call).
- Care-home checkout: `FamilyCareHomeBookingsController.Checkout`
  (`.../Controllers/FamilyCareHomeBookingsController.cs:31-34`), handler
  `CheckoutHandler`
  (`src/Modules/CareHomes/Application/.../Bookings/CareHomeBookingCommands.cs:65-128`)
  inside `CareHomeBookingReservationGuard` (serializable + advisory lock).
- Family care records: `FamilyCareRecordsController.Create`
  (`.../Controllers/FamilyCareRecordsController.cs:90-113`), handler
  `CreateFamilyCareRecordCommandHandler`
  (`src/Modules/Families/Application/.../CareRecords/FamilyCareRecords.cs:160-224`).
- Caregiver care records: `CaregiverCareRecordsController.Create`
  (`.../Controllers/CaregiverCareRecordsController.cs:142-173`), handler
  `CreateCaregiverCareRecordCommandHandler`
  (`.../CareRecords/CaregiverCareRecords.cs:250-310`).

Existing idempotency precedents (`CareHomeVisitHandlers` L184-194/262-288,
`SubmitPartnerContactCommandHandler`) are patterns only — no fingerprint and no
stored response — so they are not reused for M4.

## M5 — date-specific caregiver availability

### Endpoint (additive)

```
GET /api/v1/caregivers/{caregiverId:guid}/availability/day?date=YYYY-MM-DD&shiftType=EightHourShift
200 CaregiverDayAvailabilityResponse · 400 Caregivers.Availability.InvalidQuery
404 Caregivers.NotFound · 401/403 by the existing family audience policy
```

```csharp
public sealed record CaregiverDayAvailabilityInterval(DateTimeOffset StartsAt, DateTimeOffset EndsAt);
public sealed record CaregiverDayAvailabilityResponse(
    Guid CaregiverId, DateOnly Date, string TimeZoneId,
    BookingShiftType ShiftType, IReadOnlyList<CaregiverDayAvailabilityInterval> Intervals);
```

Add to the existing `CaregiverAvailabilityController`; the weekly action and its
DTOs stay unchanged. `StartsAt`/`EndsAt` are UTC offsets (`TimeSpan.Zero`),
`TimeZoneId` is the documented `Africa/Cairo`, conversion uses the resolved
`TimeZoneInfo` (IANA id, `"Egypt Standard Time"` fallback) in the same shape as
`CareHomeVisitScheduleRules.ResolveCairoTimeZone`.

### Declared window mapping (one rule set)

`BookingShiftType` → declared windows by duration/kind: `HomeVisit` →
`HomeVisitWindows`; `EightHourShift` → 8 h medical shifts /
`CompanionBookingType.EightHourDay`; `TwelveHourShift` → 12 h medical shifts /
companion `Overnight` (20:00→08:00); `TwentyFourHourShift` →
`TwentyFourHourLiveIn`; `Hourly` → companion `Hourly`. Fixed-duration shifts use
the stored `StartTime` + `Duration` (existing `EndsNextDay` already covers
cross-midnight). A day cannot mix shift and home-visit modes.

- Caregiver `Status != Active` or `Availability != Available` → empty intervals
  (200, not 404). No date-specific leave entity is invented.
- Busy windows come through a new port
  `ICaregiverBusyBookingLookup.GetBusyWindowsAsync(caregiverId, date)` implemented
  in `src/API/Sanad.API/CaregiversIntegration/` over `IFamiliesDbContext.Bookings`
  with **exactly** the checkout predicate: statuses
  `PendingPayment | PendingCaregiverApproval | Confirmed | InProgress` and
  `BookingDate == date && start < busy.EndTime && end > busy.StartTime`.
- Fixed-duration shifts drop the whole interval on overlap; `Hourly`/`HomeVisit`
  subtract the overlapping portion. No hold/expiry logic (caregiver bookings
  have only `AcceptanceDeadlineUtc`, an acceptance deadline, not availability).
- Mirror the existing cross-midnight gap of the overlap predicate exactly and
  document it; do not fix it in this slice.
- DST: omit an interval whose boundary falls in a spring-forward gap; resolve an
  ambiguous fall-back boundary to the standard offset deterministically. Stored
  UTC timestamps are never recomputed or relabeled.

### Shared checkout check

Ports in Families.Application: `ICaregiverAvailabilityRules.IsAvailableAsync(...)`
(implemented in `src/API/Sanad.API/CaregiversIntegration/` via a MediatR query
that calls the same private rule function) and
`ICaregiverBookingCheckoutGuard.ExecuteAsync<T>(...)` implemented in
`Families.Infrastructure/Bookings/CaregiverBookingReservationGuard.cs`
(serializable transaction + advisory lock on
`"{caregiverId:N}:{date:yyyy-MM-dd}"`, `40001`/`40P01` → `Bookings.ScheduleConflict`).

Checkout: exact containment of `[startTime, endTime)` in one returned interval;
outside declared availability → new `Bookings.AvailabilityConflict` (409);
existing overlap check moves inside the guard; caregiver-not-found stays 404.

## Files

New — shared/API: `IdempotencyKey.cs`, `IdempotencyFingerprint.cs`,
`IdempotencyRequest.cs`, `IdempotentOutcome.cs`,
`IIdempotentResponseSnapshotter.cs` (BuildingBlocks.Application/Idempotency),
`BuildingBlocks.Infrastructure/Persistence/PostgresErrors.cs`,
`Sanad.API/Idempotency/MvcIdempotentResponseSnapshotter.cs`,
`Sanad.API/CaregiversIntegration/ApiCaregiverBusyBookingLookup.cs`,
`Sanad.API/CaregiversIntegration/CaregiverAvailabilityRulesGateway.cs`.

New — Families: `Domain/Idempotency/IdempotencyRequest.cs`,
`Infrastructure/Persistence/Configurations/IdempotencyRequestConfiguration.cs`,
`Infrastructure/Bookings/CaregiverBookingReservationGuard.cs`,
`Application/Abstractions/Caregivers/ICaregiverAvailabilityRules.cs`,
`Application/Abstractions/Bookings/ICaregiverBookingCheckoutGuard.cs`.

New — CareHomes: the idempotency entity + configuration twins.

New — Caregivers: `Availability/CaregiverDayAvailability.cs`,
`Availability/GetCaregiverDayAvailabilityQueryHandler.cs`,
`Availability/CheckCaregiverAvailabilityQuery.cs`,
`Availability/CaregiverDayAvailabilityRules.cs`,
`Availability/CaregiverAvailabilityTimeZone.cs`,
`Abstractions/Data/ICaregiverBusyBookingLookup.cs`.

Changed: the four controllers (+ optional header, fingerprint, replay helper),
`CaregiverAvailabilityController` (day action), `ApiControllerBase` (replay
helpers), `ResultProblemDetailsMapper` (eight codes), `DependencyInjection`
(three gateway registrations), `IFamiliesDbContext`/`FamiliesDbContext`,
`ICareHomesDbContext`/`CareHomesDbContext`, `BookingCommands.cs`,
`CareHomeBookingCommands.cs`, `FamilyCareRecords.cs`, `CaregiverCareRecords.cs`,
existing checkout test construction sites (add inline guard +
stub availability rules), consumer docs and Postman collections.

Migrations: one per writing context — `families.idempotency_requests` and
`care_homes.idempotency_requests` (plus indexes). No Caregivers migration (read
only). History tables are already per-schema, so the two do not collide.

## Tests

`Sanad.UnitTests/BuildingBlocks/IdempotencyKeyTests.cs` and
`IdempotencyFingerprintTests.cs`; `Families/Idempotency/` checkout, both care
record creates, and a concurrency/loser-replay fact;
`CareHomes/CareHomeBookingCheckoutIdempotencyTests.cs` (in-guard replay, no
second hold); `Caregivers/CaregiverDayAvailabilityTests.cs` (mapping matrix,
unavailable → empty, cross-midnight, DST gap, busy subtraction);
`Families/Bookings/BookingCheckoutAvailabilityTests.cs`;
`API/IdempotencyHeaderContractTests.cs`, plus the existing OpenAPI and portal
controller contract tests; schema assertions for both migrations.

## Acceptance

- Absent header behaves exactly as today; replay returns the stored status and
  body with no second hold/record/audit/provider call; changed fingerprint is
  409; concurrent duplicates create exactly once.
- Availability matches declared windows minus busy windows with `Africa/Cairo`
  and UTC instants; checkout rejects outside availability with
  `Bookings.AvailabilityConflict` and stays conflict-safe under the guard.
- Zero warnings/errors on the strict Release build; focused suites green;
  Postman valid and controller↔Postman parity intact; `git diff --check` clean.
- No commit/push/deploy/remote/database write.
