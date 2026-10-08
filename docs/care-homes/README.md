# Care Homes API by consumer

Use the collection and route family for the caller's role. The server enforces these boundaries; sharing a Care Homes domain does not make the tokens interchangeable.

| Consumer | Authentication and authorization | Routes used by that surface | Postman collection | API guides |
|---|---|---|---|---|
| Family app | Family JWT with `FamilyAccess` | `/api/v1/family/care-home-bookings`, `/api/v1/family/care-home-ratings` | [Family Care Homes](../postman/app/Sanad.App.Family.CareHomes.postman_collection.json) | [Discovery and ratings](discovery.md), [booking lifecycle](booking-lifecycle.md) — Family routes and confirmation sections |
| Facility-owner portal | Facility-owner JWT with `CareHomeOwnerAccess` | `/api/v1/care-homes/facilities`, `/api/v1/care-homes/inventory/mine`, `/api/v1/care-homes/booking-requests/mine`, `/api/v1/care-homes/dashboard/mine`, `/api/v1/care-homes/revenue/mine` | [Facility Owner](../postman/care-homes/Sanad.CareHomes.Owner.postman_collection.json), [Finance and notes](../postman/care-homes/Sanad.CareHomes.Finance.postman_collection.json) | [Owner onboarding](owner-onboarding.md), [inventory and availability](inventory-and-availability.md), [booking lifecycle](booking-lifecycle.md), [finance reporting](finance-reporting.md) |
| Admin portal — Care Homes operations | Normal `SuperAdmin` or `SupportAdmin` with `CareHomesOperationalAdmin`; `ContentAdmin` is denied | `/api/v1/admin/care-homes`, `/api/v1/admin/care-homes/bookings`, `/api/v1/admin/care-homes/check-in-disputes`, `/api/v1/admin/care-homes/payouts`, `/api/v1/admin/care-homes/dashboard`, `/api/v1/admin/care-homes/revenue` | [Admin Care Homes](../postman/admins/Sanad.Admin.CareHomes.postman_collection.json), [Finance and notes](../postman/care-homes/Sanad.CareHomes.Finance.postman_collection.json) | [Admin Care Homes](../admin/care-homes.md), [Admin booking refunds](../admin/bookings.md), [Finance reporting](finance-reporting.md) |

Public discovery and approved facility media are anonymous GETs under `/api/v1/care-homes/discovery`. They are included in the Family collection because the Family app consumes them; requests explicitly disable collection authentication. They do not require a Family JWT.

## Flow boundaries

- Family checkout creates a booking and payment intent. Facility decision, physical assignment, check-in/out, and cancellation are owner operations under `CareHomeOwnerAccess`.
- Payment completion does not itself mean facility acceptance. Family check-in confirmation and dispute submission use Family routes; operational dispute resolution is Admin-only.
- Admin operational routes are not the CMS role surface. Do not use `ContentAdmin` for Care Homes review, private documents/media, inventory inspection, refund recovery, dispute resolution, or payouts.
- Use the role-specific Postman collection rather than the former combined collection. Requests labeled authored or fixture-dependent are examples, not evidence that they were run.
