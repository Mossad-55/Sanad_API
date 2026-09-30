# Elderly help requests

Help requests are created by the authenticated Elderly account from the
current active bilingual sentence-builder catalog. The server resolves the
linked profile; the client cannot submit another Elderly or Family ID.

## Sentence-builder fields

`actorKey`, `actionKey`, and `needKey` are required active catalog keys.
`qualifierKey` and `customText` are optional. `customText`, when supplied, is
plain text and is limited to 500 characters. The catalog is bilingual and
separates `Actor`, `Action`, `Need`, and `Qualifier` categories; clients should
not hard-code vocabulary or assume combinations not represented by active
catalog entries. Speech-to-text remains client-side; no audio is uploaded.

## Create

`POST /api/v1/elderly/help-requests`

Requires a Normal Elderly JWT and an `Idempotency-Key` header. The key is scoped
to the Elderly actor. A successful request starts as `Pending` and stores the
resolved bilingual catalog labels. The response contains the request ID,
identity-bound Elderly ID, selected keys, optional custom text, status, and UTC
created/updated timestamps.

```json
{
  "actorKey": "self",
  "actionKey": "need",
  "needKey": "water",
  "qualifierKey": "now",
  "customText": null
}
```

Reusing the same key with the same normalized payload returns the original
request. Reusing it with a different payload returns `409` with
`Families.HelpRequest.IdempotencyConflict`. The request is persisted before a
durable in-app alert is created for each active linked Family member whose
`helpRequestAlerts` preference is enabled, the assigned caregiver while an
active `Confirmed` or `InProgress` booking exists and the caregiver account is
active with `helpRequestAlerts` enabled, and active `SupportAdmin` accounts.
The Elderly profile must match both the authenticated identity and resolved
profile; a missing or mismatched profile produces no recipients. If any
required Identity recipient query fails, fan-out returns no recipients (fail
closed). Alert creation remains idempotent per request and recipient. Push,
email, provider delivery, outbox, and retry remain deferred to
Notifications/Events; SMS is excluded.

## List and detail

- `GET /api/v1/elderly/help-requests`
- `GET /api/v1/elderly/help-requests/{requestId}`

The list returns the caller's requests from the last year, newest first. Detail
returns one request object (not a list) for the requested ID. A missing request,
foreign request ID, deleted/inactive Family, or request outside the availability
window is reported as `404 Families.HelpRequest.NotFound` without disclosing
another user's data.

## Cancel

`POST /api/v1/elderly/help-requests/{requestId}/cancel`

The optional body is `{ "reason": "..." }`; a reason is trimmed and may not
exceed 500 characters. Cancellation is valid from `Pending` and is recorded in
the append-only history. Success returns the updated request object; attempting
to cancel again after a terminal transition returns `409
Families.HelpRequest.InvalidOperation`.

## Status lifecycle

The persisted statuses are `Pending`, `Accepted`, `InProgress`, `Resolved`,
`Rejected`, `Cancelled`, and `Reopened`. Operational transitions are described
in the [Admin guide](../../admin/elderly-help-requests.md). Requests and
history are available for one year; this slice has no delete or export route.

Common failures are `409 Families.HelpRequest.InvalidOperation`, `401` for a
missing/invalid token, `403` for a non-Elderly or restricted token, and `404`
for an unavailable request.
