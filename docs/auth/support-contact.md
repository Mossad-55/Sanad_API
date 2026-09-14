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

Validation limits:

- `subject`: 3–150 characters
- `message`: 10–2000 characters

| HTTP | `code` |
|---|---|
| 400 | `Api.Validation.Failed` |
| 401 | `Identity.Account.UserNotFound` |
| 404 | `Identity.Account.UserNotFound` |
| 409 | `Identity.Account.InvalidOperation` |

The admin reply feature is a separate feature not covered here.

## Help Center support contact card (separate, read-only)

SET-13 adds a **separate read-only route** for the hotline/contact card shown
on the Help Center screen. It only returns the one global support phone +
support email pair configured by a CMS admin; it never sends an SMS, never
sends an email, and does not interact with the ticket route above.

```text
GET /api/v1/help-center          (Normal JWT; audience derived from the token)
```

`200` response (excerpt — FAQ list omitted):

```json
{
  "faqs": [],
  "supportContact": {
    "supportPhone": "+201000000001",
    "supportEmail": "support@sanad.example"
  }
}
```

When no contact has been configured yet, the same route returns `200` with
`"supportContact": null` — the app hides the card. The pair is global: every
app role sees the same phone and email. Admin management (and the explicit
rule that `POST /api/v1/support/contact` is unchanged by it) lives in
`docs/admin/help-center.md`; the app surface is documented in
`docs/app/settings/help-support.md`.
