# Notifications slice — task checklist

Single implementation checklist for the Notifications slice. Statuses: Not started,
In progress, Blocked, Done. The contract in `plan.md` is frozen; changing it
needs a new explicit owner decision. Lane file manifests are exclusive: a
worker edits only its owned files and never another lane's.

## Lane A — Push (Codex)

Owns: `Sanad.API/Controllers/DevicesController.cs` + DTOs,
`Modules/Notifications/{Domain(DeviceToken, PushOutboxMessage),
Application(Push/*: sender abstraction, fanout, outbox commands),
Infrastructure(config, FCM sender, processor, migration, DI)}`,
`tests/.../{PushTests, FanoutTests}`,
`docs/app/notifications.md` (devices, deep links, prefs) + Postman deltas.

- [ ] Not started — Token registry + register/list/unregister endpoints with ownership; focused tests.
- [ ] Not started — `IPushSender` + FCM sender (pinned FirebaseAdmin 3.7.0) + Development stub; config-gated, fail-closed; focused tests (no secrets in logs).
- [ ] Not started — Push outbox + processor with capped backoff and invalid-token retirement; focused tests.
- [ ] Not started — Fan-out on chat/booking/payout/health events per the matrix (preferences checked server-side); focused tests.
- [ ] Not started — Docs + Postman sync; contract-mapping check.

Done criteria per item: zero-warning build, focused tests green, docs + Postman synced, branch + SHA reported. No push/merge/deploy. No live Firebase calls except against the real project with explicit per-run owner authorization and a test device.

## Lane B — Email overhaul (assignee TBD)

Owns: `Identity/Infrastructure/Messaging/EmailTemplates/*` (layout + 12 templates),
sender `multipart/alternative` upgrade, outbox enqueueing for notification emails,
`tests/.../EmailTemplateTests.cs`, docs + Postman deltas.

- [ ] Not started — Shared bilingual layout (Arabic-first RTL + English below, text wordmark, system fonts) + all 12 templates with plain-text twins; snapshot tests (no sending).
- [ ] Not started — Sender upgrade to `multipart/alternative`; outbox enqueue for notification emails (render-at-enqueue snapshots); existing `MessageId` stability kept; focused tests.
- [ ] Not started — Docs + Postman sync; contract-mapping check.

Done criteria per item: same as Lane A.

## Joint gates (Integrator-owned)

- [ ] Not started — Seam review: DTOs + error codes + template keys match `plan.md` exactly.
- [ ] Not started — Full solution build (0 warnings) + full unit + architecture suites once (new packages + shared model).
- [ ] Not started — `git diff --check`, Postman JSON validation.
- [ ] Not started — Live Firebase verification only with explicit owner authorization + test device; Development stub otherwise.

## Ready for integration (Integrator-owned discovery)

A task is mergeable only if ALL hold: its box below is checked, its report
file `docs/notifications/reports/<slug>.md` exists with branch + base/final
SHAs + gate evidence, its dependencies are already in `main`, and the branch
tip equals the reported final SHA. Otherwise it is skipped as blocked (never
force-merged). On owner trigger "integrate now", the Integrator scans these
boxes, merges qualifying tasks in dependency order (Lane A before Lane B),
marks each merged box with the `main` SHA, and pushes `main` once. Workers
never merge or push; workers write the report file as part of Done.

- [ ] lane-a-registry — report: `docs/notifications/reports/lane-a-registry.md`
- [ ] lane-a-sender — report: `docs/notifications/reports/lane-a-sender.md`
- [ ] lane-a-outbox — report: `docs/notifications/reports/lane-a-outbox.md`
- [ ] lane-a-fanout — report: `docs/notifications/reports/lane-a-fanout.md`
- [ ] lane-a-docs — report: `docs/notifications/reports/lane-a-docs.md`
- [ ] lane-b-templates — report: `docs/notifications/reports/lane-b-templates.md`
- [ ] lane-b-sender — report: `docs/notifications/reports/lane-b-sender.md`
- [ ] lane-b-docs — report: `docs/notifications/reports/lane-b-docs.md`
