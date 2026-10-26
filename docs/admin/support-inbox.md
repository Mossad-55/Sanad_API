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
GET  /api/v1/admin/support-tickets/{id}            One ticket with its replies -> 200 | 404
POST /api/v1/admin/support-tickets/{id}/status     Move the status (forward only) -> 204 | 404
POST /api/v1/admin/support-tickets/{id}/replies    Add an admin reply -> 200 | 404
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
  `{ ticketId, userId, displayName, accountType, subject, status, createdOnUtc }`.

`GET /api/v1/admin/support-tickets/{id}` returns
`{ ticketId, userId, displayName, accountType, subject, message, status,
createdOnUtc, notifiedOnUtc, statusChangedByUserId, statusChangedOnUtc,
replies[] }`, each reply being
`{ replyId, actorUserId, body, createdOnUtc }` ordered oldest first. An unknown
id is `404 Identity.Support.NotFound`.

### Sender display fields

`displayName` and `accountType` describe the ticket's sender. They are resolved
in **one batched Identity lookup per page** (never one lookup per row), reusing
the existing display read:

- `displayName` — the sender's English full name, falling back to the Arabic
  full name. `string?`: `null` when the sender row is missing or anonymized.
- `accountType` — the sender's **first-registered** account type, as its numeric
  `AccountType` value (Family = 1, MedicalCaregiver = 2, CompanionCaregiver = 3,
  Elderly = 4, SuperAdmin = 5, ContentAdmin = 6, SupportAdmin = 7,
  CareHomeOwner = 8, FinanceAdmin = 9). `int?`: `null` when the sender row is
  missing or anonymized.

The sender's **email and phone number are intentionally not returned** on either
route, and no other sender profile field is added. An unresolvable sender
degrades to both fields being `null` — the inbox read never fails for it and no
placeholder name is invented.

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

- `body` is required and accepts **1–2000 characters measured on the trimmed
  text** — the existing `SupportTicketReply.Create` rule: the stored reply is
  the trimmed value. There is deliberately **no `maxLength` on the request
  schema**: the cap applies to consumed text, not the raw JSON value, and a
  whitespace-padded body whose trimmed length fits is accepted (its stored
  reply is the trimmed form). A blank (whitespace-only) body or a body whose
  trimmed length exceeds 2000 reaches the domain guard and fails as it always
  has — the API publishes no raw schema cap for it (trimmed-oversize input
  ends in an error response, not a validation 400). Accepted values and read
  backs are unchanged (no extra trimming beyond the domain's existing trim).
- The reply is persisted **before** any notification is attempted, and the
  response returns it: `{ replyId, actorUserId, body, createdOnUtc }`.
- Replying to a `Closed` ticket is refused with
  `409 Identity.Support.Conflict` and nothing is written.
- Delivery is best effort. The owner-support email and the in-app notification
  are each attempted in isolation and a failed or unavailable channel is
  swallowed: it never fails the request and never removes the reply, which the
  owner still reads through `GET /api/v1/support/tickets/{id}`. Nothing is ever
  reported as delivered when delivery could not be attempted.
- The in-app notification stores the administrator-authored body **byte for
  byte** — the text is never reworded, translated, or trimmed by the API — and
  only its title is localized for the ticket owner's persisted UI language
  (`رد الدعم` for Arabic, `Support reply` for English; Arabic is the default for
  an owner with no persisted language). The category/type
  (`Support`/`SupportReply`) and the `SupportTicket` destination stay unchanged.

## Partner contacts (anonymous submissions)

The same inbox also carries the **anonymous partner-contact submissions** from
`POST /api/v1/partner-contact` (public, no account — see
[partner contact](../app/public/partner-contact.md)). They are a separate
aggregate with their own rows, because a support ticket always belongs to a
signed-in user and a partner contact has no identity to borrow.

```text
GET  /api/v1/admin/support-tickets/partner-contacts           List -> 200
GET  /api/v1/admin/support-tickets/partner-contacts/{id}      One submission -> 200 | 404
```

- Same policy (`SupportInboxAdmin`), same paging (`page` defaults to `1`,
  `pageSize` defaults to `25`, clamped to `1–100`), newest first.
- List item: `{ contactId, name, company, phone, companyType, city, email,
  subject, createdOnUtc }`.
- Detail: the same facts plus `message`.
- `name` and `email` are the submitter's own submitted details (request data);
  `company`, `phone`, `companyType` and `city` are `null` when they were not
  submitted.
- The anti-spam client key (the server-side digest of the caller's address) is
  **never** returned by either route.
- An unknown id is `404 Identity.PartnerContact.NotFound`.

The rows live in `identity.partner_contacts`, created by the additive Identity
migration `20261021205122_AddPartnerContacts`; the optional
`phone`/`company_type`/`city` columns come from the later additive
`20261022094500_AddPartnerContactOptionalFields`.

## Errors

| HTTP | `code` | When |
|---|---|---|
| 400 | *(bare, no code)* | `status` outside the defined 1–4 values |
| 401 | *(bare, no code)* | Missing/invalid/expired JWT |
| 403 | *(bare, no code)* | Policy rejection for accounts without `SupportInboxAdmin` |
| 404 | `Identity.Support.NotFound` | Unknown ticket id — detail, status and reply only; the list has no not-found path |
| 404 | `Identity.PartnerContact.NotFound` | Unknown partner-contact id |
| 409 | `Identity.Support.Conflict` | Refused transition or a reply to a closed ticket |

## Storage

`identity.support_tickets` gains the nullable `status_changed_by_user_id` and
`status_changed_on_utc` columns, and `identity.support_ticket_replies` is added
(reply id, ticket, actor, body, UTC instant). Both come from the additive
Identity migration `20261020191733_AddSupportTicketWorkflow` — see
`docs/internal/operations/migrations.md`.

The anonymous partner-contact rows live in `identity.partner_contacts`
(id, submitted name/company/phone/company-type/city/email/subject/message,
idempotency key, the server-side client-key digest, UTC instant), created by
the additive Identity migration `20261021205122_AddPartnerContacts` and
extended by the additive `20261022094500_AddPartnerContactOptionalFields`
(nullable `phone`, `company_type`, `city`; rows created before it read back
with all three `null`).

## Postman

`docs/postman/admins/Sanad.Admin.postman_collection.json` → folder
"21. Support inbox (SuperAdmin/SupportAdmin)".
