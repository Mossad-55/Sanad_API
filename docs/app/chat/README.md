# Chat

Chat routes are authenticated and live under `/api/v1/chat`. Every route
resolves the acting participant from the JWT; unknown or foreign
conversations, attachments, and block targets answer a privacy-preserving
`404`. Hiding removes a conversation for the deleter only — nothing is ever
erased, and a blocked sender must never learn that a block exists.

- [Conversations, attachments, read marks, blocks, and hiding (Lane A, endpoints 1–2, 5–7, 9–10)](conversations.md)
- [Messages, unread counts, delivery and read ticks (Lane B, endpoints 3–4, 8)](messages.md)
- [Reporting and the Support queue (Lane B, endpoints 11–15)](reports.md)
- [Pricing insights (Chat Plus: market ranges, null semantics, attribution)](pricing-insights.md)
- [Sync and recovery (Chat Plus: cursor lifecycle, polling, dedupe)](sync.md)
- [Realtime hub, typing, and presence (Chat Plus: transport, events, budgets)](realtime.md)
- [Operations (Chat Plus: topology, config, dispatch, migrations, Firebase/APNs checklist)](operations.md)
- [Postman Chat collection](../../postman/app/Ahlena.App.Chat.postman_collection.json)
- [Postman Admin Chat reports collection](../../postman/admins/Ahlena.Admin.Chat.postman_collection.json)

Examples across these pages are statically authored from the verified code
paths — no live API/DB run backs them. Run stateful requests only against
disposable fixtures.
