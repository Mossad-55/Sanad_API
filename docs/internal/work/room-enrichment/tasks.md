# Room enrichment — task checklist

Statuses: Not started, In progress, Blocked, Done. Contract in `plan.md` is
frozen; changing it needs a new explicit owner decision. File manifests are
exclusive (plan §4).

- [x] Done — **Phase 0.** Isolated worktree at `origin/main` (never the
  dirty `opencode/admin-users` checkout); verify clean base. Owns: worktree only.
  Committed as `6f31bbf`, rebased to `759a5d2` on `4e7f956`.
- [ ] In progress — **Phase 1. Type amenities + photos + bookability gate.**
  Owns: domain entities, type handlers + gate check, migration (authored only),
  `CareHomeInventoryController` (1–3), tests, docs + Postman deltas.
  Delivered and verified: `cea3300` + live-gate fixes `a861763`; build 0/0,
  CareHomes 194/194, Postman contract-map 0/0 absolute (Bruno +2 waived for
  live multipart proof, owner ruling A); live gate passed on the authorized
  5235/SanadBrunoTestDb target (gates 400↔200, uploads 201, 204/404/400
  negatives, sessions revoked, API stopped). Report
  `reports/phase-1-types.md` written; awaiting integration.
- [ ] In progress — **Phase 2. Room override + photos + readback.**
  Owns: room handlers (4–6), resolved names/covers on reads, tests, docs + Postman.
  Delivered and verified: `90ca26f`; build 0/0, CareHomes 204/204, Postman
  contract-map 0/0 absolute (Bruno +4 total waived for live multipart proof,
  owner ruling A); live gate passed on the owner-approved 5235/SanadBrunoTestDb
  run (display override/clear, guards 400/409, room media 201/204/404/400,
  anonymous discovery amenities + coverImageUrl, sessions revoked, API stopped).
  Report `reports/phase-2-rooms.md` written; awaiting integration.
- [ ] In progress — **Phase 3. Guides + gates.**
  Owns: `inventory-and-availability.md` deltas, collection polish, full solution
  build (0 warnings) + full unit + architecture suites once, `git diff --check`,
  Postman JSON validation, contract-map 0/0. No provider calls, no migration
  applies, no seed data.
  Delivered and verified: cover-serving caveat added to both guides, Postman
  folder 04 polished (Rename room example now omits `roomTypeId`); joint gate
  battery once on the final tree — build **0/0** (warnaserror), full unit
  **2986/2986** (3 known PG-only static skips), architecture **1/1**,
  diff-check 0, Postman JSON OK (607 requests), contract-map **Postman 0/0
  absolute** (missing Bruno 139 = baseline 135 + 4 waived, ruling A). Report
  `reports/phase-3-guides-gates.md` written; package ready for integration.

Done criteria per phase: zero-warning build, focused tests green (InMemory +
SQLite twins), docs + Postman synced, branch + SHA reported. Live API flows
per phase on the authorized disposable target; mutating live checks only with
per-run approval.

## Ready for integration (Integrator-owned discovery)

Mergeable only if ALL hold: box checked, report
`docs/internal/work/room-enrichment/reports/<slug>.md` exists with branch +
base/final SHAs + gate evidence, deps in `main`, tip matches. Otherwise skipped
as blocked (never force-merged). Workers never merge or push.

- [x] phase-1-types — report: `docs/internal/work/room-enrichment/reports/phase-1-types.md`
- [x] phase-2-rooms — report: `docs/internal/work/room-enrichment/reports/phase-2-rooms.md`
- [x] phase-3-guides-gates — report: `docs/internal/work/room-enrichment/reports/phase-3-guides-gates.md`
