# Phase 4 report — photo file serving + URL rewrite + plan corrections

Follow-up slice on `main` (owner order 2026-10-13): decision 3a approved
(discovery + owner-preview routes + URL rewrite, per the draft in
`gapfix-decision-requests.md`), 3b settled to jpeg/png, 3c settled to
hold-creation. No branch; work committed directly on `main` per the standing
instruction. No migration, no seeds, no provider calls, no live API —
automated gates only (the Phase 1–2 live multipart proofs still cover the
media pipeline; the new code paths are read-only serving).

## Delivered

New controller `src/API/Sanad.API/Controllers/CareHomeRoomMediaController.cs`
(mirrors `CareHomeProfileMediaController`; every mismatch collapses to 404,
storage misses surface as 404):

1. `GET /api/v1/care-homes/discovery/{careHomeId}/room-types/{roomTypeId}/media/{mediaId}/file`
   — `[AllowAnonymous]`; serves only for `Approved` facilities with a
   non-archived room type the media belongs to.
2. `GET /api/v1/care-homes/discovery/{careHomeId}/rooms/{roomId}/media/{mediaId}/file`
   — `[AllowAnonymous]`; same, plus the room must belong to the facility and
   both room and its type must be non-archived.
3. `GET /api/v1/care-homes/inventory/mine/room-types/{id}/media/{mediaId}/file`
   — `CareHomeOwnerAccess`; owner preview at any facility status.
4. `GET /api/v1/care-homes/inventory/mine/rooms/{id}/media/{mediaId}/file`
   — `CareHomeOwnerAccess`; owner preview at any facility status.

URL rewrite (draft item 4): all four `coverUrl`/`coverImageUrl` generation
sites now emit the public discovery form —
`UploadRoomTypeMediaCommandHandler.ToDto` (gained `facilityId`),
`UploadRoomMediaCommandHandler.ToDto` (gained `facilityId`),
`InventoryHandlerSupport.Map`, `GetCareHomeDiscoveryDetailQueryHandler`.
No other `ToDto` callers exist (verified by search).

Plan corrections: `plan.md` media rules jpeg/png/webp → jpeg/png (3b);
completeness rule "publish/activate transition" → "checkout hold creation" (3c).

Guides: `inventory-and-availability.md` + `discovery.md` "metadata only / 404"
caveats replaced with serving documentation. Postman: 2 owner preview
requests (folder 04) + 2 family anonymous requests (folder 01).

## Tests

New `tests/Sanad.UnitTests/API/CareHomeRoomMediaControllerTests.cs`: 12
theories × InMemory + SQLite twins = 24 runs — approved/unapproved/wrong
facility, archived type/room, unknown media, storage miss, stranger/anonymous
owner paths, upload + readback URL-form assertions.

Correction found by the twins during development: the first draft filtered
facilities with `f.Id.Value == careHomeId`, which EF cannot translate on
SQLite; rewritten to the proven wrapper comparison
`f.Id == new CareHomeId(careHomeId)` (same shape as the existing
`FacilityId ==` queries). A version-sequencing mistake in the upload URL test
(captured `facility.Version` too late) was also fixed. Both fixes verified by
the re-run below.

## Gate battery (final tree)

| Gate | Command / scope | Result |
|---|---|---|
| Whitespace | `git diff --check HEAD` | exit 0 |
| Build | `dotnet build Sanad.slnx --no-restore -m:1 --nologo /p:TreatWarningsAsErrors=true` | 0 warnings / 0 errors |
| New tests | `--filter FullyQualifiedName~CareHomeRoomMediaControllerTests` | 24/24 (InMemory + SQLite) |
| Focused | `--filter FullyQualifiedName~CareHomes` | 204 + 24 = 228/228 |
| Full unit | `tests/Sanad.UnitTests` | 3010 passed / 0 failed / 3 skipped (pre-existing PG-only) |
| Architecture | `tests/Sanad.ArchitectureTests` | 1/1 |
| Postman JSON | `python -m json.tool` on both touched collections | exit 0 (Owner 609, Family.CareHomes +2 requests) |
| Contract map | `Verify-ApiContractMapping.ps1` | Postman 0/0; Bruno orphans 0; missing Bruno +4 expected (new GET file routes, no waiver lift — recorded, not authored per 3d) |

## Carried forward (unchanged)

- 3d Bruno manifests: still needs waiver lift + fixture approval.
- 3e HC-013 processors: still needs a separate slice.
- No live serving proof: file downloads were not exercised against a live API
  (no disposable-target authorization in this run); serving logic is covered by
  24 automated runs including byte-exact body assertions.
