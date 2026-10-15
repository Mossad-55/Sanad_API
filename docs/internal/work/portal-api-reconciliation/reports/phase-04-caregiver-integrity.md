# PORTAL-04 report — caregiver / Identity integrity diagnosis (no repair)

Status: Done — diagnostic reviewed and accepted with the bounded mastermind
corrections below; deployed cause/count explicitly unverified.
No production access, data change, deployment, or Bruno occurred in this task.
No API/Postman route change was needed. Stop before PORTAL-05.

## Intake

- Worker base: pushed `main` `b44fb50a6997c4e46a073d5e6e7b35867190257a`
  (PORTAL-03 acceptance + PORTAL-04 brief). At intake `main == origin/main`
  with a clean tree (`git status --short --branch`: `## main...origin/main`,
  no deltas).
- Owned manifest (exact files created/modified, nothing else):
  - new `docs/internal/work/portal-api-reconciliation/diagnostics/caregiver-identity.sql`
  - new `docs/internal/work/portal-api-reconciliation/diagnostics/caregiver-identity-runbook.md`
  - new `tests/Sanad.UnitTests/Caregivers/CaregiverIntegrityFixture.cs`
    (single private bounded helper: isolated SQLite attached-schema fixture)
  - new `tests/Sanad.UnitTests/Caregivers/CaregiverIdentityIntegrityTests.cs`
  - new `tests/Sanad.UnitTests/Caregivers/CaregiverAdminListIntegrityTests.cs`
  - new `docs/internal/work/portal-api-reconciliation/reports/phase-04-caregiver-integrity.md`
    (this file)
  - `docs/internal/work/portal-api-reconciliation/plan.md` (PORTAL-04 status
    lines only)
- `tests/Sanad.UnitTests/Caregivers/Onboarding/AdminCaregiverReviewsTests.cs`
  was reused unmodified (7/7 green, not counted as new coverage).
- No production controller/handler/domain/query/configuration/migration was
  touched. No INNER JOIN filtering, count fix, fabricated name, recreated
  identity, deletion, integrity framework, package, or migration.

## Behavior map (from source, not assumption)

- List: `GET /api/v1/admin/caregivers?page=&pageSize=&status=&type=`
  (`src/API/Sanad.API/Controllers/AdminCaregiversController.cs:111-130`),
  policy `CaregiverReviewAdmin` (SuperAdmin/SupportAdmin; ContentAdmin 403).
  Validator: `Page > 0`, `PageSize 1..100`
  (`AdminCaregiverReviews.cs:36-44`).
- `GetCaregiversAdminQueryHandler` runs the list query then the count query
  with identical filters and returns both
  (`AdminCaregiverReviews.cs:57-91`).
- `CaregiversDbContext.GetAdminCaregiversAsync`
  (`CaregiversDbContext.cs:106-186`): one row per `caregivers.caregivers`,
  `LEFT JOIN identity.users ON u.id = c.user_id`, nullable
  `ArabicFullName/EnglishFullName/PhoneNumber`, ordered
  `updated_on_utc DESC, id`, `LIMIT take OFFSET skip`. Filters are optional
  equality predicates on `c.status`/`c.type`.
- `CountAdminCaregiversAsync` (`CaregiversDbContext.cs:538-571`): counts
  `caregivers.caregivers` only, same predicates, no join. TotalCount therefore
  always includes unmatched identities by construction.
- Detail (`GetCaregiverAdminDetailQueryHandler`, `AdminCaregiverReviews.cs:99-129`)
  reads the Caregivers aggregate (with certificates) only; it never consults
  Identity, so an orphaned reference still opens detail.
- Reference: `CaregiverConfiguration` stores scalar `user_id` with an index
  and no cross-module EF relationship (`CaregiverConfiguration.cs:21-26`).
  `user_id` has no unique constraint, so duplicate references are possible
  by construction (covered by diagnostic Section 3c + test).
- Expected Identity account: `AccountType.MedicalCaregiver = 2` for
  `CaregiverType.Medical = 1`; `AccountType.CompanionCaregiver = 3` for
  `CaregiverType.Companion = 2` (`AccountType.cs`).
- Narrow supported creation path (possible paths, not proven defects):
  Identity registration accepts caregiver account types
  (`RegisterUserCommandHandler.cs:162-170`); the Caregivers-side
  `BootstrapCaregiverCommandHandler` checks only duplicate `UserId` inside
  the Caregivers target and verifies nothing about Identity existence or
  account type (`CaregiverProfiles.cs:188-213`). Test seeder follows the same
  split writes (`TestUserDataSeeder.cs:402-487`).
- Identity deletion/anonymization paths (no hard delete of the caregiver row
  anywhere): pure-caregiver self-delete anonymizes (Block + PII scrub) and
  retains the user row, then deactivates the caregiver profile
  (`DeleteMyCaregiverAccount.cs:28-47,219-248`); hybrid self-delete drops only
  the caregiver account types; admin anonymize retains
  (`AdminUserAuditCommands.cs:38-57`); the only hard user delete is an
  elderly-only compensation rollback (`DeleteIdentityUser.cs:51-69`).

## Configuration wiring (keys/fallbacks/eager capture)

- Key names: `ConnectionStrings:CaregiversDatabase`, fallback
  `ConnectionStrings:IdentityDatabase` (`Caregivers/.../DependencyInjection.cs:18-29`;
  identical fallback in Families/Identity modules; design-time factories read
  `ConnectionStrings__CaregiversDatabase` then `ConnectionStrings__IdentityDatabase`).
  An absent/null module key shares the Identity fallback; an explicitly blank
  module key fails validation instead (`??` does not skip empty strings).
- Eager capture: the string is read during `AddCaregiversInfrastructure`
  (`UseNpgsql(connectionString)`); the final `IConfiguration` snapshot alone
  is therefore not proof of the captured DI options. Proven by new test
  `ShouldCaptureConnectionStringEagerly_AtRegistration` (post-registration
  config mutation has no effect on the resolved options).
- Operator check (runbook Section 3, sanitized): record effective
  host/port/database labels privately per module, run the Section 0
  fingerprint (`current_database()` + schema presence) on each reachable
  target, report only `same-database yes/no` per module pair. No secret is
  ever dumped: tests use dummy `Host=portal04-test` strings and never connect.

## Dependency map (counts/ids/statuses only; split-target limits stated)

Same Caregivers target: `caregiver_certificates` (statuses),
`caregiver_service/language/area_selections`, `caregiver_ratings`
(`caregiver_id`), `caregiver_payout_accounts` (`caregiver_id` unique;
ids/status/revision only), `caregiver_payouts` (`caregiver_id, booking_id`;
ids/status only), `caregiver_payout_account_reviews` (via payout account;
decision only). Same-or-Identity target: `identity.users` (existence/status),
`identity.user_accounts` (`user_id, account_type`), `identity.user_identity_documents`
(existence). Families target only: `families.bookings` (`caregiver_id`),
`families.medical_reports` / `families.visit_reports`
(`caregiver_id, caregiver_user_id`). A join in one database cannot inspect
another database's tables: bookings/reports sections must run independently
on the Families target (diagnostic Sections 5–6; runbook Section 4 steps
4–5). Nothing here was inspected on a live target (no access authorized).

## Fixture readiness (mandatory gate, recorded)

1. Existing configuration inspected first: no API host is needed — the
   list/count methods and handlers run against isolated local contexts only;
   no inherited database target or real provider is used (explicitly: no
   `ConnectionStrings__*IntegrationDatabase` value was relied upon;
   PostgreSQL execution recorded as not run, see below).
2. Reused valid builders: `Caregiver.Create` + `CaregiverTestData` for the
   InMemory detail case; minimal hand-built rows for the relational fixture.
   Seeded per scenario: 5 caregivers (matched medical/Active, orphan
   companion/PendingReview, wrong-role companion/NeedsCorrection, second
   orphan medical/Suspended, matched companion/Active) + 3 identity users +
   3 account rows (expected caregiver account / Family-only / expected),
   plus one duplicate-`user_id` caregiver for the duplicate control.
   Synthetic fixed ids; no real identities/credentials.
3. Isolated providers: in-memory SQLite (`DataSource=:memory:`, kept-open
   connection, `ATTACH ... AS caregivers/identity`) for relational cases;
   EF InMemory for the handler/detail mapping case. No PostgreSQL target was
   authorized or verified disposable, so PG execution is recorded as not run;
   no evidence is manufactured and no independent test is blocked by this.
4. Readback recorded: helper asserts `caregivers=5, users=3` before any
   invocation; config key names (`ConnectionStrings:CaregiversDatabase`,
   `ConnectionStrings:IdentityDatabase`), seed/reuse counts, synthetic
   ids/states, and fake-provider setup (none needed — no provider touched).
5. Only then were the selected queries/scenarios invoked. No host startup,
   migration, seeder, or job was involved.

## Verification (actual evidence)

- `dotnet build Sanad.slnx --nologo -v minimal`: exit 0, **0 warnings,
  0 errors** (strict gate clean).
- Focused tests `--no-build --no-restore`:
  `FullyQualifiedName~CaregiverIdentityIntegrityTests|~CaregiverAdminListIntegrityTests|~Onboarding.AdminCaregiverReviewsTests`:
  exit 0, **24 passed, 0 failed, 0 skipped** (17 new + 7 existing; new tests
  prove list/count/filter/paging consistency, null-enrichment shape,
  classification matrix, duplicate detection, and config fallback/preference/
  missing-key/eager-capture behavior).
- `git diff --check`: clean.
- Authored SQL/runbook checked against current mapping names
  (`UserConfiguration`, `CaregiverConfiguration`, payout/rating/report/booking
  configurations, both model snapshots); read-only/bounded construction
  verified (SELECT-only, per-section `BEGIN READ ONLY ... ROLLBACK`,
  `SET LOCAL statement_timeout = '5s'`, `:limit`/paging caps, no PII columns).
- No full suite, API smoke, deployment, or package publication (per brief;
  integration suite stays PORTAL-07). No prior gate inputs were invalidated,
  so no unrelated suite was rerun.
- PostgreSQL: not executed (no authorized disposable target). SQLite
  attached-schema fixture proves supported join/reader semantics; EF
  InMemory/LINQ cases are labeled mocked-adapter evidence. Neither proves
  PostgreSQL raw-SQL behavior.

## Results matrix (local fixture; deployed rows NOT evaluated)

| Case | Local result |
| --- | --- |
| Matched identity enriches names/phone | Proven (SQLite, real SQL) |
| Missing identity user keeps row, nulls names/phone | Proven (SQLite, real SQL) |
| Count includes orphans; list total == count total (same filters) | Proven, incl. empty page window |
| Status/type filters apply equally to list and count | Proven (Active; Companion incl. orphan) |
| Paging deterministic (`updated_on_utc DESC, id`) | Proven (2/2/1 + empty window) |
| Handler returns page/size/total/items with filters | Proven (real context) |
| Invalid paging rejected (`page 0`, `pageSize 0/101`) | Proven (validator) |
| Detail opens without any Identity row | Proven (InMemory) |
| Classification matched / missing_user / present_user_missing_expected_account | Proven (diagnostic CASE shape) |
| Duplicate `user_id` grouping | Proven (control row) |
| Config fallback/preference/missing-key/eager capture | Proven (dummy strings) |
| Six reported deployed records classified | **Not run — unverified** (no live access) |

## Recommendation (by evidence; candidate handoff, not an applied fix)

The six reported null-enrichment rows are consistent with — but not proven to
be — any of: (a) stale/test-fixture caregiver rows whose Identity users were
never created in (or visible from) the Caregivers-target database; (b) a
target mismatch where the Caregivers connection's database simply has no
`identity.users` rows while the Identity module reads a different physical
database (an absent Identity schema fails the join; an empty visible users table
null-enriches all rows; a stale/partially populated copy can affect only a subset.
The matched/missing split alone cannot rule out target mismatch); or
(c) genuinely orphaned references from a split write with no cross-module
FK (possible by construction: scalar `user_id`, no unique constraint, no
existence check at bootstrap, no caregiver-row cleanup on anonymization —
but no write-path defect was reproduced, and the isolated missing-user
fixture proves query behavior only, never the cause).

Do not switch the list to INNER JOIN, do not "fix" the count, do not
fabricate names, recreate Identity users, or delete rows: each hides the
symptom while destroying the evidence needed for (a)/(b)/(c).

Bounded next step: the operator returns the runbook Section 5 sanitized
payload (target sameness, Section 1 totals, Section 2 full ids + codes,
Section 3 class counts + 3b/3c rows, Sections 4–6 blocks, and exact preflight
gaps). On receipt, reconcile full ids against the report and classify per
row; only then draft the exact repair/quarantine/indicator change for a
separate mastermind/owner decision. If no operator evidence arrives, the
deployed finding stays open and unverified; Phase 05 proceeds independently.

## Mastermind acceptance — 2026-10-15

Reviewed worker delivery `33af090e2f4d542d5b3247f8cbf90f664ec7445b`
on clean pushed main: diagnostic/runbook, relational fixture, list/count and
classification/configuration tests, mapping names and source conclusions.
Production files remain untouched. No live data access or repair.

Bounded documentation/SQL review corrections before acceptance:

- Duplicate-list UUID extrema now aggregate `id::text`, not raw UUID values.
  This avoids relying on UUID MIN/MAX aggregates unavailable in supported
  PostgreSQL versions; text extrema are representative IDs, not creation times.
  See [PostgreSQL aggregate type support](https://www.postgresql.org/docs/18/functions-aggregate.html).
- Listings now clamp limits to 1..100, and diagnostic page offsets use bigint.
  Parameters/default choices and quoted UUID substitution are explicit. The
  script contains SELECTs that the operator must wrap; it does not silently
  claim to establish its own read-only transaction. Capped classification
  counts are not presented as complete database totals.
- Corrected null-versus-blank configuration behavior: missing/null module key
  uses Identity fallback; an explicitly blank key fails startup validation.
- Corrected the target-mismatch hypothesis: a partially populated Identity
  copy can miss only some users; some matched rows do not rule out mismatch.

Mastermind reran `dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj
--no-build --no-restore --nologo -v minimal --filter
"FullyQualifiedName~CaregiverIdentityIntegrityTests|FullyQualifiedName~CaregiverAdminListIntegrityTests|FullyQualifiedName~Onboarding.AdminCaregiverReviewsTests"`:
exit 0, **24 passed, 0 failed, 0 skipped**, no warnings emitted. Fixture/code
unchanged by review; reused worker's successful zero-warning/error build.
No build rerun needed for SQL/runbook/handoff-only corrections. Existing
architecture/route-map inputs unchanged. `git diff --check` clean.

SQL was reviewed statically; PostgreSQL execution remains **not run**. SQLite
supports the demonstrated production join/reader behavior, not a claim that
every authored PostgreSQL diagnostic statement was executed. No diagnostic was
run on Production. The deployed six-row finding stays open for authorized
operator evidence; acceptance here closes delivery of diagnostics/local tests,
not the incident's cause or a data repair. Phase 05 can proceed independently.
