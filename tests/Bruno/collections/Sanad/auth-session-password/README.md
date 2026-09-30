# Authentication, sessions, and password

Run only with the guarded local Development API and `SanadBrunoTestDb` using
`environments/local-fixtures.bru`. Requests check the target before sending.
The registration and OTP/password-reset requests use invalid/unregistered
fixtures only; they do not create accounts, send to registered users, or
change passwords. The session lifecycle logs in the seeded SupportAdmin,
rotates its refresh token, lists and revokes only the session created by this
folder, then logs out all remaining fixture sessions.

The pre-existing malformed Wellness Tips request may be skipped by Bruno
collection discovery; it is unrelated and must remain untouched.
