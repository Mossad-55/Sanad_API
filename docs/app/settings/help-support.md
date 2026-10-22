# Help Center — FAQ & support contact (app / settings)

The signed-in Help Center screen (SET-13) reads its entire content from one
route: the FAQ list for the caller's own audience plus the one global support
contact card.

**Route:** `GET /api/v1/help-center[?audience=]`

**Auth:** Normal JWT (any of the five app account types)

**Success:** `200`

The audience is derived from the JWT `account_type` claims, which carry one
claim per owned account (`Family=1, MedicalCaregiver=2, CompanionCaregiver=3,
Elderly=4, CareHomeOwner=5`). A role can never read another role's FAQ list by
URL manipulation. Admin account types receive
`403 Cms.Content.UnsupportedAudience`.

The Care-home Owner account type is a Help Center audience as of F5: an owner
token reads its own FAQ list and the shared contact card exactly like the other
app roles.

An optional `audience` query parameter selects the audience explicitly, using
the same deterministic rules as the legal routes
([legal.md](legal.md#audience-behavior)) — it is validated against the
audiences the token owns:

- owned `audience` → served;
- omitted — or empty/whitespace, treated the same as omitted — with exactly
  **one** owned audience → auto-selected;
- omitted with **more than one** owned audience → coded
  `400 Cms.Content.AudienceRequired` (account switching does not reissue a JWT,
  so the caller must name the audience);
- malformed or undefined `audience` → `400 Cms.Content.InvalidAudience`,
  checked first and for every account class, so an admin token sending a
  malformed value gets `400`, not `403`;
- well-formed but unowned `audience`, or no owned app audience (admin account
  types) → `403 Cms.Content.UnsupportedAudience`.

A repeated parameter keeps the first value (framework scalar query binding,
`ValueProviderResult.FirstValue`, with no custom binder registered); this is
safe because the value is still validated against the token's owned audiences.

Example — a multi-account token reading the Care Home Owner FAQ list:

```text
GET /api/v1/help-center?audience=5
Authorization: Bearer <normal access token>
```

## Response

```json
{
  "faqs": [
    {
      "id": { "value": "0191ae10-0000-7000-8000-000000000002" },
      "audience": 1,
      "arabicQuestion": "كيف أغيّر كلمة المرور؟",
      "englishQuestion": "How do I change my password?",
      "arabicAnswer": "من الإعدادات ثم الأمان.",
      "englishAnswer": "Open Settings, then Security.",
      "displayOrder": 1
    }
  ],
  "supportContact": {
    "supportPhone": "+201000000001",
    "supportEmail": "support@sanad.example"
  }
}
```

- `faqs` contains **only active** FAQs for the caller's audience, ordered by
  `displayOrder` (then stable id). Inactive and other-audience entries are
  never returned.
- All question/answer fields are bilingual; render per the app language.
- `supportContact` is the single global pair for every app role — never
  per-role.

## Empty / unconfigured behavior

The read surface is non-destructive before CMS seeding:

```json
HTTP 200
{
  "faqs": [],
  "supportContact": null
}
```

- Empty `faqs`: render the "no FAQs yet" state.
- `supportContact = null`: hide the call/email card. `supportContact` is
  card data only — tapping it opens the phone dialer or the mail client;
  this feature never sends SMS, email, or notifications from the backend,
  and it does not replace the support-request form.
- To actually reach support with a tracked request, the app uses the
  separate `POST /api/v1/support/contact` ticket route (unchanged, documented
  in `docs/auth/support-contact.md`).

## Errors

| HTTP | `code` | When |
|---|---|---|
| 401 | *(bare, no code)* | Missing/invalid/expired JWT |
| 403 | *(bare, no code)* | Restricted verification token (framework policy) |
| 400 | `Cms.Content.InvalidAudience` | `audience` is malformed or undefined (non-numeric, out of range, or a comma list) — checked before ownership, for every account class including admin |
| 400 | `Cms.Content.AudienceRequired` | `audience` omitted or empty/whitespace while the token owns more than one audience |
| 403 | `Cms.Content.UnsupportedAudience` | `audience` is well-formed but not owned by the caller, or the caller owns no app audience (admin account types) |
| 200 | — | Empty/unseeded list is a success, not an error |

`id` is the repository's strongly typed id JSON shape `{ "value": "<guid>" }`.

## Support tickets from the app

Reaching support with a tracked request uses the ticket routes, not the contact
card (see `docs/auth/support-contact.md`):

```text
POST /api/v1/support/contact        Submit a request -> 200
GET  /api/v1/support/contact        Anonymous contact card -> 200
GET  /api/v1/support/tickets        The caller's own tickets -> 200
GET  /api/v1/support/tickets/{id}   One own ticket with the admin replies -> 200
```

Ticket status: `1` New, `2` In Progress, `3` Resolved, `4` Closed. An admin
moves a ticket forward and replies; the reply appears in the ticket detail.
Another user's ticket id is `404`, never data.

## Admin management

FAQ create/update/activate/deactivate and the global support-contact pair are
maintained under `/api/v1/admin/help-center` — see `docs/admin/help-center.md`.
Each contact value is optional there: an unconfigured card reads back as `200`
with `supportPhone` and `supportEmail` both `null`, and the app hides whichever
options the admin has left unset.
