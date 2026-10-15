# PORTAL-04 — caregiver / Identity integrity diagnosis, not repair

Role: bounded read-only code/data diagnostic and local regression-test worker.
PORTAL-01/02/03 are accepted. Execute only PORTAL-04 and return for mastermind
review; do not start owner-portal tests (05), geography (06) or release (07).

## Intake / pinned base

Read root AGENTS.md, governance, Mastermind_Handoff, PORTAL-04 and the mandatory
fixture-readiness section of `../plan.md`. Read the report's A2 verdict, not
the whole project history. Phase 03 correction delivery was
`13a85cafcbbbccd26ed8728f930ab34c9e57202b`; the following mastermind commit
records acceptance and adds this brief. The launch message pins that final
pushed SHA. Verify clean main equals origin/main there; record actual intake.

Follow current main-direct policy. You are not alone in the codebase: preserve
others' edits, stage explicit owned paths, and stop on overlapping unowned work.
No branches/worktrees, hook bypass, merge, publish, deployment or Bruno. Commit
and push completed scoped diagnostics/tests/docs normally; return exact SHA.

## What is known, and what is not

Frontend reported seven caregivers, six with missing Identity lookups and null
names/phone, using only abbreviated caregiver IDs. This is **not** independent
proof of six orphaned production rows, nor proof of their cause.

`CaregiversDbContext.GetAdminCaregiversAsync` already LEFT JOINs
`identity.users` on user_id. Its matching count counts caregivers, including
unmatched identities. `CaregiverConfiguration` stores a scalar cross-module
UserId/index without a cross-module EF relationship. This explains the possible
null enrichment shape; it does not prove a defective write or a data reset.
The list query reads Identity from the **Caregivers connection's database**;
Identity APIs use the Identity module target. Misaligned module targets are
therefore another hypothesis, not a conclusion.

## Approved task / deliverables

1. Map the exact list/count/detail behavior, filters, pagination and relevant
   Identity account-role expectations from source. Trace the narrow supported
   caregiver creation/onboarding and Identity deletion/anonymization/seed paths.
   Distinguish a possible path from a demonstrated defect. Do not relabel a
   deliberately constructed orphan fixture as proof of a real write-path bug.
2. Prepare a bounded, parameterized **read-only** PostgreSQL diagnostic and
   operator instructions. Derive real schema/table/column names from current
   mappings/migrations, not assumptions. Cover missing Identity users, present
   users missing the expected caregiver account type, mismatched type/role and
   duplicate references only if the current model makes them meaningful.
   Include matched controls and a summary that reconciles list/count totals.
3. Inspect configuration wiring (key names/fallbacks/eager capture) without
   dumping environment variables, secrets or connection strings. Describe how
   an authorized operator checks whether effective Identity/Caregivers targets
   are the same physical database and whether required schemas exist. A target
   label/fingerprint can be compared privately; reports must be sanitized.
   Final IConfiguration alone is not proof of already-captured DI options.
4. Map dependent caregiver documents/children, bookings, reports and finance/
   payout references before any repair recommendation. Use counts/IDs/statuses,
   never document content, phone/name/email, banking values or payloads. Identify
   dependencies that cannot be inspected on a split database and hand them off
   explicitly instead of inventing zero counts. Queries must be bounded, with
   selected IDs/page limits and read-only transaction + short statement timeout.
   Use schema preflight before dependent queries; avoid a failed missing-table
   statement silently invalidating the rest of a transaction.
5. Add local fixture-backed regressions for matched/unmatched Identity cases,
   list/count/filter/paging consistency and diagnostic classifications. Reuse
   current test builders. Exercise actual production list/count SQL where a
   supported safe relational fixture can do so; no runtime query rewrite merely
   to accommodate a test. Label mocked adapter/mapping evidence as such. EF
   InMemory/LINQ cannot prove PostgreSQL raw-SQL join behavior. A SQLite
   attached-schema fixture may prove supported join/reader semantics, but is
   not PostgreSQL execution proof. Document exact provider limitations.
6. Report a recommendation by evidence: stale fixture hypothesis, target mismatch,
   or an actually reproduced write-path defect. Any product policy/integrity
   indicator, repair, quarantine or data action requires a separate exact
   mastermind/owner decision. Return a candidate handoff, not an applied fix.

## Production / diagnostic authority

This task **does not authorize connecting to the VPS or an inherited database**,
authenticated public-HTTP requests, remote data exports, seed/reset/cleanup, or
changing data. The VPS is Production, not a disposable fixture. Do not recover
credentials from files or logs, and do not infer live access from the URL.

If an operator separately supplies authorized sanitized read-only results/full
IDs, reconcile them. Otherwise deliver the diagnostic and exact operator
handoff, explicitly leaving the cause/count of the six reported records
unverified. Truncated IDs must never be expanded by guesswork. This limitation
does not block independent local tests or Phase 05, but the deployed integrity
finding stays open pending operator evidence.

The diagnostic script is authored for operator review, **not executed remotely**.
Use SELECT/preflight only inside a read-only transaction, short timeout, and
ROLLBACK. No DML/DDL, temporary-table creation on Production, repair statement,
unbounded PII export or side-effecting function. No automatic connection from a
script or inherited environment variable. Document how split-target comparisons
must be run independently; a join in one DB cannot inspect another DB's users.

## Owned manifest

- New `docs/internal/work/portal-api-reconciliation/diagnostics/caregiver-identity.sql`
  (and one separate preflight SQL file there only if necessary).
- New `docs/internal/work/portal-api-reconciliation/diagnostics/caregiver-identity-runbook.md`.
- New `tests/Sanad.UnitTests/Caregivers/CaregiverIdentityIntegrityTests.cs` and
  optionally `CaregiverAdminListIntegrityTests.cs` in that directory, with one
  private bounded helper under the same directory if essential.
- Existing `tests/Sanad.UnitTests/Caregivers/Onboarding/AdminCaregiverReviewsTests.cs`
  only if needed for directly related assertions; otherwise leave untouched.
- New `docs/internal/work/portal-api-reconciliation/reports/phase-04-caregiver-integrity.md`
  and only PORTAL-04 status/evidence in `../plan.md`.

All production controllers/handlers/domain/queries/configuration/migrations are
read-only for this task. No INNER JOIN filtering to hide rows, count-only fix,
fabricated names, recreated Identity users, profile deletions, global integrity
framework, new package or migration. Ask before an essential manifest extension.
Central handoff/index are mastermind-owned. No API/Postman route change is needed.

## Mandatory fixture readiness BEFORE any test API/scenario

1. Inspect existing safe helpers and configuration first. Prefer no API host:
   the list/count methods and handler/diagnostic need only isolated local test
   contexts/connections. No inherited database target or real provider.
2. Reuse valid fixture builders; seed only the minimal matched caregiver/user,
   deliberately missing user, wrong-role control, and dependent reference/state
   needed by a selected scenario. Use synthetic data. Read back IDs, roles,
   ownership, statuses and relations before invoking the tested operation.
3. Use isolated InMemory for mapping/handler cases and in-memory SQLite for
   compatible relational cases if possible. PostgreSQL is optional only on an
   already-authorized, exact verified disposable target with minimal fixture
   writes and **no reset**. Do not run inherited EnsureDeleted PG fixtures.
   If no such target exists, record PG execution as not run; do not manufacture
   evidence or block independent tests waiting for it.
4. Record provider/config key names, seed/reuse counts, synthetic IDs/states,
   fake provider/storage setup and readback. No real identities/credentials.
5. Only then invoke selected queries/scenarios. If a host is truly needed,
   justify it and reuse the corrected Phase 03 isolation principles: all
   module contexts safe, migrations/seeders/jobs disabled, ephemeral keys,
   temporary storage and fake providers configured before startup.

## Acceptance / verification / stop

Report exact commands, provider and what they prove; authored SQL is not live
execution. Include a small results matrix, source paths, target-wiring diagnosis,
dependency map and the remaining operator inputs. No unsupported claim that
production is clean or that the six rows are definitely orphans.

After final test code, strict solution build exit 0 / zero warnings/errors,
then only new integrity tests and relevant existing admin-review tests with
`--no-build --no-restore`; zero failures. Check SQL/runbook against current
mapping names, verify read-only/bounded construction, and `git diff --check`.
No full suite, API-wide smoke, deployment or package publication; integration
suite stays PORTAL-07. Reuse prior architecture/contract gates if their inputs
remain unchanged, rather than rerunning unrelated suites.

Commit/push normally. Return base/final SHA, exact files, fixture readiness,
test counts/skips/warnings, actual versus proposed evidence, and bounded next
recommendation. Stop before PORTAL-05; mastermind reviews this diagnosis first.
