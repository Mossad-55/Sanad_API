# Partner contact (public)

A partner/business contact request for **anonymous** visitors. It is the
public contact form path: the landing `contact-form` section, a partner
enquiry, or any visitor without an account.

**Route:** `POST /api/v1/partner-contact`
**Auth:** none — the route is anonymous (no `Authorization` header is read).
**Destination:** the existing **admin support inbox** — the submission is read
by the support-inbox admin routes.

There is **no account and no identity**: the submitter's details are stored as
**request data** on their own aggregate. No user row is created, no user id is
stored, and the API never pretends the submitter is a signed-in user.

## Request

```http
POST /api/v1/partner-contact
Content-Type: application/json
Idempotency-Key: partner-contact-1
```

```json
{
  "name": "Nour Hassan",
  "company": "Hassani Care",
  "phone": "+20 100 123 4567",
  "companyType": "Care home",
  "city": "القاهرة",
  "email": "nour@example.com",
  "subject": "Partnership",
  "message": "We would like to discuss a partnership."
}
```

| Field | Type | Rules |
|---|---|---|
| `name` | string | Required, 2–200 characters, trimmed. The submitter's own name. |
| `company` | string or `null` | Optional, ≤ 200 characters, trimmed. Omitted or blank is stored as `null`. This is the company **name** — separate from `companyType`. |
| `phone` | string or `null` | Optional, ≤ 32 characters. Digits with an optional leading `+` and the common separators (` `, `(`, `)`, `-`, `.`); at least one digit. Omitted or blank is stored as `null`. |
| `companyType` | string or `null` | Optional **free text**, ≤ 100 characters, trimmed. The API stores the submitted value as sent — there is no enum or allowed-values contract, and the form's select options are frontend-owned. Omitted or blank is stored as `null`. |
| `city` | string or `null` | Optional free text, ≤ 120 characters, trimmed (Arabic or any script). Omitted or blank is stored as `null`. |
| `email` | string | Required, a valid address, ≤ 320 characters, trimmed. The submitter's own email. |
| `subject` | string or `null` | Optional, ≤ 150 characters, trimmed. Omitted or blank is stored as `null`. |
| `message` | string | Required, 10–2000 characters, trimmed. |
| `Idempotency-Key` (header) | string | **Required**, ≤ 200 characters. See the anti-spam rules below. |

The three optional fields (`phone`, `companyType`, `city`) are additive: a
request without them is unchanged and still succeeds. They are part of the
idempotency content — the same `Idempotency-Key` replayed with a **different**
`phone`, `companyType` or `city` is the same conflict as any other content
difference.

## Success

`200 OK` with the receipt only — the submitted details are not echoed back:

```json
{
  "contactId": { "value": "0198e2c2-4444-7777-8888-000000000001" },
  "submittedOnUtc": "2026-10-21T12:00:00Z"
}
```

## Anti-spam (minimal, existing conventions)

- **Idempotency.** The same `Idempotency-Key` **with the same content** returns
  the stored contact again — no second row is created, so a client retry is
  safe. The same key **with different content** is
  `409 Identity.PartnerContact.IdempotencyConflict`.
- **Rate limit.** At most **5 submissions per hour per caller** (a sliding
  window over that caller's own submissions). The next one is
  `429 Identity.PartnerContact.RateLimited` with a `Retry-After` header saying
  when to try again. The refused request is not stored.
- There is **no CAPTCHA** and no other verification step.

The caller scope is derived by the server from the request's remote address
(one-way digest, never the raw address, never supplied by the client, and
never returned by any read).

## Errors

| HTTP | `code` | When |
|---|---|---|
| 400 | `Api.Validation.Failed` | Request validation failure (missing/long/blank fields, a malformed email, a missing `Idempotency-Key`). Nothing is stored. |
| 400 | `Identity.PartnerContact.Invalid` | The domain rules rejected the request. Nothing is stored. |
| 409 | `Identity.PartnerContact.IdempotencyConflict` | The same `Idempotency-Key` was already used with different content. |
| 429 | `Identity.PartnerContact.RateLimited` | The per-caller hourly window is exhausted (`Retry-After` header). |

## Response expectation

The team **usually replies within 2 business days**. This is an expectation,
not a guaranteed SLA.

## What the support team reads

The admin support inbox lists and opens the submissions:
`GET /api/v1/admin/support-tickets/partner-contacts` and
`GET /api/v1/admin/support-tickets/partner-contacts/{id}` — see
[admin support inbox](../../admin/support-inbox.md).

## Related

- [Landing](landing.md) — the public landing content and its contact-form section.
- [Admin support inbox](../../admin/support-inbox.md) — where the submissions land.
