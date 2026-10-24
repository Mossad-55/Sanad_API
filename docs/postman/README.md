# Postman collections — purpose and conditions

Every controller route is covered: `Verify-ApiContractMapping.ps1` must report
0 missing / 0 orphan Postman routes before any merge. All collections carry
top-level descriptions; examples are statically authored from verified
behavior and are never executed by the mapping check. Live execution happens
only through the guarded Bruno flow on disposable targets.

## Layout by audience

- `Sanad.Auth.postman_collection.json` — login, OTP, sessions, password flows.
- `app/` — family, caregiver, elderly, and public app surfaces:
  `Sanad.App.{Chat, Community, Caregiver, Caregiver.Operations, Elderly,
  Family, Family.Operations, Family.CareHomes, Library, Public}`.
- `admins/` — admin surfaces: `Sanad.Admin.{CareHomes, Chat, Community,
  Families, Finance, Ratings}` plus the general `Sanad.Admin` collection (incl. the Library CMS folder).
- `care-homes/` — the original Care Homes slice collections (`Sanad.CareHomes{,
  .Owner, .Finance}`) plus its [README](care-homes/README.md).

## Conventions (all collections)

- `{{baseUrl}}` + `{{accessToken}}` variables; auth via `Authorization: Bearer`.
- Collection variables (`pm.collectionVariables.set`) capture created ids; saved
  `4xx` examples document the stable error codes from the feature guides.
- Never commit real credentials, tokens, or `local-fixtures.bru` (git-ignored,
  generated per run). Bodies under `multipartForm` follow the Bruno rule in
  `docs/internal/operations/bruno-failure-first.md`.
