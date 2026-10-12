# SuperAdmin user management — task checklist

Single implementation checklist. Statuses: Not started, In progress, Blocked,
Done. The contract in `plan.md` is frozen; changing it needs a new explicit
owner decision. File manifests are exclusive (see plan §7).

## Order (dependency-ordered, commit per phase locally, no push/merge/deploy)

- [ ] Not started — **Phase 0.** Branch `opencode/admin-users` from latest `main`; verify clean base.
  Owns: new branch only.

- [ ] Not started — **Phase 1. List/detail/sessions + SuperAdminOnly policy.**
  Owns: policy const + DI, `AdminUsersController` (endpoints 1–2, 15), list/detail/sessions
  handlers + validators, `ICareHomeOwnerFacilityLookup` port + API impl (plan §2.1),
  tests, docs + Postman deltas.

- [ ] Not started — **Phase 2. Suspend/reactivate/block + session revocation + notices.**
  Owns: status handlers reusing domain transitions, revoke-all wiring, status-change
  notice enqueue, tests (incl. self/seeded/last-superadmin cases), docs + Postman deltas.

- [ ] Not started — **Phase 3. Password reset + account-type changes + admin create.**
  Owns: reset handler (one-time display, session wipe), change-type handler
  with promotion matrix, create handler, tests, docs + Postman deltas.

- [ ] Not started — **Phase 4. Anonymize + audit trail (+migration).**
  Owns: anonymize handler (all types), `AdminUserAction` entity + config +
  additive migration (authored only), audit list handler, tests, docs + Postman.

- [ ] Not started — **Phase 5. Docs + Postman sync, guides, gates.**
  Owns: `docs/admin/user-management.md` operator guide (new consumer file — the planning package stays here under `docs/internal/work/admin-users/`), collection polish,
  full solution build (0 warnings) + full unit + architecture suites once,
  `git diff --check`, Postman JSON validation, contract-map 0/0. No provider
  calls, no migration applies, no seed data.

Done criteria per phase: zero-warning build, focused tests green (InMemory +
SQLite twins for every new query), docs + Postman synced, branch + SHA
reported. Live API reads per phase on the authorized disposable target;
mutating live checks only with per-run approval.

## Ready for integration (Integrator-owned discovery)

A phase is mergeable only if ALL hold: its box below is checked, its report
file `docs/internal/work/admin-users/reports/<slug>.md` exists with branch +
base/final SHAs + gate evidence, its dependencies are already in `main`, and
the branch tip equals the reported final SHA (docs-only follow-ups per the
Chat/Opus convention, verified before merge). Otherwise skipped as blocked
(never force-merged). Workers never merge or push.

- [ ] phase-1-list — report: `docs/internal/work/admin-users/reports/phase-1-list.md`
- [ ] phase-2-status — report: `docs/internal/work/admin-users/reports/phase-2-status.md`
- [ ] phase-3-accounts — report: `docs/internal/work/admin-users/reports/phase-3-accounts.md`
- [ ] phase-4-audit — report: `docs/internal/work/admin-users/reports/phase-4-audit.md`
