# Admin families register

Read-only operational view of families and their members/dependents for
`SuperAdmin` and `SupportAdmin`. Use it to answer support questions about a
family's owner, membership and dependent associations without opening any
medical, contact or assessment data and without any ability to act on the
family. The routes are under `/api/v1/admin/families`.

This guide replaces nothing: family self-service routes (family profile,
members, dependents, deletion) keep their own contracts under
`docs/app/families/`. There is no Admin write, impersonation or "act as family"
route here, and no subscription or search-enrichment data is added to these
reads.

## Access

| Property | Value |
|---|---|
| Policy | `OperationalReadAdmin` |
| `access_type` | `Normal` only |
| `account_type` | `SuperAdmin (5)` or `SupportAdmin (7)` |

`ContentAdmin`, `FinanceAdmin`, `CareHomeOwner`, Family, Elderly and Caregiver
tokens are denied (`403`); an anonymous or restricted-verification caller is
`401`. Supporting an operational read for Support admins does not widen any
existing Finance booking access to family records.

## List families

```http
GET /api/v1/admin/families?page=1&pageSize=25&search=Sherif
Authorization: Bearer {{accessToken}}
```

| Query | Default | Meaning |
|---|---|---|
| `page` | `1` | Page number; clamped to at least `1` |
| `pageSize` | `25` | Clamped to `1–100` |
| `search` | — | An exact family id (`GUID`) or a case-insensitive substring of the family name |

Paging is **clamped, never an error**: a `page` below `1` reads as `1`, a
`pageSize` of `0` or negative reads as the default `25`, and a `pageSize` above
`100` reads as `100`. `search` is never an error either: a blank/whitespace
search is treated as absent (returns everything) and a search that matches no
family returns an empty `items` array with `totalCount: 0`. A value that parses
as a GUID is matched as an exact family id and ignores the name.

The response is `200 OK` with `page`, `pageSize`, `totalCount` (the filtered
count for the whole result set, not the page length) and `items`, ordered
newest-first by creation time and then family id, and stable across pages.

Each item contains:

| Field | Meaning |
|---|---|
| `familyId` | Family id |
| `name` | Family display name |
| `ownerUserId` | Id of the family owner account |
| `memberCount` | Number of member rows on the family |
| `createdOnUtc` | Family creation instant (UTC) |
| `deletedOnUtc` | Deletion instant (UTC), `null` while the family is active |

A deleted family is **retained** by the register and reports its
`deletedOnUtc`; the read never hides it. The deletion reason/message is never
returned.

## Family detail

```http
GET /api/v1/admin/families/{{familyId}}
Authorization: Bearer {{accessToken}}
```

Returns `200 OK` with the list fields plus `members` and `dependents`. An
unknown or empty `familyId` returns `404 Families.Family.NotFound`.

`members` is ordered by join instant and then user id; each entry contains:

| Field | Meaning |
|---|---|
| `userId` | Member account id |
| `role` | `FamilyRole`: `1` Owner, `2` Editor, `3` Viewer |
| `relationshipType` | `FamilyRelationshipType`: `1` Father, `2` Mother, `3` Son, `4` Daughter, `5` Brother, `6` Sister, `7` Grandfather, `8` Grandmother, `9` Grandson, `10` Granddaughter, `11` Uncle, `12` Aunt, `13` Nephew, `14` Niece, `15` Spouse, `99` Other |
| `addedByUserId` | Account id that added the member |
| `joinedOnUtc` | Join instant (UTC) |
| `status` | Current Identity account status (`1` PendingVerification, `2` Active, `3` Suspended, `4` Blocked), or `0` when the Identity profile cannot be resolved |

`dependents` lists the family's elderly associations, ordered by creation time
and then id, and each entry contains only `elderlyId`, `identityUserId` and
`relationshipType`. Dependents of a deleted family are still returned.

## Fields deliberately not returned

The projection is restricted to identity and association facts. It never
returns, for a family or any of its members/dependents:

- medical profile, notes, address, phone, health or care-assessment data;
- member display names (Arabic/English), email, avatar url or last-login time,
  even though the family-member Identity seam can resolve them;
- the family deletion reason/message;
- subscription, search-enrichment, booking or payment data;
- any impersonation token or write affordance.

## Errors

| Code | HTTP | When |
|---|---|---|
| `Families.Family.NotFound` | `404` | Unknown or empty family id on the detail route |
| `403` | `403` | Authenticated caller whose account type is not `SuperAdmin`/`SupportAdmin` |
| `401` | `401` | Missing/expired token, non-`Normal` access, or no authentication |

The list route has no 400 case: invalid paging is clamped and an unmatched
search is an empty page.

## Verification

Runnable requests are in
[`Sanad.Admin.Families.postman_collection.json`](../postman/admins/Sanad.Admin.Families.postman_collection.json).
The collection examples are authored contracts; they are not evidence of
runtime execution. End-to-end HTTP proof (role denial, unknown-id `404`,
paging clamp, empty reads) is provided by the F6 local verification adapter and
is recorded in the F6 worker brief.
