# Refund policy wave W1–W4 — release receipt

Date: 2026-10-23. Status: **Done**.

## Release revision and gates

The consolidated refund-policy wave (W1 fixes, W2 Finance money core, W3 four
refund lanes + the new Rentals module, W4 verification) was committed as
`feat(refund-policy): consolidate team-reports wave W1-W4 and release refund
policy across ten module contexts` and pushed to `origin/main`
(`4a066b3..edbccc1`). Released revision:
`edbccc153634cec7b942ae137871bb652f8ca1e2`.

Pre-release gates, all executed on the build machine:

- Strict Release build: 0 warnings / 0 errors.
- Full unit suite: **5073 passed / 0 failed / 26 skipped (5099 total)**.
- ArchitectureTests: 1/1 passed.
- `dotnet ef migrations has-pending-model-changes` clean for all ten contexts.
- API contract/route gate green: 659 signatures / 755 requests / 0 missing /
  0 orphan; all 24 Postman collections JSON-valid (Rentals collection synced
  with the 68-char idempotency-key bound, the two distinct 409 refund codes,
  and the `Finance.RentalRefund.ReconciliationRequired` ops fencing).
- Ignore-status proven per runbook §2: `.env` and `deploy/publish-out` both
  git-ignored. Staged tree swept for secret patterns: only test placeholders
  and pre-existing doc text; a **pre-existing test-mode Paymob `clientSecret`
  example** in `docs/app/families/bookings.md` (present before this wave) was
  forward-redacted in the release commit — **Owner follow-up: rotate if that
  key was ever live and scrub repository history**.

Note: this release commit also carries several narrower in-flight strands that
were completed but not yet released on `main` (partner-contact/help-center/
medical-access/UI follow-ups, CMS support-contact nullability, community
author photos, caregiver avatar/dependents) plus their migrations. All were
gated by the same full suite.

## Package

Built with `Publish-SanadApi.ps1` (Release) at the released revision. Manifest
`deploy/publish-out/sanad-deployment-manifest.json`: revision matches the push,
configuration `Release`, **ten** module migration heads
(Identity `20261022094500_AddPartnerContactOptionalFields`, Cms
`20261022103000_MakeSupportContactNullable`, Caregivers
`20261021130000_AddCaregiverReviewNotes`, Families
`20261022103000_AddBookingRefundPolicyAndSessionAllocations`, CareHomes
`20261022213000_AddCareHomeRefundPolicySnapshots`, Notifications
`20261010010618_AddEmailHtmlSnapshots`, Community
`20261021192605_AddCommunityAuthorBlocks`, Finance
`20261022230000_AddComplaintReviewsAndSubscriptionRefundRequests`, Chat
`20261013171755_AddAttachmentCleanupIndex`, Rentals
`20261023120000_CreateRentals`). Package archive
`sanad-api-edbccc1.tar.gz`, SHA-256
`563c6b9a7bb6487acb281cf6507b71997bd73971690ff7c9482b5db212515d98`
(12,491,796 bytes; 116 staged files) — SHA-256 verified identical on the build
machine and the target after transfer. The archive contained no
`appsettings.Development.json`, no `.env`, and no key material (verified by
filename and content-pattern scans before transfer).

## Target preflight and backup

Deployment continued, by reference to the immediately prior owner-designated
receipts, on Production `72.62.92.144:8091`, service `sanad-api.service`,
working directory `/root/sanad-api`, existing `/etc/sanad/sanad.env`. On-host
tooling: pg_dump/pg_restore PostgreSQL 16.15. Preflight:

- `GET /api/v1/deployment/info` returned Production, database reachable, live
  revision `1e3d2b021ba9455483b3976a712b76b40c2fef67`, all nine applied heads
  matching that revision.
- Effective database identity verified privately on-host: a single database
  `SanadDb` on localhost (Identity fallback wiring) — **new schema writes were
  expected in Families, Finance, CareHomes, Cms and the brand-new Rentals
  schema**, so a validated backup was mandatory.
- Backup created on that exact target before restart:
  `database-before-refund-policy-edbccc1-20261023.dump` (custom format,
  **747,942 bytes, 817 TOC entries**) and validated with `pg_restore -l`.
  Credentials used on-host only and never printed or recorded. Release alias
  directory `/root/sanad-releases/refund-policy-edbccc1-20261023/` also holds
  `app-before.tar.gz` (recoverable copy of the previous binary directory,
  12,146,877 bytes) and pre/post SHA-256 config fingerprints.

## Cutover

Service stopped; only application files were replaced, excluding
`appsettings*.json`; existing service unit, environment file, storage, and
key-ring preserved. Pre/post SHA-256 fingerprints of `appsettings*.json`,
`/etc/sanad/sanad.env`, and the service unit were recorded and matched
(CONFIG_UNCHANGED).

One recorded host observation: the target carries a static
`appsettings.Development.json` (preserved per runbook §3). It is inert while
the service runs with the Production environment (confirmed by the deployment
endpoint), but the Owner may choose to remove it in a later maintenance pass.

## Startup evidence and verification

Startup (before accepting traffic) checked **all ten contexts** and applied:

- Cms: 1 migration (`MakeSupportContactNullable`)
- Families: 1 (`AddBookingRefundPolicyAndSessionAllocations`)
- CareHomes: 1 (`AddCareHomeRefundPolicySnapshots`)
- Finance: 2 (`CreateFinanceMoneyCore`,
  `AddComplaintReviewsAndSubscriptionRefundRequests`)
- Rentals: 1 (`CreateRentals`, schema creation incl.
  `ck_rental_bookings_pending_refund_destination`)
- Identity/Caregivers/Notifications/Community/Chat: 0 (already current)

The API then bound `http://127.0.0.1:5001` behind nginx. A first proxied probe
a few seconds after start returned 502 while the bind was still pending; the
recaptured full journal is the authoritative startup evidence
(`startup.log`, on-host in the release alias directory).

`Verify-SanadDeployment.ps1` exited **0** over direct public HTTP:

- Remote revision exactly matched pushed/package revision `edbccc1…`.
- Production environment and database reachability confirmed.
- Required/applied migration heads **10/10 match** with the manifest.
- Startup log proved **10/10** context migration checks.
- Anonymous smoke `GET /api/v1/lookups/services` HTTP 200.

No real provider calls, no OTP sends, and no refunds were executed as part of
deployment verification; the only schema changes were the six startup
migrations on the validated backup's database.

## Limitations and Owner follow-ups

- The §4.1/4.3/4.4/4.6/4.7 refund scenarios remain **unit-proven but
  relational-unproven** (the S7 pack includes no fixture seeder; migrations
  and the TimedOut sweep query were exercised on real PostgreSQL on the
  disposable target only). Recorded in the acceptance receipt in the
  [execution brief](../worker-prompts/team-reports-refund-policy-2026-10-22.md),
  never counted as passed.
- W3 residuals stand: F3 period over-refund residual awaiting Owner
  acceptance; complaint compensation amount cap deferred; crash-before-send
  unwedgeability compensated by the shared Paymob attempt-liveness seam.
- The machines' ambient `Paymob__SecretKey` environment finding (Development
  binds the real client on the build host) remains recorded, sanitized.
- No live authenticated refund flow was executed in Production after cutover
  (deployment verification is bounded to the anonymous smokes above, per
  runbook §4.3).
