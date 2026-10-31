# Family home (mobile app)

The editorial Family home read. Content is authored and published through the
Admin CMS (`docs/admin/family-home.md`); the app only reads it.

**Route:** `GET /api/v1/family/home/sections`
**Policy:** `FamilyAccess` — a Normal JWT for a `Family` account
(`access_type` = `Normal`, `account_type` = `Family`). Restricted-verification
tokens and non-Family accounts receive `403`.

Access is authorized by the policy and then resolved server-side to a
non-deleted family the caller owns or belongs to (Owner, Editor and Viewer all
read the same editorial content). There is no personalisation, audience
targeting or per-family content.

## Response

`200` with an ordered array of published, visible sections — ordered by section
`displayOrder`, then creation time. Draft, archived and hidden sections are
never returned, and a section whose items were all omitted is dropped.

```json
[
  {
    "id": "01900000-0000-7000-8000-000000000001",
    "kind": "banners",
    "displayOrder": 0,
    "items": [
      {
        "arabicTitle": "عنوان",
        "englishTitle": "Title",
        "arabicBody": "نص",
        "englishBody": "Body",
        "imageKey": "landing/hero.png",
        "iconKey": null,
        "targetType": null,
        "targetServiceId": null,
        "serviceId": null,
        "serviceNameArabic": null,
        "serviceNameEnglish": null,
        "serviceIconKey": null,
        "serviceCaregiverType": null
      }
    ]
  }
]
```

`kind` is the kebab token `banners`, `quick-links` or `featured-services`.
Item order is the authored array order. Unset fields serialize as `null`. Ids
are plain `Guid` strings (no `{ "value": … }` wrapper).

### Navigation targets

An item that carries an action uses the same allowlisted target as the Admin
contract:

- `targetType: "services"` — open the whole catalog (`targetServiceId` null).
- `targetType: "service"` — open the catalog detail for `targetServiceId`.

There are no arbitrary URLs, free-form routes or extra parameters. A banner may
have no target at all (`targetType` null) and is then informational.

### Featured services

`featured-services` items carry `serviceId` plus the **live** catalog fields:

- `serviceNameArabic`, `serviceNameEnglish`, `serviceIconKey`;
- `serviceCaregiverType` — `1` medical, `2` companion.

A featured entry never stores editorial copy, and never exposes price or
availability. Since the values are read live, renaming or re-iconing a catalog
service updates the app without republishing the section.

## Omission rules

Reference problems never fail the whole response:

- A featured entry whose catalog service is missing or inactive is omitted.
- A `service`-target item whose service is missing or inactive is omitted with
  its item, so no dead action is rendered.
- A section with no remaining items is omitted entirely.
- A stored section payload that cannot be parsed fails the read with
  `400 Cms.FamilyHome.Incomplete` rather than returning an empty section.

## Empty result

Nothing published returns `200` with `[]`. The client should then omit the CMS
sections and keep its normal app navigation — do not invent fallback cards or
copy, and do not treat the empty list as an error.

## Errors

| Status | Code | Meaning |
|---|---|---|
| 401 | — | Missing or invalid token (no usable subject claim) |
| 403 | — | Policy denial: not a Normal Family account |
| 404 | `Families.Family.NotFound` | The caller owns no family and is not a member of any, or the family is deleted |
| 400 | `Cms.FamilyHome.Incomplete` | A published section's stored content cannot be read |

## Postman

`docs/postman/app/Ahlena.App.Family.postman_collection.json` → **Family home
sections**. Admin authoring requests live in
`docs/postman/admins/Ahlena.Admin.postman_collection.json`.
