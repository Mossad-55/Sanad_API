# Gapfix report 2 — manifest hygiene (verification only, no fixes)

- Branch: `opencode/room-enrichment-gapfix`
- Base: **`4e7f956`** (origin/main); verified range `4e7f956..88a4777`
- Files changed by this item: **none** (verification + this report)
- Method: `git diff 4e7f956..88a4777` per file, reviewed line-by-line

## Findings per supporting file

| File | Delta | Verdict |
|---|---|---|
| `Application/.../Bookings/CareHomeBookingCommands.cs` | +3 | **NOT mechanical — behavior change (stopped, see below)** |
| `Application/.../Discovery/CareHomeDiscovery.cs` | +14 | Additive ✓ — record gains `Amenities`/`CoverImageUrl`; handler adds one read-only `RoomTypeMedia` query and maps cover URL. Owner-approved (Phase 2 decision 4: touches Discovery + inventory DTOs). Public payload grows only; no existing field/order/eligibility altered. |
| `Infrastructure/.../Persistence/Configurations/CareHomeFacilityConfiguration.cs` | +18 | Additive ✓ — `ConfigureJson` private→internal; `room_types.amenities_json` mapping; rooms display-name mappings; two new entity configurations (`room_type_media`, `room_media`). No existing mapping removed. |
| `Infrastructure/.../Persistence/CareHomesDbContext.cs` + `Application/.../Abstractions/Data/ICareHomesDbContext.cs` | +2 / +2 | Additive ✓ — new `DbSet<CareHomeRoomTypeMedia>`, `DbSet<CareHomeRoomMedia>` only. |
| `Infrastructure/.../Migrations/CareHomeDbContextModelSnapshot.cs` | +125 | Additive ✓ — **0 removed lines** (verified programmatically). |
| `src/API/Sanad.API/ProblemDetail/ResultProblemDetailsMapper.cs` | +3 | Additive ✓ — new entries `MediaInvalid`=400, `MediaNotFound`=404, `RoomMediaRequired`=400; no existing mapping changed. |
| 3 test fakes: `tests/.../CareHomes/CareHomeBookingLifecycleTests.cs` | +12 | Fake-forwarders mechanical ✓ (`RoomTypeMedia`/`RoomMedia` → inner). The `PublishRoomMedia` helper + 4 call sites are **fixture companions of the stopped checkout gate below**, not an independent change. |
| 3 test fakes: `tests/.../CareHomes/CareHomeStayLifecycleTests.cs` | +4 | Mechanical ✓ — two fakes × same forwarder pair. |
| 3 test fakes: `tests/.../CareHomes/CareHomeDiscoveryTests.cs` | 1 line | Mechanical ✓ — `active.Update(...)` gains the new `[]` amenities argument (signature adaptation). |

## STOPPED item (behavior change — not fixed, per orders)

- **Path:** `src/Modules/CareHomes/Application/Sanad.Modules.CareHomes.Application/Bookings/CareHomeBookingCommands.cs`
- **Lines (current tip):** **54–56** in `CheckoutHandler` — loads
  `RoomTypeMedia`, evaluates `RoomTypeBookability.HasRequiredMedia`, and
  short-circuits checkout with `CareHomes.Inventory.RoomMediaRequired` (400).
- **Effect:** checkout hold-creation now fails when the room type lacks
  cover + ≥1 gallery photo — a real behavior change to the booking flow, not a
  mechanical/additive edit.
- **Context:** this is the Phase 1 plan-specified hold-gate (live-proven in the
  Phase 1 gate), but its placement at hold-creation vs the plan's
  publish-transition wording is exactly decision item **3c**; companion
  fixture edits are in `CareHomeBookingLifecycleTests.cs` (`PublishRoomMedia`
  helper ~line 754; call sites ~47, 681, 708, 745). No silent fix made; routed
  to the owner decision request in `gapfix-decision-requests.md`.

## Also confirmed (scope item 1c)

Legacy HC-023 Postman requests without descriptions (`Read my inventory`,
`Create bed`, `Rename bed` in folder "04") — left untouched, out of scope.

## Gates

| Gate | Command | Result |
|---|---|---|
| Whitespace | `git diff --check` | exit 0 |
| File delta totals | `git diff --stat 4e7f956..88a4777` | matched the expected 7 supporting files + 3 fakes |

## Limitations

- Review-only item; no files changed. The stopped gate finding is reported for
  the owner decision, not remediated.
