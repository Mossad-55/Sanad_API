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
names/descriptions, allocation mode, and monthly EGP price).

The public contract excludes contact details, owner identity, private
documents/storage keys, review history, availability/capacity, room
instances/beds, and resident clinical data.

## Family ratings

`GET /api/v1/family/care-home-ratings/top-10` requires a Family account. It
returns up to ten rated Care homes that also meet the public discovery
eligibility rules. Unrated and currently ineligible facilities are omitted.
Ordering uses the unrounded average descending, review count descending, then
Care-home ID ascending. The returned `averageRating` is rounded to two decimal
places; each item contains `careHomeId`, bilingual names, `averageRating`, and
`reviewsCount`.

`PUT /api/v1/family/care-home-ratings/bookings/{bookingId}` accepts
`{ "stars": 1..5, "reviewText": "optional, at most 2000 characters" }`.
The caller must own or belong to the Family on the booking, and the Family
must have confirmed check-in. Review text is trimmed; blank text becomes null.
Repeating the request edits the existing booking rating and preserves its
review count. Validation errors return 400; missing, foreign, or unconfirmed
bookings return the same ineligible response. No moderation workflow is
included.

The Postman requests are in the Care Homes collection under
`09. Family ratings (HC-TASK-041)`. Examples require the disposable Development
fixture and are not runtime-verified. Bruno was not selected because focused
automated tests provide sufficient evidence for this bounded behavior; no
stateful HTTP evidence is claimed.

## Verification and fixtures

The authored focused tests are run with:

```powershell
dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj --no-build --no-restore --filter "FullyQualifiedName~CareHomes.CareHomeDiscoveryTests" --nologo
```

The task gates also require:

```powershell
dotnet build Sanad.slnx --no-restore -m:1 --nologo
dotnet test Sanad.slnx --no-build --no-restore --nologo
```

Bruno uses exactly these four explicit paths from `tests/Bruno`:

```powershell
bru run `
  "collections/Sanad/care-homes-discovery/00-list-default.bru" `
  "collections/Sanad/care-homes-discovery/01-list-invalid-pagination.bru" `
  "collections/Sanad/care-homes-discovery/02-detail-approved-fixture.bru" `
  "collections/Sanad/care-homes-discovery/03-detail-unknown-is-404.bru" `
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
