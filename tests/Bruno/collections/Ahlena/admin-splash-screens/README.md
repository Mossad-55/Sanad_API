# Admin splash screens Bruno coverage

Run this folder only against the disposable local API with `baseUrl` exactly `http://localhost:5235` and `appEnvironment` exactly `Development`. Every request that writes data checks both values before sending. The API must use the disposable `SanadBrunoTestDb`; these splash screens are shared app content.

Run requests in sequence. The create request builds a unique `internalName` for each run, stores its ID and `imagePath`, and creates the record as Draft. The sequence covers create (201), list, detail, update with the optional file omitted, publish, unpublish, and delete (204). Publish is followed immediately by unpublish; do not run or inspect the public splash endpoint during that interval. The screen is Draft again before deletion. Delete only removes the screen row; `LocalDiskFileStorage` leaves the uploaded image on disk. After the run, remove only the corresponding file under the API output `sanad-files` directory, using the captured `imagePath` (for example `splash/<generated-name>.png`).

The folder also covers unauthenticated access (401), a caregiver role (403), unknown valid non-empty IDs (404), and create validation for a missing file (400). The missing-file request does not create a row or image. No requests are run as part of preparing this collection.
