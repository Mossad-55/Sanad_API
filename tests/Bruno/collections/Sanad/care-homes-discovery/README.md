# Care Homes public discovery

Read-only Bruno coverage for the anonymous Care Homes discovery and detail
routes. Run only with the guarded local `Development` API at
`http://localhost:55819`, backed by the disposable `SanadBrunoTestDb` and the
local-fixtures environment. Do not run against production or a shared
database.

An approved, eligible Care Home fixture must already exist in the disposable
database. No IDs need manual entry: request 00 derives the first eligible ID
read-only for request 02, and request 03 uses a deterministic all-zero GUID.
The approved fixture must have all four required documents usable on the API's
Cairo date, at least one active room type, and no private contact/document
fields in the public response. `expectedCareHomeEnglishName` and
`expectedStartingFromMonthlyPriceEgp` are optional values for exact seeded
assertions; leave them unset when the disposable fixture is intentionally
variable.

Requests use explicit paths and contain a target guard before every request.
The collection performs no setup, reset, migration, mutation, authentication,
or cleanup. No generated `local-fixtures.bru` is needed: use the checked-in
`local` environment and override only the guarded base URL and environment.
From `tests/Bruno`, after the disposable API is running and the fixture exists,
use the direct Node CLI entrypoint below. In this PowerShell environment,
`bru` resolves to a policy-blocked `bru.ps1`, while `bru.cmd` stalled; the
verified invocation bypasses both. Do not change PowerShell execution policy.

```powershell
& 'C:\Program Files\nodejs\node.exe' 'C:\Users\mosad\AppData\Roaming\npm\node_modules\@usebruno\cli\bin\bru.js' run `
  collections/Sanad/care-homes-discovery/00-list-default.bru `
  collections/Sanad/care-homes-discovery/01-list-invalid-pagination.bru `
  collections/Sanad/care-homes-discovery/02-detail-approved-fixture.bru `
  collections/Sanad/care-homes-discovery/03-detail-unknown-is-404.bru `
  --env local `
  --env-var baseUrl=http://localhost:55819 `
  --env-var appEnvironment=Development `
  --insecure --bail --reporter-skip-body
```

The direct CLI's `--version` and `run --help` were verified as Bruno CLI 4.2.0.
The four explicit discovery requests passed in the guarded disposable run:
list returned the approved facility, invalid pagination returned the expected
400 code, approved detail returned 200, and unknown detail returned the
expected 404 code. The collection remains read-only. The one-time
onboarding/Admin fixture flow creates and approves the disposable fixture; it
has no facility-delete API, so only run setup after an authorized clean DB.
During the combined setup/discovery run, 20 selected requests passed with
52/52 assertions; one unrelated invalid Wellness Tips file was skipped while
the CLI scanned the collection. The run used direct Node CLI, explicit paths,
`--bail`, and suppressed response bodies. The multipart CLI 4.2 compatibility
mode is documented in the onboarding README and fixed on all Care Homes file
requests.
