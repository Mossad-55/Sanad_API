# Care Homes Postman collections

Import the collection for the caller that the frontend implements:

| Collection | Use it for | Token / authorization |
|---|---|---|
| [Facility Owner](Sanad.CareHomes.Owner.postman_collection.json) | Owner onboarding, inventory, booking decisions, room assignment/transfers, maintenance, and stay operations | `careHomeOwnerJwt`; server policy `CareHomeOwnerAccess` |
| [Family app](../app/Sanad.App.Family.CareHomes.postman_collection.json) | Anonymous discovery plus Family booking/payment, stay confirmation/disputes, and ratings | `careHomeFamilyJwt` for Family routes; discovery calls set `noauth` |
| [Admin Care Homes](../admins/Sanad.Admin.CareHomes.postman_collection.json) | Application review, private-file reads, check-in disputes, refund recovery, inventory inspection, and payout ledger | Existing Admin environment `accessToken`; `CareHomesOperationalAdmin` allows SuperAdmin/SupportAdmin and denies ContentAdmin |
| [Care Homes finance and notes](Sanad.CareHomes.Finance.postman_collection.json) | HC-037 receipts, dashboard/revenue, CSV exports, and internal booking notes | Collection variables `careHomeOwnerJwt` or `accessToken`; Owner and Admin requests set their matching bearer token |

Configure `baseUrl` and the role token in the corresponding Postman environment. The Admin collection also expects the Admin environment's `accessToken`. Other empty collection variables are scenario IDs or fixture values; fill only those needed for the request being used.

The old [Sanad Care Homes collection file](Sanad.CareHomes.postman_collection.json) is now an empty index for compatibility. Its requests were moved into the role collections above. The broad [Sanad Admin collection](../admins/Sanad.Admin.postman_collection.json) no longer contains Care Homes domain requests; use the dedicated Admin Care Homes collection.
