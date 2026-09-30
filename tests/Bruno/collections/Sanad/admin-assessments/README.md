# Admin assessment Bruno coverage

Set Bruno's `baseUrl` to `http://localhost:5235` and `appEnvironment` to `Development` before running this folder against the disposable local API. All requests that can change assessment data check both exact values immediately before sending. The auth setup requests are also guarded because they are part of this local fixture flow.

The question create request uses a unique title and captures its ID. Tier updates and state transitions require IDs already captured in Bruno variables or supplied by a local fixture. No delete endpoint exists, so created assessment records remain in the local database; unique question text identifies this run's record.

Tier creation uses the existing disposable PNG fixture at the Bruno workspace root. The API registers `LocalDiskFileStorage`; with no configured root path, it writes under the API output directory's `sanad-files` folder (not an external provider). The request remains guarded to the local Development API. The missing-file request separately covers the 400 failure. Tier update omits its optional file and therefore exercises the no-upload path.

The sequence covers all 13 controller routes. Unauthenticated checks cover representative read, write, and multipart paths. Caregiver wrong-role coverage is representative rather than repeated for every route. Missing-ID checks cover question and tier detail, and validation covers question option count and tier missing-file handling. Created assessment rows remain in the disposable test database because the API has no delete route; names are unique per run.
