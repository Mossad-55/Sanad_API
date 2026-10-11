# Chat

Chat routes are authenticated and live under `/api/v1/chat`. Every route
resolves the acting participant from the JWT; unknown or foreign
conversations, attachments, and block targets answer a privacy-preserving
`404`. Hiding removes a conversation for the deleter only — nothing is ever
erased, and a blocked sender must never learn that a block exists.

- [Conversations, attachments, read marks, blocks, and hiding (Lane A, endpoints 1–2, 5–7, 9–10)](conversations.md)
- [Postman Chat collection](../../postman/app/Sanad.App.Chat.postman_collection.json)

Messages, unread counts, reports, and the support queue (endpoints 3–4, 8,
11–15) arrive with Lane B and are not documented here yet. Examples are
statically authored from the verified code paths — no live API/DB run backs
them. Run stateful requests only against disposable fixtures.
