# Chat slice — task checklist

Single implementation checklist for the Chat slice. Statuses: Not started,
In progress, Blocked, Done. The contract in `plan.md` is frozen; changing it
needs a new explicit owner decision. Lane file manifests are exclusive: a
worker edits only its owned files and never another lane's.

## Lane A — conversations, files, blocks, hiding (lands first)

Owns: `Sanad.API/Controllers/ChatController.cs` (endpoints 1–2, 5–7, 9–10),
`Sanad.API/Controllers/Requests/Chat*Request.cs`,
`Modules/Chat/Domain` (Conversation, Attachment, Block),
`Modules/Chat/Infrastructure` (context, configs, migration, DI),
`Modules/Chat/Application/Conversations` (+ attachments/blocks handlers),
`tests/.../Chat/ConversationTests.cs`,
`docs/app/chat/conversations.md` + Postman App chat folder (endpoints 1–2, 5–7, 9–10),
`docs/postman` JSON validation for touched collections.

- [ ] Not started — Conversation + Block domain with open-or-get, hide, and block invariants; focused domain tests.
- [ ] Not started — Persistence: context, configurations, one additive migration (authored only), DI registration.
- [ ] Not started — Conversation open/list/detail + hide endpoints with participant authorization; focused handler/controller tests.
- [ ] Not started — Attachment upload/serve with type/size/signature validation and single-use binding; focused tests with real temp-root storage.
- [ ] Not started — Block/unblock/list endpoints; focused tests (generic sender failure, unblock restore).
- [ ] Not started — Docs + Postman sync for Lane A routes; contract-mapping check.

Done criteria per item: zero-warning build, focused tests green, docs + Postman synced, branch + SHA reported. No push/merge/deploy.

## Lane B — messages, reads, reports, notifications (builds on Lane A)

Owns: `Modules/Chat/Domain` (Message, Report),
`Modules/Chat/Application/Messages` (+ reports handlers),
`Sanad.API/Controllers/ChatController.cs` (endpoints 3–4, 8, 11),
`Sanad.API/Controllers/AdminChatReportsController.cs` (endpoints 12–15),
`Modules/Notifications` producer (one file + tests),
`tests/.../Chat/MessageTests.cs` + `ReportTests.cs`,
`docs/app/chat/messages.md` + `reports.md` + Postman App/Admin additions.

- [ ] Not started — Message + Report domain (idempotency key uniqueness, read/delivered stamping rules, report lifecycle); focused domain tests. May start against the frozen contract in parallel with Lane A.
- [ ] Not started — Message send/list/receipts/unread-count endpoints with participant authorization; focused handler/controller tests.
- [ ] Not started — Report submit + Support review/uphold/dismiss endpoints under `ChatSupportAdmin`; mutual-block on uphold; focused tests.
- [ ] Not started — New-message in-app alert producer through the existing notification seam (ids only); focused tests.
- [ ] Not started — Docs + Postman sync for Lane B routes; contract-mapping check.

Done criteria per item: same as Lane A. Lane B integrates after Lane A lands.

## Joint gates (Integrator-owned)

- [ ] Not started — Seam review: DTOs + error codes match `plan.md` exactly.
- [ ] Not started — Full solution build (0 warnings) + full unit + architecture suites once.
- [ ] Not started — `git diff --check`, Postman JSON validation.
- [ ] Not started — Risk-based Bruno on disposable fixtures only, only if the contract demands live evidence.

## Ready for integration (Integrator-owned discovery)

A task is mergeable only if ALL hold: its box below is checked, its report
file `docs/chat/reports/<slug>.md` exists with branch + base/final SHAs +
gate evidence, its dependencies are already in `main`, and the branch tip
equals the reported final SHA. Otherwise it is skipped as blocked (never
force-merged). On owner trigger "integrate now", the Integrator scans these
boxes, merges qualifying tasks in dependency order (Lane A before Lane B),
marks each merged box with the `main` SHA, and pushes `main` once. Workers
never merge or push; workers write the report file as part of Done.

- [ ] lane-a-domain — report: `docs/chat/reports/lane-a-domain.md`
- [ ] lane-a-persistence — report: `docs/chat/reports/lane-a-persistence.md`
- [ ] lane-a-endpoints — report: `docs/chat/reports/lane-a-endpoints.md`
- [ ] lane-a-attachments — report: `docs/chat/reports/lane-a-attachments.md`
- [ ] lane-a-blocks — report: `docs/chat/reports/lane-a-blocks.md`
- [ ] lane-a-docs — report: `docs/chat/reports/lane-a-docs.md`
- [ ] lane-b-domain — report: `docs/chat/reports/lane-b-domain.md`
- [ ] lane-b-endpoints — report: `docs/chat/reports/lane-b-endpoints.md`
- [ ] lane-b-reports — report: `docs/chat/reports/lane-b-reports.md`
- [ ] lane-b-notifications — report: `docs/chat/reports/lane-b-notifications.md`
- [ ] lane-b-docs — report: `docs/chat/reports/lane-b-docs.md`
