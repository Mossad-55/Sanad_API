# Chat API — reporting and the Support queue

Reporting spans two surfaces of the frozen contract in `docs/chat/plan.md`:
the participant's submit route (endpoint 11, under `/api/v1/chat` with the
same JWT-participant posture as [messages.md](messages.md)) and the Support
review routes (endpoints 12–15, under `/api/v1/admin/chat/reports`). Reports
carry a required reason (1–500 characters) and resolve exactly once, with
actor/time audit. Nothing is ever erased: both outcomes retain the report row,
its reason, and the conversation history.

## Submit a report — `POST /api/v1/chat/reports`

```json
{
  "conversationId": "0198e2c2-4444-7a10-8000-00000000c001",
  "messageId": null,
  "reason": "Insults and harassment in the last two days"
}
```

Success is `201` with only the new report's id:

```json
{
  "reportId": "0198e2c2-4444-7a10-8000-00000000e001"
}
```

- **Who may report:** only a participant of the conversation. Hiding does not
  matter — a hidden conversation can still be reported by its participant
  (reporting is exactly how a conversation reaches Support).
- `messageId` is optional: `null` reports the conversation as a whole; a value
  must be a message **of that conversation** — unknown ids, the empty GUID,
  and another conversation's message id are indistinguishable privacy `404`s
  (leaking which message ids exist would leak content).
- `reason` is trimmed and must be 1–500 characters; anything else is the
  caller's own `400 Chat.Report.Invalid`.
- **Gate-then-validate order:** conversation membership and message ownership
  run *before* reason validation, so a non-participant probing with a
  deliberately bad reason still gets the generic privacy `404` and can never
  use the endpoint as a conversation-id oracle. Unknown or foreign
  conversation ids answer the same generic `404` (`Chat.Report.NotFound`,
  generic body). A missing or unparseable JSON body is rejected by MVC model
  binding first with a generic validation `400` (no Chat code).
- Submitting is not idempotent: two submits create two reports (there is no
  per-conversation dedupe in V1).

The new report enters the queue as `Open`. Review, dismiss, and uphold are
Support's side of the flow.

## Support authorization — `ChatSupportAdmin`

Endpoints 12–15 sit behind the `ChatSupportAdmin` policy, mirroring the
elderly-ops policies: an authenticated account with **normal** access type and
account type **SuperAdmin or SupportAdmin**. No other role receives
chat-admin rights. Anonymous callers receive `401`; an authenticated account
without the policy's claims receives `403`. The actor recorded in resolution
audit comes from the JWT `sub` claim. There is deliberately no participant
check in any of these handlers — the policy *is* the authorization.

**The reported-only boundary (locked).** This route family is the **only**
non-participant read path in the whole chat slice, and its currency is the
report id: a Support admin can read a conversation's messages only through
`GET …/reports/{id}/messages` for a report that exists. There is no general
chat access for Support — no list, no open, no arbitrary conversation read —
and these routes are read-only with respect to conversation state: they never
stamp delivery, never write read state, never send, and never mutate anything
except the report (and, on uphold, the block rows) through 14/15.

## Report queue — `GET /api/v1/admin/chat/reports?status=&page=&pageSize=`

```text
GET /api/v1/admin/chat/reports?status=Open&page=1&pageSize=20
```

`200` as the standard `PagedResult` envelope, newest report first
(`createdOnUtc` descending, id descending as the tie-break):

```json
{
  "items": [
    {
      "reportId": "0198e2c2-4444-7a10-8000-00000000e001",
      "conversationId": "0198e2c2-4444-7a10-8000-00000000c001",
      "messageId": "0198e2c2-4444-7a10-8000-00000000b002",
      "reporterUserId": "00000000-0000-0000-0000-000000000002",
      "reason": "Insults and harassment in the last two days",
      "status": "Open",
      "createdOnUtc": "2026-10-11T09:30:00Z",
      "resolvedOnUtc": null,
      "resolvedBy": null
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 1,
  "totalPages": 1
}
```

- `status` is optional: absent/blank means no filter; otherwise it must name
  `Open`, `Dismissed`, or `Upheld` (matched case-insensitively; a defined
  numeric enum value is also accepted). An unrecognised value is the caller's
  own `400 Chat.Report.Invalid` — silently answering with an unfiltered page
  would hide a typo.
- `page` is clamped up to a minimum of 1; `pageSize` is clamped to 1–100
  (default 20).
- The `ReportResponse` row is the same shape the detail view returns, so a row
  the queue shows is exactly what the detail expands.

## Report detail — `GET /api/v1/admin/chat/reports/{reportId}`

`200` with the full `ReportResponse` (shape above): stored report fields
(`reportId`, `conversationId`, optional `messageId`, `reporterUserId`,
`reason`), the lifecycle `status`, and the resolution audit — `resolvedOnUtc`
and `resolvedBy` (the acting admin's user id) once Support closed it, `null`
on both while `Open`. Unknown report id → `404` (plain, with code
`Chat.Report.NotFound`; the admin route never privacy-forces because Support
is never a participant).

## Reported-conversation messages — `GET /api/v1/admin/chat/reports/{reportId}/messages`

The **only non-participant read path** in the slice. `200` with an array of
every message of the reported conversation, newest first, in the endpoint 3
item shape:

```json
[
  {
    "id": "0198e2c2-4444-7a10-8000-00000000b002",
    "conversationId": "0198e2c2-4444-7a10-8000-00000000c001",
    "clientMessageId": "web-0002",
    "senderUserId": "00000000-0000-0000-0000-000000000001",
    "type": "Text",
    "text": "You people never listen — I am done with this",
    "attachment": null,
    "location": null,
    "createdOnUtc": "2026-10-11T09:15:30Z",
    "deliveredOnUtc": "2026-10-11T09:16:02Z",
    "readOnUtc": "2026-10-11T09:20:11Z"
  }
]
```

- The report id is the caller's entire authorization: it must exist, and the
  conversation it points at must still exist (a report cascades with its
  conversation, so that row cannot go missing through any supported path).
  Either absence is the same plain `404`.
- **Read-only:** unlike endpoint 3 this path never stamps `deliveredOnUtc`,
  never writes a read state, and never saves — Support observes, it does not
  participate. The `readOnUtc` tick uses the same rule as endpoint 3 with the
  reader fixed to whoever did not send the message (the only reader that
  exists once the viewer is not a participant), so this view can never
  disagree with what the participants saw.
- No cursor, no filter: the whole reported conversation is returned in one
  array — **for conversations with at most 1,000 messages**. Above that bound
  the array endpoint answers `409` with `Chat.Report.HistoryRequiresPagination`
  (never a truncated success); the endpoint is deprecated for large histories.
- **Paged history (Chat Plus):**
  `GET /api/v1/admin/chat/reports/{reportId}/messages/page?beforeId=&pageSize=30`
  returns the existing `MessageCursorPage` shape (newest first, `beforeId`
  paging, page size clamped 1–100, default 30), read-only like the array
  endpoint. Migrate the admin client to the paged endpoint before the array
  endpoint is removed.

## Dismiss — `POST /api/v1/admin/chat/reports/{reportId}/dismiss` → `204`

Closes the report without action. The reason, the reporter, and the reported
conversation stay readable through 12–13; the resolution stamps `resolvedBy`
(admin JWT user id) and `resolvedOnUtc` (server UTC). Nothing else changes:
no block is created, and the participants' chat is untouched.

## Uphold — `POST /api/v1/admin/chat/reports/{reportId}/uphold` → `204`

Records the violation (status + the same actor/time audit) **and applies a
mutual chat-block** between the reported conversation's two participants: one
block row per direction, created with the same idempotent pre-check the block
endpoint uses, so a party who already blocked the other never produces a
duplicate row (the unique pair index is the database backstop).

- The outcome is **atomic**: the Open-only guard runs before any block is
  created and everything lands in one save — a rejected re-uphold can never
  leave a partial block behind, and pre-existing block rows are left exactly
  as they are.
- The mutual block is the same state endpoint 9 manages, so it is visible in
  each party's blocked list and behaves per the locked generic-failure rule in
  messages.md: after uphold, neither participant can send to the other — each
  blocked sender receives the generic privacy `404`, never a block-specific
  code. Unblock (endpoint 9) removes one direction's row and restores that
  direction's sending; the upheld status on the report does not change.

## Report lifecycle

| Entity | States and transitions |
|---|---|
| Report | created `Open` by endpoint 11 (`201`, at most 5/hour/user per `429` abuse control) → `Dismissed` by 14 (`204`, history retained) **or** `Upheld` by 15 (`204`, violation + mutual block). A report leaves `Open` exactly once via one conditional commit — concurrent resolves race and the loser takes `409`; there is no reopen, no edit, and no delete. A second resolve in either direction is `409`. |
| Blocks (on uphold) | mutual pair created in one save with the pair's pre-existing rows respected; afterwards managed only through endpoint 9 (and future upholds). |

## Timestamps and retention

- Every stamp is server-set UTC: `createdOnUtc` on submit,
  `resolvedOnUtc` on dismiss/uphold.
- **Retention is indefinite** (locked): report rows, reasons, resolution
  audit, conversation history, and messages are never erased by any outcome.
  Dismiss closes with history; uphold records and blocks with history.

## Error reference

Every body below is `application/problem+json` with `type`, `title`, `status`,
`detail`, `instance`, `code`, and `traceId`.

| Code / condition | HTTP | Routes | Trigger |
|---|---|---|---|
| (no token or no usable `sub` claim) | `401` | 11, 12–15 | Anonymous or unusable JWT |
| (authenticated but not `ChatSupportAdmin`) | `403` | 12–15 | Account type other than SuperAdmin/SupportAdmin, or non-normal access type |
| `Chat.Report.NotFound` | `404` generic privacy | 11 | Unknown/foreign conversation; `messageId` unknown, empty GUID, or belonging to another conversation |
| `Chat.Report.NotFound` | `404` plain | 12–15 | Unknown report id; reported conversation missing (integrity backstop on 13/15) |
| `Chat.Report.Invalid` | `400` | 11 | `reason` empty, missing, or outside 1–500 characters after trim |
| `Chat.Report.Invalid` | `400` | 12 | Unrecognised `?status=` filter value |
| `Chat.Report.Invalid` | `400` | 14, 15 | Acting admin token carries no usable user id (audit would be meaningless) |
| `Chat.Report.InvalidState` | `409` | 14, 15 | The report is already resolved — dismiss-after-dismiss, uphold-after-dismiss, or the reverse; uphold touches no block row on this path |
| `Chat.Report.HistoryRequiresPagination` | `409` | 13 (array) | Reported conversation holds more than 1,000 messages — use `GET …/messages/page` |
| (MVC model-binding validation) | `400` | 11 | Missing/unparseable JSON body or non-GUID conversation id — generic `errors` payload, no Chat code |

On the app route (11) `Chat.Report.NotFound` is rewritten by the Chat
controller to the generic privacy `404`. On the admin routes (12–15) the same
code renders as a plain `404` (`title: "Not Found"`,
`detail: "The requested resource was not found."`) — Support is authorised to
see reported conversations at all, so no privacy forcing applies, and the
`code` extension survives so a client can still key on `Chat.Report.*`. The
`409` likewise keeps `Chat.Report.InvalidState` with the generic conflict
detail. Validation `400`s keep their `code`.

## Privacy boundaries (summary)

- `401`/`403` gate the Support surface; participants can submit reports but
  can never read the queue, a report, or another conversation through it.
- On the app route, unknown/foreign conversations and message ids are the
  generic privacy `404` — reporting can never be used to probe ids.
- Support's read power stops at reported conversations and is read-only; no
  route in this family sends, stamps, hides, or erases anything.
- Uphold's mutual block reveals itself to neither participant at send time
  (generic failure, per messages.md).

All examples on this page are statically authored from the verified code paths
and lane reports (`docs/chat/reports/lane-b-*.md`); they were never executed
against a live API or database. Matching Postman requests:
[Sanad.App.Chat.postman_collection.json](../../postman/app/Sanad.App.Chat.postman_collection.json)
(endpoint 11) and
[Sanad.Admin.Chat.postman_collection.json](../../postman/admins/Sanad.Admin.Chat.postman_collection.json)
(endpoints 12–15).
