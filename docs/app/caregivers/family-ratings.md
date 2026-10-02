# Family caregiver ratings

These Family-facing routes let a normal Family account rate one of its
completed caregiver bookings and view the public top-rated caregivers. They
are documented here with the caregiver feature; they are not caregiver-authored
reviews.

## `GET /api/v1/family/caregiver-ratings/top-10`

Requires `FamilyAccess` (a normal Family account token). Returns up to ten
caregivers with at least one rating, active status, and profile/rating visibility
enabled. Ranking uses the unrounded average descending, review count descending,
then caregiver UUID ascending; `averageRating` is returned to two decimal places.
The response is an array of `{ caregiverId, type, arabicFullName,
englishFullName, avatarUrl, averageRating, reviewsCount }`. Anonymous access is
401; non-Family access is 403.

## `PUT /api/v1/family/caregiver-ratings/bookings/{bookingId}`

Requires `FamilyAccess`. The caller must own or be a member of a Family that
owns the specified booking, and the booking must be `Completed`. Submit
`{ "stars": 1..5, "reviewText": "optional, at most 2000 characters" }`.
Text is trimmed and blank text is stored as `null`. The one rating for a booking
is editable; repeat requests update it instead of adding a second review. The
response contains rating/booking/caregiver IDs, stars, review text, and creation
and update UTC timestamps. Validation errors return 400; a missing, foreign, or
not-completed booking returns 404 without exposing booking data; inconsistent
legacy rating ownership returns 409. Repeated edits are safe and no booking or

The caregiver summary (`averageRating`, `reviewsCount`) is recalculated from
stored ratings on each write. SMS is not used. The Care homes rating endpoint
and ranking remain separate and are tracked in the
[Care homes task checklist](../../operations/care-homes/Care_Homes_Tasks.md).

Runnable Bruno coverage: `tests/Bruno/collections/Sanad/caregiver-ratings`,
using only the guarded Development fixture database. The Postman requests are
in `Sanad.App.Family` under `14. Caregiver ratings (Family)`.
