# Phase 1 report — room-type amenities, photos, bookability gate

## Identity

- Branch: `opencode/room-enrichment`
- Original base: `5edd341`; rebased onto **`4e7f956`** (owner ruling C) before final SHAs.
- Phase 0 (frozen package): `759a5d2` (rebased from `6f31bbf`)
- Phase 1 (deliverables): `cea3300` — *Add room-type amenities, photos, and hold bookability gate*
- Live-gate fixes: **`a861763`** — *Fix live-gate findings: amenities_json backfill default and 201 upload status*
- Final deliverable SHA: **`a861763`** (this tracking file is committed immediately after as the branch tip; see `git log`).

## Delivered

Endpoints (frozen contract §2, owner access, `mine/` prefix):

1. `PUT /room-types/{id}` — names/descriptions/price/mode + **amenities replace-set**
   (≤50, bilingual required, trimmed).
2. `POST /room-types/{id}/media` — multipart upload; 1 cover (position 0) + ≤10 gallery;
   jpeg/png, ≤5 MB, signature-checked, private storage, cleanup on failure; **201**.
3. `DELETE /room-types/{id}/media/{mediaId}` — **204**; gallery positions re-ordered.

Plus: domain media entity + readiness rule, `room_type_media` table + `amenities_json`
column (one authored migration), hold-creation gate
`CareHomes.Inventory.RoomMediaRequired` (400) in `CheckoutHandler`, error codes
`MediaInvalid` (400) / `MediaNotFound` (404), inventory readback `amenities[]` +
`media[]` (cover `coverUrl`), guide deltas
(`docs/care-homes/inventory-and-availability.md`), Postman folder "04" additions.

## Gate battery (final tree, after `a861763`)

| Gate | Command / scope | Result |
|---|---|---|
| Build | `dotnet build Sanad.slnx` | **0 warnings / 0 errors** |
| Focused tests | `dotnet test --filter FullyQualifiedName~CareHomes` | **194/194** (InMemory + SQLite twins) |
| Whitespace | `git diff --check` | exit 0 |
| Postman JSON | parse Owner collection | OK |
| Contract map | `docs/internal/tools/Verify-ApiContractMapping.ps1` | **Postman 0/0 absolute** (543 routes / 605 requests); orphan Bruno 0 |

**Bruno waiver (owner ruling A):** missing Bruno = 137 = origin/main baseline
**135** (exact figure at `4e7f956`; the originally recorded 134/136 was a
miscount — the Phase 2 report already documents the re-measurement) + exactly
**2** (endpoints 2–3, proven by set-diff of checker runs at `origin/main`
vs branch). Waived in favor of the live multipart proof below; no manifest change,
no script change. The earlier 5/5 Postman misses were confirmed a stale-base
artifact (`~/` fix landed in `4e7f956`; checker moved to `docs/internal/tools/`).

## Live gate (owner ruling B: 5235 / SanadBrunoTestDb)

Guards: seed off, Paymob + SMTP blanked, process-scoped env, loopback only,
started via a worktree-local `.opencode/start-api.ps1` copy (git-excluded, never
committed), stopped afterwards (5235 free; 5245 isolation untouched).
All 9 gate sessions (`deviceName: LiveGate`) revoked (204 each); refresh-after-logout
rejected with the generic **400** validation failure — the session row was
already deleted by logout, which is a different enforcement path from Phase 2's
revocation check (a session that still exists but was revoked returns **401
`Identity.Refresh.SessionRevoked`**). Both paths block refresh and prove
session teardown; access tokens are 15-minute JWTs.

| # | Check | Evidence |
|---|---|---|
| 1 | Migration applied on startup | `care_homes.room_type_media` + `amenities_json` present |
| 2 | Readback shape | `GET /inventory/mine` → per-type `amenities[]` + `media[]`, cover `coverUrl` |
| 3 | Amenities PUT | 200, version 40→41, 2 items round-trip |
| 4 | Gate, 0 photos | checkout → **400 `CareHomes.Inventory.RoomMediaRequired`** |
| 5 | Cover upload (real PNG, multipart) | **201** with media body |
| 6 | Gallery upload | **201**, gallery position 0 |
| 7 | Signature/content rejection (text bytes as image/png) | **400 `MediaInvalid`** |
| 8 | Unknown photo delete | **404 `MediaNotFound`** |
| 9 | Photo delete | **204**, gallery re-ordered |
| 10 | Checkout after upload (bookable) | **200** with `holdUntilUtc` |
| 11 | Delete gallery → checkout | **204** → **400 `RoomMediaRequired`** (transition back) |
| 12 | Re-upload → checkout (restored) | **201** → **200** with hold |

### Defects found and fixed (`a861763`)

1. **Migration backfill broke all inventory reads** (500): `amenities_json`
   defaulted to `""`, which the JSON value converter cannot deserialize
   (`JsonException` at `InventoryHandlerSupport.Map`). Fixed: default `"[]"` plus
   `UPDATE ... SET amenities_json = '[]' WHERE amenities_json = ''`; the
   owner-authorized disposable target was reconciled in place (3 rows). Invisible
   to unit tests because fixtures always write `[]`.
2. **Upload returned 200, frozen contract requires 201**. Fixed in
   `CareHomeInventoryController.UploadTypeMedia` (`StatusCode(201, dto)`);
   failure paths unchanged.

## Known notes (out of scope)

- `CareHomeReviewNotificationProcessor` EF-translation error at startup: pre-existing,
  documented in `.opencode/README.md` (HC-013), unrelated to this phase.
- Post-rebase, `docs/tools/` checker moved to `docs/internal/tools/`.
