# Room enrichment — task checklist

Statuses: Not started, In progress, Blocked, Done. Contract in `plan.md` is
frozen; changing it needs a new explicit owner decision. File manifests are
exclusive (plan §4).

- [ ] Not started — **Phase 0.** Isolated worktree at `origin/main` (never the
  dirty `opencode/admin-users` checkout); verify clean base. Owns: worktree only.
- [ ] Not started — **Phase 1. Type amenities + photos + bookability gate.**
  Owns: domain entities, type handlers + gate check, migration (authored only),
  `CareHomeInventoryController` (1–3), tests, docs + Postman deltas.
- [ ] Not started — **Phase 2. Room override + photos + readback.**
  Owns: room handlers (4–6), resolved names/covers on reads, tests, docs + Postman.
- [ ] Not started — **Phase 3. Guides + gates.**
  Owns: `inventory-and-availability.md` deltas, collection polish, full solution
  build (0 warnings) + full unit + architecture suites once, `git diff --check`,
  Postman JSON validation, contract-map 0/0. No provider calls, no migration
  applies, no seed data.

Done criteria per phase: zero-warning build, focused tests green (InMemory +
SQLite twins), docs + Postman synced, branch + SHA reported. Live API flows
per phase on the authorized disposable target; mutating live checks only with
per-run approval.

## Ready for integration (Integrator-owned discovery)

Mergeable only if ALL hold: box checked, report
`docs/internal/work/room-enrichment/reports/<slug>.md` exists with branch +
base/final SHAs + gate evidence, deps in `main`, tip matches. Otherwise skipped
as blocked (never force-merged). Workers never merge or push.

- [ ] phase-1-types — report: `docs/internal/work/room-enrichment/reports/phase-1-types.md`
- [ ] phase-2-rooms — report: `docs/internal/work/room-enrichment/reports/phase-2-rooms.md`
