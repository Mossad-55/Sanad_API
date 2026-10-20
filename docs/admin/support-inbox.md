# Admin support inbox (F5)

The admin surface for the support tickets submitted through
`POST /api/v1/support/contact`. One inbox for the two admin roles that already
carry the operational support duty.

**Base:** `/api/v1/admin/support-tickets`
**Policy:** `SupportInboxAdmin` — a **normal** JWT with `account_type`
`SuperAdmin` or `SupportAdmin`. Every other account type (`ContentAdmin`,
`FinanceAdmin`, the app types) receives a bare `403`; a missing, expired or
non-normal token receives `401`.

## Endpoints

```text
GET  /api/v1/admin/support-tickets                 List (filters: status, page, pageSize) -> 200
GET  /api/v1/admin/support-tickets/{id}            One ticket with its replies -> 200
POST /api/v1/admin/support-tickets/{id}/status     Move the status (forward only) -> 204
POST /api/v1/admin/support-tickets/{id}/replies    Add an admin reply -> 200
```

All bodies are `application/json`. Both writes record the acting admin and a
UTC instant.

## Status contract

```text
SupportTicketStatus: New = 1, InProgress = 2, Resolved = 3, Closed = 4
```

Movement is **forward only**. A reply never changes the status, and nothing
reopens a closed ticket:

| From | Allowed to |
|---|---|
| `New` (1) | `InProgress` (2), `Resolved` (3), `Closed` (4) |
| `InProgress` (2) | `Resolved` (3), `Closed` (4) |
| `Resolved` (3) | `Closed` (4) |
| `Closed` (4) | *(nothing — terminal)* |

Any other pair — a backward move, a same-status no-op, or anything leaving
`Closed` — is refused with `409 Identity.Support.Conflict` and **nothing is
written**. A `status` outside the defined 1–4 values returns a bare `400`
before the ticket is read.

## List and detail

`GET /api/v1/admin/support-tickets?status=&page=1&pageSize=25`

- `status` is optional (a defined 1–4 value).
- `page` defaults to `1`; `pageSize` defaults to `25` and is clamped to `1–100`.
- Newest first by `createdOnUtc`.
- Response: `{ items, page, pageSize, totalCount }`, each item
  `{ ticketId, userId, subject, status, createdOnUtc }`.

`GET /api/v1/admin/support-tickets/{id}` returns
`{ ticketId, userId, subject, message, status, createdOnUtc, notifiedOnUtc,
statusChangedByUserId, statusChangedOnUtc, replies[] }`, each reply being
`{ replyId, actorUserId, body, createdOnUtc }` ordered oldest first. An unknown
id is `404 Identity.Support.NotFound`.

## Change status

```text
POST /api/v1/admin/support-tickets/{id}/status
body: { "status": 2 }                             -> 204 No Content
```

`statusChangedByUserId` (from the JWT) and `statusChangedOnUtc` (from the
clock) are recorded on the ticket and read back through the admin detail. A
refused transition is `409 Identity.Support.Conflict` (see the matrix above);
an unknown id is `404 Identity.Support.NotFound`.

## Reply

```text
POST /api/v1/admin/support-tickets/{id}/replies
body: { "body": "We are looking into this." }     -> 200 SupportTicketReplyDto
```

- The reply is persisted **before** any notification is attempted, and the
  response returns it: `{ replyId, actorUserId, body, createdOnUtc }`.
- Replying to a `Closed` ticket is refused with
  `409 Identity.Support.Conflict` and nothing is written.
- Delivery is best effort. The owner-support email and the in-app notification
  are each attempted in isolation and a failed or unavailable channel is
  swallowed: it never fails the request and never removes the reply, which the
  owner still reads through `GET /api/v1/support/tickets/{id}`. Nothing is ever
  reported as delivered when delivery could not be attempted.

## Errors

| HTTP | `code` | When |
|---|---|---|
| 400 | *(bare, no code)* | `status` outside the defined 1–4 values |
| 401 | *(bare, no code)* | Missing/invalid/expired JWT |
| 403 | *(bare, no code)* | Policy rejection for accounts without `SupportInboxAdmin` |
| 404 | `Identity.Support.NotFound` | Unknown ticket id |
| 409 | `Identity.Support.Conflict` | Refused transition or a reply to a closed ticket |

## Storage

`identity.support_tickets` gains the nullable `status_changed_by_user_id` and
`status_changed_on_utc` columns, and `identity.support_ticket_replies` is added
(reply id, ticket, actor, body, UTC instant). Both come from the additive
Identity migration `20261020191733_AddSupportTicketWorkflow` — see
`docs/internal/operations/migrations.md`.

## Postman

`docs/postman/admins/Sanad.Admin.postman_collection.json` → folder
"21. Support inbox (SuperAdmin/SupportAdmin)".
