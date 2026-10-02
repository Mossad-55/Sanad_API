# Admin care homes

Run only against the guarded `local-fixtures` Development API. The collection
reads the existing disposable Care Home onboarding fixture, verifies the Admin
operational role boundary, and exercises list/detail/private-file/document and
application review routes through rejection, correction, owner resubmission,
verification, approval, suspension, and reactivation. It does not reset or seed
the database; run it only after the owner onboarding collection has left a
submitted application in the authorized disposable target. The complete flow is
one-time stateful setup: recreate the authorized database before rerunning,
because there is no facility-delete API. All login sessions are logged out at
the end. Never overwrite `tests/Bruno/service-icon-fixture.png`.
