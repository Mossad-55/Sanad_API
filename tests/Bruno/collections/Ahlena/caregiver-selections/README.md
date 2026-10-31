# Caregiver selections

Run only against the guarded local Development API and `SanadBrunoTestDb`.
The suite captures the seeded Medical caregiver's service/language/area ids,
replays those exact arrays to verify full-replacement success without changing
fixture state, and confirms persisted values. It checks an unknown service id,
anonymous access, Family Viewer authorization, and exact logout for both
created sessions. The unrelated malformed Wellness Tips request may be skipped
during Bruno discovery and must remain untouched.
