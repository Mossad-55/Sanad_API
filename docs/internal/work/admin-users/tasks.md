# SuperAdmin user management — task checklist

Single implementation checklist. Statuses: Not started, In progress, Blocked,
Done. The contract in `plan.md` is frozen; changing it needs a new explicit
owner decision. File manifests are exclusive (see plan §7).

## Order (dependency-ordered, commit per phase locally, no push/merge/deploy)

- [x] Done — **Phase 0.** Branch `opencode/admin-users` from latest `main`; clean base verified at `b0ac0d0`.
  Owns: new branch only.

- [x] Done — **Phase 1. List/detail/sessions + SuperAdminOnly policy.** Commit `c7cfd48`; report: `reports/phase-1-list.md`.
  Owns: policy const + DI, `AdminUsersController` (endpoints 1–2, 15), list/detail/sessions
  handlers + validators, `ICareHomeOwnerFacilityLookup` port + API impl (plan §2.1),
  tests, docs + Postman deltas.

- [x] Done — **Phase 2. Suspend/reactivate/block + session revocation + notices.**
  Owns: status handlers reusing domain transitions, revoke-all wiring, status-change
  notice enqueue, tests (incl. self/seeded/last-superadmin cases), docs + Postman deltas.
  Focused tests and live list/detail/sessions reads pass on the authorized
  disposable target. After the explicitly authorized audit-table migration,
  session revocation returned 204 and wrote an audit row; the verification
  session is revoked and API stopped. Postman mapping is 0 missing/0 orphan;
  Bruno is excluded per owner direction. Live status mutations were not run.
  See `reports/phase-2-status.md`.

- [x] Done — **Phase 3. Password reset + account-type changes + admin create.**
  Owns: reset handler (one-time display, session wipe), change-type handler
  with promotion matrix, create handler, tests, docs + Postman deltas. Admin creation
  uses the existing registration response and email/SMS OTP lifecycle; account stays
  PendingVerification until normal verification. Focused tests and read-only live
  routes pass; admin password-reset/account-type/create mutations were not run.
  Postman mapping is 0/0 and Bruno is excluded per owner direction. The authorized
  audit-table migration is installed on the disposable target.
  See `reports/phase-3-accounts.md`.

- [x] Done — **Phase 4. Anonymize + audit trail (+migration).**
  Owns: anonymize handler (all types), `AdminUserAction` entity + config +
  additive migration, audit list handler, tests, docs + Postman. The migration was
  applied only to the explicitly authorized disposable `SanadBrunoTestDb`; audit GET
  returned 200 and the audited session-revoke wrote a row. Anonymization was not run
  live. See `reports/phase-4-audit.md`.

- [x] Done — **Phase 5. Docs + Postman sync, guides, gates.**
  Owns: `docs/admin/user-management.md` operator guide (new consumer file — the planning package stays here under `docs/internal/work/admin-users/`), collection polish,
  full solution build (0 warnings) + full unit + architecture suites once,
  `git diff --check`, Postman JSON validation, contract-map 0/0. No provider
  calls, no migration applies, no seed data. Operator guide and Postman sync are
  complete. The SQLite-only companion availability fixture now assigns the
  provider-generated shadow key deterministically in its relational test. Strict
  build passed with 0 warnings/errors; full unit suite passed 2,938, skipped 3,
  failed 0; architecture passed 1/1; Postman map is 0 missing/0 orphan (Bruno's
  126 missing mappings excluded per owner direction); both collections parse;
  `git diff --check` is clean.

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
