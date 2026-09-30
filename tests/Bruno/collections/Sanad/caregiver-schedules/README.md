# Caregiver schedules

Run only against the guarded local Development API and `SanadBrunoTestDb`.
The suite captures and replays each seeded schedule, verifies persisted
readback, briefly sets the Companion schedule to the documented fixed
Overnight window (20:00–08:00), then restores and verifies the exact original
schedule. It also checks invalid windows, wrong caregiver type, anonymous and
Family Viewer access, and exact logout for the three created sessions. The
unrelated malformed Wellness Tips request may be skipped during collection
discovery and must remain untouched.
