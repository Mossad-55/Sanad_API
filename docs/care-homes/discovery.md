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

Summaries expose the ID, bilingual name and description, governorate, city,
area, and the minimum monthly EGP price among non-archived room types. Zero is
a valid price; a facility with no active room types remains listed with a null
minimum price. Detail adds the approved address, bilingual admission
conditions, amenities, medical services, and active room-type cards (bilingual
names/descriptions, allocation mode, and monthly EGP price).

The public contract excludes contact details, owner identity, private
documents/storage keys, review history, ratings, availability/capacity,
room instances/beds, and resident clinical data. The top-10 rated Care Homes
ranking is not stubbed here; it remains blocked on HC-TASK-041's rating model,
eligibility, and aggregation.

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
