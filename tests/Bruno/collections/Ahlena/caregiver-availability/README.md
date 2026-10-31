# Caregiver availability

Run only against the guarded local Development API and `SanadBrunoTestDb`.
The seeded Medical caregiver's status/availability are captured first. The
suite transitions the Active caregiver Available → Unavailable → Available,
verifies both responses and final exact baseline readback, and covers anonymous
and Family Viewer access plus exact logout for both created sessions. The
unrelated malformed Wellness Tips request may be skipped during discovery and
must remain untouched.
