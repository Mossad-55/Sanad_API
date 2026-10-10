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

- [x] Done — Token registry + register/list/unregister endpoints with ownership; focused tests. Report: `docs/notifications/reports/lane-a-registry.md`.
- [x] Done — `IPushSender` + FCM sender (pinned FirebaseAdmin 3.7.0) + Development stub; config-gated, fail-closed; focused tests (no secrets in logs). Report: `docs/notifications/reports/lane-a-sender.md`.
- [x] Done — Push outbox + processor with capped backoff and invalid-token retirement; focused tests. Report: `docs/notifications/reports/lane-a-outbox.md`.
- [x] Done — Fan-out per the applicable matrix rows (preferences checked server-side); focused tests. Report: `docs/notifications/reports/lane-a-fanout.md`.
- [x] Done — Docs + Postman sync; contract-mapping check. Report: `docs/notifications/reports/lane-a-docs.md`.

Done criteria per item: zero-warning build, focused tests green, docs + Postman synced, branch + SHA reported. No push/merge/deploy. No live Firebase calls except against the real project with explicit per-run owner authorization and a test device.

## Lane B — Email overhaul (assignee TBD)

Owns: `Identity/Infrastructure/Messaging/EmailTemplates/*` (layout + 16 template variants),
sender `multipart/alternative` upgrade, outbox enqueueing for notification emails,
`tests/.../EmailTemplateTests.cs`, docs + Postman deltas.

- [x] Done — Shared bilingual layout (Arabic-first RTL + English below, text wordmark, system fonts) + all 16 listed template variants with plain-text twins; snapshot tests (no sending). Owner clarified renewal-upcoming uses the renewed template with upcoming copy; plan-retired shares the changed template with event-specific copy. Report: `docs/notifications/reports/lane-b-templates.md`.
- [x] Done — Sender upgrade to `multipart/alternative`; outbox enqueue for existing notification emails (render-at-enqueue snapshots); existing `MessageId` stability kept; focused tests. Report: `docs/notifications/reports/lane-b-sender.md`.
- [x] Done — Docs + Postman sync; contract-mapping check. Report: `docs/notifications/reports/lane-b-docs.md`.

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

- [x] lane-a-registry — report: `docs/notifications/reports/lane-a-registry.md`
- [x] lane-a-sender — report: `docs/notifications/reports/lane-a-sender.md`
- [x] lane-a-outbox — report: `docs/notifications/reports/lane-a-outbox.md`
- [x] lane-a-fanout — report: `docs/notifications/reports/lane-a-fanout.md`
- [x] lane-a-docs — report: `docs/notifications/reports/lane-a-docs.md`
- [x] lane-b-templates — report: `docs/notifications/reports/lane-b-templates.md`
- [x] lane-b-sender — report: `docs/notifications/reports/lane-b-sender.md`
- [x] lane-b-docs — report: `docs/notifications/reports/lane-b-docs.md`
