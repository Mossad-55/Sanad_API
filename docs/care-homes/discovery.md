# Care Homes public discovery

HC-TASK-031 adds two anonymous, read-only routes:

- `GET /api/v1/care-homes/discovery?page=1&pageSize=10`
- `GET /api/v1/care-homes/discovery/{careHomeId}`

The list accepts `page >= 1` and `pageSize` from 1 through 50. Results are
ordered by Care Home ID ascending; eligibility is applied before `totalCount`
and paging. Invalid pagination returns `400` with code
`CareHomes.Discovery.InvalidQuery`. An unknown or ineligible ID returns the
same `404` response with code `CareHomes.Discovery.NotFound`.

## Public eligibility and privacy

A facility is listed only when its status is `Approved`, its
`ApprovedRevisionId` points to an approved revision, and the latest document
for each required type within that approved revision is usable on the current
Cairo date: operating license, registration, health certificate, and civil
defense. The latest document is selected by `CreatedOnUtc`, then document ID;
an expired, rejected, or otherwise unusable replacement therefore cannot be
bypassed by an older document. Newer drafts and unapproved revisions are not
public.

Family checkout rechecks this same approved-revision and current-Cairo-date
eligibility before creating a booking or capacity hold. A direct checkout for an
expired or otherwise ineligible facility returns the same not-found result used
for an unknown facility; existing stays remain readable and are not cancelled.

Summaries expose the ID, bilingual name and description, governorate, city,
area, and the minimum monthly EGP price among non-archived room types. Zero is
a valid price; a facility with no active room types remains listed with a null
minimum price. Detail adds the approved address, bilingual admission
conditions, amenities, medical services, and active room-type cards (bilingual
names/descriptions, allocation mode, and monthly EGP price, plus the type's
bilingual marketing `amenities` and `coverImageUrl` when a cover photo is
published). `coverImageUrl` points at the public discovery file route
(`GET /api/v1/care-homes/discovery/{careHomeId}/room-types/{roomTypeId}/media/{mediaId}/file`),
served anonymously for approved facilities; anything else returns `404`.
Room photos are served the same way under
`.../rooms/{roomId}/media/{mediaId}/file`.

Detail also exposes `cancellationTerms`. While the Owner-approved care-home
refund policy ships dormant this field is `null`: bookings keep the pre-policy
schedule, so the page does not advertise terms no booking would receive. After
activation it carries the facility's published structured terms as
`[{ "refundPercentage": 0-100, "deadlineHoursBeforeStart": 0-8760 }]` — the
exact tiers a booking made at that moment snapshots (clause 6; decisions #7,
#9, #10). Facilities with no published terms stay `null` and the clause-2
default schedule governs.

The public contract excludes contact details, owner identity, private
documents/storage keys, review history, availability/capacity, room
instances/beds, and resident clinical data.

## Family ratings

`GET /api/v1/family/care-home-ratings/top-10` requires a Family account. It
returns up to ten rated Care homes that also meet the public discovery
eligibility rules. Unrated and currently ineligible facilities are omitted, and
a rating hidden by an admin never contributes to the ranking. Ordering uses the
unrounded average descending, review count descending, then
Care-home ID ascending. The returned `averageRating` is rounded to two decimal
places; each item contains `careHomeId`, bilingual names, `averageRating`, and
`reviewsCount`.

`PUT /api/v1/family/care-home-ratings/bookings/{bookingId}` accepts
`{ "stars": 1..5, "reviewText": "optional, at most 2000 characters" }`.
The caller must own or belong to the Family on the booking, and the Family
must have confirmed check-in. Review text is trimmed; blank text becomes null.
Repeating the request edits the existing booking rating and preserves its
review count. Validation errors return 400; missing, foreign, or unconfirmed
bookings return the same ineligible response. Editing never changes a rating's
moderation state: a re-rated hidden rating stays hidden.

## Rating moderation and the owner ratings page

Ratings are moderated by an admin, not by the facility: a hidden rating is
excluded from every non-admin read and from every average and count, restoring
it returns it to both, and moderation never changes the stars or the text. The
facility owner can read the ratings about their own facility anonymously under
`GET /api/v1/care-homes/facilities/mine/ratings` and flag one of them with
`POST /api/v1/care-homes/facilities/mine/ratings/{ratingId}/flag`; the reviewer
is never identified, hidden ratings leave that page and its aggregates, and at
most one unresolved flag exists per rating per owner. Owners cannot hide or
restore a rating. See [Admin ratings moderation](../admin/ratings-moderation.md)
for the verbs, status codes and error codes.

The Postman requests are in the Care Homes collection under
`09. Family ratings (HC-TASK-041)` and the facility-owner ratings folder of the
[owner collection](../postman/care-homes/Ahlena.CareHomes.Owner.postman_collection.json).
Examples require the disposable Development fixture and are not runtime-verified.
Stateful localhost HTTP evidence for the moderation lifecycle is provided by
`tools/Invoke-F7LocalHttpVerification.ps1` and recorded in the F7 worker brief.

## Verification and fixtures

The authored focused tests are run with:

```powershell
dotnet test tests/Ahlena.UnitTests/Ahlena.UnitTests.csproj --no-build --no-restore --filter "FullyQualifiedName~CareHomes.CareHomeDiscoveryTests" --nologo
```

The task gates also require:

```powershell
dotnet build Ahlena.slnx --no-restore -m:1 --nologo
dotnet test Ahlena.slnx --no-build --no-restore --nologo
```

Bruno uses exactly these four explicit paths from `tests/Bruno`:

```powershell
bru run `
  "collections/Ahlena/care-homes-discovery/00-list-default.bru" `
  "collections/Ahlena/care-homes-discovery/01-list-invalid-pagination.bru" `
  "collections/Ahlena/care-homes-discovery/02-detail-approved-fixture.bru" `
  "collections/Ahlena/care-homes-discovery/03-detail-unknown-is-404.bru" `
  --env local-fixtures --insecure --bail --reporter-skip-body
```

The fixture must be disposable and local, with an approved eligible Care Home
ID in `careHomeDiscoveryId`, a non-existent GUID in
`careHomeDiscoveryUnknownId`, and optional expected values in
`expectedCareHomeEnglishName` and `expectedStartingFromMonthlyPriceEgp`.
The approved fixture must have all four required documents valid on the API's
Cairo date and may include active room types. The collection is read-only and
does not provision, reset, migrate, or clean up data. No relational PostgreSQL
integration execution is claimed here; provisioning or running against a
database requires fresh owner authorization.
