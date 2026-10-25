# Mobile report (M1–M5) — release receipt (2026-10-25)

**Status: deployed and verified on the authorized rehearsal VPS.** The release shipped
in two passes at the same target: the feature revision
`456858d8ee9115fb5ecf067a6f3216a499603e4e` (`5377bbc9..456858d8`, 105 files), then the
idempotency replay-encoding fix `8913df42aadc519c8deee4de1a16ee56fabf2e67`
(`456858d8..8913df42`, 2 files, no migration change), which is the **current live
revision**. Live at `http://72.62.92.144:8091`, service `sanad-api`
(`/root/sanad-api`), runtime environment `Production`, API bound to `127.0.0.1:5001`.

Active scope/acceptance authority:
[full-demo-corpus-2026-10-25.md](../../demo-corpus/full-demo-corpus-2026-10-25.md)
(M1–M5 contract and acceptance). Owner authorization: full release, continue without
further check-ins (2026-10-25). This receipt covers only the release; D1–D4 remain.

## Evidence — current revision `8913df42`

| Item | Result |
|---|---|
| Pushed revision built | `8913df42aadc519c8deee4de1a16ee56fabf2e67` (HEAD == `origin/main`) |
| Change | `MvcIdempotentResponseSnapshotter` clones MVC's JSON options with `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`; new `IdempotencySnapshotterTests` proves raw Arabic with no `\u` escapes |
| Publisher | `docs/internal/tools/Publish-SanadApi.ps1 -Configuration Release` — 0 warnings / 0 errors |
| Package | 110 files / 34,530,471 bytes; no `.env`, `*.local.json`, keys/certs or `appsettings.Development.json` (only the host's `appsettings.json` was excluded from the copy) |
| Archive | `deploy/sanad-api-8913df42.tar.gz`, 12,556,012 bytes, SHA-256 `914aedcb4924e7bc8ec30288acb1016d86047821370f4181081f561eb3d2c3f2` — local and remote checksums match |
| Backup (database) | `database-before.dump` 833,385 bytes, `pg_restore -l` TOC validated with 907 entries, taken against the exact validated target (`SanadDb`, loopback port 5433, single connection key) |
| Backup (binaries) | `app-before.tar.gz` 12,513,532 bytes of `/root/sanad-api` |
| Release directory | `/root/sanad-releases/mobile-report-fix-8913df42-20261025192255` (manifest, config hashes, backup dump/TOC, archive listing, startup log, sanitized log, deployment info, service status) |
| Pre-release live state | revision `456858d8ee9115fb5ecf067a6f3216a499603e4e`, `Production`, reachable, 9/9 applied == required, 0 pending |
| Migrations | **0 applied** (expected). Required heads unchanged from `456858d8`; startup logged `0 migration(s) applied` for all nine contexts; 9/9 applied == required, 0 pending |
| Configuration | `CONFIG_UNCHANGED` — SHA-256 fingerprints of `appsettings.json`, `/etc/sanad/sanad.env` and the systemd unit identical before and after |
| Fixture refund seam | **Absent/disabled** — zero effective `FixtureRefundSeamState` lines; Production validator not bypassed |
| Service | `ActiveState=active`, `SubState=running`, `NRestarts=0`; start time `2026-10-25 19:23:01 UTC` |
| Verifier | `Verify-SanadDeployment.ps1` exit 0 — remote revision match, `Production`, 9/9 required/applied, startup log proves all nine context checks, anonymous `GET /api/v1/lookups/services` HTTP 200 |
| Rollback artifacts | Both on-host artifacts retained; no automatic restore was performed or is authorized |

### First pass (`456858d8`) — historical evidence

| Item | Result |
|---|---|
| Package / archive | 110 files / 34,530,531 bytes; `sanad-api-456858d8.tar.gz` 12,555,851 bytes, SHA-256 `01d3851b9cd0994478b5f25f7e181e50a0a6d56d8e74517c903ea0323e7b9e1f` |
| Backups | `database-before.dump` 825,228 bytes / 892-entry TOC; `app-before.tar.gz` 12,453,800 bytes |
| Release directory | `/root/sanad-releases/mobile-report-456858d8-20261025190638` |
| Migrations applied | 3 at startup: Families `20261025175627_AddIdempotencyRequests`, CareHomes `20261025175720_AddIdempotencyRequests`, Community `20261025170718_AddCommunityPostViews`; 9/9 heads, 0 pending |
| Service / config / seam | active/running, `NRestarts=0`; config unchanged; seam absent/disabled |

Applied heads (unchanged across both passes):

| Context | Head |
|---|---|
| Identity | `20261022094500_AddPartnerContactOptionalFields` |
| Cms | `20261023114500_AddLibraryContentVideoStorageKey` |
| Caregivers | `20261021130000_AddCaregiverReviewNotes` |
| Families | `20261025175627_AddIdempotencyRequests` |
| CareHomes | `20261025175720_AddIdempotencyRequests` |
| Notifications | `20261010010618_AddEmailHtmlSnapshots` |
| Community | `20261025170718_AddCommunityPostViews` |
| Finance | `20261024133842_AddDuplicateChargeClaims` |
| Chat | `20261013171755_AddAttachmentCleanupIndex` |

## Authenticated readback (API over a loopback-only SSH tunnel)

Fixture logins used `r2.<role>@test.sanad.local` with the private fixture password in
memory only; tokens were never printed or persisted, and every created session was
logged out (`204`). Reads are read-only except the documented synthetic writes.

| Role | Readback |
|---|---|
| `r2.carehome.owner` | `GET /api/v1/community/posts` **200**, 3 posts, every item exposes `viewsCount` and `authorId`. `POST /api/v1/community/posts/{id}/view` **200** (repeat call on the already-viewed post returned the same count, no second row). `GET .../comments` **200**, rows carry `isOwn`, `canEdit`, `canDelete`, `isLiked` |
| `r2.family.owner` | `GET /api/v1/caregivers/{approvedCaregiverId}/availability/day?date=2026-10-26&shiftType=EightHourShift` **200** with `timeZoneId=Africa/Cairo` and `intervals` present; weekly `GET .../availability` **200** (checked in pass one, unchanged code since) |
| `r2.family.owner` (idempotency) | See the byte-identical evidence below; changed-body call **409** `Families.CareRecords.IdempotencyConflict`, one stored row |
| `r2.support` | `GET /api/v1/admin/subscriptions` **200**; `GET /api/v1/admin/subscriptions/plans` **403** (still forbidden) |

### Idempotency replay byte-identity — previously failed, now verified

Pass one returned a **FAIL** on this check: the fresh 201 was 445 bytes with raw UTF-8
Arabic while the replayed stored snapshot was 473 bytes with `\uXXXX` escaping (28-byte
escape expansion), though JSON-equal. The `8913df42` fix clones MVC's options with the
relaxed encoder in the snapshotter. Re-checked on the live revision with an Arabic
hospital name (`مستشفى سند التجريبي`) and details containing Arabic and an HTML-ish
character (`SANAD DEMO — <b>ملاحظة</b> للمراجعة`), same `Idempotency-Key` and body:

| Check | Result |
|---|---|
| Fresh 201 / replay 201 | `201` / `201`, same record id |
| Response bytes | **440 / 440**, `byte_equal=true` |
| Escaping | no `\u` sequences in either body; Arabic and `<b>` present as raw UTF-8 |
| UTF-8 decode | both decode to identical JSON strings |
| Stored rows | exactly one new record (`6 → 7`), no second idempotency row |
| Changed body, same key | **409** `Families.CareRecords.IdempotencyConflict`; row count unchanged |
| Repeat view / comments / day availability / support 403 | unchanged after redeploy (no regression) |

The strict "byte-identical body" acceptance check now **PASSES**.

## Smoke writes created (synthetic, additive — reuse/skip later, never delete)

- `community.PostViews`: one viewer row for `r2.carehome.owner` on post
  `01a15588-3d20-78ee-a67b-7aa522aea4dc`; counter `0 → 1`. The follow-up `view` call did
  not add another row.
- `families.care_records` / `families.idempotency_requests`, pass one — three synthetic
  records for dependent `01a17319-d059-7b7b-b92d-94e7ec18d0a4`, visit date `2026-10-26`:
  - `01a17478-d5d9-7e75-9931-31daca20e923` — `SANAD RELEASE CHECK Hospital`, details
    `Synthetic mobile-report release readback record; safe demo data.`
  - `01a17479-2742-7a64-81b1-e5fb89c81f73` — same synthetic payload
  - `01a1747a-7eeb-71d6-a121-588e617edd23` — same synthetic payload (documented
    create/replay/conflict evidence)
- `families.care_records` / `families.idempotency_requests`, pass two — one synthetic
  record `01a17485-010a-7fc4-81fd-cfeb09d31cfb`, hospital `مستشفى سند التجريبي`, details
  `SANAD DEMO — <b>ملاحظة</b> للمراجعة`, visit date `2026-10-26`, with its matching
  `family.care-records.create` idempotency row.

Nothing was deleted or updated; the care-record count moved `3 → 6` across the first
pass and `6 → 7` in the fix verification.

## Deferred, not part of this release

- **D1–D4 demo corpus** remains the next slice: it must account for the four synthetic
  care records and one post view above, the new `families.idempotency_requests`,
  `care_homes.idempotency_requests`, `community.PostViews` / `Posts.ViewsCount`
  surfaces, and the fixture refund seam still being disabled (money-state groups stay
  blocked until a non-Production seam configuration is separately settled and verified).
- No reset, deletion, provider call, money movement or G1–G4/demo seeding was performed.
  Host storage, key-ring, service unit and unit configuration are untouched.
