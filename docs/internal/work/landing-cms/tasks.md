# Landing CMS — task checklist

Statuses: Not started, In progress, Blocked, Done. Contract in `plan.md` is
frozen; changing it needs a new explicit owner decision. File manifests are
exclusive (plan §4).

- [x] Done — **Phase 0.** Branch `opencode/landing-cms` from latest `main`; verify clean base.
- [x] Done — **Phase 1. Section registry + public reads + stats.**
  Owns: domain entity + typed ids, `LandingController` (1+3; preview deferred
  to Phase 2 by owner decision), stats aggregation, tests, docs + Postman
  deltas. Report: `reports/phase-1-reads.md`.
- [ ] Not started — **Phase 2. Admin CRUD + lifecycle + media (+migration).**
  Owns: admin handlers (4–11), `AdminLandingController`, config + additive
  migration (authored only), upload handling, tests, docs + Postman.
- [ ] Not started — **Phase 3. Testimonial refs + FAQ refs + contact flow.**
  Owns: ref resolution + skip-missing semantics, contact intake wiring, tests,
  docs + Postman.
- [ ] Not started — **Phase 4. History/audit + guides + gates.**
  Owns: audit rows, `docs/app/public/landing.md`, collection polish, full
  solution build (0 warnings) + full unit + architecture suites once,
  `git diff --check`, Postman JSON validation, contract-map 0/0. No provider
  calls, no migration applies, no seed data.

Done criteria per phase: zero-warning build, focused tests green (InMemory +
SQLite twins), docs + Postman synced, branch + SHA reported. Live API reads
per phase on the authorized disposable target; mutating live checks only with
per-run approval.

## Ready for integration (Integrator-owned discovery)

Mergeable only if ALL hold: box checked, report `docs/internal/work/landing-cms/reports/<slug>.md`
exists with branch + base/final SHAs + gate evidence, deps in `main`, tip
matches. Otherwise skipped as blocked (never force-merged). Workers never
merge or push.

- [ ] phase-1-reads — report: `docs/internal/work/landing-cms/reports/phase-1-reads.md`
- [ ] phase-2-admin — report: `docs/internal/work/landing-cms/reports/phase-2-admin.md`
- [ ] phase-3-refs — report: `docs/internal/work/landing-cms/reports/phase-3-refs.md`
