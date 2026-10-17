# FUR-05 — Owner settlements release receipt

Date: 2026-10-17 (Africa/Cairo). **Status: Done — accepted, committed, pushed,
published, deployed and verified.**

Traceability: [approved contract](../contracts/fur-05-owner-settlements.md) →
[worker delivery](fur-05-worker-delivery.md) →
[Mastermind review](fur-05-mastermind-review.md) → this release receipt.
Phase commit and deployed revision: `ca2ef2fbe16c395194301a88637aa93008e7066f`
(`Complete FUR-05 owner settlements`), pushed to `origin/main`; post-push
`HEAD` and `origin/main` matched. The owner directed completion and deployment
of FUR-05 on 2026-10-17.

## Review and gates

- Mastermind review found no implementation or test defect. The Postman
  description was corrected to state the actual shared checkout-time ordering.
- Strict solution build passed with 0 warnings and 0 errors.
- Guarded full Unit suite passed **3,482 passed / 0 failed / 31 skipped**. The
  guarded child confirmed integration targets absent, nine module targets
  isolated, and migrations/seeders disabled. The first restricted attempt hit a
  testhost connection timeout; the normal-access retry passed.
- Guarded Architecture suite passed **1 passed / 0 failed / 0 skipped**. Its
  first restricted attempt had the same testhost timeout; the normal-access
  retry passed.
- Worker-focused tests passed 56/56; Postman collection and embedded response
  parsed. `git diff --check` passed. The controller/Postman mapping check
  reported 557 controller method signatures and 621 requests; its own printed
  gap counts are not treated as proof because the comparison collections are
  unpopulated. Independent comparison against the previously accepted 556/556
  unique route baseline confirms this phase adds one matching method/path on
  each side, for 557/557 unique route coverage.
- No Bruno run or authenticated owner-ledger request was made. Post-deployment
  OpenAPI inspection confirmed `GET /api/v1/care-homes/payouts/mine` is present
  and documents HTTP 200. Automated tests cover route metadata, policy, owner
  scoping and ledger behavior.

## Package and deployment

- Fresh Release package was built from the pushed commit at
  `deploy/publish-out-fur-05-ca2ef2f/`. Publisher exit 0; the manifest revision
  matches the commit. Its nine required migration identifiers were independently
  checked against source. The package has no `.env`, local settings, keys,
  certificates, or `appsettings.Development.json`; its `appsettings.json`
  contains only `Logging` and `AllowedHosts` keys.
- Transferred archive: `deploy/sanad-api-fur-05-ca2ef2f.tar.gz`, 119 tar entries,
  SHA-256 `428D1515313F7CC1A0929461B20C3E36B46C5F7904DEB91AE00F4C63D950F108`.
- Target: previously owner-designated Production VPS `72.62.92.144`, service
  `sanad-api.service`, working directory `/root/sanad-api`. Preflight found the
  service active with `NRestarts=0`, deployed revision
  `31b13ccbb548b27267403e2db686e2beea63a732`, Production environment, and
  reachable database. Effective runtime connection configuration used the
  shared `localhost:5433/SanadDb` target for all nine contexts; credentials are
  not recorded. All nine currently applied migration heads matched the new
  package before cutover, so no migration was pending.
- A private, validated rollback archive was retained on-host at
  `/root/sanad-releases/fur-05-ca2ef2f-20261017/api-before.tar.gz`; SHA-256
  `31c8e51873ca676519a84b850f6397e2c6e37ecd0fff7767e5fb41666a79c1df`.
- The existing host `appsettings.json`, `appsettings.Development.json`, systemd
  unit, and service environment file were preserved. Before/after SHA-256
  fingerprints were unchanged:
  - service environment: `e5b4ec5e4f1f3a575eb4023ef6aa081ec87aa614d95a7b93877eb51b86877a65`
  - systemd unit: `c018192e0480b90cdb01af0e6d27c7505bbdca1a87aa9fd732361e74f843f98e`
  - host `appsettings.json`: `200332b434757a0054a3d356c91cde01f2c48712be8aa2bcb0f3e973619f06e3`
  - host `appsettings.Development.json`: `e92cd3f4ec157c20bf868f8bd4e5de185903e7a46b24c7839a7fa583f0432cb8`
- Startup logs show checks for all nine contexts and **zero migrations applied**.
  The service is active on revision `ca2ef2fbe16c395194301a88637aa93008e7066f`
  with `NRestarts=0` and database reachability true.
- The standard `Verify-SanadDeployment.ps1` passed through a temporary
  localhost-only SSH tunnel. It confirmed exact revision, Production
  environment, database reachability, 9/9 required/applied migration heads,
  startup checks for all nine contexts, and anonymous
  `GET /api/v1/lookups/services` HTTP 200. The tunnel was closed afterward.
- The first post-start proxy polls briefly received connection-refused while
  the API bound its listener; the later probe and standard verifier passed with
  no restart. A remote command wrapper returned nonzero after the successful
  cutover because a CRLF affected only its final log-display command; separate
  readback and the standard verifier confirmed successful deployment.

## Scope and limitations

No migration was added or applied; no payout, debt, booking, provider, or
configuration data was changed. No real OTP, SMS, email, payment, or push call
occurred. Direct public HTTP from the build machine remains unverified; the
on-host and SSH-tunneled checks are the deployment evidence. The authenticated
owner ledger was not queried live because this release had no authorized owner
fixture/token; its behavior is covered by the passing isolated tests.

FUR-05 is Done. FUR-06 remains Not started and deferred at the owner's request;
the parallel-work decision will be discussed with the owner before any further
contract or implementation work.
