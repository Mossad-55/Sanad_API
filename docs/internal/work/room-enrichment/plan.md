# Room enrichment (per-room marketing content) — frozen plan (v1, read-only)

Owner-ordered 2026-10-12 from the room-update mock audit. Nothing here
authorizes code, migration applies, seeds, provider calls, or runtime.
Implementation starts only on explicit owner order per phase batch.

## 0. Golden rule

Write marketing content once (room type), override/display per room, compute
counts live. No duplicated copy-paste across 12 rooms; no editable totals.

## 1. Scope (closed — new fields need a new decision)

- Room type gains: amenities set (bilingual items, ≤50), photo collection
  (cover + gallery, same media rules as facility: jpeg/png, 5 MB,
  private storage, cleanup on failure).
- Room gains: optional display-name override (bilingual, falls back to type
  name); optional per-room photos (same media rules, min 0).
- Totals (total/available rooms/beds) stay computed, read-only, served inside
  existing inventory responses — no new endpoints for them.
- Type completeness rule (mirrors the facility cover+2 rule): a room type
  becomes bookable only with cover + ≥1 gallery photo; enforced at checkout
  hold creation with dedicated `400 CareHomes.Inventory.RoomMediaRequired`.

## 2. Endpoint catalog (frozen contract)

Owner (`CareHomeOwnerAccess`), under `api/v1/care-homes/inventory`:

| # | Verb + route | Purpose | Success | Failures |
|---|---|---|---|---|
| 1 | `PUT /room-types/{id}` | Update names/descriptions/price/mode + amenities set | 200 | 400 validation / 404 / 409 active-occupancy (mode change only) |
| 2 | `POST /room-types/{id}/media` | Upload type photo (multipart, kind cover/gallery) | 201 | 400 content/size |
| 3 | `DELETE /room-types/{id}/media/{mediaId}` | Remove type photo | 204 | 404 |
| 4 | `PUT /rooms/{id}` | Rename number + display-name override + change type | 200 | 400 / 404 / 409 active-occupancy (number only) |
| 5 | `POST /rooms/{id}/media` | Upload room photo (multipart) | 201 | 400 content/size |
| 6 | `DELETE /rooms/{id}/media/{mediaId}` | Remove room photo | 204 | 404 |

Public/family read surfaces gain resolved display names + type amenities +
cover URLs (additive fields only, same privacy posture as facility media).

## 3. Business rules and cases

1. Type amenities: bilingual items ≤50, no nulls (facility precedent verbatim).
2. Type media: 1 cover (position 0) + ≤10 gallery; room media: same caps, min 0.
3. Bookability gate: type needs cover + ≥1 gallery before its rooms accept holds;
   code `CareHomes.Inventory.RoomMediaRequired`, checked at hold creation.
4. Totals/availability remain computed; any client-posted totals are ignored.
5. Display-name override falls back to type name; blank clears the override.
6. Number/type changes keep existing active-occupancy guards byte-for-byte.

Cases: override fallback/clear, amenities validation bounds, media content/size
rejections, bookability gate with 0/1/cover-only/gallery-only states, occupancy
guards unchanged, readback shapes with resolved names + covers.

## 4. File manifest (exclusive)

- `Modules/CareHomes/Domain/.../CareHomeInventory.cs` (type amenities + media
  entities; room display-name override) — domain only, no other aggregates.
- `Modules/CareHomes/Application/.../Inventory/` (update/upload/remove handlers
  + hold-gate check) + `Modules/CareHomes/Infrastructure/` (configs + **one
  additive migration, authored only**) + `ICareHomesDbContext` sets.
- `Sanad.API/Controllers/CareHomeInventoryController.cs` (endpoints 1–6) + requests.
- `tests/.../CareHomes/CareHomeRoomEnrichmentTests.cs` (+ SQLite twins).
- `docs/care-homes/inventory-and-availability.md` deltas + Postman care-homes
  collection additions + JSON validation.
- `docs/internal/work/room-enrichment/{tasks.md,handoff.md,reports/}` tracking.

## 5. Verification (incl. live API)

Per phase: zero-warning build; focused tests (InMemory + SQLite twins);
docs + Postman synced; diff-check; contract-map 0/0. Joint gate: full unit +
arch once. **Live gate per phase** on the disposable target: real multipart
uploads, bookability gate 400→200 transitions, readback shapes; sessions
revoked; API stopped. Mutating live checks need per-run approval.

## 6. Decisions recorded (locked 2026-10-12)

Type-level amenities/photos with room display-name override (no per-room
description duplication); totals computed read-only; cover+1 completeness gate;
no per-bed marketing content (labels only, unchanged).
