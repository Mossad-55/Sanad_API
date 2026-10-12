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

- [x] phase-1-types — report: `docs/internal/work/room-enrichment/reports/phase-1-types.md` (merged into `main` via `b3c4fba`)
- [x] phase-2-rooms — report: `docs/internal/work/room-enrichment/reports/phase-2-rooms.md` (merged via `b3c4fba`)
- [x] phase-3-guides-gates — report: `docs/internal/work/room-enrichment/reports/phase-3-guides-gates.md` (merged via `b3c4fba`)

## Gapfix slice (`opencode/room-enrichment-gapfix`, base `4e7f956`, from `88a4777`)

- [x] gapfix-cosmetic-evidence — Phase 1 report corrected (Bruno baseline 135,
  refresh 400-logout vs 401-SessionRevoked paths); HC-023 undescribed Postman
  requests left untouched. Report: `reports/gapfix-cosmetic-evidence.md`.
- [x] gapfix-manifest-hygiene — 7 supporting files + 3 fakes verified;
  BookingCommands checkout gate (lines 54–56) found non-mechanical and
  STOPPED for owner decision (3c), not fixed. Report:
  `reports/gapfix-manifest-hygiene.md`.
- [x] gapfix-decision-requests — drafts for all six gap items authored.
  Report: `reports/gapfix-decision-requests.md`.
- [ ] **Blocked** — cover serving route (3a): needs explicit owner order
  approving the draft discovery-scoped file route(s) (frozen decision 5).
- [ ] **Blocked** — webp acceptance (3b): needs owner ruling on truth
  (plan jpeg/png/webp vs code jpeg/png).
- [ ] **Blocked** — gate point (3c): needs owner ruling on truth
  (plan publish-transition vs code hold-creation at BookingCommands:54–56).
- [ ] **Blocked** — Bruno +4 manifests (3d): waiver A stands; needs explicit
  waiver lift + disposable fixture approval before any authoring.
- [ ] **Blocked** — HC-013 processor fixes (3e): out of slice; needs a
  separate owner-ordered slice.
