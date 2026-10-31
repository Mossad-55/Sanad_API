# Caregiver pricing

Run only against the guarded local Development API and `SanadBrunoTestDb`
using `environments/local-fixtures.bru`. The suite temporarily updates the
seeded Medical and Companion caregiver prices, verifies each returned profile,
restores the exact original prices, and reads both profiles back. It also
checks validation, wrong-account-type, unauthenticated, and exact-session
logout behavior. No bookings or payment intents are created.

The pre-existing malformed Wellness Tips request may be skipped during Bruno
discovery; it is unrelated and must remain untouched.
