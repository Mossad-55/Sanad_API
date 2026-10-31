# Caregiver certificates

Run only against the guarded local Development API and `SanadBrunoTestDb`.
The suite adds a disposable Additional certificate using the read-only local
PNG fixture, verifies Pending/readback, removes the exact generated row and
private file, then checks the original two Verified mandatory certificates
and Active/Available state. Duplicate mandatory add and unknown-ID file
replacement exercise upload compensation; mandatory removal is rejected.
Mandatory-file replacement is deliberately excluded because it changes an
Active caregiver to PendingReview/Unavailable. The suite checks missing-file,
wrong caregiver type, anonymous and Family Viewer responses, and exact logout
for all three sessions. Do not alter the reserved image fixture. The unrelated
malformed Wellness Tips request may be skipped during discovery and must remain
untouched.
