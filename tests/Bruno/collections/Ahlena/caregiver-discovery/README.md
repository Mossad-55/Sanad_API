# Caregiver discovery

Run only against the guarded local Development API and `SanadBrunoTestDb` with
`environments/local-fixtures.bru`. The suite logs in as the seeded medical
caregiver, obtains an active caregiver ID from search, verifies the public
profile and a HomeVisit quote, checks unknown-ID and anonymous cases, then
logs out the exact session created by the run. It does not create bookings or
change caregiver data.

The pre-existing malformed Wellness Tips request may be skipped during Bruno
discovery; it is unrelated and must remain untouched.
