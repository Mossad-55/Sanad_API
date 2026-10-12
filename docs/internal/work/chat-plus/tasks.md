# Chat-plus — task checklist

Statuses: Not started, In progress, Blocked, Done. Contract in `plan.md` is
frozen; changing it needs a new explicit owner decision. File manifests are
exclusive (plan §7). Voice messages are out of scope — any voice work arriving
here is rejected back to the owner.

- [ ] Not started — **Phase 0.** Branch `opencode/chat-plus` from latest `main`; verify clean base.
- [ ] Not started — **Phase 1. Pricing insights.**
  Owns: insights query/handler, caregiver read route (per plan §2.1),
  tests, docs + Postman deltas.
- [ ] Not started — **Phase 2. Hub core + events + fallback protocol.**
  Owns: `Hubs/ChatHub.cs`, group auth + re-verification, message/receipt
  emission, typing with expiry + spam guard, transport config, reconnect
  replay via existing list endpoint, hub unit tests, realtime guide draft.
- [ ] Not started — **Phase 3. Presence + bus seam.**
  Owns: heartbeat/timeout presence honoring privacy, `IMessageBus` seam +
  in-memory implementation + flag + contract tests, multi-device OR logic.
- [ ] Not started — **Phase 4. Guides + gates (incl. socket-kill proof).**
  Owns: `pricing-insights.md` + `realtime.md` (protocol + fallback levels +
  reconnect flow), collection polish, full solution build (0 warnings) + full
  unit + architecture suites once, `git diff --check`, Postman JSON
  validation, contract-map 0/0. No provider calls, no migration applies, no
  seed data.

Done criteria per phase: zero-warning build, focused tests green (InMemory +
SQLite twins for queries; hub scoping/expiry/privacy/replay unit tests), docs +
Postman synced, branch + SHA reported. Live gates per plan §8 on the authorized
disposable target — including the socket-kill → polling-convergence proof.
Mutating live checks only with per-run approval.

## Ready for integration (Integrator-owned discovery)

Mergeable only if ALL hold: box checked, report
`docs/internal/work/chat-plus/reports/<slug>.md` exists with branch +
base/final SHAs + gate evidence, deps in `main`, tip matches. Otherwise skipped
as blocked (never force-merged). Workers never merge or push.

- [ ] phase-1-pricing — report: `docs/internal/work/chat-plus/reports/phase-1-pricing.md`
- [ ] phase-2-hub — report: `docs/internal/work/chat-plus/reports/phase-2-hub.md`
- [ ] phase-3-presence — report: `docs/internal/work/chat-plus/reports/phase-3-presence.md`
