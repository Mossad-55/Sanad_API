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

- [x] Complete — Seam review: task reports record DTO/error-code/template-key contract mapping; the approved registration-token contract is used by the sender.
- [x] Complete — Full solution build with `--warnaserror` (0 warnings) + full unit + architecture suites; see `lane-a-sender.md` for latest counts.
- [x] Complete — `git diff --check` and changed Postman JSON validation; both touched collections parsed successfully.
- [ ] Not started — Live Firebase verification only with explicit owner authorization + test device; Development stub otherwise.

## Ready for integration (Integrator-owned discovery)

A task is mergeable only if ALL hold: its box below is checked, its report
file `docs/notifications/reports/<slug>.md` exists with branch + base/final
implementation SHAs + gate evidence, its dependencies are already in `main`,
and the reported final implementation SHA is an ancestor of the current branch
tip. The branch tip may include later report or closeout commits: requiring a
tracked report to contain the exact SHA of the commit that contains that same
report is self-referential. The Integrator verifies the ancestor relationship,
reviews the complete base-to-tip diff and every later commit, and blocks any
unreported or out-of-scope change. On owner trigger "integrate now", the
Integrator scans these boxes, merges qualifying tasks in dependency order
(Lane A before Lane B), marks each merged box with the resulting `main` SHA,
and pushes `main` once. Workers never merge or push; workers write the report
file as part of Done.

- [x] lane-a-registry — report: `docs/notifications/reports/lane-a-registry.md` — merged into main at `9005f145a4bdec7a24f86e50452b72547cd0d3cd`
- [x] lane-a-sender — report: `docs/notifications/reports/lane-a-sender.md` — merged into main at `9005f145a4bdec7a24f86e50452b72547cd0d3cd`
- [x] lane-a-outbox — report: `docs/notifications/reports/lane-a-outbox.md` — merged into main at `9005f145a4bdec7a24f86e50452b72547cd0d3cd`
- [x] lane-a-fanout — report: `docs/notifications/reports/lane-a-fanout.md` — merged into main at `9005f145a4bdec7a24f86e50452b72547cd0d3cd`
- [x] lane-a-docs — report: `docs/notifications/reports/lane-a-docs.md` — merged into main at `9005f145a4bdec7a24f86e50452b72547cd0d3cd`
- [x] lane-b-templates — report: `docs/notifications/reports/lane-b-templates.md` — merged into main at `9005f145a4bdec7a24f86e50452b72547cd0d3cd`
- [x] lane-b-sender — report: `docs/notifications/reports/lane-b-sender.md` — merged into main at `9005f145a4bdec7a24f86e50452b72547cd0d3cd`
- [x] lane-b-docs — report: `docs/notifications/reports/lane-b-docs.md` — merged into main at `9005f145a4bdec7a24f86e50452b72547cd0d3cd`
