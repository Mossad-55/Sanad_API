# Phase 2 report — room display override, room photos, resolved readback

## Identity

- Branch: `opencode/room-enrichment`
- Base: **`4e7f956`** (origin/main, unchanged)
- Phase 0 (frozen package): `759a5d2`
- Phase 1: `cea3300` + live-gate fixes `a861763` + tracking `e253716`
- Phase 2 (deliverables): **`90ca26f`** — *Add room display override, room photos, and richer readback (Phase 2)* (17 files, +768/−18)
- Final deliverable SHA: **`90ca26f`** (this tracking file is committed immediately after as the branch tip; see `git log`).

## Owner decisions applied (approved plan, decisions 1–5)

1. Occupancy-guard message stays byte-for-byte but the guard fires **only when
   the trimmed room number actually changes** — display/type-only updates on an
   occupied room return 200; a number change returns
   **409 `CareHomes.Inventory.ActiveOccupancy`**.
2. `roomTypeId` omitted or `Guid.Empty` = no change; a non-empty value must be a
   non-archived same-facility type, else **400 `CareHomes.Inventory.InvalidTarget`**
   ("Room type was not found or is archived.").
3. **Single migration** — Phase 2 DDL merged into the existing Phase 1 migration
   `20261012143753_AddCareHomeRoomTypeAmenitiesAndMedia.cs` (no second migration);
   model snapshot regenerated (pure additions, LF + UTF-8 BOM preserved).
4. Touches `CareHomeDiscovery.cs` + inventory DTOs only; no family booking DTO changes.
5. `coverUrl` points at a non-existent GET file route — **left as-is** per owner
   decision; serving gap documented below and live-confirmed (404).

## Delivered

Endpoints (frozen contract, owner access, `mine/` prefix):

4. `PUT /rooms/{id}` — extended (backward-compatible optional params): rename +
   **display override** (`arabicDisplayName`/`englishDisplayName`) + optional
   type change. Both display languages must be set together or cleared together;
   both blank → clear → resolved falls back to type name; mixed →
   **400 `CareHomes.Inventory.Invalid`**; explicit JSON nulls = omitted (no
   change); max 200 chars.
5. `POST /rooms/{id}/media` — multipart upload; 1 cover (position 0) + ≤10
   gallery; jpeg/png, ≤5 MB, signature-checked, private storage; **201**.
6. `DELETE /rooms/{id}/media/{mediaId}` — **204**; `404 MediaNotFound`,
   `400 MediaInvalid`; gallery positions re-ordered.

Plus: domain `CareHomeRoomMedia` + `CareHomeRoom.SetDisplayName`, `room_media`
table + `rooms.arabic_display_name`/`english_display_name` columns (single
extended migration), room readback gains raw override + `ResolvedArabicName`/
`ResolvedEnglishName` + `media[]` (cover `coverUrl`), discovery room-type cards
gain `amenities[]` + `coverImageUrl`, guide deltas
(`inventory-and-availability.md`, `discovery.md`), Postman folder "04" additions
(rename body += display fields; new "Upload room media" / "Remove room media",
reusing `{{roomMediaId}}`).

Tests: 10 new cases in `CareHomeRoomEnrichmentTests.cs` (domain display rules,
display handler roundtrip/clear/mixed/blank-number, type-change validation,
display-under-occupancy, room-media roundtrip/caps/theories/unknown-room, SQLite
unique-index twin with `ChangeTracker.Clear`). Three test fakes received
mechanical `RoomMedia` forwarders (manifest file-list exception; no behavior
change) plus one nullable-flow warning fix (`Save(db, f!, ct)`).

## Gate battery (final tree, `90ca26f`, re-run after the live gate)

| Gate | Command / scope | Result |
|---|---|---|
| Build | `dotnet build` (worktree) | **0 warnings / 0 errors** |
| Focused tests | `dotnet test --filter FullyQualifiedName~CareHomes` | **204/204** (InMemory + SQLite twins) |
| Whitespace | `git diff --check origin/main...HEAD` | exit 0 |
| Postman JSON | parse Owner collection | OK (607 requests) |
| Contract map | `docs/internal/tools/Verify-ApiContractMapping.ps1` | **Postman 0/0 absolute** (545 routes / 607 requests); orphan Bruno 0 |

**Bruno waiver (owner ruling A):** missing Bruno = **139** = origin/main
baseline **135** (measured at `4e7f956`) + exactly **4** (Phase 1's 2 room-type
media endpoints + Phase 2's 2 room media endpoints; delta isolated by checker
runs at `4e7f956`, `e253716`, and `90ca26f`). Waived in favor of the live
multipart proof below; no manifest change, no script change. Correction: the
Phase 1 report recorded the baseline as 134; the measured baseline at `4e7f956`
is 135 (miscount, no code impact — phase delta was exactly +2 then and +2 now).

## Live gate (owner-approved run on 5235 / SanadBrunoTestDb)

Guards: seed off, Paymob + SMTP blanked, process-scoped env, loopback only,
started via the worktree-local `.opencode/start-api.ps1` (git-excluded, never
committed), stopped afterwards (5235 free; 5245 isolation untouched; scratch
tokens deleted).

**Migration note:** the shared disposable DB already carried the history row
`20261012143753` (Phase 1), so EF skipped the extended migration by design; the
Phase 2 DDL (2 display columns, `care_homes.room_media`, unique index) was
applied manually via a temp reconcile script and verified present. Startup
logged "No migrations were applied" for all contexts.

| # | Check | Evidence |
|---|---|---|
| 1 | DDL reconcile | display columns + `room_media` + index present; startup applied 0 migrations |
| 2 | Readback shape | rooms expose `arabicDisplayName`/`englishDisplayName`/`resolved*`/`media[]`; unresolved → type name (version 47) |
| 3 | Display override (both languages, same number, occupied room) | **200**, version 47→48; resolved = override; Arabic stored **byte-exact** (DB hex == UTF-8 of غرفة مميزة) |
| 4 | Display-only update while occupied (decision 1) | same call as #3 on a room held by an active `Accepted` booking (bed-level interval) → **200** |
| 5 | Number change while occupied | **409 `CareHomes.Inventory.ActiveOccupancy`**, no mutation |
| 6 | Mixed display (one set, one blank) | **400 `CareHomes.Inventory.Invalid`** |
| 7 | Garbage `roomTypeId` | **400 `CareHomes.Inventory.InvalidTarget`** |
| 8 | Clear display (both blank strings) | **200**, version 48→49; resolved falls back to type name |
| 9 | Room cover upload (multipart, real PNG) | **201** with media body + `coverUrl`; version→50 |
| 10 | Room gallery upload | **201**, `coverUrl: null` |
| 11 | Readback after uploads | media count 2; coverUrl only on cover |
| 12 | Text bytes as image/png | **400 `CareHomes.Inventory.MediaInvalid`** |
| 13 | Unknown mediaId delete | **404 `CareHomes.Inventory.MediaNotFound`** |
| 14 | Gallery delete | **204**; media 2→1 |
| 15 | Cover delete (cleanup) | **204**; media 0 |
| 16 | GET cover `file` URL (decision 5 gap) | **404** — serving gap live-confirmed, left as-is |
| 17 | Anonymous discovery detail | Shared type: `amenities` (2) + `coverImageUrl` present; other types empty arrays; URL uses the same owner-route convention |
| 18 | Session revocation | every `LiveGate*` session **204**-revoked (list → 0); refresh-after-revoke → **401 `Identity.Refresh.SessionRevoked`** |

Fixture mutation was net-zero: room HC023-SH-A display set then cleared, room
media uploaded then fully deleted (204s), facility version advanced 47→53, no
other state changed.

### Tooling gotchas hit (not API defects)

- Access tokens are 15-minute JWTs — one mid-gate re-login; the logout/refresh
  header needs `deviceSessionId.value` (the login response nests the guid).
- PS 5.1 has no `Invoke-RestMethod -Form`; multipart uploads ran through
  `curl.exe -F`, refresh bodies written to a temp file to avoid quote mangling.

## Known notes (out of scope)

- **Cover serving gap:** `coverUrl`/`coverImageUrl` point at
  `/api/v1/care-homes/inventory/mine/rooms|room-types/{id}/media/{mediaId}/file`,
  which no GET route serves (live 404, check #16). Owner decision 5 keeps the
  contract as-is; a discovery-scoped serving route is needed before frontends
  render room photos.
- `CareHomeReviewNotificationProcessor` EF-translation error at startup:
  pre-existing, documented (HC-013). A `CareHomeBookingNotificationProcessor`
  retry of a stale outbox item hit the same translation class this run; both
  processors are untouched by this phase.
- Postman Bruno coverage for the 4 media endpoints is waived (ruling A) — live
  multipart proof in checks 9–15.
