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
