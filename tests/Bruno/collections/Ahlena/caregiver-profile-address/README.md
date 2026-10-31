# Caregiver profile and address

Run only against the guarded local Development API and `SanadBrunoTestDb`
using `environments/local-fixtures.bru`. The suite reads the seeded Medical
caregiver profile (whose detailed address is null), sets a temporary address,
verifies it, clears it back to null, and verifies restoration. It also checks
validation/auth/policy failures and logs out both exact sessions it creates.

The pre-existing malformed Wellness Tips request may be skipped during Bruno
discovery; it is unrelated and must remain untouched.
