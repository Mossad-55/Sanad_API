# FUR-04 — Care Home readback

Date: 2026-10-16 (Africa/Cairo). **Status: owner-approved, including test-schema addendum, on 2026-10-16; worker brief prepared.**

## Traceability and source pin

- Intake: [frontend audit items 24–29 and fixture-first test contract](../frontend-report-audit-2026-10-16.md#6-item-by-item-audit--a3-missing-fields), plus the [FUR roadmap](../plan.md#frontend-follow-up-sequence--2026-10-16).
- Baseline: `14677d961813a94be602390e7801623c766ad53c` (`main == origin/main`, clean at planning start).
- This is the next sequential phase after FUR-03. Roadmap inclusion is not feature approval. Frontend repositories remain uninspected; no client-code defect is asserted.
- Existing surfaces: owner facility `GET /api/v1/care-homes/facilities/mine`; admin application list/detail `GET /api/v1/admin/care-homes` and `GET /api/v1/admin/care-homes/{careHomeId}`; owner operational booking detail `GET /api/v1/care-homes/booking-requests/mine/{bookingId}/operational`; Admin booking detail `GET /api/v1/admin/care-homes/bookings/{bookingId}`; Admin disputes `GET /api/v1/admin/care-homes/check-in-disputes`; profile media file routes for owner, Admin and public approved discovery.
- Current findings were source-inspected in `CareHomeProfileCommands.cs`, `AdminCareHomeApplications.cs`, `CareHomeProfileMediaController.cs`, `CareHomeStayCommands.cs`, `AdminCareHomeBookings.cs`, and `CareHomeFacility.cs`.

## Proposed objective

Complete additive readback on the existing authorized Care Home owner/Admin routes so that current profile revisions, revision media, booking resident snapshots, dispute context, owner identity display and review-actor attribution are inspectable without changing lifecycle rules or granting additional access. Correct the source-identified EF typed-ID risk in the existing profile-media authorization queries only if exact PostgreSQL proof demonstrates a failure. No endpoint or business command is proposed.

## Proposed observable contract

1. **Owner profile revisions (intake #24).** Keep the existing `Draft` meaning and shape. Add nullable, explicitly revision-bound readbacks for the active submitted revision and approved revision (revision ID plus profile projection), rather than one ambiguous `CurrentProfile`. `SubmittedProfile` is populated only while a submitted revision is pending review; `ApprovedProfile` follows `ApprovedRevisionId` and remains available while a newer editable draft exists. Preserve owner-only private contact fields. A GET must not create, save, or mutate a draft. Never return these owner contact fields through public discovery.
2. **Admin revision media (intake #25).** Add each revision's ordered media metadata to the existing Admin application detail: media ID, kind, position, content type, length, and a URI to the already-authorized Admin file route. Do not serialize storage keys. Preserve the existing owner and public file routes' respective authorization/current-approved-revision rules. Test/fix the exact host-side facility-ID predicates in those file guards only if the Npgsql test proves a translation defect; do not change access policy to work around translation.
3. **Owner operational booking detail (intake #26).** Add the booking's stored resident Arabic/English names and age snapshot to the existing owner operational response. These are immutable booking snapshots; do not recalculate from the current Elderly/Identity account or add a cross-module lookup. Preserve own-facility scoping and existing 404 behavior for missing/foreign bookings.
4. **Admin dispute display (intake #27).** Enrich the existing Admin disputes read with resident snapshot names and an explicitly named booking amount: the booking's original stored `TotalAmount` in its stored currency, not refund/net payout. Keep current dispute history/status fields and `CareHomesOperationalAdmin` authorization. This phase does not add an owner dispute-list endpoint or grant resolution powers; owner dispute-list capability remains FUR-08.
5. **Application owner display (intake #28).** Preserve `OwnerUserId`. Proposed addition is nullable Arabic/English account display names in Admin application list/detail, with graceful missing/anonymized identity behavior. **Recommendation: do not expose owner phone in this response.** The current user-detail API is SuperAdmin-only, while Care Home operational Admin includes SupportAdmin; reusing that surface would broaden personal-data access. Any phone exposure requires an explicit owner decision and a scoped access design; it must not be inferred from the intake's requested field.
6. **Review actor display (intake #29).** Preserve actor IDs and event ordering/history. Proposed addition on Admin application review history is nullable actor Arabic/English display names and a separately named `ActorCurrentRoles` field, whose values are explicitly current account roles at read time—not the role held when the historical event occurred. Missing/anonymized users produce null display enrichment without breaking history. **Recommendation: do not add historical role snapshots in this phase**; doing so requires a migration and cannot truthfully backfill old events. Do not add staff identity/role enrichment to the owner response unless separately decided; preserve its current actor-ID history shape for now.

## Compatibility and authority boundaries

- Additive nullable response fields only; existing verbs/routes, pagination, status/error behavior, DTO field meanings and current authorization policies remain unchanged. No new endpoint or request field.
- Admin application/revision/dispute reads remain under `CareHomesOperationalAdmin` (SuperAdmin + SupportAdmin). Owner facility/operational reads remain under `CareHomeOwnerAccess`. Existing Admin and owner file routes keep their scopes; public file access remains restricted to the approved revision.
- No owner dispute list, dispute resolution, owner-account phone exposure, role-policy change, current-profile public exposure, or data repair is included by default.
- No migration/configuration change is expected under the recommended current-role-at-read semantics. If a selected decision requires cross-module infrastructure, a schema change, new permission, configuration or migration, disclose the exact requirement before implementation and stop for separate approval.

## Test and evidence contract

- Fixture-first, fake storage/identity dependencies where applicable. Reuse only an owner-authorized disposable target after read-only readiness checks; never use the Production VPS, production-mode API, Bruno, broad seeding, destructive reset or real provider.
- PostgreSQL target for this phase is exactly `SanadFur04VerifyDb` on local `localhost:5432`, with `ConnectionStrings__Fur04VerificationDatabase`, fail-closed exact host/database checks and inherited integration-target neutralization in the actual test child process. **Owner explicitly approved creation/use of this exact target and application of existing committed CareHomes and Identity schemas/migrations to it.** Do not add/edit migrations or apply uncommitted migrations. If another module schema or new migration/configuration is required, stop and disclose the exact requirement for separate approval. Do not use FUR-03's `SanadFur03VerifyDb` or any shared target.
- Focused unit/API tests: owner profile state matrix (Draft; PendingReview; NeedsCorrection/Rejected; Approved; Approved plus a newer draft), no GET mutation; Admin application detail with multiple revisions/media ordered by kind/position; owner/admin/public media authorization and foreign-revision denial; resident Arabic/English/age snapshot; dispute amount semantics and resident data; missing/anonymized owner/actor handling; role change after historical event proves any role field is explicitly current, not historical.
- PostgreSQL proof: execute exact current application/media/booking/dispute read query paths with Npgsql. In particular exercise owner/admin/public media route facility predicates with allowed and foreign/unapproved revision cases. InMemory/SQLite results do not substitute for translation evidence.
- Strict solution build (zero warnings/errors), focused tests, relevant route/policy/schema checks, `git diff --check`, and exact command/count reporting. No full suite by default absent runtime cross-cutting risk.
- Worker report includes source pin, changed files, exact commands/results, fixture pre/post readbacks, child-process target sanitization, error/status/permission compatibility, migration/config disclosure, limitations and the verified release status. Update Admin and Care Home Owner audience handoffs with issue → contract → implementation/tests → release receipt/N/A links; do not claim frontend code was inspected.

## Owner decisions and approval

Owner approved this proposal and the test-schema addendum on 2026-10-16: separate nullable
submitted/approved profile projections alongside `Draft`; account names only (no
phone) for Care Home Admin; current-role-at-read semantics only on Admin history,
owner history unchanged and no historical-role snapshot/migration; bounded six
item scope with owner dispute-list deferred to FUR-08; and creation/use of only
`SanadFur04VerifyDb` on local `localhost:5432`, subject to the stop/read-only
preflight above. This is contract approval, not release approval.

### Approved test-schema addendum

Source tracing shows the approved owner-name/current-role fields require an
Identity display lookup. Owner approved applying the existing **committed
Identity migrations/schema** to the same isolated `SanadFur04VerifyDb` for
synthetic lookup tests, in addition to the committed CareHomes schema. This
approval does not include any other module schema, a new/uncommitted migration,
or any production/shared Identity database. The worker must inspect the target
read-only first and stop if its identity/content is unclear; no reset or drop.

The complete contract and addendum are locked. The separate exact worker prompt
and short paste-ready High-effort kickoff are prepared. Contract approval does
not authorize commit, push or deployment; release approval will be requested
after complete delivery and review.
