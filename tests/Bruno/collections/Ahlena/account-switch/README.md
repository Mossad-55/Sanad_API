# Account choices

Run with the guarded `local-fixtures` Development API and disposable
`SanadBrunoTestDb`. This suite covers owned-account listing, unsupported and
duplicate account behavior, and adding/reading back `CareHomeOwner` account
type 8. The add request is repeatable: the first run creates the test account
choice and later runs accept only the expected AlreadyExists conflict. The
seeded family owner's disposable identity retains that account choice.
