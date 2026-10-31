# Caregiver privacy preferences

Run only against the guarded local Development API and `SanadBrunoTestDb`
using `environments/local-fixtures.bru`. The suite reads the seeded medical
caregiver's exact baseline, changes one visibility flag, verifies persistence,
restores the original four booleans, and verifies the final state. It also
checks validation/auth/policy failures and logs out both exact sessions it
creates. No booking or other caregiver data is changed.

The pre-existing malformed Wellness Tips request may be skipped during Bruno
discovery; it is unrelated and must remain untouched.
