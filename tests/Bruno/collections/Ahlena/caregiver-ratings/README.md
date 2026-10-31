# Family caregiver ratings

Run against the guarded local Development API and disposable `SanadBrunoTestDb`
using the `local-fixtures` environment. The seeded Family owner selects a
completed caregiver booking, rates/edits that booking, then verifies the
caregiver appears in the public Family top-10 list. Requests also cover
anonymous access (401), non-Family denial (403), invalid stars, and an unknown
booking (404). The rating is persisted as disposable test data; the endpoint
does not alter booking state. Both sessions are logged out at the end.
