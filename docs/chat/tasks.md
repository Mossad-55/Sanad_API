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

- [x] Done — Conversation + Block domain with open-or-get, hide, and block invariants; focused domain tests.
- [x] Done — Persistence: context, configurations, one additive migration (authored only), DI registration.
- [x] Done — Conversation open/list/detail + hide endpoints with participant authorization; focused handler/controller tests.
- [x] Done — Attachment upload/serve with type/size/signature validation and single-use binding; focused tests with real temp-root storage.
- [x] Done — Block/unblock/list endpoints; focused tests (generic sender failure, unblock restore).
- [x] Done — Docs + Postman sync for Lane A routes; contract-mapping check.

Done criteria per item: zero-warning build, focused tests green, docs + Postman synced, branch + SHA reported. No push/merge/deploy.

## Lane B — messages, reads, reports, notifications (builds on Lane A)

Owns: `Modules/Chat/Domain` (Message, Report),
`Modules/Chat/Application/Messages` (+ reports handlers),
`Sanad.API/Controllers/ChatController.cs` (endpoints 3–4, 8, 11),
`Sanad.API/Controllers/AdminChatReportsController.cs` (endpoints 12–15),
`Modules/Notifications` producer (one file + tests),
`tests/.../Chat/MessageTests.cs` + `ReportTests.cs`,
`docs/app/chat/messages.md` + `reports.md` + Postman App/Admin additions.

- [x] Done — Message + Report domain (idempotency key uniqueness, read/delivered stamping rules, report lifecycle); focused domain tests. May start against the frozen contract in parallel with Lane A.
- [x] Done — Message send/list/receipts/unread-count endpoints with participant authorization; focused handler/controller tests.
- [x] Done — Report submit + Support review/uphold/dismiss endpoints under `ChatSupportAdmin`; mutual-block on uphold; focused tests.
- [x] Done — New-message in-app alert producer through the existing notification seam (ids only); focused tests.
- [x] Done — Docs + Postman sync for Lane B routes; contract-mapping check.

Done criteria per item: same as Lane A. Lane B integrates after Lane A lands.

## Joint gates (Integrator-owned — all run on merged `main` `4864a93`, 2026-10-11)

- [x] Done — Seam review: DTOs + error codes match `plan.md` exactly (lane-a-docs + lane-b-docs contract maps re-verified: 0 gaps; merged file list is owned Chat/docs/Postman + 4 known shared files).
- [x] Done — Full solution build (0 warnings, 0 errors, `TreatWarningsAsErrors`) + full unit (2822 passed, 0 failed, 3 Postgres-only skips) + architecture suite (1/1) once.
- [x] Done — `git diff --check` clean, Postman JSON validation (App + Admin Chat collections both parse).
- [x] Done — Risk-based Bruno: not selected (no live HTTP boundary beyond unit coverage; Postman examples statically validated, never executed). Recorded limitation, not a blocker.

## Ready for integration (Integrator-owned discovery)

A task is mergeable only if ALL hold: its box below is checked, its report
file `docs/chat/reports/<slug>.md` exists with branch + base/final SHAs +
gate evidence, its dependencies are already in `main`, and the branch tip
equals the reported final SHA. Otherwise it is skipped as blocked (never
force-merged). On owner trigger "integrate now", the Integrator scans these
boxes, merges qualifying tasks in dependency order (Lane A before Lane B),
marks each merged box with the `main` SHA, and pushes `main` once. Workers
never merge or push; workers write the report file as part of Done.

- [x] lane-a-domain — report: `docs/chat/reports/lane-a-domain.md` (task commit `60aed69`; merged into `main` via `5d26f9f`)
- [x] lane-a-persistence — report: `docs/chat/reports/lane-a-persistence.md` (task commits `9323639` + rescaffold addendum `c51de33`; merged via `5d26f9f`)
- [x] lane-a-endpoints — report: `docs/chat/reports/lane-a-endpoints.md` (task commits `de7a52d` + endpoint-7 addendum `c51de33`; merged via `5d26f9f`)
- [x] lane-a-attachments — report: `docs/chat/reports/lane-a-attachments.md` (task commit `c128c1e`; merged via `5d26f9f`)
- [x] lane-a-blocks — report: `docs/chat/reports/lane-a-blocks.md` (task commit `7a6c7cb`; merged via `5d26f9f`)
- [x] lane-a-docs — report: `docs/chat/reports/lane-a-docs.md` (task commit `ac6e7c2`; merged via `5d26f9f`)
- [x] lane-b-domain — report: `docs/chat/reports/lane-b-domain.md` (task commit `0c61f4c`, branch `opencode/chat-lane-b`; merged into `main` via `4864a93`)
- [x] lane-b-endpoints — report: `docs/chat/reports/lane-b-endpoints.md` (task commit `878140e`, branch `opencode/chat-lane-b`; merged via `4864a93`)
- [x] lane-b-reports — report: `docs/chat/reports/lane-b-reports.md` (task commit `952e49f`, branch `opencode/chat-lane-b`; merged via `4864a93`)
- [x] lane-b-notifications — report: `docs/chat/reports/lane-b-notifications.md` (task commit `0bbdbaa`, branch `opencode/chat-lane-b`; merged via `4864a93`)
- [x] lane-b-docs — report: `docs/chat/reports/lane-b-docs.md` (task commit `be880a6`, branch `opencode/chat-lane-b`; merged via `4864a93`)
