# Gapfix report 3 — decision requests + drafts (no implementation)

- Branch: `opencode/room-enrichment-gapfix`
- Base: **`4e7f956`** (origin/main); branch base `88a4777`
- Files changed: this report only — **no code, no routes, no manifests, no DB**
- Frozen-contract reminder: cover-serving, webp, gate-point, and the Bruno
  waiver may not change without a new explicit owner decision.

## 3a. Cover serving (404) — proposal draft, DO NOT IMPLEMENT

**Decision needed:** approve or reject adding the serving route(s) below; if
approved, confirm discovery-only vs owner+discovery, and whether `coverUrl`
values in inventory readback and discovery payloads should be rewritten to the
new route (existing stored URLs are relative strings generated at read time,
so a route addition fixes all reads without a data migration).

**Draft proposal (mirrors the existing facility-media pattern in
`CareHomeProfileMediaController`):**

1. `GET /api/v1/care-homes/discovery/{careHomeId}/room-types/{roomTypeId}/media/{mediaId}/file`
   — `[AllowAnonymous]`; serve only when the facility is `Approved` with an
   approved revision (same eligibility check as `ReadApproved`,
   `CareHomeProfileMediaController.cs:77-84`), the media belongs to that room
   type, and the room type is non-archived. 404 on any mismatch (no existence
   oracle). This covers both room-type and room photos: room media URLs would
   route through their room type (`room → roomTypeId`) or get their own
   `{roomId}` variant if the owner prefers.
2. Optional owner variant:
   `GET /api/v1/care-homes/inventory/mine/rooms|room-types/{id}/media/{mediaId}/file`
   — owner-scoped like `ReadOwner` (`:50-62`), so owners can preview private
   photos that are not yet published on an approved facility.
3. Storage: reuse `storage.OpenReadAsync` + `File(...)` as `Open` (`:86-91`);
   no storage-key exposure; content type from the stored media row.
4. Contract touch: discovery/inventory `coverUrl`/`coverImageUrl` strings would
   switch from the currently-frozen (404) `inventory/mine/...` form to the new
   public form — this **is** the frozen decision-5 change and requires the
   owner's explicit go.

**Blocked until:** explicit owner order naming this route set.

## 3b. webp — which is truth?

- `plan.md` media rules say **jpeg/png/webp**.
- Code (`CareHomeRoomTypeMediaCommands` / `CareHomeRoomMediaCommands`,
  shared validation) accepts **jpeg/png only**; a webp upload returns
  `400 CareHomes.Inventory.MediaInvalid`.
- Guides and Postman describe jpeg/png.

**Decision needed:** (i) contract = jpeg/png → plan.md gets a correction note
(docs-only fix, small slice); or (ii) contract = jpeg/png/webp → code +
validation tests + guides + Postman + a signature-validation fixture update
(follow-up slice with owner order). **Not implemented either way.**

## 3c. Gate point — which is truth?

- Plan wording points at a **publish-transition** gate (bookability flips when
  photos are published/removed).
- Code enforces at **hold-creation**: `Bookings/CareHomeBookingCommands.cs`
  lines **54–56** (`CheckoutHandler` returns
  `CareHomes.Inventory.RoomMediaRequired` when cover+gallery are missing),
  with companion fixtures in `CareHomeBookingLifecycleTests.cs`.

Behavior is live-proven and consistent day-to-day (any checkout attempt is
rejected), but the enforcement point differs from the plan text.

**Decision needed:** (i) hold-creation is truth → plan wording gets a
correction note; or (ii) publish-transition is truth → code moves the check to
media upload/remove handlers (type-level bookability flag) — a follow-up slice
with owner order, including negative-path tests. **Code untouched.**

## 3d. Bruno +4 — waiver stands, nothing authored

Missing Bruno = 139 = baseline 135 + 4 (room-type media ×2, room media ×2),
waived by owner ruling A in favor of the Phase 1/2 live multipart proofs.

**Decision needed to unblock:** explicit waiver lift + approval of disposable
fixtures for multipart Bruno requests (file assets, expectedVersion flow,
session fixture). Until then: **no Bruno manifests authored, no fixtures
created.**

## 3e. HC-013 processor noise — separate slice (report only)

- `CareHomeReviewNotificationProcessor.cs:59` — `u.Id.Value == @ownerId` fails
  EF translation at startup (documented HC-013).
- `CareHomeBookingNotificationProcessor.cs:115` — `@targets.Contains(u.Id.Value)`
  same translation class; observed retrying a stale outbox item during the
  Phase 2 live gate.

Both are pre-existing, unrelated to room enrichment, and must not be "fixed"
inside this slice. **Decision needed:** open a separate slice (identity
notification processors) with its own manifest and gate plan.

## 3f. Migration reconcile — docs note (informational, no decision)

Stale disposable databases that already carry EF history row
`20261012143753` will not re-apply the *extended* Phase 2 migration; Phase 2
DDL (rooms display columns, `care_homes.room_media`, unique index) must be
applied manually via a temp reconcile script, as done on the authorized
5235/SanadBrunoTestDb target. Fresh databases are unaffected (EF applies the
single migration normally). Recorded here and in the Phase 2/3 reports; no
code or plan change proposed.

## Gates (this item)

| Gate | Command | Result |
|---|---|---|
| Whitespace | `git diff --check` | exit 0 (slice-final run) |
| Scope | `git status --porcelain` | report + tasks files only |

## Slice-final gate battery (whole gapfix branch, after all items)

| Gate | Command | Result |
|---|---|---|
| Whitespace | `git diff --check` | exit 0 |
| Full build | `dotnet build Sanad.slnx --no-restore -m:1 --nologo /p:TreatWarningsAsErrors=true` | **0 warnings / 0 errors** |
| Postman JSON | `python -m json.tool` on Owner collection | exit 0 (607 requests) |
| Contract map | `docs/internal/tools/Verify-ApiContractMapping.ps1` | **Postman 0/0 absolute**; orphan Bruno 0; missing Bruno **139** (= baseline 135 + 4 waived, ruling A; script exit 1 = the unchanged waived-Bruno condition) |

Environmental note: the first build attempt failed with 5 × CS0246
(`FirebaseAdmin` in `Notifications.Infrastructure`) because this clone's
NuGet assets were stale from the 96-behind `main` checkout; `dotnet restore
Sanad.slnx` (exit 0) fixed it and the gated build then passed clean. No source
changes were involved.

## Limitations

- Drafts are proposals only; no route, code, manifest, fixture, or DB object
  was created. Each blocked item needs its explicit owner decision before any
  implementation slice starts.
