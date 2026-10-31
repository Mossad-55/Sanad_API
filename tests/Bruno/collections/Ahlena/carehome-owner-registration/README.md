# Care-home owner registration

Run only against the guarded local Development API and disposable
`SanadBrunoTestDb` using `local-fixtures`. The request registers a unique
CareHomeOwner (`accountType: 8`) with password and email/phone identities. It
asserts that registration succeeds and both verification request IDs are
returned; OTP remains verification-only. This creates a pending-verification
test account in the disposable fixture database and does not send real SMS or
email.
