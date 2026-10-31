# HC-TASK-020 inventory coverage

Disposable local fixture collection. Before running it, authenticate an owner and ensure that owner has a Care Home facility: the test-user seeder creates the owner account but does not create a facility. Either run the Care Homes onboarding fixture first or provision the facility through the owner facility route. Facility creation persists in the disposable database. Room types and rooms also persist, so repeat runs require a clean fixture facility or explicit cleanup/unique names. Request 13 archives the created bed, and request 23 restores it, leaving the final active state documented by the collection.

The collection creates assets through owner routes, reads them through owner/Admin routes, and exercises availability. Booking creation is intentionally deferred to HC-032/034; the occupancy provider seam is empty here.
