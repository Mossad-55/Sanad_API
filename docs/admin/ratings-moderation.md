# Admin ratings moderation

Visibility moderation for family ratings, split by what was rated: caregiver
ratings and Care Home ratings. Both targets share **one** mechanism — the same
verbs, the same 500-character reason bound, the same status codes and the same
error-code shape — and add a Care Home owner flag surface that caregiver
ratings do not have (a caregiver is not a facility owner).

Use this guide to review a rating that a family reported, hide it while it is
being handled, and restore it afterwards. Hiding is a **visibility** control,
not an accuracy judgement: it never changes the stars, the review text or the
authorship, and restore is always available.

## Access

| Route group | Policy | `access_type` | `account_type` |
|---|---|---|---|
| `/api/v1/admin/caregivers/ratings` | `CaregiverReviewAdmin` | `Normal` only | `SuperAdmin (5)` or `SupportAdmin (7)` |
| `/api/v1/admin/care-homes/ratings` | `CareHomesOperationalAdmin` | `Normal` only | `SuperAdmin (5)` or `SupportAdmin (7)` |
| `/api/v1/care-homes/facilities/mine/ratings` | `CareHomeOwnerAccess` | `Normal` only | `CareHomeOwner (8)` |

`ContentAdmin`, `FinanceAdmin`, Family, Caregiver and Elderly tokens are denied
(`403`); a missing, expired or non-`Normal` token is `401`. A Care Home owner is
denied on both admin route groups, and an admin account is denied on the owner
route.

## What hiding means

A hidden rating is excluded from **every** non-admin read and from **every**
aggregate average and count. Restoring returns it to both. Concretely:

- The stored caregiver aggregate (`averageRating`, `reviewsCount` on the
  caregiver profile) is recomputed over the non-hidden set inside the same
  transaction as the hide or the restore, so every stored-aggregate consumer
  (caregiver profile, discovery search including `minRating`, earnings summary,
  family top-10 cards) moves together.
- The Care Home owner page and its `averageStars`/`ratingCount` exclude hidden
  ratings; no facility aggregate is stored.
- The CMS landing testimonial resolver skips a hidden caregiver rating.

Moderation never mutates or deletes a rating: stars, review text, reviewer and
history are retained. Rating eligibility is unchanged, and a family re-rating
(`PUT` on the family route) updates stars/text **without** clearing the
moderation state — a re-rated hidden rating stays hidden.

Hide and restore are attributed and recorded append-only. The rating row carries
the current state (`moderationState`, `hiddenByUserId`, `hiddenOnUtc`,
`hideReason`), and each action appends a row with the actor, the instant and the
reason. Restoring clears the rating's hide facts; the append-only action row
keeps the reason it was given.

## Admin caregiver ratings

```http
GET /api/v1/admin/caregivers/ratings?caregiverId=&stars=&moderation=&page=1&pageSize=25
Authorization: Bearer {{accessToken}}
```

| Query | Default | Meaning |
|---|---|---|
| `caregiverId` | — | Only ratings about this caregiver |
| `stars` | — | Only ratings with exactly this star value (`1`–`5`) |
| `moderation` | `all` | `visible`, `hidden` or `all` (case-insensitive) |
| `page` | `1` | Page number, at least `1` |
| `pageSize` | `25` | `1`–`100` |

Paging and filters are **validated, never clamped**: a `page` below `1`, a
`pageSize` outside `1`–`100`, a `stars` value outside `1`–`5` or an unknown
`moderation` value returns `400 Caregivers.Rating.InvalidQuery`. An unknown
`caregiverId` is not an error — it returns an empty page.

The response is `200 OK` with `items`, `page`, `pageSize` and `totalCount`
(counted after filtering and before paging), ordered newest-first by
`createdOnUtc` and then rating id. Each item carries only:

| Field | Meaning |
|---|---|
| `ratingId` | Rating id |
| `caregiverId` | Rated caregiver id |
| `bookingId` | Completed booking that produced the rating |
| `familyId` | Rating family id |
| `stars` | Star value |
| `reviewText` | Retained review text, `null` when blank |
| `createdOnUtc` / `updatedOnUtc` | Creation and last-edit instants (UTC) |
| `moderationState` | `visible` or `hidden` |
| `hiddenByUserId` | Hiding admin, `null` while visible |
| `hiddenOnUtc` | Hide instant, `null` while visible |
| `hideReason` | Retained hide reason, `null` while visible |

No reviewer name, phone, email, avatar or any other account fact is projected.

## Admin Care Home ratings

```http
GET /api/v1/admin/care-homes/ratings?facilityId=&stars=&moderation=&flagged=&page=1&pageSize=25
Authorization: Bearer {{accessToken}}
```

Same filters, defaults and `400 CareHomes.Rating.InvalidQuery` validation rules,
plus:

| Query | Default | Meaning |
|---|---|---|
| `facilityId` | — | Only ratings about this facility |
| `flagged` | — | `true` = ratings with an open owner flag, `false` = ratings without one |

Ordering is **unresolved-flagged first**, then newest-first by `createdOnUtc`
and rating id. Items carry the caregiver item fields with `facilityId` instead
of `caregiverId`, plus the unresolved flag summary:

| Field | Meaning |
|---|---|
| `unresolvedFlagId` | Open owner flag id, `null` when the rating is not flagged |
| `unresolvedFlagReason` | Reason the owner gave, `null` when not flagged |
| `unresolvedFlaggedOnUtc` | Instant the owner flagged, `null` when not flagged |

Resolved flags are not part of the projection; the flagged filter and these
fields follow the open case only.

## Admin actions

```http
POST /api/v1/admin/caregivers/ratings/{ratingId}/hide      { "reason": "..." }
POST /api/v1/admin/caregivers/ratings/{ratingId}/restore   { "reason": "..." }
POST /api/v1/admin/care-homes/ratings/{ratingId}/hide      { "reason": "..." }
POST /api/v1/admin/care-homes/ratings/{ratingId}/keep      { "reason": "..." }
POST /api/v1/admin/care-homes/ratings/{ratingId}/restore   { "reason": "..." }
```

All five return `204 No Content` on success. `reason` is required, trimmed, and
at most 500 characters. `keep` is Care Home only: it closes the rating's open
owner flag by leaving the rating visible, and changes no visibility. Hiding or
keeping a Care Home rating **resolves that rating's open flag in the same
transaction**, recording the resolving admin, the instant and the action taken
(`Hidden` or `Kept`).

Failures are checked in this order, so a bad reason never masks a missing
rating:

| Code | HTTP | When |
|---|---|---|
| `Caregivers.Rating.NotFound` / `CareHomes.Rating.NotFound` | `404` | Unknown rating id (and, on the owner route, a rating that is not about the caller's own facility) |
| `Caregivers.Rating.InvalidReason` / `CareHomes.Rating.InvalidReason` | `400` | Missing, blank or over-500-character `reason` |
| `Caregivers.Rating.InvalidQuery` / `CareHomes.Rating.InvalidQuery` | `400` | Invalid admin list filter or paging |
| `Caregivers.Rating.AlreadyHidden` / `CareHomes.Rating.AlreadyHidden` | `409` | Hide on an already hidden rating |
| `Caregivers.Rating.AlreadyVisible` / `CareHomes.Rating.AlreadyVisible` | `409` | Restore on an already visible rating |
| `CareHomes.Rating.NoOpenFlag` | `409` | Keep on a rating with no open owner flag |
| `CareHomes.Facility.NotFound` | `404` | Owner read by an owner with no facility |

There is no silent no-op: an illegal transition is a `409` rather than a
success, and a concurrent flag resolution loses with `409` as well.

## Owner facility ratings

```http
GET /api/v1/care-homes/facilities/mine/ratings?page=1&pageSize=25
Authorization: Bearer {{ownerAccessToken}}
```

The owner sees the ratings about their own facility and nothing else: hidden
ratings leave both the page and the aggregates, and the reviewer is never
identified — no name, user id, family id, phone, email or avatar appears in an
item or an error. `page`/`pageSize` are validated (`400
CareHomes.Rating.InvalidQuery`); an owner account with no facility returns
`404 CareHomes.Facility.NotFound`.

The response is `200 OK` with `items`, `page`, `pageSize`, `totalCount`,
`averageStars` (over the visible ratings, rounded half away from zero to two
decimals) and `ratingCount`. `totalCount` counts the visible ratings before
paging. Items are ordered newest-first and carry only:

| Field | Meaning |
|---|---|
| `ratingId` | Rating id |
| `stars` | Star value |
| `reviewText` | Retained review text, `null` when blank |
| `createdOnUtc` | Creation instant (UTC) |
| `flagStatus` | `None`, `Open` or `Resolved` for **this owner's own** latest flag |
| `myFlagReason` | The reason this owner gave, `null` when `None` |
| `myFlaggedOnUtc` | When this owner flagged, `null` when `None` |

`Resolved` means this owner flagged the rating at some point and the case is
closed; it does not say whether the rating was hidden or kept.

## Owner flag

```http
POST /api/v1/care-homes/facilities/mine/ratings/{ratingId}/flag   { "reason": "..." }
```

`204 No Content`, with `reason` required and at most 500 characters. At most one
**unresolved** flag exists per rating per owner: repeating the call while the
owner's flag is still open returns `204` and writes nothing — no duplicate row
and no history row. Flagging again after the case was resolved opens a fresh
case. An unknown rating or a rating that is not about one of the caller's own
facilities returns the same `404 CareHomes.Rating.NotFound`, so the route never
confirms whether a foreign rating exists; a blank or over-500-character reason
returns `400 CareHomes.Rating.InvalidReason`. Owners cannot hide, restore or
keep a rating, and cannot see who reviewed it.

## Verification

Focused tests:

```powershell
dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj --no-build --filter "FullyQualifiedName~AdminCaregiverRatingModerationTests|FullyQualifiedName~AdminCareHomeRatingModerationTests|FullyQualifiedName~OwnerCareHomeRatingTests|FullyQualifiedName~RatingsModerationAuthorizationTests"
```

Runnable requests are in
[`Sanad.Admin.Ratings.postman_collection.json`](../postman/admins/Sanad.Admin.Ratings.postman_collection.json);
the owner read and flag are in the
[facility-owner collection](../postman/care-homes/Sanad.CareHomes.Owner.postman_collection.json).
Collection examples are authored contracts, not runtime evidence. Localhost HTTP
proof (role denial, the flag → hide → restore lifecycle, aggregate agreement,
unknown-id `404`, blank/over-long reason `400` and the `409` conflicts) is
provided by `tools/Invoke-F7LocalHttpVerification.ps1` and recorded in the F7
worker brief.
