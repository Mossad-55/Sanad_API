# Contact support

A logged-in user submits a support request. The route is shared by every signed-in account type. The `UserId` comes from the JWT, never from the body.

Development base URL:

```text
https://localhost:7296
```

## POST `/api/v1/support/contact`

Submits a support request. The request body contains only `subject` and `message`. Identity fields are never read from the body.

```bash
curl -sS -X POST https://localhost:7296/api/v1/support/contact \
  -H "Authorization: Bearer ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "subject": "Help needed",
    "message": "I need assistance with my account."
  }'
```

`200` response:

```json
{
  "ticketId": { "value": "018f0000-0000-7000-8000-000000000001" },
  "status": 1,
  "submittedOnUtc": "2026-09-11T10:00:00Z"
}
```

- `ticketId`: the unique identifier of the support ticket
- `status`: `1` for `New`
- `submittedOnUtc`: the UTC timestamp when the ticket was created

The ticket is persisted before a support email is attempted. Email delivery failure does not fail the request or remove the ticket. Local tests should use the Development email sender to prevent outbound delivery.

Validation limits:

- `subject`: 3–150 characters
- `message`: 10–2000 characters

| HTTP | `code` |
|---|---|
| 400 | `Api.Validation.Failed` |
| 401 | `Identity.Account.UserNotFound` |
| 404 | `Identity.Account.UserNotFound` |
| 409 | `Identity.Account.InvalidOperation` |

## GET `/api/v1/support/contact` (anonymous)

The contact card without a session, for the pre-login support screen. The
response is exactly the same single global phone/email pair as the Help Center
card, and nothing else:

```bash
curl -sS https://localhost:7296/api/v1/support/contact
```

`200` response (both fields are `null` when no pair has been configured yet):

```json
{
  "supportPhone": "+201000000001",
  "supportEmail": "support@ahlena.example"
}
```

The route is `[AllowAnonymous]`. It never sends SMS or email and never touches
the ticket tables. Either field may be `null` on its own when the admin has
configured only one of them; clients show only the options that are set.

## GET `/api/v1/support/tickets`

The signed-in user's own tickets, newest first. `page` defaults to `1`;
`pageSize` defaults to `25` (clamped to `1-100`).

```json
{
  "items": [
    {
      "ticketId": "018f0000-0000-7000-8000-000000000001",
      "subject": "Help needed",
      "status": 2,
      "createdOnUtc": "2026-09-11T10:00:00Z"
    }
  ],
  "page": 1,
  "pageSize": 25,
  "totalCount": 1
}
```

## GET `/api/v1/support/tickets/{id}`

One of the caller's own tickets together with the admin replies, oldest first.
A ticket that belongs to another user — or does not exist — is
`404 Identity.Support.NotFound`, so the route never confirms someone else's
ticket.

```json
{
  "ticketId": "018f0000-0000-7000-8000-000000000001",
  "subject": "Help needed",
  "message": "I need assistance with my account.",
  "status": 2,
  "createdOnUtc": "2026-09-11T10:00:00Z",
  "notifiedOnUtc": "2026-09-11T10:00:05Z",
  "replies": [
    {
      "replyId": "018f0000-0000-7000-8000-000000000009",
      "actorUserId": "018f0000-0000-7000-8000-000000000010",
      "body": "We are looking into this.",
      "createdOnUtc": "2026-09-11T12:00:00Z"
    }
  ]
}
```

`status`: `1` New, `2` In Progress, `3` Resolved, `4` Closed. The admin inbox
that moves a ticket forward and answers it is documented in
`docs/admin/support-inbox.md`; the list and detail item ids are plain UUIDs
here (the submit response keeps its typed `{ "value": ... }` shape).

## Help Center support contact card (separate, read-only)

SET-13 adds a **separate read-only route** for the hotline/contact card shown
on the Help Center screen. It only returns the one global support phone +
support email pair configured by a CMS admin; it never sends an SMS, never
sends an email, and does not interact with the ticket route above.

```text
GET /api/v1/help-center[?audience=]   (Normal JWT; audience from the token)
```

The audience is derived from the token's owned accounts; a token that owns more
than one audience names the wanted one with `?audience=` (see
`docs/app/settings/help-support.md`). This changes nothing about the support
contact pair itself.

`200` response (excerpt — FAQ list omitted):

```json
{
  "faqs": [],
  "supportContact": {
    "supportPhone": "+201000000001",
    "supportEmail": "support@ahlena.example"
  }
}
```

When no contact has been configured yet, the same route returns `200` with
`"supportContact": null` — the app hides the card. An individually unset value
arrives as `null` inside a populated pair (for example
`{ "supportPhone": null, "supportEmail": "support@ahlena.example" }`), so the app
hides only the missing option. The pair is global: every
app role sees the same phone and email. Admin management (and the explicit
rule that `POST /api/v1/support/contact` is unchanged by it) lives in
`docs/admin/help-center.md`; the app surface is documented in
`docs/app/settings/help-support.md`.
