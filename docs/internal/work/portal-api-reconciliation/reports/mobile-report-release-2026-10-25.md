# Mobile report (M1–M5) — release receipt (2026-10-25)

**Status: deployed and verified on the authorized rehearsal VPS.** Release revision
`456858d8ee9115fb5ecf067a6f3216a499603e4e` (`5377bbc9..456858d8`, 105 files) is live
at `http://72.62.92.144:8091`, service `sanad-api` (`/root/sanad-api`), runtime
environment `Production`, API bound to `127.0.0.1:5001`.

Active scope/acceptance authority:
[full-demo-corpus-2026-10-25.md](../../demo-corpus/full-demo-corpus-2026-10-25.md)
(M1–M5 contract and acceptance). Owner authorization: full release, continue without
further check-ins (2026-10-25). This receipt covers only the release; D1–D4 remain.

## Evidence

| Item | Result |
|---|---|
| Pushed revision built | `456858d8ee9115fb5ecf067a6f3216a499603e4e` (HEAD == `origin/main`) |
| Publisher | `docs/internal/tools/Publish-SanadApi.ps1 -Configuration Release` — 0 warnings / 0 errors |
| Package | 110 files / 34,530,531 bytes; no `.env`, `*.local.json`, keys/certs or `appsettings.Development.json` (only the host's `appsettings.json` was excluded from the copy) |
| Archive | `deploy/sanad-api-456858d8.tar.gz`, 12,555,851 bytes, SHA-256 `01d3851b9cd0994478b5f25f7e181e50a0a6d56d8e74517c903ea0323e7b9e1f` — local and remote checksums match |
| Backup (database) | `database-before.dump` 825,228 bytes, `pg_restore -l` TOC validated with 892 entries, taken against the exact validated target (`SanadDb`, loopback port 5433, single connection key) |
| Backup (binaries) | `app-before.tar.gz` 12,453,800 bytes of `/root/sanad-api` |
| Release directory | `/root/sanad-releases/mobile-report-456858d8-20261025190638` (manifest, config hashes, backup dump/TOC, archive listing, startup log, sanitized log, deployment info, service status) |
| Pre-release live state | revision `beb315fe89dfa2f0a5367b57c694674ecb4352d4`, `Production`, reachable, 9/9 applied == required, 0 pending |
| Migrations applied | 3 new, all at startup after the validated backup: Families `20261025175627_AddIdempotencyRequests`, CareHomes `20261025175720_AddIdempotencyRequests`, Community `20261025170718_AddCommunityPostViews`; the other six heads unchanged; 9/9 applied == required, 0 pending |
| Configuration | `CONFIG_UNCHANGED` — SHA-256 fingerprints of `appsettings.json`, `/etc/sanad/sanad.env` and the systemd unit identical before and after; no environment/configuration edit, no reset, no deletion, no clock change |
| Fixture refund seam | **Absent/disabled** — zero effective `FixtureRefundSeamState` lines in the startup log; the Production validator was not bypassed and no configuration was changed |
| Service | `ActiveState=active`, `SubState=running`, `NRestarts=0` (stable, same count as before the cutover); start time `2026-10-25 19:06:45 UTC` |
| Verifier | `Verify-SanadDeployment.ps1` exit 0 — remote revision match, `Production`, 9/9 required/applied, startup log proves all nine context checks, anonymous `GET /api/v1/lookups/services` HTTP 200 |
| Rollback artifacts | Both on-host artifacts retained; no automatic restore was performed or is authorized |

Startup log migration evidence (sanitized): `Identity/Cms/Caregivers/Notifications/Chat/Finance`
`0 migration(s) applied`; `Families`, `CareHomes`, `Community` each logged
`Applying migration '<head>'` followed by `1 migration(s) applied`; `Now listening on:
http://127.0.0.1:5001`; no error/failure lines.

Applied heads after deploy:

| Context | Head |
|---|---|
| Identity | `20261022094500_AddPartnerContactOptionalFields` |
| Cms | `20261023114500_AddLibraryContentVideoStorageKey` |
| Caregivers | `20261021130000_AddCaregiverReviewNotes` |
| Families | `20261025175627_AddIdempotencyRequests` **(new)** |
| CareHomes | `20261025175720_AddIdempotencyRequests` **(new)** |
| Notifications | `20261010010618_AddEmailHtmlSnapshots` |
| Community | `20261025170718_AddCommunityPostViews` **(new)** |
| Finance | `20261024133842_AddDuplicateChargeClaims` |
| Chat | `20261013171755_AddAttachmentCleanupIndex` |

## Authenticated readback (API over a loopback-only SSH tunnel)

Fixture logins used `r2.<role>@test.sanad.local` with the private fixture password in
memory only; tokens were never printed or persisted, and every created session was
logged out (`204`). Reads are read-only except the two documented synthetic writes at
the end.

| Role | Readback |
|---|---|
| `r2.carehome.owner` | `GET /api/v1/community/posts` **200**, 3 posts, every item exposes `viewsCount` and `authorId`. `POST /api/v1/community/posts/{id}/view` twice → **200/200**, count `1` on both (repeat did not increment); target post `01a15588-3d20-78ee-a67b-7aa522aea4dc`, authored by another user, non-anonymous. `GET /api/v1/community/posts/{id}/comments` **200**, the row carries `isOwn`, `canEdit`, `canDelete`, `isLiked` |
| `r2.family.owner` | `GET /api/v1/caregivers/{approvedCaregiverId}/availability/day?date=2026-10-26&shiftType=EightHourShift` **200** with `timeZoneId=Africa/Cairo` and `intervals` present (0 intervals for that day); caregiver `01a0a606-867c-7f9a-902e-cf5921d8fcf8`. Weekly `GET /api/v1/caregivers/{id}/availability` **200**, unchanged shape |
| `r2.family.owner` (idempotent care record) | Two `POST /api/v1/family/dependents/01a17319-d059-7b7b-b92d-94e7ec18d0a4/care-records` calls with the same `Idempotency-Key` and body → **201 / 201**, same record id `01a1747a-7eeb-71d6-a121-588e617edd23`, exactly one stored record (`before 5 → after 6`, only the created id added). Third call with the same key and changed `details` → **409** `Families.CareRecords.IdempotencyConflict`, and the list count stayed 6 |
| `r2.support` | `GET /api/v1/admin/subscriptions` **200**; `GET /api/v1/admin/subscriptions/plans` **403** (the previously forbidden surface stays forbidden) |

### Recorded deviation — replay body formatting (not blocking the deployment)

The two 201 responses replay the same record and parse to the same JSON, but they are
not byte-for-byte identical: the fresh response is 445 bytes with raw UTF-8 Arabic,
while the replayed stored snapshot is 473 bytes with `\uXXXX` escaping for the same
`sourceNameAr` value (the 28-byte delta is exactly that escape expansion; all field
names/values are otherwise equal and `json.loads(raw1) == json.loads(raw2)` is true).
Root cause is serializer-option divergence: MVC's output formatter writes relaxed JSON
while the snapshotter serializes with the raw `JsonOptions.JsonSerializerOptions`.
The stored snapshot is returned byte-exactly on replay, and no second record, audit row
or idempotency row was created. The strict "byte-identical body" acceptance check is
therefore **FAIL** and is recorded here for Mastermind disposition; the semantic
contract (same status, same record, one stored row, 409 on mismatch) passed. This is
wire formatting only — no client parsing, data or security impact.

## Smoke writes created (synthetic, additive)

- `community.PostViews`: one viewer row for `r2.carehome.owner` on post
  `01a15588-3d20-78ee-a67b-7aa522aea4dc`; the counter moved `0 → 1` and a repeat call
  returned `1`.
- `families.care_records` / `families.idempotency_requests`: three synthetic
  `SANAD RELEASE CHECK Hospital` records for dependent
  `01a17319-d059-7b7b-b92d-94e7ec18d0a4`, visit date `2026-10-26`, details
  `Synthetic mobile-report release readback record; safe demo data.` — ids
  `01a17478-d5d9-7e75-9931-31daca20e923`,
  `01a17479-2742-7a64-81b1-e5fb89c81f73`,
  `01a1747a-7eeb-71d6-a121-588e617edd23`. Two are the aborted first/diagnostic
  attempts (each still committed exactly one row); the last is the documented
  create/replay/conflict evidence. Each has a matching `family.care-records.create`
  idempotency row; nothing was deleted or updated.

## Deferred, not part of this release

- **D1–D4 demo corpus** remains the next slice: it must account for the three synthetic
  care records above, the single recorded post view, the new `families.idempotency_requests`,
  `care_homes.idempotency_requests` and `community.PostViews` / `Posts.ViewsCount`
  surfaces, and the fixture refund seam still being disabled (money-state groups stay
  blocked until a non-Production seam configuration is separately settled and verified).
- No reset, deletion, provider call, money movement or G1–G4/demo seeding was performed.
  Host storage, key-ring, service unit and unit configuration are untouched.
