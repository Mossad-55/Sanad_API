# FUR-04 — Care Home readback: whole-phase worker brief

Owner-approved contract: [`../contracts/fur-04-care-home-readback.md`](../contracts/fur-04-care-home-readback.md)

## Assignment and source pin

Implement and verify the complete FUR-04 contract in one delivery. Begin from
`main` at source revision `14677d961813a94be602390e7801623c766ad53c` (the verified
baseline when this contract was locked). Inspect `git status`, `HEAD`, and the
relevant diff before editing. Preserve all existing owner/mastermind planning
changes and unrelated/private files. If `HEAD` or the working tree has moved,
do not overwrite or rebase user work; report the actual state and proceed only
where the scope remains safe and compatible.

Do not commit, push, publish, deploy, or start another phase. Return the saved
delivery report and stop for Mastermind review.

## Locked observable scope

Follow the approved contract exactly; it is authoritative for field names,
semantics, privacy, access, and test evidence.

1. Owner facility read: preserve `Draft`; add nullable revision-bound submitted
   and approved profile readbacks. Submitted is the pending-review revision;
   approved follows `ApprovedRevisionId` even if a newer draft exists. No GET
   mutation. Keep owner-only private contact data out of public discovery.
2. Admin application detail: include ordered revision media metadata and an
   Admin-file-route URI; never expose storage keys. Keep current owner/Admin/
   public file authorization. Correct typed-ID query predicates only if the
   exact Npgsql proof demonstrates a translation defect.
3. Owner operational booking detail: include the saved resident Arabic/English
   name and age snapshots; do not query current Elderly/Identity records.
   Preserve own-facility scope and existing 404 behavior.
4. Admin dispute read: include resident snapshots and clearly named original
   booking `TotalAmount` plus stored currency—not refund, payout, or net amount.
   Keep existing history/status and `CareHomesOperationalAdmin` permission.
5. Admin application owner display: nullable Arabic/English account names only;
   preserve `OwnerUserId`; no owner phone or other added personal data.
6. Admin review history: preserve actor IDs/order and add nullable Arabic/English
   actor names plus separately named **current-at-read** roles. Missing or
   anonymized identities yield null enrichment. Do not claim historical roles,
   add role snapshots/migration, or change owner history.

All changes are additive on existing routes. No new endpoint/request field,
policy expansion, owner dispute list, dispute-resolution power, public private
profile data, or lifecycle change. Any newly discovered migration, config,
permission, cross-module infrastructure, or scope requirement not expressly
approved here is a stop-and-report item; do not implement it without owner
approval.

## Likely ownership map (confirm exact paths before editing)

- Application: `CareHomeProfileCommands.cs`, `AdminCareHomeApplications.cs`,
  `CareHomeStayCommands.cs`, `AdminCareHomeBookings.cs`, and directly required
  DTO/mapping files.
- API/media: `CareHomeProfileMediaController.cs` and directly required
  serialization/controller files. Do not add routes or alter policies.
- Tests: the existing owner profile, Admin applications, profile-media,
  booking lifecycle, Admin booking/dispute, policy, and response-schema tests;
  add a dedicated FUR-04 PostgreSQL test file rather than repurposing FUR-03's
  test/guard.
- Gate: `docs/internal/tools/Test-SanadPortalGate.ps1` must explicitly strip and
  fail-closed assert absence of both
  `ConnectionStrings__Fur03VerificationDatabase` and
  `ConnectionStrings__Fur04VerificationDatabase` from the actual `dotnet test`
  child. Preserve existing integration-target neutralization and dummy module
  targets. Prove the guard with harmless inherited sentinel values and confirm
  no PostgreSQL contact occurs in the guarded full-unit invocation.
- Feature docs/Postman: update the applicable existing Care Home Admin and
  owner guides and their existing Postman collections/examples. Do not invent
  a new route. Keep examples consistent with actual JSON wire names and nulls.
- Audience handoffs: create dated reports under
  `docs/frontend-handoffs/admin/admin-portal/phase-04-care-home-readback/` and
  `docs/frontend-handoffs/admin/care-home-owner/phase-04-care-home-readback/`,
  link both from `docs/frontend-handoffs/README.md`, and connect intake issue →
  approved contract → implementation/tests → release receipt (pending at worker
  completion). State explicitly that frontend repositories were not inspected.
- Save the complete evidence report at
  `docs/internal/work/portal-api-reconciliation/reports/fur-04-worker-delivery.md`.
  Update only the FUR-04 status/traceability in the plan and Mastermind handoff;
  do not mark Done or claim deployment.

Avoid unrelated cleanup and unnecessary file ownership expansion. Record every
changed path in the delivery report; explain any required file outside the
likely map before extending scope.

## Fixture-first and database safety (mandatory)

The only authorized PostgreSQL target is the disposable local
`localhost:5432/SanadFur04VerifyDb`, via
`ConnectionStrings__Fur04VerificationDatabase`. Owner explicitly approved
creating/using this exact target and applying **only existing committed
CareHomes and Identity schemas/migrations** to it for synthetic tests.

Before any DB-backed test or API startup:

1. Inspect configuration and the exact target read-only. Validate host and
   database name fail-closed; do not print connection strings or secrets.
2. If the named DB already exists, inspect its identity, schema/migration
   heads, and fixture contents read-only. Reuse only if clearly disposable and
   safe. If ownership/content is unclear, stop; do not reset, drop, truncate,
   or repurpose it. If absent, create only this exact named local DB.
3. Apply only the existing committed CareHomes and Identity migrations needed
   for this contract. Read migration heads and fixture preconditions back before
   tests. Do not run startup migration implicitly. Any additional schema/module
   requirement is outside approval: stop and report it.
4. Use minimal synthetic owner/admin/resident/facility/revision/media/booking/
   dispute fixtures, fake storage and fake external providers. Read back the
   exact fixture relationships and states before execution and after cleanup.
   No production/VPS target, `SanadFur03VerifyDb`, `SanadBrunoTestDb`, shared
   database, real provider, Bruno, or broad seed/reset is allowed.
5. The FUR-04 PostgreSQL tests must be separately opt-in and reject any target
   other than the exact approved local host/database. The guarded Unit suite
   must neutralize inherited integration and both FUR03/FUR04 opt-in targets in
   the actual test child; never pass the FUR-04 target through that guard.

If prerequisites are not safely available, report the exact blocker and finish
independent non-DB work; do not improvise a target or weaken guards. Never put
secrets, tokens, OTPs, real-user PII, or raw production logs in reports,
commits, Postman, or handoffs.

## Required tests and acceptance gates

- Owner profile state matrix: Draft; PendingReview; NeedsCorrection/Rejected;
  Approved; Approved with a newer Draft. Assert correct revision binding and
  fields, no GET save/mutation, and no private fields in public discovery.
- Admin application: multiple revisions and media order; URI targets the
  existing Admin file route; no storage key in JSON. Test owner/admin/public
  allowed and foreign/unapproved denied cases without policy broadening.
- Owner booking: stored Arabic/English/age snapshots remain stable if current
  identity data differs; own-facility result and missing/foreign 404 behavior.
- Admin disputes: stored resident snapshots, original stored amount/currency
  semantics, existing status/history and policy.
- Identity display: owner and actor Arabic/English names, missing/anonymized
  null behavior, and a changed current role proving the field is current at
  read—not a historical role. No phone field.
- PostgreSQL/Npgsql: execute exact current profile-media owner/Admin/public
  facility predicates plus the exact changed read query paths. InMemory/SQLite
  is not translation/concurrency proof. Report per-case counts and readbacks.
- Build `Sanad.slnx` with strict repository settings; zero warnings/errors.
  Run focused relevant tests, the guarded full Unit suite after changing its
  gate, and relevant Architecture/API route-policy/schema checks. JSON-parse
  changed Postman collections; validate handoff links and `git diff --check`.
  Record exact commands, exit codes, passed/failed/skipped counts and explain
  every skip. Do not claim a test passed based on an attempted/timed-out run.

## Required worker report

The saved report must include: baseline and actual HEAD/worktree state; scope
summary; every changed file; contract-to-implementation/test traceability;
exact test commands and results; PostgreSQL target readiness and migration
head evidence; fixture pre/post readback and cleanup; proof of child-process
sanitization using harmless inherited sentinels; compatibility, permission,
error and null semantics; configuration/migration disclosure; limitations and
unproven frontend-reported assumptions; release/deployment status explicitly
“not performed—awaiting Mastermind review and owner release approval.” Do not
mark the phase Done. Then stop and return the report for source-level review.
